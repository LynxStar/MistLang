using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class IntegralConstantRuleTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void AConstantArm_CoversALaterArmOfTheSameValueInAnotherBox()
    {
        var built = new HandBuiltModel();
        var wide = built.Type("Int64");
        var earlier = new ComparisonPattern(HandBuiltModel.Node(), wide, ComparisonKind.Equal, HandBuiltModel.Literal(built.Type("Int32"), 1));
        var later = new ComparisonPattern(HandBuiltModel.Node(), wide, ComparisonKind.Equal, HandBuiltModel.Literal(wide, 1L));

        Assert.Equal("MIST0199", PatternAnalyzer.ArmFinding([earlier], later, wide, built.Interner)?.Number.Value);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AConstantArm_DoesNotCoverADifferentValue()
    {
        var built = new HandBuiltModel();
        var wide = built.Type("Int64");
        var earlier = new ComparisonPattern(HandBuiltModel.Node(), wide, ComparisonKind.Equal, HandBuiltModel.Literal(wide, 1L));
        var later = new ComparisonPattern(HandBuiltModel.Node(), wide, ComparisonKind.Equal, HandBuiltModel.Literal(wide, 2L));

        Assert.Null(PatternAnalyzer.ArmFinding([earlier], later, wide, built.Interner));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheSameEnumValueInTwoIntegralBoxes_IsADuplicate()
    {
        var fragment = new DeclarationFragments();
        var wide = fragment.Built.Type("Int64");
        wide.Modifiers = DeclarationFragments.Public with { IsReadonly = true };
        var enumeration = new EnumType("Twice", "Refusal", null, null, DeclarationFragments.At<EnumDeclarationNode>())
        {
            UnderlyingType = wide,
            Modifiers = DeclarationFragments.Public,
        };
        enumeration.MembersList.Add(new EnumMember("A", false, HandBuiltModel.Literal(fragment.Built.Type("Int32"), 1),
            DeclarationFragments.At<EnumMemberNode>(20)));
        enumeration.MembersList.Add(new EnumMember("B", false, HandBuiltModel.Literal(wide, 1L),
            DeclarationFragments.At<EnumMemberNode>(30)));
        fragment.Types.Add(enumeration);

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal("MIST0167", finding.Number.Value);
    }
}