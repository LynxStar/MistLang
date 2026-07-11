using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using ProgramModelInstance = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Driver;

/// <summary>
/// The experimental (v2) compilation pipeline:
/// Stage 4: Build ProgramModel from ASTs
/// Stage 5: Run new analyzers against the ProgramModel
/// Stage 6: Emit via new emitter consuming the ProgramModel
/// </summary>
public sealed class ExperimentalPipeline : ICompilationPipeline
{
    public CompilerResult Execute(PipelineInput input)
    {
        var diagnostics = input.Diagnostics;

        // Stage 4: Build Program Model
        var programModel = BuildProgramModel(input, diagnostics);
        if(programModel is null)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 5: Run Analyzers
        var hasErrors = RunAnalyzers(programModel, input, diagnostics);
        if(hasErrors)
        {
            return new CompilerResult(false, null, diagnostics.ToImmutableArray());
        }

        // Stage 6: Emit
        return Emit(programModel, input, diagnostics);
    }

    /// <summary>
    /// Stage 4 – Builds the <see cref="IProgramModel"/> from the parsed ASTs.
    /// Creates the <see cref="AmbientTypeProviderV2"/> using a factory so that the
    /// provider receives the ProgramModel after Phase 1 has registered source types.
    /// The provider is disposed when construction completes.
    /// Returns null on fatal construction failure.
    /// </summary>
    private static IProgramModel? BuildProgramModel(PipelineInput input, DiagnosticBag diagnostics)
    {
        var assemblyPaths = ResolveSdkAndAdditionalPaths(input, diagnostics);

        return ProgramModelBuilder.Build(
            input.Compilation,
            input.Asts,
            input.Options,
            diagnostics,
            ambientProviderFactory: (programModel, diag) =>
                AmbientTypeProviderV2.Create(
                    assemblyPaths,
                    (ProgramModelInstance)programModel,
                    diag));
    }

    /// <summary>
    /// Resolves the full set of assembly paths needed by the ambient provider:
    /// SDK reference assemblies (System.Runtime, etc.) merged with any NuGet/framework
    /// assembly paths already resolved by the driver.
    /// </summary>
    private static ImmutableArray<string> ResolveSdkAndAdditionalPaths(PipelineInput input, DiagnosticBag diagnostics)
    {
        var sdkRefPath = SdkDiscovery.FindReferenceAssemblyPath(
            input.Options.TargetFramework, diagnostics);

        if(sdkRefPath is null)
        {
            // SDK not found — use only the resolved package/framework paths.
            return input.ResolvedAssemblyPaths;
        }

        var sdkPaths = Directory.GetFiles(sdkRefPath, "*.dll");
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(var path in sdkPaths)
        {
            _ = merged.Add(path);
        }

        if(!input.ResolvedAssemblyPaths.IsDefaultOrEmpty)
        {
            foreach(var path in input.ResolvedAssemblyPaths)
            {
                _ = merged.Add(path);
            }
        }

        return merged.ToImmutableArray();
    }

    /// <summary>
    /// Stage 5 – Runs the new analyzer suite against the <see cref="IProgramModel"/>.
    /// Returns true if any analyzer produced an error.
    /// </summary>
    private static bool RunAnalyzers(IProgramModel programModel, PipelineInput input, DiagnosticBag diagnostics)
    {
        // TODO: Run new analyzers that consume IProgramModel
        _ = programModel;
        _ = input;
        _ = diagnostics;
        return false;
    }

    /// <summary>
    /// Stage 6 – Emits output via the new emitter consuming the <see cref="IProgramModel"/>.
    /// </summary>
    private static CompilerResult Emit(IProgramModel programModel, PipelineInput input, DiagnosticBag diagnostics)
    {
        // TODO: Invoke new emitter that consumes IProgramModel
        _ = programModel;
        _ = input;
        _ = diagnostics;

        return new CompilerResult(false, null, diagnostics.ToImmutableArray());
    }
}