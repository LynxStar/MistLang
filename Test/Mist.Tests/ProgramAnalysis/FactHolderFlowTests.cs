using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactHolderFlowTests
{
    internal const int Last = 40;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefArgumentEndsTheLocalOnlyAfterTheCall(bool needAfter)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(5L)), f.Need(FactHolderFragments.Read(a)),
            HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(a)))];
        if(needAfter)
        {
            body.Add(f.Need(FactHolderFragments.Read(a, Last)));
        }

        AssertRefusedOnlyAfter(f, body, needAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWriteThroughARefLocalEndsTheLocalItNamesOnlyThere(bool needAfter)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(5L)), f.Need(FactHolderFragments.Read(a)),
            FactHolderFragments.Declare(r, FactHolderFragments.Read(a)), FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(6L))];
        if(needAfter)
        {
            body.Add(f.Need(FactHolderFragments.Read(a, Last)));
        }

        AssertRefusedOnlyAfter(f, body, needAfter);
    }

    [Fact]
    public void AWriteToTheNamedLocalEndsTheRefLocal()
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(5L)), FactHolderFragments.Declare(r, FactHolderFragments.Read(a)),
            f.Need(FactHolderFragments.Read(r)), FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(6L)), f.Need(FactHolderFragments.Read(r, Last))];

        AssertRefusedOnlyAfter(f, body, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALocalALocalFunctionWritesKeepsOnlyItsTypeWhetherOrNotTheFunctionIsCalled(bool called)
    {
        var f = new FactHolderFragments();
        var d = FactHolderFragments.Int(f.H, "d");
        var write = f.Method("W", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(d), f.Number(6L))));
        List<Statement> body = [FactHolderFragments.Declare(d, f.Number(5L)), new LocalFunctionStatement(write, [])];
        if(called)
        {
            body.Add(HandBuiltRefinements.Evaluate(f.Free(write)));
        }

        body.Add(f.Need(FactHolderFragments.Read(d, Last)));

        AssertRefusedOnlyAfter(f, body, true);
    }

    [Theory]
    [InlineData("handed-to-that-function", true)]
    [InlineData("never-handed-by-ref", false)]
    public void ALocalANestedFunctionReadsEndsThereAtAWriteThroughThatFunctionsRefParameterOnlyIfItMayBeItsStorage(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var b = FactHolderFragments.Int(f.H, "b");
        var p = f.ByRef("p");
        var write = f.Method("W", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.If(f.H.Binary(FactHolderFragments.Read(a), BinaryOperatorKind.Equal, f.Number(5L)),
            FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(1L)), f.Need(FactHolderFragments.Read(a, Last)))), f.H.Int, p.Parameter!);
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(5L)), FactHolderFragments.Declare(b, f.Number(0L)), new LocalFunctionStatement(write, [p]),
            HandBuiltRefinements.Evaluate(f.Free(write, FactHolderFragments.Read(form == "handed-to-that-function" ? a : b)))];

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("field-of-this", false)]
    [InlineData("field-of-this", true)]
    [InlineData("ref-parameter", false)]
    [InlineData("ref-parameter", true)]
    [InlineData("local-a-nested-function-reads", false)]
    [InlineData("local-a-nested-function-reads", true)]
    public void AHolderHandedOutByRefKeepsItsFactsUpToTheCall(string holder, bool needAfter)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var p = f.ByRef("p");
        Expression place(int offset = 0)
        {
            return holder switch
            {
                "field-of-this" => FactHolderFragments.Own(f.A, offset),
                "ref-parameter" => FactHolderFragments.Read(p, offset),
                _ => FactHolderFragments.Read(a, offset),
            };
        }

        var reader = f.Method("Read", f.Pair, HandBuiltModel.Block(new ReturnStatement(FactHolderFragments.Read(a), f.H.Int)), f.H.Int);
        List<Statement> body = [FactHolderFragments.Set(place(), f.Number(5L)), f.Need(place()), HandBuiltRefinements.Evaluate(f.Free(f.Bump(), place()))];
        if(holder == "local-a-nested-function-reads")
        {
            body.InsertRange(0, [FactHolderFragments.Declare(a, f.Number(0L)), new LocalFunctionStatement(reader, [])]);
        }

        if(needAfter)
        {
            body.Add(f.Need(place(Last)));
        }

        AssertRefusedOnlyAfter(f, body, needAfter);
    }

    [Theory]
    [InlineData("named-by-a-ref-local", "Int32", true)]
    [InlineData("handed-by-ref", "Int32", true)]
    [InlineData("handed-by-ref", "Int64", false)]
    public void AFieldHandedOutByRefEndsAtAWriteThroughARefParameterThatMayNameIt(string handed, string parameter, bool refused)
    {
        var f = new FactHolderFragments();
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var p = f.ByRef("p", f.H.Built.Type(parameter));
        List<Statement> body = [handed == "named-by-a-ref-local" ? FactHolderFragments.Declare(r, FactHolderFragments.Own(f.A))
                : HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Own(f.A))),
            FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)), FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(1L)),
            f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("calls-a-function-writing-the-local", true, false, true)]
    [InlineData("calls-a-function-writing-the-local", true, true, true)]
    [InlineData("calls-a-function-writing-the-local", false, false, true)]
    [InlineData("writes-the-local-itself", false, false, true)]
    [InlineData("calls-a-function-writing-another-local", true, false, false)]
    public void AWriteToALocalOutsideANestedFunctionEndsItsRefParametersThatMayNameIt(string form, bool handed, bool loop, bool refused)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var b = FactHolderFragments.Int(f.H, "b");
        var x = f.ByRef("x");
        var increment = f.Method("Inc", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(form == "calls-a-function-writing-another-local" ? b : a), f.Number(6L))));
        var write = form == "writes-the-local-itself" ? FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(6L)) : HandBuiltRefinements.Evaluate(f.Free(increment));
        var need = f.Need(FactHolderFragments.Read(x, Last));
        List<Statement> inner = handed ? [FactHolderFragments.Set(FactHolderFragments.Read(x), f.Number(5L)), HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(x)))] : [];
        inner.Add(FactHolderFragments.Set(FactHolderFragments.Read(x), f.Number(5L)));
        inner.AddRange(loop ? [new WhileStatement(HandBuiltRefinements.Local(f.H.Bool, "flag"), HandBuiltModel.Block(need, write))] : [write, need]);
        var m = f.Method("M", f.Pair, HandBuiltModel.Block([.. inner]), f.H.Int, x.Parameter!);
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(0L)), FactHolderFragments.Declare(b, f.Number(0L)), new LocalFunctionStatement(increment, []),
            new LocalFunctionStatement(m, [x]), HandBuiltRefinements.Evaluate(f.Free(m, FactHolderFragments.Read(a)))];

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("local-a-nested-function-reads", ByRefDirection.In, false)]
    [InlineData("local-a-nested-function-reads", ByRefDirection.Ref, true)]
    [InlineData("ref-parameter", ByRefDirection.In, false)]
    [InlineData("ref-parameter", ByRefDirection.Ref, true)]
    public void AHolderHandedReadonlyRefKeepsItsFactsAcrossTheCall(string holder, ByRefDirection direction, bool refused)
    {
        var f = new FactHolderFragments();
        var a = holder == "ref-parameter" ? f.ByRef("p") : FactHolderFragments.Int(f.H, "a");
        var reader = f.Method("W", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(a))));
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(5L)),
            HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, direction, isReadonly: true), FactHolderFragments.Read(a))), f.Need(FactHolderFragments.Read(a, Last))];
        if(holder == "local-a-nested-function-reads")
        {
            body.InsertRange(0, [FactHolderFragments.Declare(a, f.Number(0L)), new LocalFunctionStatement(reader, [])]);
        }

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ALocalHandedByRefToACallWhoseResultIsRefLikeKeepsOnlyItsType(bool refLike, bool refused)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var view = new LocalVariable("view", refLike ? f.Span : HandBuiltModel.Struct("Copy"));
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(0L)),
            FactHolderFragments.Declare(view, f.Free(f.Viewing(view.Type), FactHolderFragments.Read(a))),
            FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(5L)), f.Need(FactHolderFragments.Read(a, Last))];

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("ref-local-written-after-the-local", true)]
    [InlineData("ref-local-written-before-the-local", false)]
    [InlineData("ref-local-written-after-the-need", false)]
    [InlineData("ref-local-of-a-ref-local-written-after-the-local", true)]
    [InlineData("the-call-assigned", true)]
    [InlineData("the-call-compounded", true)]
    [InlineData("the-call-passed-by-ref-after-the-local-is-set-again", true)]
    public void AWriteThroughAReferenceACallHandsBackEndsWhatTheCallWasHandedByRef(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var x = FactHolderFragments.Int(f.H, "x", isRef: true);
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var id = f.Identity();
        var y = f.ByRef("y");
        var set = f.Method("Set", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(y), f.Number(1L))), f.H.Int,
            y.Parameter!, FactHolderFragments.Parameter("z", f.H.Int));
        InvocationExpression aliasOf(LocalVariable local)
        {
            return f.Free(id, FactHolderFragments.Read(local));
        }

        Statement five()
        {
            return FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(5L));
        }

        Statement write(int value)
        {
            return FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(value));
        }

        var need = f.Need(FactHolderFragments.Read(a, Last));
        Statement[] statements = form switch
        {
            "ref-local-written-after-the-local" => [FactHolderFragments.Declare(r, aliasOf(a)), five(), write(1), need],
            "ref-local-written-before-the-local" => [FactHolderFragments.Declare(r, aliasOf(a)), write(1), five(), need],
            "ref-local-written-after-the-need" => [five(), need, FactHolderFragments.Declare(r, aliasOf(a)), write(6)],
            "ref-local-of-a-ref-local-written-after-the-local" => [FactHolderFragments.Declare(x, FactHolderFragments.Read(a)), FactHolderFragments.Declare(r, aliasOf(x)),
                five(), write(1), need],
            "the-call-assigned" => [five(), FactHolderFragments.Set(aliasOf(a), f.Number(1L)), need],
            "the-call-compounded" => [five(), HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), aliasOf(a), BinaryOperatorKind.Add,
                f.Number(1L), f.H.Int, null, IncrementForm.None)), need],
            "the-call-passed-by-ref-after-the-local-is-set-again" => [HandBuiltRefinements.Evaluate(f.Free(set, aliasOf(a),
                HandBuiltRefinements.Assign(FactHolderFragments.Read(a), f.Number(5L)))), need],
            _ => throw new InvalidOperationException(form),
        };

        AssertRefusedOnlyAfter(f, [FactHolderFragments.Declare(a, f.Number(0L)), .. statements], refused);
    }

    [Theory]
    [InlineData("ref-local-of-the-local")]
    [InlineData("ref-local-bound-to-a-reference-a-call-hands-back")]
    public void AWriteThroughARefLocalInsideALoopEndsTheLocalAfterIt(string alias)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var named = alias == "ref-local-of-the-local" ? (Expression)FactHolderFragments.Read(a) : f.Free(f.Identity(), FactHolderFragments.Read(a));
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(0L)), FactHolderFragments.Declare(r, named),
            FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(5L)),
            new WhileStatement(HandBuiltRefinements.Local(f.H.Bool, "flag"), HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L)))),
            f.Need(FactHolderFragments.Read(a, Last))];

        AssertRefusedOnlyAfter(f, body, true);
    }

    [Fact]
    public void ACallAssignedThroughEndsTheFieldsItsBodyWrites()
    {
        var f = new FactHolderFragments();
        var writeA = new Method("WriteA", f.H.Int, [], new MemberModifiers { IsRef = true })
        {
            Body = HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(1L)), new ReturnStatement(FactHolderFragments.Own(f.A), f.H.Int, isRef: true)),
            DeclaringType = f.Pair,
        };
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)),
            FactHolderFragments.Set(FactHolderFragments.Call(f.This, writeA), f.Number(2L)), f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, true);
    }

    internal static void AssertRefusedOnlyAfter(FactHolderFragments f, List<Statement> body, bool refused)
    {
        var findings = f.H.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. body]));
        if(refused)
        {
            var finding = Assert.Single(findings);
            Assert.Equal("MIST0272", finding.Number.Value);
            Assert.Equal(Last + 1, finding.Position().Start.Column);
        }
        else
        {
            Assert.Empty(findings);
        }
    }
}