using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// V2 C# emitter that consumes an <see cref="IProgramModel"/> and produces one
/// compilable C# source file per Mist source file, then compiles them into a DLL
/// using <see cref="CSharpCompilationDriver"/>.
/// </summary>
internal sealed partial class CSharpEmitterV2
{
    private readonly IProgramModel programModel;

    private IReadOnlyList<(MistType Definition, IReadOnlyList<int> Widths)> monomorphs = [];
    private VariadicUseRegistry variadicUses = new();
    private readonly CompilerOptions options;
    private readonly ImmutableArray<string> referencePaths;
    private readonly DiagnosticBag diagnostics;
    private readonly Func<string, Type?> clrType;
    private readonly ImmutableArray<ExternalAssembly> externalAssemblies;
    private readonly ImmutableArray<ComposedProject> composedProjects;
    private readonly ImmutableArray<string> runtimeIdentifiers;
    private readonly ImmutableArray<RuntimeAsset> runtimeAssets;
    private readonly string? frameworkAssetsDirectory;
    private readonly List<string> emittedFiles = [];
    private readonly Dictionary<string, DeclarationOrigin> unitOrigins = [];

    public CSharpEmitterV2(
        IProgramModel programModel,
        CompilerOptions options,
        ImmutableArray<string> referencePaths,
        DiagnosticBag diagnostics,
        Func<string, Type?> clrType,
        ImmutableArray<ExternalAssembly> externalAssemblies = default,
        ImmutableArray<ComposedProject> composedProjects = default,
        ImmutableArray<string> runtimeIdentifiers = default,
        ImmutableArray<RuntimeAsset> runtimeAssets = default,
        string? frameworkAssetsDirectory = null)
    {
        this.runtimeIdentifiers = runtimeIdentifiers;
        this.runtimeAssets = runtimeAssets;
        this.frameworkAssetsDirectory = frameworkAssetsDirectory;
        this.programModel = programModel;
        this.options = options;
        this.referencePaths = referencePaths;
        this.diagnostics = diagnostics;
        this.clrType = clrType;
        this.externalAssemblies = externalAssemblies;
        this.composedProjects = composedProjects;
    }

    public int EmittedFileCount => emittedFiles.Count;

    public int RuntimeAssetsDeployed { get; private set; }

