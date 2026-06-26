// Feature: node-origin-tracking, Property 7: TestOrigin Diagnostic Creation Never Throws
// **Validates: Requirements 6.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Storm.Runtime;
using SourceSpan = Storm.Runtime.SourceSpan;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for TestOrigin diagnostic creation safety.
///
/// Property 7: TestOrigin Diagnostic Creation Never Throws.
/// For any SyntaxNode with TestOrigin and any SourceSpan value (including zero-length
/// and large offsets), calling DiagnosticHelper.ResolveLocation SHALL return a valid result
/// without throwing an exception, using "&lt;unknown&gt;" as the path and default as the location.
/// </summary>
public class TestOriginSafetyPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates random SourceSpan values including edge cases:
    /// zero-length spans, large offsets, and max int values.
    /// </summary>
    private static Gen<SourceSpan> GenSourceSpan()
    {
        var genNormal = from start in Gen.Choose(0, 10000)
                        from length in Gen.Choose(0, 10000)
                        select new SourceSpan(start, length);

        var genZeroLength = from start in Gen.Choose(0, int.MaxValue / 2)
                            select new SourceSpan(start, 0);

        var genLargeOffset = from start in Gen.Choose(100000, int.MaxValue / 2)
                             from length in Gen.Choose(0, 1000)
                             select new SourceSpan(start, length);

        var genMaxInt = Gen.Elements(
            new SourceSpan(int.MaxValue, 0),
            new SourceSpan(int.MaxValue, 1),
            new SourceSpan(0, int.MaxValue),
            new SourceSpan(int.MaxValue, int.MaxValue),
            new SourceSpan(int.MaxValue / 2, int.MaxValue / 2));

        return Gen.OneOf(genNormal, genZeroLength, genLargeOffset, genMaxInt);
    }

    /// <summary>
    /// Generates concrete SyntaxNode subclass instances.
    /// </summary>
    private static Gen<SyntaxNode> GenSyntaxNode()
    {
        return Gen.OneOf(
            Gen.Constant<SyntaxNode>(new BlockNode()),
            Gen.Constant<SyntaxNode>(new IntTypePrimitiveTypeNode()),
            Gen.Constant<SyntaxNode>(new CompilationUnitNode()));
    }

    public static Arbitrary<SourceSpan> ArbSourceSpan()
    {
        return GenSourceSpan().ToArbitrary();
    }

    public static Arbitrary<SyntaxNode> ArbSyntaxNode()
    {
        return GenSyntaxNode().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any SyntaxNode with TestOrigin and any SourceSpan value,
    /// DiagnosticHelper.ResolveLocation SHALL return without throwing.
    /// The result SHALL have path "&lt;unknown&gt;", default location, and null context.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TestOriginSafetyPropertyTests) })]
    public bool TestOrigin_ResolveLocation_NeverThrows_ReturnsUnknown(SyntaxNode node, SourceSpan span)
    {
        node.Origin = TestOrigin.Instance;
        node.Span = span;

        var (path, location, syntheticContext) = DiagnosticHelper.ResolveLocation(node);

        return path == "<unknown>"
            && location == default
            && syntheticContext is null;
    }

    /// <summary>
    /// For any SyntaxNode with null Origin and any SourceSpan value,
    /// DiagnosticHelper.ResolveLocation SHALL return without throwing.
    /// Null origin is treated the same as TestOrigin.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TestOriginSafetyPropertyTests) })]
    public bool NullOrigin_ResolveLocation_NeverThrows_ReturnsUnknown(SyntaxNode node, SourceSpan span)
    {
        node.Origin = null;
        node.Span = span;

        var (path, location, syntheticContext) = DiagnosticHelper.ResolveLocation(node);

        return path == "<unknown>"
            && location == default
            && syntheticContext is null;
    }
}