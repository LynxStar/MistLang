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

    public BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, string projectName, DiagnosticBag diagnostics)
    {
        var bag = new DiagnosticBag();

        try
        {
            // Step 1: Emit C# source files
            CSharpEmitter.Emit(model, outputDirectory, projectName, bag);

            if(bag.HasErrors)
            {
                // Requirement 5.10: if MIST_BE_0002 (or any backend error)
                // was raised during emission, produce no output artifact.
                // Clean up any partially-emitted files.
                CleanOutputDirectory(outputDirectory);

                return new BackendResult(
                    Succeeded: false,
                    PrimaryArtifactPath: null,
                    Diagnostics: bag.ToImmutableArray());
            }

            // Step 2: Compile the emitted C# project
            var compileResult = CSharpCompilationDriver.Compile(outputDirectory, binDirectory, projectName, bag);

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

    /// <summary>
    /// Removes all emitted files from the output directory to ensure no
    /// output artifact is produced when the backend encounters errors
    /// (Requirement 5.10).
    /// </summary>
    private static void CleanOutputDirectory(string outputDirectory)
    {
        try
        {
            if (Directory.Exists(outputDirectory))
            {
                foreach (var file in Directory.GetFiles(outputDirectory))
                    File.Delete(file);
            }
        }
        catch
        {
            // Best-effort cleanup; the compilation already failed.
        }
    }
}
