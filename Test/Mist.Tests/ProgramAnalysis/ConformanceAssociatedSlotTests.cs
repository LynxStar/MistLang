using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: ConformanceAnalyzer over a hand-built `abstract IBag { type Element; Element Get(); T Pick<T>(T value); }`,
// reached directly or through `abstract INamedBag : IBag`, and one conformer, whose nested alias, when it has
// one, is `type Element = string`.
[Trait("Tier", "2")]
public sealed class ConformanceAssociatedSlotTests
{
    [Theory]
    [InlineData("String", true, "")]
    [InlineData("Int32", true, "MIST_PA_SLOT_SIGNATURE_MISMATCH")]
    [InlineData("Int32", false, "MIST_PA_TYPE_SLOT_UNFILLED")]
    public void AnAnswerUsesTheSlotsFillerWhereTheSlotIsNamed(string returned, bool filled, string expected)
    {
        var fragment = Build(filled, out var conformer, out _);
        _ = DeclarationFragments.Method(conformer, "Get", fragment.Built.Type(returned));
        Pick(conformer, constrained: null);

        var findings = fragment.Run(new ConformanceAnalyzer());

        Assert.Equal(expected, string.Join(", ", findings.Select(finding => finding.Number.Value)));
        Assert.All(findings.Where(finding => finding.Number.Value == "MIST_PA_SLOT_SIGNATURE_MISMATCH"),
            finding => Assert.Contains("slot 'string Get()'", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "MIST_PA_SLOT_CONSTRAINT_STRENGTHENED")]
    public void AGenericAnswerRequiringMoreThanItsSlotPromises_IsRefused(bool constrained, string expected)
    {
        var fragment = Build(filled: true, out var conformer, out var shown);
        _ = DeclarationFragments.Method(conformer, "Get", fragment.Built.Type("String"));
        Pick(conformer, constrained ? shown : null);

        var findings = fragment.Run(new ConformanceAnalyzer());

        Assert.Equal(expected, string.Join(", ", findings.Select(finding => finding.Number.Value)));
        Assert.All(findings, finding => Assert.Contains("requires 'IShow' of type parameter 'U'", finding.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "MIST_PA_TYPE_SLOT_UNFILLED")]
    public void AConformerOfADerivedAbstract_OwesTheSlotItInherits(bool filled, string expected)
    {
        var fragment = Build(filled, out var conformer, out _, throughDerived: true);
        _ = DeclarationFragments.Method(conformer, "Get", fragment.Built.Type("String"));
        Pick(conformer, constrained: null);

        var findings = fragment.Run(new ConformanceAnalyzer());

        Assert.Equal(expected, string.Join(", ", findings.Select(finding => finding.Number.Value)));
        Assert.All(findings, finding => Assert.Contains("'IBag.Element'", finding.Message, StringComparison.Ordinal));
    }

    private static DeclarationFragments Build(bool filled, out ClassType conformer, out AbstractType shown, bool throughDerived = false)
    {
        var fragment = new DeclarationFragments();
        shown = fragment.Abstract("IShow");
        var bag = fragment.Abstract("IBag");
        var element = new TypeSlot("Element", 0, bag.Scope, bag);
        bag.TypeSlotsList.Add(element);
        _ = DeclarationFragments.Method(bag, "Get", element);
        var picked = new TypeParameter("T", 0, bag.Scope, bag);
        var pick = DeclarationFragments.Method(bag, "Pick", picked, picked);
        pick.TypeParametersList.Add(picked);
        var named = fragment.Abstract("INamedBag");
        named.BaseAbstractTypes.Add(bag);

        conformer = fragment.Class("Strings");
        conformer.BaseAbstractTypes.Add(throughDerived ? named : bag);

        if(filled)
        {
            _ = fragment.Alias("Element", fragment.Built.Type("String"), conformer);
        }

        return fragment;
    }

    private static void Pick(ClassType conformer, IMistType? constrained)
    {
        var answering = new TypeParameter("U", 0, conformer.Scope, conformer);
        var pick = DeclarationFragments.Method(conformer, "Pick", answering, answering);
        pick.TypeParametersList.Add(answering);

        if(constrained is not null)
        {
            answering.ConstraintsList.Add(new TypeConstraint(constrained));
        }
    }
}