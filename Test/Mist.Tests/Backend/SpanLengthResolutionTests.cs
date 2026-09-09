// Tier 3 (Component): a dependently-typed Standard type answers its length from the type parameter
// alone, built over the REAL Standard sources. No Roslyn and no process spawning.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// The length of a span and of an array is its dependent TYPE PARAMETER, and nothing else on the type
/// may answer to that name. A second declaration -- a property Standard spells, or one a projection
/// adds -- makes every read of it MIST_PM_AMBIGUOUS_MEMBER, which is a whole feature area refused
/// rather than a member degraded. Built over the real Standard sources, so re-adding one fails here.
/// </summary>
public sealed class SpanLengthResolutionTests
{
    [Theory]
    [InlineData("Span<int> s = new Span<int>(numbers); int n = s.Length;")]
    [InlineData("ReadOnlySpan<int> s = new ReadOnlySpan<int>(numbers); int n = s.Length;")]
    [InlineData("int n = numbers.Length;")]
    public void LengthResolves(string statement)
    {
        Assert.DoesNotContain(
            DiagnosticsFor(statement),
            diagnostic => diagnostic.Id.Value == "MIST_PM_AMBIGUOUS_MEMBER");
    }

    [Theory]
    [InlineData("Span<int> s = new Span<int>(numbers); int n = s.Length;")]
    [InlineData("int n = numbers.Length;")]
    public void LengthIsTheTypeParameter(string statement)
    {
        Assert.DoesNotContain(
            DiagnosticsFor(statement),
            diagnostic => diagnostic.Id.Value == "MIST_PM_UNRESOLVED_MEMBER");
    }

    private static ImmutableArray<Diagnostic> DiagnosticsFor(string statement)
    {
        _ = StandardModelProbe.Build($"int[] numbers = [1, 2, 3];\n{statement}", out var diagnostics);
        return diagnostics;
    }
}