using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class PredicateReadDecisionTests
{
    [Theory]
    [InlineData("write-only", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("no-accessors", "MIST_PA_NO_GET_ACCESSOR")]
    [InlineData("get-set", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("mutable-field", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("get-init", null)]
    [InlineData("get-only", null)]
    [InlineData("readonly-field", null)]
    public void APredicateReadIsJudgedByItsAccessorsAndStability(string member, string? expected)
    {
        var refusal = DeclarationShapeAnalyzer.PredicateRead(Member(member), purity: null);

        Assert.Equal(expected, refusal?.Id.Value);
    }

    private static LookupResult Member(string member)
    {
        var owner = HandBuiltModel.Class("Jar");
        var integer = new HandBuiltModel().Type("Int32");

        if(member.EndsWith("-field", StringComparison.Ordinal))
        {
            var modifiers = DeclarationFragments.Public with { IsReadonly = member == "readonly-field" };
            return new FieldResult(new Field("size", integer, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner }, owner, []);
        }

        var property = new Property("Size", integer, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = member is "write-only" or "no-accessors" ? null : new PropertyAccessor { IsAuto = true },
            Setter = member switch
            {
                "write-only" or "get-set" => new PropertyAccessor { IsAuto = member == "get-set" },
                "get-init" => new PropertyAccessor { IsAuto = true, IsInit = true },
                _ => null,
            },
        };
        return new PropertyResult(property, owner, []);
    }
}