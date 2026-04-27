using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Backend that translates a semantic model into C# source code and invokes
/// the .NET compiler to produce an executable assembly.
/// </summary>
public sealed class CSharpTranspilerBackend : IBackend
{
    public BackendId Id => new("csharp");

    public IAmbientTypeResolver? GetAmbientTypeResolver() => new CSharpAmbientTypeResolver();

    public BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, DiagnosticBag diagnostics)
    {
        var bag = new DiagnosticBag();

        try
        {
            // Step 1: Emit C# source files
            CSharpEmitter.Emit(model, outputDirectory, bag);

            if(bag.HasErrors)
            {
                return new BackendResult(
                    Succeeded: false,
                    PrimaryArtifactPath: null,
                    Diagnostics: bag.ToImmutableArray());
            }

            // Step 2: Compile the emitted C# project
            var compileResult = CSharpCompilationDriver.Compile(outputDirectory, binDirectory, bag);

            return compileResult;
        }
        catch(Exception ex)
        {
            bag.Add(new Diagnostic(
                new DiagnosticId("MIST_BACKEND_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                $"C# transpiler backend failed: {ex.Message}",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));

            return new BackendResult(
                Succeeded: false,
                PrimaryArtifactPath: null,
                Diagnostics: bag.ToImmutableArray());
        }
    }
}
