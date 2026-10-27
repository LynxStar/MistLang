using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
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
    private static ImmutableArray<CompilationUnit> cachedUnits;
    private static readonly object CacheLock = new();

    /// <summary>
    /// Loads every stdlib <c>.mist</c> file colocated with the compiler binary
    /// and returns them as <see cref="CompilationUnit"/>s. I/O failures are
    /// reported as <see cref="CommonDiagnostics.StdlibParse"/> errors on
    /// <paramref name="diagnostics"/> and do not throw.
    /// Results are cached after the first successful load.
    /// </summary>
    /// <param name="diagnostics">Diagnostic bag to receive any load errors.</param>
    public static ImmutableArray<CompilationUnit> LoadFor(DiagnosticBag diagnostics)
    {
        // Fast path: return cached units if available.
        var cached = cachedUnits;
        if(!cached.IsDefault)
        {
            return cached;
        }

        lock(CacheLock)
        {
            // Double-check after acquiring lock.
            if(!cachedUnits.IsDefault)
            {
                return cachedUnits;
            }

            var stdlibRoot = Path.Combine(AppContext.BaseDirectory, "Standard");
            var units = LoadFrom(stdlibRoot, diagnostics);

            // An empty listing without an error is what a Standard directory looks like before a
            // build has copied into it, and caching it would leave the process without a stdlib.
            if(!diagnostics.HasErrors && !units.IsEmpty)
            {
                cachedUnits = units;
            }

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
            diagnostics.ReportTyped(CommonDiagnostics.StdlibParse, new FileOnly(stdlibRoot),
                new PathValue(stdlibRoot), new TextValue(ex.Message));
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
                diagnostics.ReportTyped(CommonDiagnostics.StdlibParse, new FileOnly(path),
                new PathValue(path), new TextValue(ex.Message));
                continue;
            }

            var sourceText = new SourceText(path, contents);
            units.Add(new CompilationUnit(sourceText, "Standard", stdlibRoot)
            {
                IsStdlib = true,
            });
        }

        return units.ToImmutable();
    }
}