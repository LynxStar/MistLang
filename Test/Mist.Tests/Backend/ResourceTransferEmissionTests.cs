// How the C# releases a linear value its plan gives to something other than a block declaration: a
// parameter's or a Drop field's finally around the whole body, a for initializer's around the loop, a with
// releasing its holder after the pop, a lent value's around its statement, and a hand-off that waits for
// its callee to be entered. Asserted on the whole emitted text, as the local emission is.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ResourceTransferEmissionTests
{
    private readonly LifetimeFragments f = new();

    [Theory]
    [InlineData("struct")]
    [InlineData("ref-struct")]
    [InlineData("constructed-struct")]
    [InlineData("class")]
    public void AnAffineValueIsAutomaticallyDroppedOnlyInsideWith(string shape)
    {
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Affine);
        var source = f.Local("source", type);
        var unscoped = Emitted(HandBuiltModel.Block(Declare(source, Fresh(type))));
        var scoped = Emitted(HandBuiltModel.Block(Declare(source, Fresh(type)), With(Ref(source), HandBuiltModel.Block())));

        Assert.DoesNotContain(".Drop()", unscoped, StringComparison.Ordinal);
        Assert.Equal(1, scoped.Split(".Drop()", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("struct", "drop", 1)]
    [InlineData("ref-struct", "drop", 1)]
    [InlineData("constructed-struct", "drop", 1)]
    [InlineData("class", "drop", 1)]
    [InlineData("struct", "initialize", 0)]
    [InlineData("struct", "owned-argument", 0)]
    [InlineData("struct", "owned-field", 0)]
    [InlineData("struct", "array", 0)]
    public void AnAffineWithDoesNotReleaseWhatItsBodyConsumes(string shape, string operation, int drops)
    {
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Affine);
        var source = f.Local("source", type);
        var destination = f.Local("destination", type);
        var scoped = With(Ref(source), HandBuiltModel.Block(f.MoveValue(operation, source, destination)));
        var emitted = Emitted(HandBuiltModel.Block(Declare(source, Fresh(type)), scoped));

        Assert.Equal(drops, emitted.Split(".Drop()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AnAffineScopeFlagEndsBeforeTheBindingIsReassignedAndScopedAgain()
    {
        var source = f.Local("source", f.DisciplinedValue("struct", SubstructuralDiscipline.Affine));
        var first = With(Ref(source), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(Drop(source))))));
        var second = With(Ref(source), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(Drop(source))))));
        var emitted = Emitted(HandBuiltModel.Block(Declare(source, Fresh(source.Type)), first, Run(Assign(source, Fresh(source.Type))), second));

        Assert.DoesNotContain("MistOwnership.Taken", emitted, StringComparison.Ordinal);
        Assert.Equal(2, emitted.Split("MistOwnership.Handed", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, emitted.Split("= true;", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AnAffineWithBindingAndAnAffineParameterUseTheSameScopedGuard()
    {
        var type = f.DisciplinedValue("struct", SubstructuralDiscipline.Affine);
        var binding = f.Local("binding", type);
        var parameter = Owned("parameter", type);
        var boundScope = With(Fresh(type), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(Drop(binding))))), binding);
        var passedScope = With(Ref(parameter), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(DropOf(Ref(parameter)))))));
        var bound = Emitted(HandBuiltModel.Block(boundScope));
        var passed = EmittedMethod(f.Function("Use", HandBuiltModel.Block(passedScope), parameter));

        Assert.Contains("MistOwnership.Handed(binding, ref", bound, StringComparison.Ordinal);
        Assert.Contains("MistOwnership.Handed(parameter, ref", passed, StringComparison.Ordinal);
        Assert.Equal(2, passed.Split(".Drop()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void AParameterDroppedOnOneBranchIsReleasedUnderItsFlag()
    {
        var t = Owned("t", f.Token);

        Assert.Equal(
            """
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
            EmittedMethod(f.Function("Use", HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(DropOf(Ref(t)))))), t)));
    }

    [Fact]
    public void ALeaseParameterIsNotReleasedByItsCallee()
    {
        var t = Owned("t", f.Token, OwnershipKind.Frozen);

        Assert.Equal("Sink.Risky();", EmittedMethod(f.Function("Look", HandBuiltModel.Block(Run(f.Call(f.Risky))), t)));
    }

    /// <summary>
    /// The first field is released first, as a synthesized Drop releases them, and only the one the
    /// body may have dropped is guarded.
    /// </summary>
    [Fact]
    public void ADropReleasesWhatItsBodyLeftHeldInDeclarationOrder()
    {
        var drop = f.HolderDrop(HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(DropOf(Ref(f.Second)))))));

        Assert.Equal(
            """
            var __e1 = true;
            try
            {
                try
                {
                    if (flag)
                    {
                        MistOwnership.Handed(this.Second, ref __e1).Drop();
                    }
                }
                finally
                {
                    this.First.Drop();
                }
            }
            finally
            {
                if (__e1)
                {
                    this.Second.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            EmittedMethod(drop, f.Holder));
    }

    [Fact]
    public void AForInitializerIsReleasedOnceTheLoopIsLeft()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            {
                Token t = new Token(1);
                try
                {
                    for (; flag; )
                    {
                        break;
                    }
                }
                finally
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(f.For(Declare(t, f.New(1)), HandBuiltModel.Block(new BreakStatement())))));
    }

    [Fact]
    public void AWithReleasesItsHolderAfterThePopInsteadOfTheHoldersScope()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            {
                var __e1 = t;
                MistContext.Push<Token>(__e1);
                try
                {
                }
                finally
                {
                    MistContext.Pop<Token>();
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), With(Ref(t), HandBuiltModel.Block()))));
    }

    /// <summary>
    /// The with clears the flag it releases under, so the holder's own scope finds nothing left to drop.
    /// </summary>
    [Fact]
    public void AWithOnOneBranchClearsTheFlagItReleasesUnder()
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
                    {
                        var __e2 = t;
                        MistContext.Push<Token>(__e2);
                        try
                        {
                        }
                        finally
                        {
                            MistContext.Pop<Token>();
                            if (__e1)
                            {
                                __e1 = false;
                                t.Drop();
                            }
                        }
                    }
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
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), f.If(HandBuiltModel.Block(With(Ref(t), HandBuiltModel.Block()))))));
    }

    [Fact]
    public void AWithBindingHandedOnIsReleasedOnlyWhereItWasNot()
    {
        var x = f.Local("x");

        Assert.Equal(
            """
            {
                var x = new Token(1);
                var __e1 = true;
                MistContext.Push<Token>(x);
                try
                {
                    if (flag)
                    {
                        Sink.Take(MistOwnership.Handed(x, ref __e1));
                    }
                }
                finally
                {
                    MistContext.Pop<Token>();
                    if (__e1)
                    {
                        x.Drop();
                    }
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(With(f.New(1), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(f.Call(f.Take, Ref(x)))))), x))));
    }

    [Fact]
    public void AWithOverAFieldLeavesItToItsContainer()
    {
        Assert.DoesNotContain(".Drop()", Emitted(HandBuiltModel.Block(With(Ref(f.First), HandBuiltModel.Block()))), StringComparison.Ordinal);
    }

    [Fact]
    public void AValueLentToAFrozenParameterIsReleasedOnceItsStatementIsDone()
    {
        Assert.Equal(
            """
            Token __e1 = default!;
            var __e2 = false;
            try
            {
                Sink.Peek(MistOwnership.Taken(__e1 = new Token(1), ref __e2));
            }
            finally
            {
                if (__e2)
                {
                    __e1.Drop();
                }
            }
            Sink.Risky();
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Run(f.Call(f.Peek, f.New(1))), Run(f.Call(f.Risky)))));
    }

    /// <summary>
    /// A loop condition lends afresh every time it runs, so the last loan is released as the body
    /// begins and the final one by the statement's finally.
    /// </summary>
    [Fact]
    public void AValueLentByALoopConditionIsReleasedEachIteration()
    {

        Assert.Equal(
            """
            Token __e1 = default!;
            var __e2 = false;
            try
            {
                while (Sink.Looks(MistOwnership.Taken(__e1 = new Token(1), ref __e2)))
                {
                    if (__e2)
                    {
                        __e2 = false;
                        __e1.Drop();
                    }
                    Sink.Risky();
                }
            }
            finally
            {
                if (__e2)
                {
                    __e1.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(new WhileStatement(Looks(f.New(1)), HandBuiltModel.Block(Run(f.Call(f.Risky)))))));
    }

    [Fact]
    public void AHandOffThroughAReferenceClearsTheFlagOnlyWhenTheReceiverIsThere()
    {
        var t = f.Local("t");

        Assert.Equal(
            """
            Token t = new Token(1);
            var __e1 = true;
            try
            {
                MistOwnership.Present(b, out var __e2).Use(MistOwnership.HandedTo(__e2, t, ref __e1));
            }
            finally
            {
                if (__e1)
                {
                    t.Drop();
                }
            }
            """.ReplaceLineEndings("\n"),
            Emitted(HandBuiltModel.Block(Declare(t, f.New(1)), Run(f.CallOn(Ref(new LocalVariable("b", f.Box)), conditional: false, Ref(t))))));
    }

    [Fact]
    public void ADeconstructedFreshElementIsReleasedByItsTarget()
    {
        var a = f.Local("a");
        var n = f.Local("n", f.Int32);
        var emitted = Emitted(HandBuiltModel.Block(new TupleDeconstructionStatement([a, n], f.Tuple(f.New(1), HandBuiltModel.Literal(f.Int32, 2)))));

        Assert.EndsWith(
            """
            try
            {
            }
            finally
            {
                a.Drop();
            }
            """.ReplaceLineEndings("\n"),
            emitted,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOwnershipHelpersCarryARefStructAndKeepTheFlagUnescaped()
    {
        var writer = new CSharpWriter();
        LifetimeLowering.Emit(writer);
        var helpers = writer.ToString();

        Assert.Contains("public static T Handed<T>(T value, scoped ref bool owned) where T : allows ref struct", helpers, StringComparison.Ordinal);
        Assert.Contains("public static T Taken<T>(T value, scoped ref bool owned) where T : allows ref struct", helpers, StringComparison.Ordinal);
    }

    private InvocationExpression Looks(Expression lent)
    {
        var looks = new Method("Looks", f.Boolean, [Owned("t", f.Token, OwnershipKind.Frozen)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        return f.Call(looks, lent);
    }
}