    /// <summary>
    /// Emits all compilation units to disk and compiles the result.
    /// <para>
    /// One C# file per Mist file, which is what preserves file scope: a file's using
    /// directives reach its own types and nothing else, exactly as they do in the source.
    /// Emitting per TYPE instead would either lose the usings or leak one file's imports
    /// into another's.
    /// </para>
    /// </summary>
    public CompilerResult Emit()
    {
        diagnostics.Stage = PipelineStage.Backend;
        if(Transpile() is not { } startup)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Compile the emitted sources using the in-process Roslyn driver.
        var binDir = options.BinDirectory;
        _ = Directory.CreateDirectory(binDir);

        var compileResult = CSharpCompilationDriver.Compile(
            options.OutputDirectory,
            binDir,
            options.ProjectName,
            diagnostics,
            emittedFileCount: emittedFiles.Count,
            mainTypeName: startup.MainTypeName,
            additionalReferencePaths: referencePaths,
            outputType: options.OutputType,
            externalAssemblies: externalAssemblies,
            composedProjects: composedProjects,
            declarationOrigins: FacadeTypeSelection.Origins(emittedFiles, unitOrigins),
            frameworkReferences: options.FrameworkReferences,
            runtimeIdentifiers: runtimeIdentifiers,
            runtimeAssets: runtimeAssets,
            projectFolder: options.ProjectFolder,
            frameworkAssetsDirectory: frameworkAssetsDirectory);

        if(!compileResult.Succeeded)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray(), compileResult.Native);
        }

        RuntimeAssetsDeployed = compileResult.RuntimeAssetsDeployed;
        return new CompilerResult(true, compileResult.PrimaryArtifactPath, diagnostics.ToImmutableArray(), compileResult.Native);
    }

    /// <summary>
    /// Everything short of compiling the C#: the sources and the project file on disk, and what the
    /// program starts at, or null when a Mist error means there is nothing worth compiling.
    /// </summary>
    internal Startup? Transpile()
    {
        using var emission = EmissionDiagnostics.Enter(diagnostics);
        if(Startup.Of(programModel, options) is not { } startup)
        {
            return null;
        }

        var carriesEntryPoint = programModel.Assemblies.Any(assembly => assembly.EntryPoints.Count > 0);

        var outputDir = options.OutputDirectory;
        _ = Directory.CreateDirectory(outputDir);

        ContextLowering.Reset();
        LifetimeLowering.Reset();
        ArrayLowering.Reset();
        ArrayEnumeration.Reset();
        TaskCombinators.Reset();
        InvariantCulture.Reset();
        BodyEmitter.ResetTemporaries();
        var closures = ConstraintClosureMap.Build((ProgramModel.Model.ProgramModel)programModel, diagnostics);
        using var promotion = AsyncPromotion.Enter((ProgramModel.Model.ProgramModel)programModel);
        using var supply = IntrinsicSupply.Enter((ProgramModel.Model.ProgramModel)programModel, clrType);
        using var specialization = UnionSpecialization.Enter((ProgramModel.Model.ProgramModel)programModel, diagnostics);
        using var ceiling = DelegateCeiling.Enter((ProgramModel.Model.ProgramModel)programModel, diagnostics);
        using var nullability = NativeSlotNullability.Enter((ProgramModel.Model.ProgramModel)programModel);

        (monomorphs, variadicUses) = MonomorphSet.Discover((ProgramModel.Model.ProgramModel)programModel);

        ReportForeignConformances();

        // A .g.cs file is generated code to MSBuild, compiled with nullable analysis off whatever the
        // project says, where an annotated reference is CS8669 and an unannotated one is oblivious.
        using(TypeNameEmitter.Oblivious())
        {
            EmitConstructorConstraintInterfaces(outputDir, emittedFiles);
            EmitStructInitMarker(outputDir, emittedFiles);
            EmitUnionCarriers(outputDir, emittedFiles);
            EmitDependentCarriers(outputDir, emittedFiles);
            EmitMonomorphs(outputDir, emittedFiles, closures);
            EmitVoidStandIn(outputDir, emittedFiles);
            IntrinsicMemberEmitter.EmitFile((ProgramModel.Model.ProgramModel)programModel, outputDir, emittedFiles, diagnostics);
        }

        var usings = UsingSelection.Of(programModel);

        // Standard's compilation units go through the same path as a user's, and what the target
        // already supplies is dropped one declaration at a time by TypeEmitter.ProducesDeclaration:
        // Bootstrap is intrinsic throughout and so emits nothing, IClonable is ordinary Mist and
        // emits as itself.
        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource)
            {
                continue;
            }

            var origin = FacadeTypeSelection.OriginOf(assembly);
            foreach(var unit in assembly.CompilationUnits)
            {
                var written = emittedFiles.Count;
                EmitCompilationUnit(
                    unit, assembly, ReferenceEquals(unit, startup.TopLevelUnit), closures, usings, outputDir, emittedFiles);
                if(emittedFiles.Count > written)
                {
                    unitOrigins[emittedFiles[^1]] = origin;
                }
            }
        }

        EmitContextRuntime(outputDir, emittedFiles);
        EmitOwnershipRuntime(outputDir, emittedFiles);
        EmitArrayRuntime(outputDir, emittedFiles);
        EmitTaskRuntime(outputDir, emittedFiles);
        EmitInvariantRuntime(outputDir, emittedFiles);
        LocaleStartup.EmitFile(options, outputDir, emittedFiles);

        var csprojContent = ProjectFileEmitter.Emit(options, carriesEntryPoint, startup.MainTypeName);
        var csprojPath = Path.Combine(outputDir, $"{options.ProjectName}.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        // Transpilation is done; compiling it is a separate step, and it must not run if
        // transpilation failed. Roslyn would answer with C# errors against generated files,
        // which describe symptoms in code the user never wrote and bury the Mist-level cause
        // that is already sitting in the bag. The pipeline gates Stages 4 and 5 before calling
        // here, so an error at this point is emission's own.
        return diagnostics.HasErrors ? null : startup;
    }

    /// <summary>
    /// A variadic family is not emitted where it was written: C# cannot express it once, so
    /// EmitMonomorphs writes one declaration per arity instead and this skips the original -- unless
    /// it is emitted once per instantiation, which closes its packs along with everything else and is
    /// written in its namespace like any other instance.
    /// </summary>
    internal static List<DeclaredType> WrittenDeclarations(CompilationUnit unit)
    {
        return [.. unit.Types
            .Where(TypeEmitter.ProducesDeclaration)
            .Where(type => Monomorph.PacksOf(type).Count == 0 || UnionSpecialization.Specializes(type))];
    }

    /// <summary>
    /// Per program rather than per emitted type: a foreign conformer is never emitted, so a check
    /// placed at its declaration would never run.
    /// </summary>
    private void ReportForeignConformances()
    {
        foreach(var conformance in ((ProgramModel.Model.ProgramModel)programModel).ConformancesList)
        {
            if(conformance.ConformingType.Assembly.HasMistSource)
            {
                continue;
            }

            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.ForeignConformance,
                conformance.DeclarationNode,
                null,
                DiagnosticSnapshot.Type(conformance.ConformingType),
                DiagnosticSnapshot.Type(conformance.AbstractType));
        }
    }

    internal static bool IsOpen(IMistType type)
    {
        return TypeParameterReach.Collect(type).Count > 0;
    }
}