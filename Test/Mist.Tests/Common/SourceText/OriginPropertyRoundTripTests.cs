using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;
using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Common.SourceText;

public class OriginPropertyRoundTripTests
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
            "default value insertion");
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OriginPropertyRoundTripTests) })]
    public bool Origin_SetThenGet_ReturnsEqualValue(SyntaxNode node, NodeOrigin origin)
    {
        node.Origin = origin;
        return node.Origin == origin;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OriginPropertyRoundTripTests) })]
    public bool Origin_SetThenGet_ReturnsSameReference(SyntaxNode node, NodeOrigin origin)
    {
        node.Origin = origin;
        return ReferenceEquals(node.Origin, origin);
    }
}