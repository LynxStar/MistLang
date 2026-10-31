using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class NativeSlotAnalyzerTests
{
    [Fact]
    public void ARequiredSlotNothingFills_IsReportedOnceAtTheType()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        _ = NativeSlotFragments.Slot(clr, "GetKeyForItem", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        var source = f.Source("Catalog", clr);

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0459", diagnostic.Number.Value);
        Assert.Contains("'Catalog' does not fill slot 'string GetKeyForItem(int)' of 'Keyed'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(source.DeclarationNode!.Span.Start + 1, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AMemberRepeatingASealedSlot_IsReportedOnceAtTheMember()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("SealingDerived");
        _ = NativeSlotFragments.Slot(clr, "Twice", NativeSlotKind.Sealed, f.Int, Accessibility.Public, f.Int);
        var member = f.Member(f.Source("Mine", clr), "Twice", f.Int, parameters: f.Int);

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0460", diagnostic.Number.Value);
        Assert.Contains("'Mine.Twice' cannot fill slot 'int Twice(int)' of 'SealingDerived'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(member.DeclarationNode!.Span.Start + 1, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("return type", "the slot declares 'string'")]
    [InlineData("accessibility", "the slot declares protected")]
    [InlineData("static", "the slot declares an instance member")]
    public void AMemberDifferingFromItsSlot_IsReportedOnceAtTheMemberNamingTheDifference(string differs, string declares)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Hooked");
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        var member = f.Member(
            f.Source("Mine", clr), "Hook", differs == "return type" ? f.Int : f.Text,
            differs == "accessibility" ? Accessibility.Public : Accessibility.Protected, isStatic: differs == "static", parameters: f.Int);

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0461", diagnostic.Number.Value);
        Assert.Contains($"but differs in {differs}: {declares}.", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(member.DeclarationNode!.Span.Start + 1, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void APropertyFillAddingASetter_IsReportedNamingTheAccessor()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Labeled");
        _ = NativeSlotFragments.SlotProperty(clr, "Label", NativeSlotKind.Defaulted, f.Text, settable: false);
        _ = f.MemberProperty(f.Source("Mine", clr), "Label", f.Text, settable: true);

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0461", diagnostic.Number.Value);
        Assert.Contains("differs in set accessor: the slot declares no set accessor.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAutoPropertyFillOmittingTheSlotsSetter_IsReportedNamingTheAccessor()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Exception");
        _ = NativeSlotFragments.SlotProperty(clr, "HelpLink", NativeSlotKind.Defaulted, f.Text, settable: true);
        var member = f.MemberProperty(f.Source("Oops", clr), "HelpLink", f.Text, settable: false);

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0461", diagnostic.Number.Value);
        Assert.Contains(
            "'Oops.HelpLink' fills slot 'string HelpLink' of 'Exception' but differs in set accessor: the slot declares a set accessor, which an auto-property filling it must declare too.",
            diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(member.DeclarationNode!.Span.Start + 1, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void ABodiedGetterFillOmittingTheSlotsSetter_IsAdmittedSilently()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Exception");
        _ = NativeSlotFragments.SlotProperty(clr, "HelpLink", NativeSlotKind.Defaulted, f.Text, settable: true);
        _ = f.MemberProperty(f.Source("Oops", clr), "HelpLink", f.Text, settable: false, bodied: true);

        Assert.Empty(f.Run(new NativeSlotAnalyzer()));
    }

    [Fact]
    public void AGenericFillStrengtheningItsSlot_IsReportedNamingTheConstraint()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Ranker");
        var slot = NativeSlotFragments.Slot(clr, "First", NativeSlotKind.Defaulted, f.Void);
        slot.ParametersList.Add(new Parameter("a", DeclarationFragments.Parameter(slot, slot.Scope, "T")));
        var filler = f.Member(f.Source("Larger", clr), "First", f.Void);
        var u = DeclarationFragments.Parameter(filler, filler.Scope, "T");
        filler.ParametersList.Add(new Parameter("a", u));
        u.ConstraintsList.Add(new TypeConstraint(NativeSlotFragments.Projected("IRanked")));

        var diagnostic = Assert.Single(f.Run(new NativeSlotAnalyzer()));

        Assert.Equal("MIST0461", diagnostic.Number.Value);
        Assert.Contains("differs in the constraints of 'T': the slot declares no 'IRanked' constraint.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnfilledDefaultedSlotAndAFill_AreAdmittedSilently()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Hooked");
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        _ = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        _ = f.Member(f.Source("Mine", clr), "Key", f.Text, Accessibility.Protected, parameters: f.Int);

        Assert.Empty(f.Run(new NativeSlotAnalyzer()));
    }

    [Fact]
    public void AnOverloadOfASlot_IsAdmittedSilently()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Sealed");
        _ = NativeSlotFragments.Slot(clr, "Twice", NativeSlotKind.Sealed, f.Int, Accessibility.Public, f.Int);
        _ = f.Member(f.Source("Mine", clr), "Twice", f.Int, parameters: [f.Int, f.Int]);

        Assert.Empty(f.Run(new NativeSlotAnalyzer()));
    }

    [Fact]
    public void ARequiredSlotBehindAnUnresolvedBase_IsNotReported()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        _ = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        var middle = f.Source("Middle", clr);
        middle.BaseUnresolved = true;
        _ = f.Source("Mine", middle);

        Assert.Empty(f.Run(new NativeSlotAnalyzer()));
    }

    [Fact]
    public void TheRuleReportsNoRedeclaration()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Collection");
        _ = NativeSlotFragments.Slot(clr, "Add", NativeSlotKind.None, f.Void, Accessibility.Public, f.Int);
        _ = f.Member(f.Source("Loud", clr), "Add", f.Void, parameters: f.Int);

        Assert.Empty(f.Run(new NativeSlotAnalyzer()));
    }
}