// Feature: node-origin-tracking, Property 5: SyntheticOrigin Chain Resolves to Terminal ParsedOrigin
// **Validates: Requirements 4.2, 7.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for SyntheticOrigin chain resolution.
///
/// Property 5: SyntheticOrigin Chain Resolves to Terminal ParsedOrigin.
/// For any chain of SyntheticOrigin records (depth 1 to N) whose terminal
/// DerivedFrom is a ParsedOrigin, DiagnosticHelper.ResolveLocation SHALL
/// return the path and location from that terminal ParsedOrigin's SourceText.
/// </summary>
public class SyntheticChainResolutionPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Input record for the property test: a synthetic chain with a terminal ParsedOrigin.
    /// </summary>
#pragma warning disable CA1034, CA1819 // CA1034: nested type, CA1819: array property in test input record
    public record SyntheticChainInput(
        Mist.Common.Source.SourceText TerminalSource,
        int SpanStart,
        int SpanLength,
        int ChainDepth,
        string[] Reasons)
    {
        public override string ToString()
        {
            return $"SyntheticChainInput(Path=\"{TerminalSource.Path}\", ChainDepth={ChainDepth}, SpanStart={SpanStart}, SpanLength={SpanLength})";
        }
    }
#pragma warning restore CA1034, CA1819

    /// <summary>
    /// Generates non-empty source contents with newlines for multi-line coverage.
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
            "single line content",
            "a\nb\nc\nd\ne\nf\ng\nh");
    }

    /// <summary>
    /// Generates a non-empty reason string for SyntheticOrigin.
    /// </summary>
    private static Gen<string> GenReason()
    {
        return Gen.Elements(
            "desugaring",
            "macro expansion",
            "for-in lowering",
            "implicit return",
            "default constructor",
            "property accessor",
            "operator overload",
            "type erasure",
            "lambda lifting",
            "closure capture");
    }

    /// <summary>
    /// Generates a SyntheticChainInput with a random chain depth (1-10),
    /// random intermediate reasons, and a random terminal ParsedOrigin
    /// with a valid span within content bounds.
    /// </summary>
    private static Gen<SyntheticChainInput> GenSyntheticChainInput()
    {
        return from path in Gen.Elements("file.mist", "src/main.mist", "lib/utils.mist", "test.mist", "a/b/c.mist")
               from contents in GenNonEmptyContents()
               from chainDepth in Gen.Choose(1, 10)
               from reasons in Gen.ArrayOf(GenReason(), chainDepth)
               from start in Gen.Choose(0, contents.Length - 1)
               from length in Gen.Choose(1, contents.Length - start)
               select new SyntheticChainInput(
                   new Mist.Common.Source.SourceText(path, contents),
                   start,
                   length,
                   chainDepth,
                   reasons);
    }

    public static Arbitrary<SyntheticChainInput> ArbSyntheticChainInput()
    {
        return GenSyntheticChainInput().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a chain of SyntheticOrigin records with the given reasons,
    /// terminating at the provided ParsedOrigin.
    /// </summary>
    private static NodeOrigin BuildChain(ParsedOrigin terminal, string[] reasons)
    {
        // Start from the terminal and wrap outward
        NodeOrigin current = terminal;
        for(var i = reasons.Length - 1; i >= 0; i--)
        {
            current = new SyntheticOrigin(current, reasons[i]);
        }

        return current;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any chain of SyntheticOrigin records whose terminal DerivedFrom is a ParsedOrigin,
    /// DiagnosticHelper.ResolveLocation SHALL return the path from that terminal ParsedOrigin's SourceText.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyntheticChainResolutionPropertyTests) })]
    public bool ResolveLocation_SyntheticChain_ReturnsTerminalParsedOriginPath(SyntheticChainInput input)
    {
        var terminal = new ParsedOrigin(input.TerminalSource);
        var outerOrigin = BuildChain(terminal, input.Reasons);

        var node = new BlockNode
        {
            Origin = outerOrigin,
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        return DiagnosticHelper.ResolveLocation(node).Location.Path == input.TerminalSource.Path;
    }

    /// <summary>
    /// For any chain of SyntheticOrigin records whose terminal DerivedFrom is a ParsedOrigin,
    /// DiagnosticHelper.ResolveLocation SHALL return a LinePositionSpan equal to
    /// the terminal ParsedOrigin's SourceText.ToLinePositionSpan(node.Span).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyntheticChainResolutionPropertyTests) })]
    public bool ResolveLocation_SyntheticChain_ReturnsCorrectLinePositionSpan(SyntheticChainInput input)
    {
        var terminal = new ParsedOrigin(input.TerminalSource);
        var outerOrigin = BuildChain(terminal, input.Reasons);

        var node = new BlockNode
        {
            Origin = outerOrigin,
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var location = DiagnosticHelper.ResolveLocation(node).Location.Position();

        var expectedSpan = new SourceSpan(input.SpanStart, input.SpanLength);
        var expectedLocation = input.TerminalSource.ToLinePositionSpan(expectedSpan);

        return location.Start.Line == expectedLocation.Start.Line
            && location.Start.Column == expectedLocation.Start.Column
            && location.End.Line == expectedLocation.End.Line
            && location.End.Column == expectedLocation.End.Column;
    }

    /// <summary>
    /// For any chain of SyntheticOrigin records (depth >= 1),
    /// DiagnosticHelper.ResolveLocation SHALL return one synthesis frame per link, outermost first.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyntheticChainResolutionPropertyTests) })]
    public bool ResolveLocation_SyntheticChain_HasNonNullSyntheticContext(SyntheticChainInput input)
    {
        var terminal = new ParsedOrigin(input.TerminalSource);
        var outerOrigin = BuildChain(terminal, input.Reasons);

        var node = new BlockNode
        {
            Origin = outerOrigin,
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var frames = DiagnosticHelper.ResolveLocation(node).Frames;

        return frames.Length == input.Reasons.Length && frames.Select(frame => ((SynthesisFrame)frame).Reason).SequenceEqual(input.Reasons);
    }
}