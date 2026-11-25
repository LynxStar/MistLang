using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class IntrinsicStorageMemberConstructionTests
{
    private const int MemberOffset = 40;
    private const int AccessorOffset = 60;

    private const string NotIntrinsicType = "Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type 'Holder' is not intrinsic.";
    private const string NotPublic = "The 'intrinsic' modifier requires the member to be declared 'public'.";
    private const string HasBody = "Intrinsic member 'Count' must not have a body; use ';' to mark it as backend-provided.";

    private readonly BindingFragments f = new();

    [Theory]
    [InlineData("field", "private")]
    [InlineData("field", "internal")]
    [InlineData("field", "unwritten")]
    [InlineData("block-property", "protected")]
    [InlineData("block-property", "unwritten")]
    public void ANonPublicIntrinsicMemberOfAnIntrinsicTypeIsRefused(string kind, string accessibility)
    {
        _ = Build(intrinsicHost: true, Member(kind, intrinsic: true, accessibility));

        var diagnostic = AssertRefusedOnce("MIST0329", MemberOffset);
        Assert.Equal(NotPublic, diagnostic.Message);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("block-property")]
    public void APublicIntrinsicMemberOfAnOrdinaryTypeIsRefused(string kind)
    {
        _ = Build(intrinsicHost: false, Member(kind, intrinsic: true, "public"));

        var diagnostic = AssertRefusedOnce("MIST0328", MemberOffset);
        Assert.Equal(NotIntrinsicType, diagnostic.Message);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("block-property")]
    public void ANonPublicIntrinsicMemberOfAnOrdinaryTypeIsRefusedForBoth(string kind)
    {
        _ = Build(intrinsicHost: false, Member(kind, intrinsic: true, "private"));

        AssertRefusals([("MIST0328", NotIntrinsicType), ("MIST0329", NotPublic)]);
    }

    [Fact]
    public void APublicIntrinsicExpressionPropertyOfAnIntrinsicTypeIsRefusedForItsBody()
    {
        var holder = Build(intrinsicHost: true, Member("expression-property", intrinsic: true, "public"));

        var diagnostic = AssertRefusedOnce("MIST0326", MemberOffset);
        Assert.Equal(HasBody, diagnostic.Message);
        Assert.True(Assert.Single(holder.PropertiesList).Modifiers.IsIntrinsic);
    }

    [Theory]
    [InlineData(true, "private", "MIST0329")]
    [InlineData(false, "public", "MIST0328")]
    [InlineData(false, "private", "MIST0328", "MIST0329")]
    public void AnIntrinsicExpressionPropertyIsRefusedForItsPlacementAndItsBody(bool intrinsicHost, string accessibility, params string[] placement)
    {
        _ = Build(intrinsicHost, Member("expression-property", intrinsic: true, accessibility));

        AssertRefusals([.. placement.Select(number => (number, number == "MIST0328" ? NotIntrinsicType : NotPublic)), ("MIST0326", HasBody)]);
    }

    [Theory]
    [InlineData("block-get")]
    [InlineData("expression-get")]
    [InlineData("block-set")]
    public void ABodyOnAnIntrinsicBlockPropertysAccessorIsRefusedAtTheAccessor(string accessor)
    {
        var property = BlockProperty(intrinsic: true, "public", Accessor(accessor));

        _ = Build(intrinsicHost: true, new PropertyMemberMemberDeclarationNode { Property = property });

        var diagnostic = AssertRefusedOnce("MIST0326", AccessorOffset);
        Assert.Equal(HasBody, diagnostic.Message);
    }

    [Theory]
    [InlineData("field", true, true, "public")]
    [InlineData("block-property", true, true, "public")]
    [InlineData("field", false, false, "private")]
    [InlineData("block-property", true, false, "private")]
    [InlineData("expression-property", false, false, "private")]
    public void AnIntrinsicMemberDeclaredWhereTheRulesAllowAndAnOrdinaryMemberAreAdmitted(string kind, bool intrinsicHost, bool intrinsic, string accessibility)
    {
        var holder = Build(intrinsicHost, Member(kind, intrinsic, accessibility));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var modifiers = kind == "field" ? Assert.Single(holder.FieldsList).Modifiers : Assert.Single(holder.PropertiesList).Modifiers;
        Assert.Equal(intrinsic, modifiers.IsIntrinsic);
    }

    private Diagnostic AssertRefusedOnce(string number, int offset)
    {
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(number, diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(offset + 1, diagnostic.Position().Start.Column);
        return diagnostic;
    }

    private void AssertRefusals((string Number, string Message)[] expected)
    {
        var refusals = f.Diagnostics.ToImmutableArray();
        Assert.Equal(expected, refusals.Select(diagnostic => (diagnostic.Number.Value, diagnostic.Message)));
        Assert.All(refusals, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(MemberOffset + 1, diagnostic.Position().Start.Column);
        });
    }

    private ClassType Build(bool intrinsicHost, MemberDeclarationNode member)
    {
        var body = BindingFragments.At<TypeBodyDeclarationNode>(0);
        body.Modifiers = intrinsicHost ? [new PublicModifierModifierNode(), new IntrinsicModifierModifierNode()] : [new PublicModifierModifierNode()];
        body.Name = BindingFragments.Token("Holder");
        body.Keyword = new ClassKeywordTypeKeywordNode();
        body.Members = [member];

        // A Standard host, so an intrinsic one is admitted and only the member's own rules report.
        var holder = new ClassType("Holder", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body })
        {
            Assembly = new MistAssembly(MistAssembly.StandardName, MistAssemblyKind.Standard),
        };
        new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(holder);
        return holder;
    }

    private static MemberDeclarationNode Member(string kind, bool intrinsic, string accessibility)
    {
        switch(kind)
        {
            case "field":
                var field = BindingFragments.At<FieldDeclarationNode>(MemberOffset);
                field.Modifiers = Modifiers(intrinsic, accessibility);
                field.Type = BindingFragments.TypeName("Int32");
                field.Name = BindingFragments.Token("count");
                return new FieldMemberMemberDeclarationNode { Field = field };

            case "block-property":
                return new PropertyMemberMemberDeclarationNode
                {
                    Property = BlockProperty(intrinsic, accessibility, BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(AccessorOffset)),
                };

            default:
                var expression = BindingFragments.At<ExpressionBodiedPropertyPropertyDeclarationNode>(MemberOffset);
                expression.Modifiers = Modifiers(intrinsic, accessibility);
                expression.Type = BindingFragments.TypeName("Int32");
                expression.Name = BindingFragments.Token("Count");
                expression.Body = BindingFragments.Expression(BindingFragments.Identifier("count"));
                return new PropertyMemberMemberDeclarationNode { Property = expression };
        }
    }

    private static BlockPropertyPropertyDeclarationNode BlockProperty(bool intrinsic, string accessibility, AccessorDeclarationNode accessor)
    {
        var property = BindingFragments.At<BlockPropertyPropertyDeclarationNode>(MemberOffset);
        property.Modifiers = Modifiers(intrinsic, accessibility);
        property.Type = BindingFragments.TypeName("Int32");
        property.Name = BindingFragments.Token("Count");
        property.Accessors = [accessor];
        return property;
    }

    private static AccessorDeclarationNode Accessor(string form)
    {
        switch(form)
        {
            case "block-get":
                var blockGet = BindingFragments.At<BlockGetAccessorAccessorDeclarationNode>(AccessorOffset);
                blockGet.Body = new BlockNode();
                return blockGet;

            case "expression-get":
                var expressionGet = BindingFragments.At<ExpressionGetAccessorAccessorDeclarationNode>(AccessorOffset);
                expressionGet.Expression = BindingFragments.Expression(BindingFragments.Identifier("count"));
                return expressionGet;

            default:
                var blockSet = BindingFragments.At<BlockSetAccessorAccessorDeclarationNode>(AccessorOffset);
                blockSet.Body = new BlockNode();
                return blockSet;
        }
    }

    private static ImmutableArray<ModifierNode> Modifiers(bool intrinsic, string accessibility)
    {
        ModifierNode? access = accessibility switch
        {
            "public" => new PublicModifierModifierNode(),
            "private" => new PrivateModifierModifierNode(),
            "protected" => new ProtectedModifierModifierNode(),
            "internal" => new InternalModifierModifierNode(),
            _ => null,
        };

        return [.. new[] { access, intrinsic ? new IntrinsicModifierModifierNode() : null }.OfType<ModifierNode>()];
    }
}