using System.Collections.Immutable;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

[Trait("Tier", "1")]
public sealed class AccessModifierSyntaxTests
{
    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("internal")]
    public void EachAccessKeywordAloneIsOneAccessModifier(string keyword)
    {
        var modifier = Node(keyword);

        Assert.True(AccessModifierSyntax.IsAccessModifier(modifier));
        Assert.Equal(1, AccessModifierSyntax.CountAccessModifiers([modifier]));
    }

    [Theory]
    [InlineData("sealed")]
    [InlineData("static")]
    [InlineData("intrinsic")]
    [InlineData("readonly")]
    [InlineData("pure")]
    [InlineData("local")]
    [InlineData("stampable")]
    [InlineData("owned")]
    [InlineData("affine")]
    [InlineData("linear")]
    [InlineData("relevant")]
    [InlineData("async")]
    [InlineData("ref")]
    public void EachOtherModifierIsNoAccessModifier(string keyword)
    {
        var modifier = Node(keyword);

        Assert.False(AccessModifierSyntax.IsAccessModifier(modifier));
        Assert.Equal(0, AccessModifierSyntax.CountAccessModifiers([modifier]));
    }

    [Theory]
    [InlineData("protected internal", 2)]
    [InlineData("private protected", 2)]
    [InlineData("internal protected", 2)]
    [InlineData("public private", 2)]
    [InlineData("public public", 2)]
    [InlineData("public sealed public", 2)]
    [InlineData("public private protected internal", 4)]
    [InlineData("sealed public static readonly", 1)]
    [InlineData("static async sealed", 0)]
    public void EveryAccessKeywordInAListCountsOnce(string keywords, int expected)
    {
        var modifiers = keywords.Split(' ').Select(Node).ToImmutableArray();

        Assert.Equal(expected, AccessModifierSyntax.CountAccessModifiers(modifiers));
    }

    [Fact]
    public void AnEmptyListHasNoAccessModifier()
    {
        Assert.Equal(0, AccessModifierSyntax.CountAccessModifiers([]));
    }

    [Fact]
    public void AnAbsentListHasNoAccessModifier()
    {
        Assert.Equal(0, AccessModifierSyntax.CountAccessModifiers(default));
    }

    private static ModifierNode Node(string keyword)
    {
        return keyword switch
        {
            "public" => new PublicModifierModifierNode(),
            "private" => new PrivateModifierModifierNode(),
            "protected" => new ProtectedModifierModifierNode(),
            "internal" => new InternalModifierModifierNode(),
            "sealed" => new SealedModifierModifierNode(),
            "static" => new StaticModifierModifierNode(),
            "intrinsic" => new IntrinsicModifierModifierNode(),
            "readonly" => new ReadonlyModifierModifierNode(),
            "pure" => new PureModifierModifierNode(),
            "local" => new LocalModifierModifierNode(),
            "stampable" => new StampableModifierModifierNode(),
            "owned" => new OwnedModifierModifierNode(),
            "affine" => new AffineModifierModifierNode(),
            "linear" => new LinearModifierModifierNode(),
            "relevant" => new RelevantModifierModifierNode(),
            "async" => new AsyncModifierModifierNode(),
            "ref" => new RefModifierModifierNode(),
            _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword, null),
        };
    }
}