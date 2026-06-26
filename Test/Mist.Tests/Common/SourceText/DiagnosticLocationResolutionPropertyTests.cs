// Feature: node-origin-tracking, Property 4: ParsedOrigin Resolves to Correct Location
// **Validates: Requirements 4.1**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for ParsedOrigin location resolution.
///
/// Property 4: ParsedOrigin Resolves to Correct Location.
/// For any node with a ParsedOrigin and a non-empty Span,
/// DiagnosticHelper.ResolveLocation SHALL return a path equal to
/// ParsedOrigin.Source.Path and a LinePositionSpan equal to
/// ParsedOrigin.Source.ToLinePositionSpan(node.Span).
/// </summary>
public class DiagnosticLocationResolutionPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Input record pairing a SourceText with a valid span within its content bounds.
    /// </summary>
#pragma warning disable CA1034
    public record LocationInput(Mist.Common.Source.SourceText Source, int SpanStart, int SpanLength)
    {
        public override string ToString()
        {
            return $"LocationInput(Path=\"{Source.Path}\", ContentsLength={Source.Contents.Length}, SpanStart={SpanStart}, SpanLength={SpanLength})";
        }
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates non-empty source contents (at least 1 character) with newlines for multi-line coverage.
    /// </summary>
    private static Gen<string> GenNonEmptyContents()
    {
        return Gen.Elements(
            "a",
            "hello world",
            "line1\nline2\nline3",
            "class Foo {\n  let x = 1\n  let y = 2\n}",
            "fn main() {\n  print(42)\n}",
            "ab\r\ncd\r\nef",
            "single",
            "a\nb\nc\nd\ne\nf\ng\nh");
    }

    /// <summary>
    /// Generates a LocationInput with a valid span within the content bounds.
    /// Ensures SpanStart + SpanLength &lt;= Contents.Length.
    /// </summary>
    private static Gen<LocationInput> GenLocationInput()
    {
        return from path in Gen.Elements("file.mist", "src/main.mist", "lib/utils.mist", "test.mist", "a/b/c.mist")
               from contents in GenNonEmptyContents()
               from start in Gen.Choose(0, contents.Length - 1)
               from length in Gen.Choose(1, contents.Length - start)
               select new LocationInput(new Mist.Common.Source.SourceText(path, contents), start, length);
    }

    public static Arbitrary<LocationInput> ArbLocationInput()
    {
        return GenLocationInput().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any node with a ParsedOrigin and a valid Span within content bounds,
    /// DiagnosticHelper.ResolveLocation SHALL return a path equal to
    /// ParsedOrigin.Source.Path.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ReturnsCorrectPath(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var (path, _, _) = DiagnosticHelper.ResolveLocation(node);

        return path == input.Source.Path;
    }

    /// <summary>
    /// For any node with a ParsedOrigin and a valid Span within content bounds,
    /// DiagnosticHelper.ResolveLocation SHALL return a LinePositionSpan equal to
    /// source.ToLinePositionSpan(span).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ReturnsCorrectLinePositionSpan(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var (_, location, _) = DiagnosticHelper.ResolveLocation(node);

        var expectedSpan = new SourceSpan(input.SpanStart, input.SpanLength);
        var expectedLocation = input.Source.ToLinePositionSpan(expectedSpan);

        return location.Start.Line == expectedLocation.Start.Line
            && location.Start.Column == expectedLocation.Start.Column
            && location.End.Line == expectedLocation.End.Line
            && location.End.Column == expectedLocation.End.Column;
    }

    /// <summary>
    /// For any node with a ParsedOrigin and a valid Span within content bounds,
    /// DiagnosticHelper.ResolveLocation SHALL return null for SyntheticContext.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ParsedOrigin_HasNullSyntheticContext(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var (_, _, syntheticContext) = DiagnosticHelper.ResolveLocation(node);

        return syntheticContext is null;
    }
}