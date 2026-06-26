// Feature: node-origin-tracking, Property 1: NodeOrigin Constructor Invariants
// **Validates: Requirements 1.2, 1.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Text;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for NodeOrigin constructor invariants.
///
/// Property 1: NodeOrigin Constructor Invariants.
/// For any valid SourceText instance, constructing a ParsedOrigin SHALL produce a record
/// whose Source property is the same instance. For any valid NodeOrigin and non-empty string,
/// constructing a SyntheticOrigin SHALL produce a record whose DerivedFrom is the same origin
/// and whose Reason is the same string.
/// </summary>
public class NodeOriginConstructionPropertyTests
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
            "default value insertion",
            "pattern compilation",
            "type erasure");
    }

    /// <summary>
    /// Generates random NodeOrigin variants (ParsedOrigin, SyntheticOrigin, TestOrigin).
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

    public static Arbitrary<Mist.Common.Text.SourceText> ArbSourceText() =>
        GenSourceText().ToArbitrary();

    public static Arbitrary<NodeOrigin> ArbNodeOrigin() =>
        GenNodeOrigin().ToArbitrary();

#pragma warning disable CA1034
    public record SyntheticOriginInput(NodeOrigin Origin, string Reason)
    {
        public override string ToString() => $"SyntheticOriginInput(Origin={Origin}, Reason=\"{Reason}\")";
    }
#pragma warning restore CA1034

    public static Arbitrary<SyntheticOriginInput> ArbSyntheticOriginInput() =>
        (from origin in GenNodeOrigin()
         from reason in GenNonEmptyReason()
         select new SyntheticOriginInput(origin, reason))
        .ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid SourceText instance, constructing a ParsedOrigin SHALL produce
    /// a record whose Source property is reference-equal to the original SourceText.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NodeOriginConstructionPropertyTests) })]
    public bool ParsedOrigin_Source_IsReferenceEqual(Mist.Common.Text.SourceText sourceText)
    {
        var origin = new ParsedOrigin(sourceText);
        return ReferenceEquals(origin.Source, sourceText);
    }

    /// <summary>
    /// For any valid NodeOrigin and non-empty string, constructing a SyntheticOrigin
    /// SHALL produce a record whose DerivedFrom is the same origin and whose Reason
    /// is the same string.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NodeOriginConstructionPropertyTests) })]
    public bool SyntheticOrigin_Fields_ArePreserved(SyntheticOriginInput input)
    {
        var synthetic = new SyntheticOrigin(input.Origin, input.Reason);
        return ReferenceEquals(synthetic.DerivedFrom, input.Origin)
            && synthetic.Reason == input.Reason;
    }

    /// <summary>
    /// SyntheticOrigin SHALL throw ArgumentException when constructed with an empty reason.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NodeOriginConstructionPropertyTests) })]
    public bool SyntheticOrigin_EmptyReason_ThrowsArgumentException(NodeOrigin origin)
    {
        try
        {
            _ = new SyntheticOrigin(origin, "");
            return false; // Should have thrown
        }
        catch(ArgumentException)
        {
            return true;
        }
    }
}