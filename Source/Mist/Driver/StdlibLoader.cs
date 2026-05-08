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
    // ── Static cache ─────────────────────────────────────────────────────
    // The stdlib files never change during a process lifetime (they ship
    // alongside the compiler binary). Caching the loaded CompilationUnits
    // avoids re-reading 19 files from disk on every CompilerDriver.Compile()
    // call — a significant win for property-based tests that compile
    // hundreds of programs in a single test run.
    private static ImmutableArray<CompilationUnit>? _cachedUnits;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Loads every stdlib <c>.mist</c> file colocated with the compiler binary
    /// and returns them as <see cref="CompilationUnit"/>s. I/O failures are
    /// reported as <see cref="DiagnosticId.StdlibParse"/> errors on
    /// <paramref name="diagnostics"/> and do not throw.
    /// Results are cached after the first successful load.
    /// </summary>
    /// <param name="backend">
    /// The resolved backend. Reserved for future backend-specific stdlib
    /// variants; currently ignored.
    /// </param>
    /// <param name="diagnostics">Diagnostic bag to receive any load errors.</param>
    public static ImmutableArray<CompilationUnit> LoadFor(IBackend backend, DiagnosticBag diagnostics)
    {
        _ = backend; // reserved for future backend-specific stdlib selection

        // Fast path: return cached units if available.
        if (_cachedUnits.HasValue)
            return _cachedUnits.Value;

        lock (_cacheLock)
        {
            // Double-check after acquiring lock.
            if (_cachedUnits.HasValue)
                return _cachedUnits.Value;

            var stdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
            var units = LoadFrom(stdlibRoot, diagnostics);

            // Only cache if loading succeeded (no errors).
            if (!diagnostics.HasErrors)
                _cachedUnits = units;

            return units;
        }
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
                Location: default,
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
                    Location: default,
                    CompilationUnitPath: path,
                    AnalyzerName: null));
                continue;
            }

            var sourceText = new SourceText(path, contents);
            units.Add(new CompilationUnit(sourceText)
            {
                IsStdlib = true,
                NamespaceRoot = "Standard",
                ProjectFolder = stdlibRoot,
            });
        }

        return units.ToImmutable();
    }
}
