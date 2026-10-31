using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class NativeSlotFillsTests
{
    [Theory]
    [InlineData("Defaulted")]
    [InlineData("Required")]
    public void AMemberRepeatingAVirtualOrAbstractSlot_FillsIt(string kind)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Hooked");
        var slot = NativeSlotFragments.Slot(clr, "Hook", Kind(kind), f.Text, Accessibility.Protected, f.Int);
        var source = f.Source("Mine", clr);
        var filler = f.Member(source, "Hook", f.Text, Accessibility.Protected, parameters: f.Int);

        var fill = Assert.Single(NativeSlotFills.Of(source, f.Built.Interner));

        Assert.Same(filler, fill.Filler.Member);
        Assert.Same(slot, fill.Slot.Member);
        Assert.Same(clr, fill.Ancestor);
        Assert.Empty(NativeSlotFills.Collisions(source, f.Built.Interner));
    }

    [Fact]
    public void Unfilled_NamesARequiredSlotAndNotADefaultedOne()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        var required = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        var source = f.Source("Mine", clr);

        var (slot, ancestor, _) = Assert.Single(NativeSlotFills.Unfilled(source, f.Built.Interner));

        Assert.Same(required, slot.Member);
        Assert.Same(clr, ancestor);
    }

    [Theory]
    [InlineData("Sealed")]
    [InlineData("None")]
    public void AMemberRepeatingASealedOrOrdinaryMember_CollidesInsteadOfFilling(string kind)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Closed");
        var against = NativeSlotFragments.Slot(clr, "Twice", Kind(kind), f.Int, Accessibility.Public, f.Int);
        var source = f.Source("Mine", clr);
        var member = f.Member(source, "Twice", f.Int, parameters: f.Int);

        var collision = Assert.Single(NativeSlotFills.Collisions(source, f.Built.Interner));

        Assert.Equal((member, against, Kind(kind), null), (collision.Member.Member, collision.Against.Member, collision.Kind, collision.Mismatch));
        Assert.Empty(NativeSlotFills.Of(source, f.Built.Interner));
    }

    [Theory]
    [InlineData("return type")]
    [InlineData("accessibility")]
    [InlineData("static")]
    public void ACandidateDifferingFromItsSlot_NamesWhatDiffers(string differs)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Hooked");
        _ = NativeSlotFragments.Slot(clr, "Hook", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        var source = f.Source("Mine", clr);
        _ = f.Member(
            source, "Hook", differs == "return type" ? f.Int : f.Text,
            differs == "accessibility" ? Accessibility.Public : Accessibility.Protected, isStatic: differs == "static", parameters: f.Int);

        var collision = Assert.Single(NativeSlotFills.Collisions(source, f.Built.Interner));

        Assert.Equal((NativeSlotKind.Defaulted, differs), (collision.Kind, collision.Mismatch));
        Assert.Empty(NativeSlotFills.Of(source, f.Built.Interner));
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, "set accessor")]
    public void APropertyFill_MayOmitAnAccessorButNotAddOne(bool slotSettable, bool fillSettable, string? mismatch)
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Labeled");
        _ = NativeSlotFragments.SlotProperty(clr, "Label", NativeSlotKind.Defaulted, f.Text, slotSettable);
        var source = f.Source("Mine", clr);
        _ = f.MemberProperty(source, "Label", f.Text, fillSettable);

        Assert.Equal(mismatch is null ? 1 : 0, NativeSlotFills.Of(source, f.Built.Interner).Count());
        Assert.Equal(mismatch, NativeSlotFills.Collisions(source, f.Built.Interner).SingleOrDefault()?.Mismatch);
    }

    [Fact]
    public void AGenericAncestorsSlot_IsReadThroughTheInheritanceSubstitution()
    {
        var f = new NativeSlotFragments();
        var collection = NativeSlotFragments.Projected("Collection`1");
        var element = DeclarationFragments.Parameter(collection, collection.Scope, "T");
        var insert = NativeSlotFragments.Slot(collection, "InsertItem", NativeSlotKind.Defaulted, f.Void, Accessibility.Protected, f.Int, element);
        var source = f.Source("Loud", f.Built.Interner.GetOrCreateConstructedType(collection, [f.Int]));
        _ = f.Member(source, "InsertItem", f.Void, Accessibility.Protected, parameters: [f.Int, f.Int]);

        Assert.Same(insert, Assert.Single(NativeSlotFills.Of(source, f.Built.Interner)).Slot.Member);
    }

    [Fact]
    public void ADerivedTypeRestatingAMistAncestorsFill_AlsoFillsTheSlot()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        var slot = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        var middle = f.Source("Middle", clr);
        _ = f.Member(middle, "Key", f.Text, Accessibility.Protected, parameters: f.Int);
        var derived = f.Source("Derived", middle);
        var restated = f.Member(derived, "Key", f.Text, Accessibility.Protected, parameters: f.Int);

        var fill = Assert.Single(NativeSlotFills.Of(derived, f.Built.Interner));

        Assert.Equal((restated, slot), (fill.Filler.Member, fill.Slot.Member));
        Assert.Empty(NativeSlotFills.Unfilled(derived, f.Built.Interner));
    }

    [Fact]
    public void AMistAncestorsFill_AnswersARequiredSlotForTheDerivedType()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        _ = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        var middle = f.Source("Middle", clr);
        _ = f.Member(middle, "Key", f.Text, Accessibility.Protected, parameters: f.Int);

        Assert.Empty(NativeSlotFills.Unfilled(f.Source("Derived", middle), f.Built.Interner));
    }

    [Fact]
    public void TheNearestProjectedDeclarationDecides_SoAClrOverrideOfAnAbstractSlotLeavesNothingUnfilled()
    {
        var f = new NativeSlotFragments();
        var root = NativeSlotFragments.Projected("RequiredBase");
        _ = NativeSlotFragments.Slot(root, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        var overriding = NativeSlotFragments.Projected("OverridingDerived", root);
        var nearest = NativeSlotFragments.Slot(overriding, "Key", NativeSlotKind.Defaulted, f.Text, Accessibility.Protected, f.Int);
        var source = f.Source("Mine", overriding);
        _ = f.Member(source, "Key", f.Text, Accessibility.Protected, parameters: f.Int);

        Assert.Empty(NativeSlotFills.Unfilled(f.Source("Empty", overriding), f.Built.Interner));
        Assert.Same(nearest, Assert.Single(NativeSlotFills.Of(source, f.Built.Interner)).Slot.Member);
    }

    [Fact]
    public void AMethodsOwnTypeParameter_IsMatchedByPosition()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Converter");
        var slot = NativeSlotFragments.Slot(clr, "Convert", NativeSlotKind.Defaulted, f.Text, Accessibility.Public);
        slot.ParametersList.Add(new Parameter("value", DeclarationFragments.Parameter(slot, slot.Scope, "TIn")));
        var source = f.Source("Mine", clr);
        var filler = f.Member(source, "Convert", f.Text);
        filler.ParametersList.Add(new Parameter("value", DeclarationFragments.Parameter(filler, filler.Scope, "TOther")));

        Assert.Same(slot, Assert.Single(NativeSlotFills.Of(source, f.Built.Interner)).Slot.Member);
    }

    [Fact]
    public void AClrResultDeclaredNullable_IsFilledByTheNonOptionalType()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Object");
        var optional = f.Built.Interner.GetOrCreateUnionType([f.Text, f.Void]);
        var slot = new Method("ToString", optional, [], new MemberModifiers { Accessibility = Accessibility.Public, IsClrProjected = true })
        {
            DeclaringType = clr,
            SlotKind = NativeSlotKind.Defaulted,
        };
        clr.MethodsList.Add(slot);
        var source = f.Source("Mine", clr);
        _ = f.Member(source, "ToString", f.Text);

        Assert.Same(slot, Assert.Single(NativeSlotFills.Of(source, f.Built.Interner)).Slot.Member);
    }

    [Fact]
    public void AChainWithAnUnresolvedBase_AnswersNothing()
    {
        var f = new NativeSlotFragments();
        var clr = NativeSlotFragments.Projected("Keyed");
        _ = NativeSlotFragments.Slot(clr, "Key", NativeSlotKind.Required, f.Text, Accessibility.Protected, f.Int);
        _ = NativeSlotFragments.Slot(clr, "Twice", NativeSlotKind.None, f.Int, Accessibility.Public, f.Int);
        var middle = f.Source("Middle", clr);
        middle.BaseUnresolved = true;
        var source = f.Source("Mine", middle);
        _ = f.Member(source, "Twice", f.Int, parameters: f.Int);

        Assert.Empty(NativeSlotFills.Unfilled(source, f.Built.Interner));
        Assert.Empty(NativeSlotFills.Collisions(source, f.Built.Interner));
    }

    [Fact]
    public void ASourceTypeWithNoProjectedAncestor_HasNoSlots()
    {
        var f = new NativeSlotFragments();
        var mistBase = f.Source("MistBase", null);
        _ = f.Member(mistBase, "ToString", f.Text);
        var source = f.Source("Mine", mistBase);
        _ = f.Member(source, "ToString", f.Text);

        Assert.Empty(NativeSlotFills.Of(source, f.Built.Interner));
        Assert.Empty(NativeSlotFills.Collisions(source, f.Built.Interner));
    }

    private static NativeSlotKind Kind(string name)
    {
        return Enum.Parse<NativeSlotKind>(name);
    }
}