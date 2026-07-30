using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using static Mist.ProgramModel.Construction.Types.TypeBuilder;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// V2 C# emitter that consumes an <see cref="IProgramModel"/> and produces
/// compilable C# source files organized by namespace, then compiles them
/// into a DLL using <see cref="CSharpCompilationDriver"/>.
/// </summary>
internal sealed class CSharpEmitterV2
{
    private readonly IProgramModel programModel;
    private readonly CompilerOptions options;
    private readonly DiagnosticBag diagnostics;

    public CSharpEmitterV2(
        IProgramModel programModel,
        CompilerOptions options,
        DiagnosticBag diagnostics)
    {
        this.programModel = programModel;
        this.options = options;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Emits all types to disk and compiles the result.
    /// Returns a <see cref="CompilerResult"/>.
    /// <para>
    /// Every assembly is emitted into a single CLR assembly, so a composed program contains
    /// one <c>Main</c> per project and Roslyn reports CS0017 unless told which to use. The
    /// compiled project's own entry point, recorded during member resolution, is what
    /// disambiguates. Once emission splits per assembly this becomes unnecessary; see
    /// <c>backlog/multi-project-emission.md</c>.
    /// </para>
    /// <para>
    /// Fails before emitting anything if an executable was requested and no entry point was
    /// recorded.
    /// </para>
    /// </summary>
    public CompilerResult Emit()
    {
        var isTopLevelEntry = options.EntryPoint == "top-level";
        var entryPoint = isTopLevelEntry ? null : programModel.SelfAssembly?.EntryPoint;

        // An executable with no entry point cannot be produced — unless the project uses
        // top-level statements, in which case C# handles entry point selection itself.
        if(entryPoint is null && !isTopLevelEntry && options.OutputType == ProjectOutputType.Executable)
        {
            diagnostics.Report(
                BackendDiagnostics.MissingEntryPoint, default, options.ProjectName, (object?)options.ProjectName);
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var outputDir = options.OutputDirectory;
        _ = Directory.CreateDirectory(outputDir);

        var emittedFiles = new List<string>();

        // Emit each source type as a separate .cs file. Standard library types are
        // provided by the runtime and must not be emitted.
        var sourceTypes = programModel
            .TypesByFQN
            .Values
            .Where(t => t.Assembly.HasMistSource && t.Assembly.Kind != MistAssemblyKind.Standard)
            .OfType<MemberType>()
            .Where(t => t is not ConcreteType { IsIntrinsic: true })
            ;

        foreach(var memberType in sourceTypes)
        {
            if(isTopLevelEntry && memberType.Name == TopLevelTypeName)
            {
                EmitTopLevelType(memberType, outputDir, emittedFiles);
                continue;
            }

            var relativePath = TypeEmitter.GetRelativePath(memberType);
            var fullPath = Path.Combine(outputDir, relativePath);

            var dir = Path.GetDirectoryName(fullPath);
            if(dir is not null)
            {
                _ = Directory.CreateDirectory(dir);
            }

            var source = TypeEmitter.Emit(memberType, diagnostics);
            File.WriteAllText(fullPath, source);
            emittedFiles.Add(fullPath);
        }

        // Emit the .csproj file.
        var csprojContent = ProjectFileEmitter.Emit(options);
        var csprojPath = Path.Combine(outputDir, $"{options.ProjectName}.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        // Transpilation is done; compiling it is a separate step, and it must not run if
        // transpilation failed. Roslyn would answer with C# errors against generated files,
        // which describe symptoms in code the user never wrote and bury the Mist-level cause
        // that is already sitting in the bag. The pipeline gates Stages 4 and 5 before calling
        // here, so an error at this point is emission's own.
        if(diagnostics.HasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Compile the emitted sources using the in-process Roslyn driver.
        var binDir = options.BinDirectory;
        _ = Directory.CreateDirectory(binDir);

        var compileResult = CSharpCompilationDriver.Compile(
            outputDir,
            binDir,
            options.ProjectName,
            diagnostics,
            emittedFileCount: emittedFiles.Count,
            mainTypeName: entryPoint?.FullyQualifiedName,
            outputType: options.OutputType);

        if(!compileResult.Succeeded)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        return new CompilerResult(true, compileResult.PrimaryArtifactPath, diagnostics.ToImmutableArray());
    }

    private void EmitTopLevelType(MemberType type, string outputDir, List<string> emittedFiles)
    {
        var writer = new CSharpWriter();
        writer.WriteLine("using System;");

        // Emit a using for the project namespace so TLS code can reference types
        // declared in the same project.
        if(!string.IsNullOrEmpty(type.NamespaceName))
        {
            writer.WriteLine($"using {type.NamespaceName};");
        }

        writer.WriteBlankLine();

        // Emit the Main body's statements directly as top-level statements.
        var mainMethod = type.MethodsList.FirstOrDefault(m => m.Name == "Main");
        if(mainMethod?.Body is not null)
        {
            BodyEmitter.EmitBlock(mainMethod.Body, writer, diagnostics);
        }

        var namespacePath = type.NamespaceName.Replace('.', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(outputDir, namespacePath, "Program.cs");

        var dir = Path.GetDirectoryName(fullPath);
        if(dir is not null)
        {
            _ = Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, writer.ToString());
        emittedFiles.Add(fullPath);
    }
}