// The linear task is the value here because a relevant one handed on is deliberately absent: drop-semantics
// Requirements 4.4, 6.3 and 7.6 keep a relevant local's scope-exit drop wherever its value went, and the
// compiler elides it, a disagreement recorded in Carbonek rather than pinned.

using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ExitDropMoveTests
{
    private readonly LifetimeFragments f = new();
    private readonly ClassType valueTask;
    private readonly ConstructedType pending;
    private readonly Method produce;

    public ExitDropMoveTests()
    {
        valueTask = f.Model.Type("ValueTask<>");
        valueTask.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear, IsIntrinsic = true };
        var result = new TypeParameter("TResult", 0, valueTask.Scope, valueTask);
        valueTask.TypeParametersList.Add(result);
        valueTask.MethodsList.Add(new Method("Drop", result, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = valueTask });
        pending = new ConstructedType(valueTask, [f.Int32]);
        produce = new Method("Produce", pending, [], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
    }

    [Fact]
    public void ALinearTaskNeverHandedOnIsAwaitedAtExitFromItsInitializer()
    {
        var t = f.Local("t", pending);
        var initializer = f.Call(produce);
        var declaration = Declare(t, initializer);

        var exit = Assert.Single(PurityJudge.ExitDrops(HandBuiltModel.Block(declaration, Run(f.Call(f.Risky))), member: null));

        Assert.Equal("t", exit.Name);
        Assert.True(exit.IsAwait);
        Assert.Same(valueTask.MethodsList.Single(), exit.Drop);
        Assert.Same(initializer.Node, exit.At);
        Assert.Same(declaration, exit.Declaration);
    }

    [Fact]
    public void ALinearTaskReturnedOrExplicitlyAwaitedIsNotAwaitedAgainAtExit()
    {
        var returned = f.Local("t", pending);
        var awaited = f.Local("t", pending);

        Assert.Empty(Dropped(Declare(returned, f.Call(produce)), Return(Ref(returned))));
        Assert.Empty(Dropped(Declare(awaited, f.Call(produce)), Run(Drop(awaited))));
    }

    [Fact]
    public void ALinearTaskMovedIntoAnOwnedParameterLeavesTheCallerButOneLentByRefStays()
    {
        var take = Callee(new ParameterModifier(IsReadonly: false, IsRef: false, OwnershipKind.Owned));
        var lend = Callee(new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted));
        var moved = f.Local("t", pending);
        var lent = f.Local("t", pending);

        Assert.Empty(Dropped(Declare(moved, f.Call(produce)), Run(f.Call(take, Ref(moved)))));
        Assert.Equal(["t"], Dropped(Declare(lent, f.Call(produce)), Run(f.Call(lend, Ref(lent)))));
    }

    [Fact]
    public void ALinearTaskMovedIntoAConstructionLeavesTheCaller()
    {
        var holder = HandBuiltModel.Class("Waiter");
        var constructor = new Constructor("Waiter", holder, [Owned("t", pending)], default) { DeclaringType = holder };
        var t = f.Local("t", pending);

        Assert.Empty(Dropped(Declare(t, f.Call(produce)), Run(new NewExpression(HandBuiltModel.Node(), holder, [Ref(t)], constructor, [], [pending]))));
    }

    [Fact]
    public void OnlyTheDisciplinesThatRequireADropOweOneAtExit()
    {
        var plain = f.Local("p", f.Plain);
        var once = f.Local("o", f.Once);
        var shared = f.Local("s", f.Shared);

        Assert.Equal(["s"], Dropped(Declare(plain, f.New(1, f.Plain)), Declare(once, f.New(2, f.Once)), Declare(shared, f.New(3, f.Shared))));
    }

    [Fact]
    public void ALocalDeclaredInANestedLambdaOrLocalFunctionIsDroppedByThatScopeAlone()
    {
        var captured = f.Local("inner", f.Shared);
        var lambda = f.Lambda(HandBuiltModel.Block(Declare(captured, f.New(1, f.Shared))));
        var nested = f.Local("nested", f.Shared);
        var function = new LocalFunctionStatement(f.Function("Local", HandBuiltModel.Block(Declare(nested, f.New(2, f.Shared)))), []);
        var body = HandBuiltModel.Block(Run(lambda), function);

        Assert.Empty(PurityJudge.ExitDrops(body, member: null));
        Assert.Equal(["inner"], PurityJudge.ExitDrops(lambda, member: null).Select(exit => exit.Name));
        Assert.Equal(["nested"], PurityJudge.ExitDrops(function, member: null).Select(exit => exit.Name));
    }

    [Fact]
    public void ARelevantParameterIsDroppedWhereItsFunctionLambdaOrLocalFunctionIsDeclared()
    {
        var parameter = new Parameter("s", f.Shared);
        var method = f.Function("Use", HandBuiltModel.Block(Run(f.Call(f.Risky))), parameter);
        var local = new LocalFunctionStatement(f.Function("Local", HandBuiltModel.Block(Run(f.Call(f.Risky))), new Parameter("s", f.Shared)), []);
        var argument = f.Local("s", f.Shared);
        var lambda = new LambdaExpression(
            HandBuiltModel.Node(), [argument], null, HandBuiltModel.Block(Run(f.Call(f.Risky))), false, f.Model.Interner.GetOrCreateFunctionType([f.Shared], f.Void));

        DroppedAt(method.DeclarationNode!, method.Body!, method);
        DroppedAt(local.Function.DeclarationNode!, local, member: null);
        DroppedAt(lambda.Node, lambda, member: null);
    }

    [Fact]
    public void AParameterIsNotDroppedAtANestedBlocksExitOrByAFunctionItWasLentTo()
    {
        var inner = HandBuiltModel.Block(Run(f.Call(f.Risky)));
        var method = f.Function("Use", HandBuiltModel.Block(inner), new Parameter("s", f.Shared));
        var lent = f.Function("Look", HandBuiltModel.Block(Run(f.Call(f.Risky))), new Parameter("t", f.Token, new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted)));

        Assert.Empty(PurityJudge.ExitDrops(inner, method));
        Assert.Empty(PurityJudge.ExitDrops(method.Body!, member: null));
        Assert.Empty(PurityJudge.ExitDrops(lent.Body!, lent));
    }

    private Method Callee(ParameterModifier modifier)
    {
        return new Method("Await", f.Void, [new Parameter("t", pending, modifier)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
    }

    private static void DroppedAt(SyntaxNode declared, object root, object? member)
    {
        var exit = Assert.Single(PurityJudge.ExitDrops(root, member));

        Assert.Equal("s", exit.Name);
        Assert.False(exit.IsAwait);
        Assert.Same(declared, exit.At);
    }

    private static IEnumerable<string> Dropped(params Statement[] statements)
    {
        return PurityJudge.ExitDrops(HandBuiltModel.Block(statements), member: null).Select(exit => exit.Name);
    }
}