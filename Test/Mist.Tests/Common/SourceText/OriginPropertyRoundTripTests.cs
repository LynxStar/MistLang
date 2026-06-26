// Feature: node-origin-tracking, Property 2: Origin Property Round-Trip
// **Validates: Requirements 2.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Text;
using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for Origin property round-trip on SyntaxNode.
///
/// Property 2: Origin Property Round-Trip.
/// For any SyntaxNode and any NodeOrigin variant (ParsedOrigin, SyntheticOrigin, or TestOrigin),
/// setting the Origin property and then reading it back SHALL return a value equal to what was set.
/// </summary>
public class OriginPropertyRoundTripTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates random SourceText instances with arbitrary path and contents.
    /// </summary>
    private static Gen<Mist.Common.Text.SourceText> GenSourceText()
    {
        return from path in Gen.Elements("file.mist", "src/main.mist", "lib/utils.mist", "test.mist", "a.mist")
               from contents in Gen.Elements("", "hello", "class Foo {}", "let x = 1\nlet y = 2", "fn main() {\n  print(42)\n}")
               select new Mist.Common.Text.SourceText(path, contents);
    }

    /// <summary>
    /// Generates non-empty reason strings for SyntheticOrigin.
    /// </summary>
    private static Gen<string> GenNonEmptyReason()
    {
        return Gen.Elements(
            "desugaring",
            "macro expansion",
            "for-in lowering",
            "implicit conversion",
            "default value insertion");
    }

    /// <summary>
    /// Generates random NodeOrigin variants (ParsedOrigin, SyntheticOrigin, or TestOrigin).
    /// </summary>
    private static Gen<NodeOrigin> GenNodeOrigin()
    {
        var genParsed = GenSourceText().Select(s => (NodeOrigin)new ParsedOrigin(s));
        var genTest = Gen.Constant((NodeOrigin)TestOrigin.Instance);
        var genSynthetic =
            from origin in genParsed
            from reason in GenNonEmptyReason()
            select (NodeOrigin)new SyntheticOrigin(origin, reason);

        return Gen.OneOf(genParsed, genTest, genSynthetic);
    }

    /// <summary>
    /// Generates concrete SyntaxNode subclass instances.
    /// Uses several simple node types from the generated AST.
    /// </summary>
    private static Gen<SyntaxNode> GenSyntaxNode()
    {
        return Gen.OneOf(
            Gen.Constant<SyntaxNode>(new BlockNode()),
            Gen.Constant<SyntaxNode>(new IntTypePrimitiveTypeNode()),
            Gen.Constant<SyntaxNode>(new CompilationUnitNode()));
    }

    public static Arbitrary<NodeOrigin> ArbNodeOrigin()
    {
        return GenNodeOrigin().ToArbitrary();
    }

    public static Arbitrary<SyntaxNode> ArbSyntaxNode()
    {
        return GenSyntaxNode().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any SyntaxNode and any NodeOrigin variant, setting the Origin property
    /// and then reading it back SHALL return a value equal to what was set.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OriginPropertyRoundTripTests) })]
    public bool Origin_SetThenGet_ReturnsEqualValue(SyntaxNode node, NodeOrigin origin)
    {
        node.Origin = origin;
        return node.Origin == origin;
    }

    /// <summary>
    /// For any SyntaxNode and any NodeOrigin variant, setting the Origin property
    /// and then reading it back SHALL return a reference-equal value (same instance).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OriginPropertyRoundTripTests) })]
    public bool Origin_SetThenGet_ReturnsSameReference(SyntaxNode node, NodeOrigin origin)
    {
        node.Origin = origin;
        return ReferenceEquals(node.Origin, origin);
    }
}