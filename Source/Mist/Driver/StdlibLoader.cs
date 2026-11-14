using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.Driver;

/// <summary>
/// Approach B (source prelude) per design § *Stdlib Loading Strategy*: the sources ship beside the compiler
/// binary (Requirement 10.9).
/// </summary>
public static class StdlibLoader
{
    // The stdlib cannot change during a process, and property-based tests compile hundreds of programs in one.
    private static ImmutableArray<CompilationUnit> cachedUnits;
    private static readonly object CacheLock = new();

    public static ImmutableArray<CompilationUnit> LoadFor(DiagnosticBag diagnostics)
    {
        var cached = cachedUnits;
        if(!cached.IsDefault)
        {
            return cached;
        }

        lock(CacheLock)
        {
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

    /// <summary>Test hook; production callers go through <see cref="LoadFor"/>.</summary>
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