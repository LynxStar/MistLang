// The drops a scope makes without writing them, as purity, readonly inference and context calls read
// them: a linear local's is judged exactly when some path -- a branch, a loop exit or a throw -- leaves
// its scope still owning the value, which is when the lowered release can run.

using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ExitDropReachabilityTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void AValueHandedOnBeforeAnythingCanThrowDropsNothingAtExit()
    {
        var t = f.Local("t");

        Assert.Empty(Dropped(Declare(t, f.New(1)), Return(Ref(t))));
    }

    [Fact]
    public void ADropOnOneBranchLeavesTheOtherBranchsExitDrop()
    {
        var t = f.Local("t");
        var exit = Assert.Single(PurityJudge.ExitDrops(HandBuiltModel.Block(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Run(Drop(t))))), member: null));

        Assert.Equal("t", exit.Name);
        Assert.Same(f.Token.MethodsList.Single(method => method.Name == "Drop"), exit.Drop);
    }

    [Fact]
    public void AThrowBeforeTheHandOffReachesTheExitDrop()
    {
        var t = f.Local("t");

        Assert.Equal(["t"], Dropped(Declare(t, f.New(1)), Run(f.Call(f.Risky)), Return(Ref(t))));
    }

    [Fact]
    public void ALocalTransferDropsOnlyTheDestination()
    {
        var t = f.Local("t");
        var r = f.Local("r");

        Assert.Equal(["r"], Dropped(Declare(t, f.New(1)), Declare(r, Ref(t))));
    }

    [Fact]
    public void ALeaseIsNeverDroppedAndItsLenderIs()
    {
        var t = f.Local("t");
        var v = f.Local("v", ownership: OwnershipKind.Frozen);

        Assert.Equal(["t"], Dropped(Declare(t, f.New(1)), Declare(v, Ref(t))));
    }

    [Fact]
    public void AConsumedBindingDropsTheValueReassignedToIt()
    {
        var t = f.Local("t");

        Assert.Equal(["t"], Dropped(Declare(t, f.New(1)), Run(Drop(t)), Run(Assign(t, f.New(2)))));
    }

    /// <summary>
    /// With no initializer the drop is filed where the binding first came to own a value.
    /// </summary>
    [Fact]
    public void AnUninitializedBindingIsDroppedFromWhereItWasAssigned()
    {
        var r = f.Local("r");
        var assignment = Assign(r, f.New(1));
        var exit = Assert.Single(PurityJudge.ExitDrops(HandBuiltModel.Block(Declare(r, null), Run(assignment)), member: null));

        Assert.Equal("r", exit.Name);
        Assert.Same(assignment.Node, exit.At);
    }

    [Fact]
    public void ARelevantAliasIsDroppedThroughEveryReference()
    {
        var s = f.Local("s", f.Shared);
        var alias = f.Local("alias", f.Shared);

        Assert.Equal(["s", "alias"], Dropped(Declare(s, f.New(1, f.Shared)), Declare(alias, Ref(s))));
    }

    private static IEnumerable<string> Dropped(params Statement[] statements)
    {
        return PurityJudge.ExitDrops(HandBuiltModel.Block(statements), member: null).Select(exit => exit.Name);
    }
}