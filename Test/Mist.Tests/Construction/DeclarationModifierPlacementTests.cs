using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class DeclarationModifierPlacementTests
{
    [Theory]
    [InlineData("enum")]
    [InlineData("alias")]
    [InlineData("newtype")]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("abstract")]
    public void AsyncOnATypeDeclarationIsRefused(string declaration)
    {
        var refusal = Assert.Single(DeclarationModifierPlacement.Refusals(Declaration(declaration), [Modifier("async")]));

        Assert.Equal("MIST_PM_ASYNC_NOT_ALLOWED", refusal.Id.Value);
    }

    [Theory]
    [InlineData("public static ref")]
    [InlineData("public static readonly ref")]
    public void RefOnAnOperatorIsRefused(string written)
    {
        var refusal = Assert.Single(DeclarationModifierPlacement.Refusals(
            Declaration("operator"), [.. written.Split(' ').Select(Modifier)]));

        Assert.Equal("MIST_PM_REF_ON_OPERATOR", refusal.Id.Value);
    }

    [Theory]
    [InlineData("public ref")]
    [InlineData("public readonly ref")]
    [InlineData("public ref readonly")]
    [InlineData("public static ref")]
    [InlineData("private static readonly ref")]
    public void RefOnAFieldIsRefusedOnce(string written)
    {
        var refusal = Assert.Single(DeclarationModifierPlacement.Refusals(
            Declaration("field"), [.. written.Split(' ').Select(Modifier)]));

        Assert.Equal("MIST_PM_REF_ON_FIELD", refusal.Id.Value);
    }

    [Fact]
    public void AModifierWrittenTwiceIsRefusedOnce()
    {
        _ = Assert.Single(DeclarationModifierPlacement.Refusals(Declaration("class"), [Modifier("async"), Modifier("async")]));
    }

    [Fact]
    public void ARefParameterOnAnOperatorIsNotRefused()
    {
        var plus = (OperatorDeclarationNode)Declaration("operator");
        plus.Parameters = new ParameterListNode
        {
            Parameter = new ParameterNode { RefModifier = BindingFragments.Token("ref"), Type = BindingFragments.TypeName("Vec"), Name = BindingFragments.Token("left") },
            Parameters = [new ParameterNode { Type = BindingFragments.TypeName("Vec"), Name = BindingFragments.Token("right") }],
        };

        Assert.Empty(DeclarationModifierPlacement.Refusals(plus, [Modifier("public"), Modifier("static")]));
    }

    [Theory]
    [InlineData("class", "readonly")]
    [InlineData("struct", "readonly")]
    [InlineData("abstract", "readonly")]
    [InlineData("enum", "readonly")]
    [InlineData("newtype", "readonly")]
    [InlineData("class", "owned")]
    [InlineData("struct", "owned")]
    [InlineData("class", "affine")]
    [InlineData("struct", "affine")]
    [InlineData("struct", "ref")]
    [InlineData("method", "async")]
    [InlineData("local-function", "async")]
    [InlineData("method", "ref")]
    [InlineData("field", "readonly")]
    [InlineData("field", "static")]
    [InlineData("operator", "pure")]
    [InlineData("operator", "intrinsic")]
    public void ASpecifiedPlacementIsAdmitted(string declaration, string modifier)
    {
        Assert.Empty(DeclarationModifierPlacement.Refusals(Declaration(declaration), [Modifier(modifier)]));
    }

    [Theory]
    [InlineData("operator", "async")]
    [InlineData("cast-operator", "async")]
    [InlineData("constructor", "async")]
    [InlineData("field", "async")]
    [InlineData("property", "async")]
    [InlineData("indexer", "async")]
    [InlineData("enum", "pure")]
    [InlineData("class", "ref")]
    [InlineData("abstract", "owned")]
    [InlineData("alias", "sealed")]
    public void APlacementAnotherRuleRefusesIsLeftToThatRule(string declaration, string modifier)
    {
        Assert.Empty(DeclarationModifierPlacement.Refusals(Declaration(declaration), [Modifier(modifier)]));
    }

    private static SyntaxNode Declaration(string declaration)
    {
        return declaration switch
        {
            "enum" => new EnumDeclarationNode(),
            "alias" => new TypeAliasDeclarationNode(),
            "newtype" => new NewtypeDeclarationNode(),
            "class" => new TypeBodyDeclarationNode { Keyword = new ClassKeywordTypeKeywordNode() },
            "struct" => new TypeBodyDeclarationNode { Keyword = new StructKeywordTypeKeywordNode() },
            "abstract" => new TypeBodyDeclarationNode { Keyword = new AbstractKeywordTypeKeywordNode() },
            "method" => new MethodDeclarationNode(),
            "local-function" => new LocalFunctionDeclarationNode(),
            "operator" => new OperatorDeclarationNode { Symbol = new PlusOpOperatorSymbolNode() },
            "cast-operator" => new CastOperatorDeclarationNode(),
            "constructor" => new ConstructorDeclarationNode(),
            "field" => new FieldDeclarationNode(),
            "property" => new BlockPropertyPropertyDeclarationNode(),
            "indexer" => new IndexerDeclarationNode(),
            _ => throw new ArgumentException("Unknown declaration", nameof(declaration)),
        };
    }

    private static ModifierNode Modifier(string modifier)
    {
        return modifier switch
        {
            "public" => new PublicModifierModifierNode(),
            "private" => new PrivateModifierModifierNode(),
            "static" => new StaticModifierModifierNode(),
            "async" => new AsyncModifierModifierNode(),
            "readonly" => new ReadonlyModifierModifierNode(),
            "owned" => new OwnedModifierModifierNode(),
            "affine" => new AffineModifierModifierNode(),
            "ref" => new RefModifierModifierNode(),
            "pure" => new PureModifierModifierNode(),
            "intrinsic" => new IntrinsicModifierModifierNode(),
            "sealed" => new SealedModifierModifierNode(),
            _ => throw new ArgumentException("Unknown modifier", nameof(modifier)),
        };
    }
}