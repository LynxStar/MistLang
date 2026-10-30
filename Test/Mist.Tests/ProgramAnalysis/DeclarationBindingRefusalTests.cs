using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: direct declaration builders consume hand-built syntax and models with stub ambient types.
[Trait("Tier", "2")]
public sealed class DeclarationBindingRefusalTests
{
    [Theory]
    [InlineData("duplicate-type-name", "MIST0295")]
    [InlineData("intrinsic-reserved-for-standard", "MIST0327")]
    [InlineData("stampable-modifier-on-a-class", "MIST0317")]
    [InlineData("stampable-modifier-on-a-member", "MIST0317")]
    [InlineData("static-property-with-an-init-accessor", "MIST0315")]
    [InlineData("operator-on-abstract", "MIST0335")]
    [InlineData("newtype-over-an-enum", "MIST0419")]
    [InlineData("enum-variant-omitted-over-an-inferred-union", "MIST0398")]
    [InlineData("conformance-mapping-signature-mismatch", "MIST0402")]
    [InlineData("newtype-clone-passes-itself-as-its-source", "MIST0418")]
    public void DeclarationBuilderReportsItsRefusal(string refusal, string diagnosticId)
    {
        var f = new BindingFragments();
        switch(refusal)
        {
            case "duplicate-type-name":
                var first = BindingFragments.At<TypeAliasDeclarationNode>(10);
                first.Name = BindingFragments.Token("Dup");
                var later = BindingFragments.At<TypeAliasDeclarationNode>();
                later.Name = BindingFragments.Token("Dup");
                _ = new TypeBuilder(f.Builder).Build(f.Types, "Probe", new CompilationUnitNode
                {
                    Items = [new TypeItemTopLevelItemNode { TypeDeclaration = new TypeAliasTypeTypeDeclarationNode { TypeAliasDeclaration = first } },
                        new TypeItemTopLevelItemNode { TypeDeclaration = new TypeAliasTypeTypeDeclarationNode { TypeAliasDeclaration = later } }],
                }, MistAssembly.Synthetic);
                break;
            case "stampable-modifier-on-a-class":
                var stampableClass = Body(new StampableModifierModifierNode());
                _ = ModifierBuilder.Build(stampableClass, f.Diagnostics, declaringType: null);
                break;
            case "stampable-modifier-on-a-member":
                var member = BindingFragments.At<MethodDeclarationNode>();
                member.Modifiers = [new StampableModifierModifierNode()];
                _ = ModifierBuilder.Build(member, f.Diagnostics, declaringType: HandBuiltModel.Struct("Token"));
                break;
            case "intrinsic-reserved-for-standard":
                var intrinsic = new ClassType("Ghost", "Probe", f.Types, null,
                    new BodyTypeTypeDeclarationNode { TypeBody = Body(new IntrinsicModifierModifierNode()) });
                new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(intrinsic);
                break;
            case "static-property-with-an-init-accessor":
                var setter = new AccessorBuilder(f.Builder).BuildInitAccessor(null, null,
                    new AccessorSite("P", new MemberModifiers { IsStatic = true }, null, IsAbstract: false), HandBuiltModel.Node(30),
                    isAuto: true, body: null, expression: null);
                Assert.NotNull(setter);
                Assert.False(setter.IsInit);
                break;
            case "operator-on-abstract":
                var body = Body();
                body.Keyword = new AbstractKeywordTypeKeywordNode();
                body.Members = [BindingFragments.At<OperatorMemberMemberDeclarationNode>()];
                var abstraction = new AbstractType("Shape", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body });
                new AbstractTypeBuilder(f.Builder, f.Builder.Resolver).Build(abstraction);
                break;
            case "newtype-over-an-enum":
                f.Register(HandBuiltModel.Enum("Color", f.Built.Type("Int32")));
                var declaration = BindingFragments.At<NewtypeDeclarationNode>();
                declaration.Name = BindingFragments.Token("Tint");
                declaration.SourceType = BindingFragments.TypeName("Color");
                new NewtypeBuilder(f.Builder, f.Builder.Resolver).Build(new Newtype("Tint", "Probe", f.Types, null, declaration));
                break;
            case "enum-variant-omitted-over-an-inferred-union":
                var mixed = new EnumType("Mixed", "Probe", f.Types, null, null);
                mixed.MembersList.AddRange([new EnumMember("Num", false, HandBuiltModel.Literal(f.Built.Type("Int32"), 1L)),
                    new EnumMember("Text", false, HandBuiltModel.Literal(f.Built.Type("String"), "x")),
                    new EnumMember("Other", false, null, BindingFragments.At<EnumMemberNode>())]);
                EnumMemberBinder.BindAll(mixed, f.Builder);
                _ = Assert.IsType<UnionType>(mixed.UnderlyingType);
                Assert.NotNull(mixed.MembersList[0].Value);
                Assert.NotNull(mixed.MembersList[1].Value);
                break;
            case "conformance-mapping-signature-mismatch":
                var conformer = HandBuiltModel.Class("Card");
                var contract = new AbstractType("Described", "Probe", f.Types, null, null);
                _ = BindingFragments.Member(conformer, "Text", f.Built.Type("String"), f.Built.Type("Int32"));
                _ = BindingFragments.Member(contract, "Describe", f.Built.Type("String"));
                f.Register(conformer);
                f.Register(contract);
                var mapping = BindingFragments.At<WithDeclarationNode>();
                mapping.TypeName = BindingFragments.Token("Card");
                mapping.AbstractName = BindingFragments.Token("Described");
                mapping.Mappings = [new MemberMappingNode { SourceName = BindingFragments.Token("Text"), TargetName = BindingFragments.Token("Describe") }];
                f.Builder.PendingConformances.Add(new PendingConformance(mapping, f.Types, "Probe"));
                new ConformanceBuilder(f.Builder, f.Builder.Resolver).BuildAll();
                _ = Assert.Single(conformer.ConformancesList);
                Assert.Equal(["Describe"], conformer.ConformancesList[0].UnresolvedTargets);
                break;
            case "newtype-clone-passes-itself-as-its-source":
                RefuseClone(f);
                break;
            default:
                throw new ArgumentException("Unknown refusal", nameof(refusal));
        }

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    private static TypeBodyDeclarationNode Body(params ModifierNode[] modifiers)
    {
        var body = BindingFragments.At<TypeBodyDeclarationNode>();
        body.Name = BindingFragments.Token("Probe");
        body.Keyword = new ClassKeywordTypeKeywordNode();
        body.Modifiers = [.. modifiers];
        body.Members = [];
        body.Attributes = [];
        body.Constraints = [];
        return body;
    }

    private static void RefuseClone(BindingFragments f)
    {
        var lion = HandBuiltModel.Class("Lion");
        var puma = HandBuiltModel.Class("Puma");
        puma.CloneSource = lion;
        puma.CloneSourceType = lion;
        puma.NewtypeDeclaration = BindingFragments.At<NewtypeDeclarationNode>();
        var original = Assert.Single(f.Diagnostics.Withhold(() =>
            f.Diagnostics.ReportAtNode(ProgramAnalysisDiagnostics.NoImplicitConversion, HandBuiltModel.Node(40), null, "Puma", "Lion")).Occurrences);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        CloneDiagnostics.Run([puma], _ => f.Diagnostics.Add(original), f.Diagnostics);
    }
}