using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Driver;

/// <summary>
/// The project file's diagnostics as <c>mist inspect</c> and <c>mist restore</c> show them, before anything is
/// resolved and whether or not a configuration came back; an error stops the command as it stops a build.
/// </summary>
public static class ManifestFeedback
{
    public static bool Report(ImmutableArray<Diagnostic> diagnostics, TextWriter error)
    {
        foreach(var diagnostic in diagnostics)
        {
            error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }

        return diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}