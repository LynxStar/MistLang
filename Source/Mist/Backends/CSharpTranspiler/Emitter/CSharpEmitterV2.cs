using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

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

    public CSharpEmitterV2(IProgramModel programModel, CompilerOptions options, DiagnosticBag diagnostics)
    {
        this.programModel = programModel;
        this.options = options;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Emits all types to disk and compiles the result.
    /// Returns a <see cref="CompilerResult"/>.
    /// </summary>
    public CompilerResult Emit()
    {
        var outputDir = options.OutputDirectory;
        _ = Directory.CreateDirectory(outputDir);

        var emittedFiles = new List<string>();

        // Emit each source type as a separate .cs file.
        var sourceTypes = programModel
            .TypesByFQN
            .Values
            .Where(t => t.Origin != TypeOrigin.Ambient)
            .OfType<ConcreteType>()
            .Where(t => !t.IsIntrinsic)
            ;

        foreach(var memberType in sourceTypes)
        {
            var relativePath = TypeEmitter.GetRelativePath(memberType);
            var fullPath = Path.Combine(outputDir, relativePath);

            var dir = Path.GetDirectoryName(fullPath);
            if(dir is not null)
            {
                _ = Directory.CreateDirectory(dir);
            }

            var source = TypeEmitter.Emit(memberType);
            File.WriteAllText(fullPath, source);
            emittedFiles.Add(fullPath);
        }

        // Emit the .csproj file.
        var csprojContent = ProjectFileEmitter.Emit(options);
        var csprojPath = Path.Combine(outputDir, $"{options.ProjectName}.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        // Compile the emitted sources using the in-process Roslyn driver.
        var binDir = options.BinDirectory;
        _ = Directory.CreateDirectory(binDir);

        var compileResult = CSharpCompilationDriver.Compile(
            outputDir,
            binDir,
            options.ProjectName,
            diagnostics,
            emittedFileCount: emittedFiles.Count,
            outputType: options.OutputType);

        if(!compileResult.Succeeded)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        return new CompilerResult(true, compileResult.PrimaryArtifactPath, diagnostics.ToImmutableArray());
    }
}