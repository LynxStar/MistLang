// The single decisions a lifetime plan stands on, each called with inputs built directly: whether a call
// is surely entered, whether an argument is a fresh value only lent to its callee, whether an expression
// merely views a value someone else holds, and which types are tracked at all.

using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ResourceTransferDecisionTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void OnlyACallThatCanFailToBeginLeavesItsArgumentsWithTheCaller()
    {
        var box = Ref(new LocalVariable("b", f.Box));

        Assert.Equal(CallEntry.Conditional, CallEntries.Of(f.CallOn(box, conditional: true), _ => false));
        Assert.Equal(CallEntry.Absentable, CallEntries.Of(f.CallOn(box, conditional: false), _ => false));
        Assert.Equal(CallEntry.Certain, CallEntries.Of(f.CallOn(box, conditional: false), _ => true));
        Assert.Equal(CallEntry.Certain, CallEntries.Of(f.CallOn(new ThisExpression(HandBuiltModel.Node(), f.Box), conditional: false), _ => false));
        Assert.Equal(CallEntry.Certain, CallEntries.Of(f.Call(f.Take), _ => false));
    }

    [Fact]
    public void OnlyAFreshValuePassedToAFrozenOrBorrowedParameterIsLent()
    {
        var borrowed = new Method("Borrow", f.Void, [Owned("t", f.Token, OwnershipKind.Borrowed)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var fresh = f.New(1);
        var made = f.Call(f.Make);

        Assert.Equal([fresh], LentValues.Of(f.Call(f.Peek, fresh)).Select(lent => lent.Value));
        Assert.Equal([made], LentValues.Of(f.Call(borrowed, made)).Select(lent => lent.Value));
        Assert.Empty(LentValues.Of(f.Call(f.Peek, Ref(f.Local("t")))));
        Assert.Empty(LentValues.Of(f.Call(f.Take, f.New(2))));
        Assert.Empty(LentValues.Of(f.Call(f.Lend, f.New(3))));
    }

    [Fact]
    public void ALeaseOrAFieldReadOnlyViewsAValueItsHolderReleases()
    {
        Assert.True(LifetimeOwners.IsView(Ref(f.Local("v", ownership: OwnershipKind.Frozen))));
        Assert.True(LifetimeOwners.IsView(Ref(f.Local("b", ownership: OwnershipKind.Borrowed))));
        Assert.True(LifetimeOwners.IsView(Ref(Owned("p", f.Token, OwnershipKind.Frozen))));
        Assert.True(LifetimeOwners.IsView(Ref(f.First)));
        Assert.False(LifetimeOwners.IsView(Ref(f.Local("t"))));
        Assert.False(LifetimeOwners.IsView(f.New(1)));
        Assert.False(LifetimeOwners.IsView(Ref(f.Local("s", f.Shared, OwnershipKind.Frozen))));
    }

    [Fact]
    public void ALinearRefStructIsTrackedLikeAnyLinearValue()
    {
        Assert.True(LocalLifetimes.IsTracked(f.Lease));
    }

    /// <summary>
    /// A linear type with no Drop and no droppable field has nothing a release could call.
    /// </summary>
    [Fact]
    public void ALinearTypeWithNothingToReleaseIsNotTracked()
    {
        var ticket = HandBuiltModel.Class("Ticket");
        ticket.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        ticket.FieldsList.Add(new Field("Id", f.Int32, OwnershipKind.Unrestricted, default) { DeclaringType = ticket });

        Assert.False(LocalLifetimes.IsTracked(ticket));
        Assert.True(LocalLifetimes.IsTracked(f.Holder));
    }
}