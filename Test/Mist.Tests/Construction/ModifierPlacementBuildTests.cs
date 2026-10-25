using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ModifierPlacementBuildTests
{
    [Theory]
    [InlineData("enum")]
    [InlineData("alias")]
    [InlineData("newtype")]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("abstract")]
    public void AsyncOnATypeDeclarationIsReportedOnceAtIt(string declaration)
    {
        var diagnostics = new DiagnosticBag();

        _ = Build(declaration, diagnostics, new AsyncModifierModifierNode());

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_ASYNC_NOT_ALLOWED", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void RefOnAnOperatorIsReportedOnceAtIt()
    {
        var diagnostics = new DiagnosticBag();

        _ = Build("operator", diagnostics, new RefModifierModifierNode());

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_REF_ON_OPERATOR", diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("sealed", "MIST_PM_SEALED_ON_OPERATOR")]
    [InlineData("readonly", "MIST_PM_READONLY_ON_OPERATOR")]
    [InlineData("owned", "MIST_PM_OWNED_ON_OPERATOR")]
    [InlineData("affine", "MIST_PM_AFFINE_ON_OPERATOR")]
    public void AnUnsupportedOperatorModifierIsReportedOnceAtItsDeclaration(string modifier, string expected)
    {
        var diagnostics = new DiagnosticBag();
        var written = modifier switch
        {
            "sealed" => (ModifierNode)new SealedModifierModifierNode(),
            "readonly" => new ReadonlyModifierModifierNode(),
            "owned" => new OwnedModifierModifierNode(),
            _ => new AffineModifierModifierNode(),
        };

        _ = Build("operator", diagnostics, written);

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
    }

    [Fact]
    public void ReadonlyRefOperatorRetainsItsSingleRefReturnRefusal()
    {
        var diagnostics = new DiagnosticBag();

        _ = Build("operator", diagnostics, new ReadonlyModifierModifierNode(), new RefModifierModifierNode());

        Assert.Equal("MIST_PM_REF_ON_OPERATOR", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
    }

    [Fact]
    public void StandardIntrinsicPureOperatorRetainsItsCallableMetadata()
    {
        var diagnostics = new DiagnosticBag();

        var modifiers = Build("operator", diagnostics, new IntrinsicModifierModifierNode(), new PureModifierModifierNode());

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.True(modifiers.IsIntrinsic);
        Assert.True(modifiers.IsStatic);
        Assert.True(modifiers.IsPure);
        Assert.True(modifiers.IsReadonly);
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
    [InlineData("method", "async")]
    [InlineData("method", "ref")]
    [InlineData("operator", "pure")]
    public void ASpecifiedPlacementBuildsWithoutADiagnostic(string declaration, string modifier)
    {
        var diagnostics = new DiagnosticBag();

        _ = Build(declaration, diagnostics, modifier switch
        {
            "readonly" => new ReadonlyModifierModifierNode(),
            "owned" => new OwnedModifierModifierNode(),
            "affine" => new AffineModifierModifierNode(),
            "async" => new AsyncModifierModifierNode(),
            "ref" => new RefModifierModifierNode(),
            _ => new PureModifierModifierNode(),
        });

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void PureAndAsyncOnAnEnumAreEachReportedOnce()
    {
        var diagnostics = new DiagnosticBag();

        _ = Build("enum", diagnostics, new PureModifierModifierNode(), new AsyncModifierModifierNode());

        Assert.Equal(
            ["MIST_PM_PURITY_MODIFIER_PLACEMENT", "MIST_PM_ASYNC_NOT_ALLOWED"],
            diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value));
    }

    private static MemberModifiers Build(string declaration, DiagnosticBag diagnostics, params ModifierNode[] modifiers)
    {
        switch(declaration)
        {
            case "enum":
                var enumeration = BindingFragments.At<EnumDeclarationNode>();
                enumeration.Modifiers = [.. modifiers];
                return ModifierBuilder.Build(enumeration, diagnostics, declaringType: null);

            case "alias":
                var alias = BindingFragments.At<TypeAliasDeclarationNode>();
                alias.Modifiers = [.. modifiers];
                return ModifierBuilder.Build(alias, diagnostics, declaringType: null);

            case "newtype":
                var newtype = BindingFragments.At<NewtypeDeclarationNode>();
                newtype.Modifiers = [.. modifiers];
                return ModifierBuilder.Build(newtype, diagnostics, declaringType: null);

            case "class":
            case "struct":
            case "abstract":
                var body = BindingFragments.At<TypeBodyDeclarationNode>();
                body.Keyword = declaration switch
                {
                    "struct" => new StructKeywordTypeKeywordNode(),
                    "abstract" => new AbstractKeywordTypeKeywordNode(),
                    _ => new ClassKeywordTypeKeywordNode(),
                };
                body.Modifiers = [.. modifiers];
                return ModifierBuilder.Build(body, diagnostics, declaringType: null);

            case "method":
                var method = BindingFragments.At<MethodDeclarationNode>();
                method.Modifiers = [.. modifiers];
                return ModifierBuilder.ForMember(method, diagnostics, HandBuiltModel.Struct("Vec"));

            case "operator":
                var plus = BindingFragments.At<OperatorDeclarationNode>();
                plus.Modifiers = [new PublicModifierModifierNode(), new StaticModifierModifierNode(), .. modifiers];
                plus.Symbol = new PlusOpOperatorSymbolNode();
                return ModifierBuilder.ForMember(plus, diagnostics, HandBuiltModel.Struct("Vec"));

            default:
                throw new ArgumentException("Unknown declaration", nameof(declaration));
        }
    }
}