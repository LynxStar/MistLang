// What the C# does with a linear local's obligation: which finally releases it, under which flag, and
// at which node that flag changes. Asserted on the whole emitted body, because a release can be
// present and still wrong -- on the wrong path, in the wrong order, or after the hand-off it should
// have seen -- and only the full text shows which. No stdout: none of the exceptional paths here is
// observable in a Mist program, which has no way to catch.

using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class LocalLifetimeEmissionTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void ALocalTransferReleasesOnlyTheDestination()
    {
        var t = f.Local("t");
        var r = f.Local("r");

        Assert.Equal(
            """
            Token t = new Token(1);
            Token r = t;
            try
            {
            }
            finally
            {
                r.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Declare(r, Ref(t)))));
    }

    [Fact]
    public void ADropOnOneBranchClearsTheFlagThatGuardsTheRelease()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            var __e1 = true;
            try
            {
                if (flag)
                {
                    MistOwnership.Handed(t, ref __e1).Drop();
                }
            }
            finally
            {
                if (__e1)
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Run(Drop(t)))))));
    }

    [Fact]
    public void AReturnOnOneBranchHandsTheValueOnAndTheOtherPathReleasesIt()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            var __e1 = true;
            try
            {
                if (flag)
                {
                    return MistOwnership.Handed(t, ref __e1);
                }
                return new Token(2);
            }
            finally
            {
                if (__e1)
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(Return(Ref(t)))), Return(f.New(2)))));
    }

    /// <summary>
    /// The flag is set only once the assignment has stored the value: a throw from the initializer
    /// finds it still clear, the first value already gone and the second never made.
    /// </summary>
    [Fact]
    public void AConsumedBindingTakesItsFreshValueAfterTheAssignment()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            var __e1 = true;
            try
            {
                MistOwnership.Handed(t, ref __e1).Drop();
                MistOwnership.Taken(t = new Token(2), ref __e1);
            }
            finally
            {
                if (__e1)
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Run(Drop(t)), Run(Assign(t, f.New(2))))));
    }

    [Fact]
    public void ADestinationDeclaredWithoutAValueStartsUnowned()
    {
        var r = f.Local("r");
        var t = f.Local("t");

        Assert.Equal(
            """
            Token r = default!;
            var __e1 = false;
            try
            {
                {
                    Token t = new Token(1);
                    MistOwnership.Taken(r = t, ref __e1);
                }
                Sink.Risky();
            }
            finally
            {
                if (__e1)
                {
                    r.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(
                Declare(r, null),
                HandBuiltModel.Block(Declare(t, f.New(1)), Run(Assign(r, Ref(t)))),
                Run(f.Call(f.Risky)))));
    }

    /// <summary>
    /// The value is handed on once the argument after it that may still throw has been evaluated, so a
    /// throw from that argument finds it the caller's to release.
    /// </summary>
    [Fact]
    public void AnArgumentIsHandedOnAfterTheLastArgumentThatMayThrow()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            var __e1 = true;
            try
            {
                Sink.Pair(t, MistOwnership.Handed(Sink.Risky(), ref __e1));
            }
            finally
            {
                if (__e1)
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Run(f.Call(f.Pair, Ref(t), f.Call(f.Risky))))));
    }

    [Fact]
    public void AValueHandedOnBeforeAnythingCanThrowNeedsNoRelease()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            Sink.Take(t);
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Run(f.Call(f.Take, Ref(t))))));
    }

    /// <summary>
    /// Each release sits inside the try of every binding declared before it, guarded or not, so the last
    /// declared is the first released.
    /// </summary>
    [Fact]
    public void ReleasesStayInReverseDeclarationOrder()
    {
        var first = f.Local("first");
        var second = f.Local("second");
        var third = f.Local("third");

        Assert.Equal(
            """
            Token first = new Token(1);
            try
            {
                Token second = new Token(2);
                var __e1 = true;
                try
                {
                    Token third = new Token(3);
                    try
                    {
                        if (flag)
                        {
                            MistOwnership.Handed(second, ref __e1).Drop();
                        }
                    }
                    finally
                    {
                        third.Drop();
                    }
                }
                finally
                {
                    if (__e1)
                    {
                        second.Drop();
                    }
                }
            }
            finally
            {
                first.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(
                Declare(first, f.New(1)),
                Declare(second, f.New(2)),
                Declare(third, f.New(3)),
                f.If(HandBuiltModel.Block(Run(Drop(second)))))));
    }

    [Fact]
    public void ALoopLocalIsReleasedPerIterationAndOnBreak()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            while (flag)
            {
                Token t = new Token(1);
                var __e1 = true;
                try
                {
                    if (flag)
                    {
                        break;
                    }
                    Sink.Take(MistOwnership.Handed(t, ref __e1));
                }
                finally
                {
                    if (__e1)
                    {
                        t.Drop();
                    }
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(f.While(HandBuiltModel.Block(
                Declare(t, f.New(1)),
                f.If(HandBuiltModel.Block(new BreakStatement())),
                Run(f.Call(f.Take, Ref(t))))))));
    }

    [Fact]
    public void LendingKeepsAnUnconditionalRelease()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            try
            {
                Sink.Lend(t);
                Sink.Risky();
            }
            finally
            {
                t.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Run(f.Call(f.Lend, Ref(t))), Run(f.Call(f.Risky)))));
    }

    [Fact]
    public void ALeaseReleasesNothingAndItsLenderReleasesOnce()
    {
        var t = f.Local("t");
        var v = f.Local("v", ownership: OwnershipKind.Frozen);

        Assert.Equal(
            """
            Token t = new Token(1);
            try
            {
                Token v = t;
                Sink.Risky();
            }
            finally
            {
                t.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Declare(v, Ref(t)), Run(f.Call(f.Risky)))));
    }

    /// <summary>
    /// Relevant is aliasable and dropped through every reference; affine and unrestricted are never
    /// dropped at scope exit. None of the three has its obligation moved by a local transfer.
    /// </summary>
    [Fact]
    public void OtherDisciplinesKeepTheirScopeExitRules()
    {
        var s = f.Local("s", f.Shared);
        var alias = f.Local("alias", f.Shared);
        var once = f.Local("once", f.Once);
        var plain = f.Local("plain", f.Plain);

        Assert.Equal(
            """
            Shared s = new Shared(1);
            try
            {
                Shared alias = s;
                try
                {
                    Once once = new Once(2);
                    Plain plain = new Plain(3);
                }
                finally
                {
                    alias.Drop();
                }
            }
            finally
            {
                s.Drop();
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(
                Declare(s, f.New(1, f.Shared)),
                Declare(alias, Ref(s)),
                Declare(once, f.New(2, f.Once)),
                Declare(plain, f.New(3, f.Plain)))));
    }

    /// <summary>
    /// A task keeps the obligation its promotion reads: a Task or ValueTask held to the end is awaited
    /// unconditionally beside a guarded linear local, one awaited by its own Drop is not awaited again,
    /// and every one owing something at its end is among the bindings a promotion reads.
    /// </summary>
    [Fact]
    public void TasksBesideAGuardedLinearLocalKeepTheirCompletion()
    {
        var task = Task("Task<>", SubstructuralDiscipline.Relevant);
        var valueTask = Task("ValueTask<>", SubstructuralDiscipline.Linear);
        var pending = f.Local("pending", task);
        var joined = f.Local("joined", valueTask);
        var awaited = f.Local("awaited", valueTask);
        var t = f.Local("t");
        var body = HandBuiltModel.Block(
            Declare(pending, f.Call(Producer(task))),
            Declare(joined, f.Call(Producer(valueTask))),
            Declare(awaited, f.Call(Producer(valueTask))),
            Declare(t, f.New(1)),
            Run(Drop(awaited)),
            f.If(HandBuiltModel.Block(Run(Drop(t)))));

        var emitted = Emitted(body);

        Assert.Equal(
            ["await awaited;", "if (__e1)", "t.Drop();", "await joined;", "await pending;"],
            emitted.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("await ", StringComparison.Ordinal) || line is "if (__e1)" or "t.Drop();"));
        Assert.Equal(["pending", "joined", "t"], DropInsertion.ReleasedBindings(body).Select(declaration => declaration.Local.Name));
    }

    private IMistType Task(string definition, SubstructuralDiscipline discipline)
    {
        var type = f.Model.Type(definition);
        type.Modifiers = new() { Discipline = discipline, IsIntrinsic = true };

        if(!type.MethodsList.Exists(method => method.Name == "Drop"))
        {
            type.MethodsList.Add(new Method("Drop", f.Int32, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = type });
        }

        return f.Model.Interner.GetOrCreateConstructedType(type, [f.Int32]);
    }

    private Method Producer(IMistType returns)
    {
        var method = new Method("Produce", returns, [], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        return method;
    }
}