using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class LocalLifetimeTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void ADeclarationTakingTheLocalWholeMovesTheObligationToTheDestination()
    {
        var t = f.Local("t");
        var r = f.Local("r");
        var plan = Plan(Declare(t, f.New(1)), Declare(r, Ref(t)));

        Assert.Equal(LifetimeRelease.None, plan.Release(t));
        Assert.Equal(LifetimeRelease.AtExit, plan.Release(r));
    }

    [Fact]
    public void AChainOfTransfersLeavesOnlyTheLastOwner()
    {
        var a = f.Local("a");
        var b = f.Local("b");
        var c = f.Local("c");
        var plan = Plan(Declare(a, f.New(1)), Declare(b, Ref(a)), Declare(c, Ref(b)));

        Assert.Equal([LifetimeRelease.None, LifetimeRelease.None, LifetimeRelease.AtExit], [plan.Release(a), plan.Release(b), plan.Release(c)]);
    }

    /// <summary>
    /// <c>b.Drop(); b = a;</c> -- the destination's first value is gone, so the second is a fresh lifetime
    /// it owes, and the source owes nothing once it is taken, except across the throw that can come first.
    /// </summary>
    [Fact]
    public void AssigningIntoAConsumedDestinationGivesItTheSourcesObligation()
    {
        var a = f.Local("a");
        var b = f.Local("b");
        var source = Ref(a);
        var assignment = Assign(b, source);
        var plan = Plan(Declare(a, f.New(1)), Declare(b, f.New(2)), Run(Drop(b)), Run(assignment));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(a));
        Assert.Equal(LifetimeRelease.Guarded, plan.Release(b));
        Assert.Equal([new LifetimeMark(a, Owned: false)], plan.MarksAfter(source));
        Assert.Equal([new LifetimeMark(b, Owned: true)], plan.MarksAfter(assignment));
    }

    [Fact]
    public void AMoveOutOfAnInnerBlockLeavesTheOuterDestinationOwing()
    {
        var r = f.Local("r");
        var t = f.Local("t");
        var plan = Plan(
            Declare(r, null),
            HandBuiltModel.Block(Declare(t, f.New(1)), Run(Assign(r, Ref(t)))),
            Run(f.Call(f.Risky)));

        Assert.Equal(LifetimeRelease.None, plan.Release(t));
        Assert.Equal(LifetimeRelease.Guarded, plan.Release(r));
    }

    /// <summary>
    /// The first reproduction: a Drop on one branch cannot remove the release from the other.
    /// </summary>
    [Fact]
    public void ADropOnOneBranchLeavesTheOtherBranchOwing()
    {
        var t = f.Local("t");
        var drop = Drop(t);
        var plan = Plan(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Run(drop))));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(((MemberAccessExpression)drop.Receiver).Receiver));
    }

    [Fact]
    public void AReturnOnOneBranchLeavesTheOtherBranchOwing()
    {
        var t = f.Local("t");
        var returned = Ref(t);
        var plan = Plan(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Return(returned))), Return(f.New(2)));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(returned));
    }

    [Fact]
    public void ALocalHandedOnBeforeAnythingCanThrowIsReleasedNowhere()
    {
        var t = f.Local("t");

        Assert.Equal(LifetimeRelease.None, Plan(Declare(t, f.New(1)), Return(Ref(t))).Release(t));
        Assert.Equal(LifetimeRelease.None, Plan(Declare(t, f.New(1)), Run(f.Call(f.Take, Ref(t)))).Release(t));
    }

    /// <summary>
    /// A throw from the call before the hand-off leaves the scope with the value still owned, so the
    /// release must survive -- guarded, since the path that reaches the hand-off owes nothing.
    /// </summary>
    [Fact]
    public void AStepThatMayThrowBeforeTheHandOffKeepsTheRelease()
    {
        var t = f.Local("t");
        var plan = Plan(Declare(t, f.New(1)), Run(f.Call(f.Risky)), Run(f.Call(f.Take, Ref(t))));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
    }

    [Fact]
    public void AnArgumentIsHandedOnAfterTheLastArgumentThatMayStillThrow()
    {
        var t = f.Local("t");
        var passed = Ref(t);
        var later = f.Call(f.Risky);
        var plan = Plan(Declare(t, f.New(1)), Run(f.Call(f.Pair, passed, later)));

        Assert.Empty(plan.MarksAfter(passed));
        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(later));
    }

    [Fact]
    public void AnArgumentFollowedByNothingThatThrowsIsHandedOnAtItself()
    {
        var t = f.Local("t");
        var passed = Ref(t);
        var plan = Plan(Declare(t, f.New(1)), Run(f.Call(f.Risky)), Run(f.Call(f.Pair, passed, HandBuiltModel.Literal(f.Int32, 1))));

        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(passed));
    }

    /// <summary>
    /// A by-reference argument is a loan, so the caller keeps the obligation and it never varies.
    /// </summary>
    [Fact]
    public void LendingLeavesTheReleaseUnconditional()
    {
        var t = f.Local("t");

        Assert.Equal(LifetimeRelease.AtExit, Plan(Declare(t, f.New(1)), Run(f.Call(f.Lend, Ref(t)))).Release(t));
    }

    /// <summary>
    /// The third reproduction: consuming the first value does not end the binding's obligations.
    /// </summary>
    [Fact]
    public void AConsumedBindingOwesTheFreshValueAssignedToIt()
    {
        var t = f.Local("t");
        var reassignment = Assign(t, f.New(2));
        var plan = Plan(Declare(t, f.New(1)), Run(Drop(t)), Run(reassignment));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
        Assert.Equal([new LifetimeMark(t, Owned: true)], plan.MarksAfter(reassignment));
    }

    /// <summary>
    /// If the initializer of the second value throws, nothing new is owned and the first is already
    /// gone; the only other throw is the callee's, after the hand-off. So no path owes a release.
    /// </summary>
    [Fact]
    public void AFailedReinitializerCreatesNothingToRelease()
    {
        var t = f.Local("t");
        var plan = Plan(Declare(t, f.New(1)), Run(Drop(t)), Run(Assign(t, f.Call(f.Make))), Run(f.Call(f.Take, Ref(t))));

        Assert.Equal(LifetimeRelease.None, plan.Release(t));
    }

    [Fact]
    public void ALoopLocalHandedOnSomeIterationsIsGuarded()
    {
        var t = f.Local("t");
        var plan = Plan(f.While(HandBuiltModel.Block(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Run(f.Call(f.Take, Ref(t))))))));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
    }

    [Fact]
    public void ABreakLeavesTheLoopLocalOwned()
    {
        var t = f.Local("t");
        var plan = Plan(f.While(HandBuiltModel.Block(
            Declare(t, f.New(1)),
            f.If(HandBuiltModel.Block(new BreakStatement())),
            Run(f.Call(f.Take, Ref(t))))));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
    }

    [Fact]
    public void AnOuterBindingReintroducedEveryIterationIsGuarded()
    {
        var t = f.Local("t");
        var plan = Plan(Declare(t, f.New(0)), f.While(HandBuiltModel.Block(Run(Drop(t)), Run(Assign(t, f.New(1))))));

        Assert.Equal(LifetimeRelease.Guarded, plan.Release(t));
    }

    [Fact]
    public void ALeaseOwnsNothingAndLeavesItsLenderUnconditional()
    {
        var t = f.Local("t");
        var frozen = f.Local("v", ownership: OwnershipKind.Frozen);
        var borrowed = f.Local("b", ownership: OwnershipKind.Borrowed);
        var plan = Plan(Declare(t, f.New(1)), Declare(frozen, Ref(t)), Declare(borrowed, Ref(t)), Run(f.Call(f.Risky)));

        Assert.Equal(
            [LifetimeRelease.AtExit, LifetimeRelease.None, LifetimeRelease.None],
            [plan.Release(t), plan.Release(frozen), plan.Release(borrowed)]);
    }

    [Fact]
    public void ADeclarationNeverAssignedOwesNothing()
    {
        var r = f.Local("r");

        Assert.Equal(LifetimeRelease.None, Plan(Declare(r, null), Run(f.Call(f.Risky))).Release(r));
    }

    /// <summary>
    /// A lambda's body runs when it is called, if ever, so it neither hands on the enclosing local nor
    /// shares its plan -- its own locals are planned on its own graph.
    /// </summary>
    [Fact]
    public void ALambdaPlansItsOwnLocalsAndHandsOnNoneOfTheEnclosingOnes()
    {
        var t = f.Local("t");
        var u = f.Local("u");
        var captured = f.Lambda(HandBuiltModel.Block(Run(f.Call(f.Take, Ref(t))), Declare(u, f.New(2)), f.If(HandBuiltModel.Block(Run(Drop(u))))));
        var plan = Plan(Declare(t, f.New(1)), Run(captured));

        Assert.Equal(LifetimeRelease.AtExit, plan.Release(t));
        Assert.Equal(LifetimeRelease.Guarded, plan.Release(u));
    }

    [Fact]
    public void OnlyAnOwnedLinearTypeIsTracked()
    {
        var task = f.Model.Type("ValueTask<>");
        task.Modifiers = new() { Discipline = SubstructuralDiscipline.Linear };

        Assert.True(LocalLifetimes.IsTracked(f.Token));
        Assert.False(LocalLifetimes.IsTracked(f.Shared));
        Assert.False(LocalLifetimes.IsTracked(f.Once));
        Assert.False(LocalLifetimes.IsTracked(f.Plain));
        Assert.False(LocalLifetimes.IsTracked(f.Model.Interner.GetOrCreateConstructedType(task, [f.Int32])));
    }

    private static Planned Plan(params Statement[] statements)
    {
        var body = HandBuiltModel.Block(statements);
        return new Planned(LocalLifetimes.Plan(body), body);
    }

    private sealed record Planned(LifetimePlan Lifetimes, Block Body)
    {
        public LifetimeRelease Release(LocalVariable local)
        {
            return Lifetimes.ReleaseOf(BodyWalker.DescendantsAndSelf(Body)
                .OfType<LocalDeclarationStatement>()
                .Single(declaration => ReferenceEquals(declaration.Local, local)));
        }

        public IReadOnlyList<LifetimeMark> MarksAfter(Expression node)
        {
            return Lifetimes.MarksAfter(node);
        }
    }
}