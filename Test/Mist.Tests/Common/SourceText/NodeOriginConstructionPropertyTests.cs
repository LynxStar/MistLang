using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;

namespace Mist.Tests.Common.SourceText;

public class NodeOriginConstructionPropertyTests
{
    private static Gen<Mist.Common.Source.SourceText> GenSourceText()
    {
        return from path in Gen.Elements("file.mist", "src/main.mist", "lib/utils.mist", "test.mist", "a.mist")
               from contents in Gen.Elements("", "hello", "class Foo {}", "let x = 1\nlet y = 2", "fn main() {\n  print(42)\n}")
               select new Mist.Common.Source.SourceText(path, contents);
    }

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

    public static Arbitrary<Mist.Common.Source.SourceText> ArbSourceText()
    {
        return GenSourceText().ToArbitrary();
    }

    public static Arbitrary<NodeOrigin> ArbNodeOrigin()
    {
        return GenNodeOrigin().ToArbitrary();
    }

#pragma warning disable CA1034
    public record SyntheticOriginInput(NodeOrigin Origin, string Reason)
    {
        public override string ToString()
        {
            return $"SyntheticOriginInput(Origin={Origin}, Reason=\"{Reason}\")";
        }
    }
#pragma warning restore CA1034

    public static Arbitrary<SyntheticOriginInput> ArbSyntheticOriginInput()
    {
        return (from origin in GenNodeOrigin()
                from reason in GenNonEmptyReason()
                select new SyntheticOriginInput(origin, reason))
        .ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NodeOriginConstructionPropertyTests) })]
    public bool ParsedOrigin_Source_IsReferenceEqual(Mist.Common.Source.SourceText sourceText)
    {
        var origin = new ParsedOrigin(sourceText);
        return ReferenceEquals(origin.Source, sourceText);
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NodeOriginConstructionPropertyTests) })]
    public bool SyntheticOrigin_Fields_ArePreserved(SyntheticOriginInput input)
    {
        var synthetic = new SyntheticOrigin(input.Origin, input.Reason);
        return ReferenceEquals(synthetic.DerivedFrom, input.Origin)
            && synthetic.Reason == input.Reason;
    }

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