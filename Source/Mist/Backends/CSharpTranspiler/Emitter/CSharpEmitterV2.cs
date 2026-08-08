using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// V2 C# emitter that consumes an <see cref="IProgramModel"/> and produces one
/// compilable C# source file per Mist source file, then compiles them into a DLL
/// using <see cref="CSharpCompilationDriver"/>.
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
    /// Emits all compilation units to disk and compiles the result.
    /// <para>
    /// One C# file per Mist file, which is what preserves file scope: a file's using
    /// directives reach its own types and nothing else, exactly as they do in the source.
    /// Emitting per TYPE instead would either lose the usings or leak one file's imports
    /// into another's.
    /// </para>
    /// <para>
    /// Every assembly is emitted into a single CLR assembly, so a composed program contains
    /// one <c>Main</c> per project and Roslyn reports CS0017 unless told which to use. The
    /// compiled project's own entry point, recorded during member resolution, is what
    /// disambiguates. Once emission splits per assembly this becomes unnecessary; see
    /// <c>backlog/multi-project-emission.md</c>.
    /// </para>
    /// </summary>
    public CompilerResult Emit()
    {
        var isTopLevelEntry = options.EntryPoint == "top-level";

        // A top-level-statement entry point is C#'s to find: the statements compile into a
        // synthesized type whose name is not ours to predict, so naming one would be wrong.
        var entryPoint = isTopLevelEntry ? null : programModel.SelfAssembly?.EntryPoint;

        // An executable with no entry point cannot be produced. Reported here as a Mist
        // error rather than left to Roslyn, because it is the emitter that cannot do its
        // job and the Mist-level cause is clearer than the C# symptom. Duplicate entry
        // points are caught earlier, during member resolution.
        if(entryPoint is null && !isTopLevelEntry && options.OutputType == ProjectOutputType.Executable)
        {
            diagnostics.Report(
                BackendDiagnostics.MissingEntryPoint, default, options.ProjectName, (object?)options.ProjectName);
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        var outputDir = options.OutputDirectory;
        _ = Directory.CreateDirectory(outputDir);

        var emittedFiles = new List<string>();
        var closures = ConstraintClosureMap.Build((ProgramModel.Model.ProgramModel)programModel, diagnostics);

        EmitConstructorConstraintInterfaces(outputDir, emittedFiles);
        EmitStructInitMarker(outputDir, emittedFiles);

        // The standard library is provided by the runtime, so its declarations are resolved
        // against but never emitted.
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                EmitCompilationUnit(
                    unit, assembly, isTopLevelEntry, closures, outputDir, emittedFiles);
            }
        }

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

    /// <summary>
    /// Emits a single compilation unit as one C# source file.
    /// </summary>
    private void EmitCompilationUnit(
        CompilationUnit unit,
        IAssembly assembly,
        bool isTopLevelEntry,
        ConstraintClosureMap closures,
        string outputDir,
        List<string> emittedFiles)
    {
        // An intrinsic's implementation is supplied by the backend, so a declaration for it
        // would collide with the real one.
        var emittableTypes = unit.Types
            .Where(t => t is not ConcreteType { IsIntrinsic: true })
            .ToList();

        // A file that declares nothing emittable would produce a .cs holding only usings,
        // which Roslyn accepts but which adds a file to the compilation for no reason.
        if(emittableTypes.Count == 0 && !unit.HasTopLevelStatements)
        {
            return;
        }

        var writer = new CSharpWriter();

        EmitUsings(unit, writer);

        if(unit.HasTopLevelStatements && isTopLevelEntry)
        {
            EmitTopLevelStatements(unit, writer);
        }

        if(emittableTypes.Count > 0)
        {
            if(unit.HasTopLevelStatements && isTopLevelEntry)
            {
                writer.WriteBlankLine();
            }

            EmitFileTypes(emittableTypes, unit.HasTopLevelStatements && isTopLevelEntry, closures, writer);
        }

        // Determine output path: {outputDir}/{Assembly.Name}/{RelativePath}/{FileName}.cs
        var fileName = DeriveOutputFileName(unit);
        var pathSegments = new List<string> { outputDir, assembly.Name };
        if(!string.IsNullOrEmpty(unit.RelativePath))
        {
            pathSegments.Add(unit.RelativePath);
        }

        pathSegments.Add(fileName);
        var fullPath = Path.Combine([.. pathSegments]);

        var dir = Path.GetDirectoryName(fullPath);
        if(dir is not null)
        {
            _ = Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, writer.ToString());
        emittedFiles.Add(fullPath);
    }

    private static void EmitStructInitMarker(string outputDir, List<string> emittedFiles)
    {
        var writer = new CSharpWriter();
        writer.WriteLine($"namespace {ConstructorConstraintInterfaces.Namespace};");
        writer.WriteBlankLine();
        writer.WriteLine($"public readonly struct {StructAncestorFactory.MarkerTypeName}");
        using(writer.Block())
        {
        }

        writer.WriteBlankLine();
        writer.WriteLine($"public readonly struct {DefaultSynthesis.MarkerTypeName}");
        using(writer.Block())
        {
        }

        var path = Path.Combine(outputDir, "__StructInit.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    /// <summary>
    /// One interface per distinct parameterized constructor constraint signature, in its own file
    /// because the declaration that constrains and the type that closes may sit in any namespace and
    /// each emitted file carries only its own unit's usings.
    /// <para>
    /// Collected from the CONSTRAINTS rather than from what closes them, because a <c>where</c> clause
    /// names the interface whether or not the generic is ever instantiated.
    /// </para>
    /// </summary>
    private void EmitConstructorConstraintInterfaces(string outputDir, List<string> emittedFiles)
    {
        var signatures = new Dictionary<string, IReadOnlyList<IMistType>>(StringComparer.Ordinal);

        foreach(var type in EmittableTypes())
        {
            foreach(var parameter in type.TypeParametersList)
            {
                Collect(parameter, signatures);
            }

            foreach(var method in type.MethodsList)
            {
                foreach(var parameter in method.TypeParametersList)
                {
                    Collect(parameter, signatures);
                }
            }
        }

        if(signatures.Count == 0)
        {
            return;
        }

        var writer = new CSharpWriter();
        writer.WriteLine($"namespace {ConstructorConstraintInterfaces.Namespace};");
        writer.WriteBlankLine();

        var first = true;
        foreach(var (name, signature) in signatures.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;
            writer.WriteLine($"public interface {name}<T>");
            using(writer.Block())
            {
                var parameters = ConstructorConstraintInterfaces.Parameters(signature);
                writer.WriteLine(
                    $"static abstract T {ConstructorConstraintInterfaces.MemberName}({parameters});");
            }
        }

        var path = Path.Combine(outputDir, "__ConstructorConstraints.g.cs");
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }

    private static void Collect(TypeParameter parameter, Dictionary<string, IReadOnlyList<IMistType>> signatures)
    {
        foreach(var signature in ConstraintClosureRegistry.Parameterized(parameter))
        {
            signatures[ConstructorConstraintInterfaces.InterfaceName(signature)] = signature;
        }
    }

    private IEnumerable<MemberType> EmittableTypes()
    {
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource || assembly.Kind == MistAssemblyKind.Standard)
            {
                continue;
            }

            foreach(var unit in assembly.CompilationUnits)
            {
                foreach(var type in unit.Types)
                {
                    yield return type;
                }
            }
        }
    }

    private static void EmitUsings(CompilationUnit unit, CSharpWriter writer)
    {
        // Unconditional because TypeNameEmitter projects the Standard namespace onto C#
        // keywords and System types, so a file can reach System without naming it.
        var usings = new HashSet<string>(StringComparer.Ordinal) { "System" };

        foreach(var ns in unit.Usings)
        {
            // Standard is Mist's stdlib namespace and has no C# counterpart to import --
            // its types arrive through the System projection above.
            if(ns != "Standard")
            {
                _ = usings.Add(ns);
            }
        }

        foreach(var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            writer.WriteLine($"using {ns};");
        }

        writer.WriteBlankLine();
    }

    private void EmitTopLevelStatements(CompilationUnit unit, CSharpWriter writer)
    {
        var fileNamespace = unit.Types.FirstOrDefault()?.NamespaceName ?? "";
        if(!string.IsNullOrEmpty(fileNamespace))
        {
            writer.WriteLine($"using {fileNamespace};");
            writer.WriteBlankLine();
        }

        if(unit.TopLevelMethod?.Body is not null)
        {
            BodyEmitter.EmitBlock(unit.TopLevelMethod.Body, writer, diagnostics);
        }
    }

    private void EmitFileTypes(
        List<MemberType> types, bool hasTopLevelStatements, ConstraintClosureMap closures, CSharpWriter writer)
    {
        var fileNamespace = types[0].NamespaceName;

        if(hasTopLevelStatements)
        {
            // C# requires a file-scoped namespace to precede every other member, which the
            // top-level statements already emitted violate. A block namespace has no such rule.
            writer.WriteLine($"namespace {fileNamespace}");
            using(writer.Block())
            {
                var first = true;
                foreach(var type in types)
                {
                    if(!first)
                    {
                        writer.WriteBlankLine();
                    }

                    first = false;
                    TypeEmitter.EmitTypeBody(type, closures, writer, diagnostics);
                }
            }
        }
        else
        {
            writer.WriteLine($"namespace {fileNamespace};");
            writer.WriteBlankLine();

            var first = true;
            foreach(var type in types)
            {
                if(!first)
                {
                    writer.WriteBlankLine();
                }

                first = false;
                TypeEmitter.EmitTypeBody(type, closures, writer, diagnostics);
            }
        }
    }

    private static string DeriveOutputFileName(CompilationUnit unit)
    {
        return $"{Path.GetFileNameWithoutExtension(unit.SourcePath)}.cs";
    }
}