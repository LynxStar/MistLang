using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Tests;

/// <summary>
/// The derived line and column of a diagnostic that a test knows points somewhere; a test about an
/// absent location pattern-matches <see cref="Diagnostic.Location"/> instead.
/// </summary>
internal static class DiagnosticLocationExtensions
{
    public static LinePositionSpan Position(this Diagnostic diagnostic)
    {
        return diagnostic.Location.Position();
    }

    public static LinePositionSpan Position(this Location location)
    {
        return location.Lines ?? throw new Xunit.Sdk.XunitException($"the location is {location}, which has no line and column");
    }
}