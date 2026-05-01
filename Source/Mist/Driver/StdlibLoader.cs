using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Loads the Standard Library source files that ship alongside the compiler binary.
/// Implements Approach B (source prelude) per design § *Stdlib Loading Strategy*.
/// Files are located at <c>&lt;AppContext.BaseDirectory&gt;/Standard/**/*.mist</c>
/// (Requirement 10.9) and returned as <see cref="CompilationUnit"/>s with
/// <see cref="CompilationUnit.IsStdlib"/> set to <c>true</c>.
/// </summary>
public static class StdlibLoader
{
    /// <summary>
    /// Loads every stdlib <c>.mist</c> file colocated with the compiler binary
    /// and returns them as <see cref="CompilationUnit"/>s. I/O failures are
    /// reported as <see cref="DiagnosticId.StdlibParse"/> errors on
    /// <paramref name="diagnostics"/> and do not throw.
    /// </summary>
    /// <param name="backend">
    /// The resolved backend. Reserved for future backend-specific stdlib
    /// variants; currently ignored.
    /// </param>
    /// <param name="diagnostics">Diagnostic bag to receive any load errors.</param>
    public static ImmutableArray<CompilationUnit> LoadFor(IBackend backend, DiagnosticBag diagnostics)
    {
        _ = backend; // reserved for future backend-specific stdlib selection
        var stdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
        return LoadFrom(stdlibRoot, diagnostics);
    }

    /// <summary>
    /// Test hook: load stdlib sources from an arbitrary directory. Production
    /// callers go through <see cref="LoadFor"/>, which anchors the root at
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    internal static ImmutableArray<CompilationUnit> LoadFrom(string stdlibRoot, DiagnosticBag diagnostics)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(stdlibRoot, "*.mist", SearchOption.AllDirectories);
        }
        catch(Exception ex)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.StdlibParse,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Failed to load Standard Library source: {stdlibRoot}: {ex.Message}",
                Location: null,
                CompilationUnitPath: stdlibRoot,
                AnalyzerName: null));
            return ImmutableArray<CompilationUnit>.Empty;
        }

        // Stable ordering for reproducible diagnostics and test assertions.
        Array.Sort(files, StringComparer.Ordinal);

        var units = ImmutableArray.CreateBuilder<CompilationUnit>(files.Length);
        foreach(var path in files)
        {
            string contents;
            try
            {
                contents = File.ReadAllText(path);
            }
            catch(Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.StdlibParse,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Failed to load Standard Library source: {path}: {ex.Message}",
                    Location: null,
                    CompilationUnitPath: path,
                    AnalyzerName: null));
                continue;
            }

            var sourceText = new SourceText(path, contents);
            units.Add(new CompilationUnit(sourceText) { IsStdlib = true });
        }

        return units.ToImmutable();
    }
}
