// Who releases a linear value that is not a block's own declaration, and where the obligation moves, over
// bodies built by hand. Each reference is matched by the binding it reaches, not by its name.

using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ResourceTransferPlanTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void AnOwnedParameterDroppedOnOneBranchIsReleasedOnTheOther()
    {
        var t = Owned("t", f.Token);
        var dropped = Ref(t);
        var use = f.Function("Use", HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(DropOf(dropped))))), t);
        var plan = LocalLifetimes.Plan(use.Body!, use);

        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(t));
        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(dropped));
    }

    [Fact]
    public void AConsumedParameterOwesCleanupOnlyWhenAssignedAFreshValue()
    {
        var parameter = Owned("t", f.Token);
        var flag = new Parameter("replace", f.Boolean);
        var dropped = Ref(parameter);
        var condition = Ref(flag);
        var replacement = Assign(dropped.Local, f.New(2));
        var body = HandBuiltModel.Block(
            Run(DropOf(dropped)),
            new IfStatement(condition, HandBuiltModel.Block(Run(replacement)), null));
        var method = f.Function("ReplaceIf", body, parameter, flag);

        var plan = LocalLifetimes.Plan(body, method);

        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(parameter));
        Assert.Equal([new LifetimeMark(parameter, Owned: false)], plan.MarksAfter(dropped));
        Assert.Empty(plan.MarksAfter(condition));
        Assert.Equal([new LifetimeMark(parameter, Owned: true)], plan.MarksAfter(replacement));
    }

    [Fact]
    public void APlainParameterIsReleasedExactlyWhereItIsStillHeld()
    {
        var kept = new Parameter("t", f.Token);
        var returned = new Parameter("t", f.Token);
        var passed = new Parameter("t", f.Token);

        Assert.Equal(LifetimeRelease.AtExit, Release(kept, HandBuiltModel.Block(Run(f.Call(f.Risky)))));
        Assert.Equal(LifetimeRelease.Guarded, Release(returned, HandBuiltModel.Block(f.If(HandBuiltModel.Block(Return(Ref(returned)))), Return(f.New(2)))));
        Assert.Equal(LifetimeRelease.None, Release(passed, HandBuiltModel.Block(Run(f.Call(f.Take, Ref(passed))))));
    }

    [Fact]
    public void AParameterLeftByAnEarlyReturnOrMovedIntoALocalIsReleasedByWhoeverHoldsIt()
    {
        var returning = Owned("t", f.Token);
        var moving = Owned("t", f.Token);
        var destination = f.Local("u");
        var early = f.Function("Early", HandBuiltModel.Block(f.If(HandBuiltModel.Block(new ReturnStatement(null, null))), Run(DropOf(Ref(returning)))), returning);
        var moved = f.Function("Moved", HandBuiltModel.Block(Declare(destination, Ref(moving)), Run(f.Call(f.Risky))), moving);
        var movedPlan = LocalLifetimes.Plan(moved.Body!, moved);

        Assert.Equal(LifetimeRelease.Guarded, LocalLifetimes.Plan(early.Body!, early).ReleaseOf(returning));
        Assert.Equal([LifetimeRelease.None, LifetimeRelease.AtExit], [movedPlan.ReleaseOf(moving), movedPlan.ReleaseOf(destination)]);
    }

    [Fact]
    public void ALocalShadowingAParameterLeavesTheParameterReleasedAtExit()
    {
        var t = Owned("t", f.Token);
        var inner = f.Local("t");
        var use = f.Function("Use", HandBuiltModel.Block(HandBuiltModel.Block(Declare(inner, f.New(5)), Run(Drop(inner)))), t);
        var plan = LocalLifetimes.Plan(use.Body!, use);

        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(t));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(inner));
    }

    [Fact]
    public void ALeaseParameterIsNeverReleasedByItsCallee()
    {
        var frozen = Owned("f", f.Token, OwnershipKind.Frozen);
        var borrowed = Owned("b", f.Token, OwnershipKind.Borrowed);
        var lent = new Parameter("r", f.Token, new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted));
        var use = f.Function("Use", HandBuiltModel.Block(Run(f.Call(f.Risky))), frozen, borrowed, lent);
        var plan = LocalLifetimes.Plan(use.Body!, use);

        Assert.Equal(
            [LifetimeRelease.None, LifetimeRelease.None, LifetimeRelease.None],
            [plan.ReleaseOf(frozen), plan.ReleaseOf(borrowed), plan.ReleaseOf(lent)]);
    }

    [Fact]
    public void ADropReleasesAFieldOnTheBranchThatDidNotAndEveryUntouchedOne()
    {
        var dropped = Ref(f.Second);
        var drop = f.HolderDrop(HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(DropOf(dropped))))));
        var plan = LocalLifetimes.Plan(drop.Body!, drop, f.Holder);

        Assert.Equal([LifetimeRelease.AtExit, LifetimeRelease.Guarded], [plan.ReleaseOf(f.First), plan.ReleaseOf(f.Second)]);
        Assert.Equal([new LifetimeMark(f.Second, Owned: false)], plan.MarksAfter(dropped));
    }

    [Fact]
    public void AFieldTheDropHandsBackIsNotReleased()
    {
        var drop = f.HolderDrop(HandBuiltModel.Block(Return(Ref(f.First))));
        var plan = LocalLifetimes.Plan(drop.Body!, drop, f.Holder);

        Assert.Equal([LifetimeRelease.None, LifetimeRelease.AtExit], [plan.ReleaseOf(f.First), plan.ReleaseOf(f.Second)]);
    }

    [Fact]
    public void AForInitializerIsReleasedOnceTheLoopIsLeft()
    {
        var kept = f.Local("t");
        var handed = f.Local("u");
        var broken = Plan(f.For(Declare(kept, f.New(1)), HandBuiltModel.Block(new BreakStatement())));
        var passed = Plan(f.For(Declare(handed, f.New(1)), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(f.Call(f.Take, Ref(handed))), new BreakStatement())))));

        Assert.Equal(LifetimeRelease.AtExit, broken.ReleaseOf(kept));
        Assert.Equal(LifetimeRelease.Guarded, passed.ReleaseOf(handed));
    }

    [Fact]
    public void AWithGivenAHolderReleasesTheInstalledInstanceNotTheHolder()
    {
        var t = f.Local("t");
        var scope = With(Ref(t), HandBuiltModel.Block(Run(f.Call(f.Risky))));
        var plan = Plan(Declare(t, f.New(1)), scope);

        Assert.Null(plan.ReleaseAt(scope));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(t));
    }

    [Fact]
    public void AWithOnOneBranchLeavesTheHolderOwingOnTheOther()
    {
        var t = f.Local("t");
        var scope = With(Ref(t), HandBuiltModel.Block());
        var plan = Plan(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(scope)));

        Assert.Null(plan.ReleaseAt(scope));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(t));
    }

    [Fact]
    public void ALoanInsideItsWithDoesNotDischargeTheScopesRelease()
    {
        var t = f.Local("t");
        var scope = With(Ref(t), HandBuiltModel.Block(Run(f.Call(f.Peek, Ref(t)))));
        var plan = Plan(Declare(t, f.New(1)), scope);

        Assert.Null(plan.ReleaseAt(scope));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(t));
    }

    [Fact]
    public void AWithBindingReleasesItsFreshValueAtScopeExit()
    {
        var x = f.Local("x");
        var scope = With(f.New(1), HandBuiltModel.Block(Run(f.Call(f.Risky))), x);
        var plan = Plan(scope);

        Assert.Null(plan.ReleaseAt(scope));
        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(x));
    }

    [Fact]
    public void AWithBindingOfAHolderIsThatHolder()
    {
        var t = f.Local("t");
        var x = f.Local("x");
        var scope = With(Ref(t), HandBuiltModel.Block(Run(f.Call(f.Peek, Ref(x)))), x);
        var plan = Plan(Declare(t, f.New(1)), scope);

        Assert.False(plan.Covers(x));
        Assert.Null(plan.ReleaseAt(scope));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(t));
    }

    [Fact]
    public void AWithOverALeaseOrAFieldOwnsNothing()
    {
        var t = f.Local("t");
        var v = f.Local("v", ownership: OwnershipKind.Frozen);
        var x = f.Local("x");
        var y = f.Local("y");
        var leased = With(Ref(v), HandBuiltModel.Block(), x);
        var viewed = With(Ref(f.First), HandBuiltModel.Block(), y);
        var plan = Plan(Declare(t, f.New(1)), Declare(v, Ref(t)), leased, viewed);

        Assert.Equal([false, false], [plan.Covers(x), plan.Covers(y)]);
        Assert.Null(plan.ReleaseAt(leased));
        Assert.Null(plan.ReleaseAt(viewed));
        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(t));
    }

    [Fact]
    public void AnAffineWithBindingLeftOnlyByAReturnIsReleasedByItsScope()
    {
        var x = Bound("x");
        var scope = With(f.New(1, f.Once), HandBuiltModel.Block(new ReturnStatement(null, null)), x);

        Assert.Equal(Released(x), Plan(scope).ReleaseAt(scope));
    }

    /// <summary>
    /// The loop never ends, so a throw from the call is the only way out of the scope and the only step
    /// that can say the scope still owes its release.
    /// </summary>
    [Fact]
    public void AnAffineWithBindingLeftOnlyByAThrowIsReleasedByItsScope()
    {
        var x = Bound("x");
        var scope = With(f.New(1, f.Once), HandBuiltModel.Block(Forever(Run(f.Call(f.Risky)))), x);

        Assert.Equal(Released(x), Plan(scope).ReleaseAt(scope));
    }

    [Fact]
    public void NestedAffineWithBindingsAreEachReleasedByTheirOwnScope()
    {
        var x = Bound("x");
        var y = Bound("y");
        var inner = With(f.New(2, f.Once), HandBuiltModel.Block(), y);
        var outer = With(f.New(1, f.Once), HandBuiltModel.Block(inner), x);
        var plan = Plan(outer);

        Assert.Equal([Released(x), Released(y)], [plan.ReleaseAt(outer), plan.ReleaseAt(inner)]);
    }

    [Fact]
    public void AThrowInsideNestedWithsLeavesEachScopeReleasingItsOwnBinding()
    {
        var x = Bound("x");
        var y = Bound("y");
        var inner = With(f.New(2, f.Once), HandBuiltModel.Block(Forever(Run(f.Call(f.Risky)))), y);
        var outer = With(f.New(1, f.Once), HandBuiltModel.Block(inner), x);
        var plan = Plan(outer);

        Assert.Equal([Released(x), Released(y)], [plan.ReleaseAt(outer), plan.ReleaseAt(inner)]);
    }

    [Fact]
    public void APropagatedResultTakenFromAPropagatedResultTakesOverItsCarriersRelease()
    {
        var shapes = new ResultModel();
        var inner = shapes.Result(f.Token, shapes.Text);
        var made = new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(f.Int32), [], shapes.Result(inner, shapes.Text), null, null);
        var unwrapped = shapes.Propagate(made, inner);
        var taken = f.Local("taken");
        var plan = Plan(Declare(taken, shapes.Propagate(unwrapped, inner)));

        Assert.Contains(plan.MarksAfter(unwrapped), mark => ReferenceEquals(mark.Owner, made) && !mark.Owned);
        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(taken));
    }

    [Fact]
    public void HandingOnAPatternBindingHandsOnTheHolderItViews()
    {
        var k = f.Local("k");
        var t = f.Local("t");
        var passed = Ref(t);
        var plan = Plan(Declare(k, f.New(1)), new IfStatement(f.Is(Ref(k), t), HandBuiltModel.Block(Run(f.Call(f.Take, passed))), null));

        Assert.False(plan.Covers(t));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(k));
        Assert.Equal([new LifetimeMark(k, Owned: false)], plan.MarksAfter(passed));
    }

    [Fact]
    public void ADeconstructedElementIsOwnedByItsTargetAsADeclarationWouldBe()
    {
        var t = f.Local("t");
        var a = f.Local("a");
        var n = f.Local("n", f.Int32);
        var b = f.Local("b");
        var m = f.Local("m", f.Int32);
        var handed = Ref(t);
        var plan = Plan(
            Declare(t, f.New(1)),
            new TupleDeconstructionStatement([a, n], f.Tuple(handed, HandBuiltModel.Literal(f.Int32, 2))),
            new TupleDeconstructionStatement([b, m], f.Tuple(f.New(3), HandBuiltModel.Literal(f.Int32, 4))));

        Assert.Equal(
            [LifetimeRelease.None, LifetimeRelease.AtExit, LifetimeRelease.AtExit],
            [plan.ReleaseOf(t), plan.ReleaseOf(a), plan.ReleaseOf(b)]);
        Assert.False(plan.Covers(n));
    }

    [Fact]
    public void StoringAHolderInAFieldHandsItToTheContainer()
    {
        var t = f.Local("t");

        Assert.Equal(LifetimeRelease.None, Plan(Declare(t, f.New(1)), Run(Store(f.First, Ref(t)))).ReleaseOf(t));
    }

    /// <summary>
    /// The call is skipped when its receiver is absent, leaving the value with the caller: the release
    /// must survive under a flag only the call's own argument clears.
    /// </summary>
    [Fact]
    public void AConditionalCallLeavesWhatItWasHandedOwnedUntilItRuns()
    {
        var t = f.Local("t");
        var passed = Ref(t);
        var plan = Plan(Declare(t, f.New(1)), Run(f.CallOn(Ref(new LocalVariable("b", f.Box)), conditional: true, passed)));

        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(t));
        Assert.Equal([new LifetimeMark(t, Owned: false)], plan.MarksAfter(passed));
    }

    [Fact]
    public void ACallThroughAReferenceHandsOnOnlyOnceItIsEntered()
    {
        var t = f.Local("t");
        var passed = Ref(t);
        var call = f.CallOn(Ref(new LocalVariable("b", f.Box)), conditional: false, passed);
        var plan = Plan(Declare(t, f.New(1)), Run(call));

        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(t));
        Assert.Equal([new LifetimeMark(t, Owned: false, call)], plan.MarksAfter(passed));
        Assert.True(plan.Gates(call));
    }

    [Fact]
    public void ACallOnThisIsAlwaysEntered()
    {
        var t = f.Local("t");
        var call = f.CallOn(new ThisExpression(HandBuiltModel.Node(), f.Box), conditional: false, Ref(t));

        Assert.Equal(LifetimeRelease.None, Plan(Declare(t, f.New(1)), Run(call)).ReleaseOf(t));
    }

    private LocalVariable Bound(string name)
    {
        return new LocalVariable(name, f.Once) { IsContextBound = true };
    }

    private WhileStatement Forever(params Statement[] body)
    {
        return new WhileStatement(HandBuiltModel.Literal(f.Boolean, true), HandBuiltModel.Block(body));
    }

    private static (object Owner, LifetimeRelease Release)? Released(LocalVariable binding)
    {
        return (binding, LifetimeRelease.AtExit);
    }

    private LifetimeRelease Release(Parameter parameter, Block body)
    {
        var method = f.Function("Use", body, parameter);
        return LocalLifetimes.Plan(body, method).ReleaseOf(parameter);
    }

    private static LifetimePlan Plan(params Statement[] statements)
    {
        return LocalLifetimes.Plan(HandBuiltModel.Block(statements));
    }
}