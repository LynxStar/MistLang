using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactHolderFlowTests
{
    private const int Last = 40;

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
    [InlineData("nothing-between", OwnershipKind.Owned, false)]
    [InlineData("nothing-between", OwnershipKind.Unrestricted, true)]
    [InlineData("nothing-between", OwnershipKind.Borrowed, true)]
    [InlineData("call-writing-another-member", OwnershipKind.Owned, false)]
    [InlineData("call-writing-the-member", OwnershipKind.Owned, true)]
    [InlineData("borrowed-argument", OwnershipKind.Owned, true)]
    [InlineData("unrestricted-argument-earlier", OwnershipKind.Owned, true)]
    [InlineData("call-that-cannot-reach-it", OwnershipKind.Owned, false)]
    [InlineData("ref-argument-a-call-on-it-hands-back", OwnershipKind.Owned, true)]
    [InlineData("lent-earlier-to-a-callee-that-reads-it", OwnershipKind.Owned, false)]
    [InlineData("lent-earlier-to-a-callee-that-keeps-it", OwnershipKind.Owned, true)]
    [InlineData("extension-call-earlier-that-reads-its-receiver", OwnershipKind.Owned, false)]
    [InlineData("extension-call-earlier-that-keeps-its-receiver", OwnershipKind.Owned, true)]
    public void AMemberOfAnOwnedObjectKeepsItsFactsUntilACallMayWriteIt(string between, OwnershipKind ownership, bool refused)
    {
        var f = new FactHolderFragments();
        var b = f.Object("b", ownership);
        List<Statement> body = [f.Created(b)];
        Statement[] reads(LocalVariable x)
        {
            return [f.Need(FactHolderFragments.Member(FactHolderFragments.Read(x), f.V))];
        }

        Statement[] keeps(LocalVariable x)
        {
            return [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(x))];
        }

        Expression? earlier = between switch
        {
            "unrestricted-argument-earlier" => f.Free(f.Method("Keep", f.Pair, null, null, FactHolderFragments.Parameter("x", f.Box)), FactHolderFragments.Read(b)),
            "lent-earlier-to-a-callee-that-reads-it" => f.Free(f.Lender("Lend", f.Box, reads).Method, FactHolderFragments.Read(b)),
            "lent-earlier-to-a-callee-that-keeps-it" => f.Free(f.Lender("Lend", f.Box, keeps).Method, FactHolderFragments.Read(b)),
            "extension-call-earlier-that-reads-its-receiver" => FactHolderFragments.Call(FactHolderFragments.Read(b), f.Extension("Peek", reads)),
            "extension-call-earlier-that-keeps-its-receiver" => FactHolderFragments.Call(FactHolderFragments.Read(b), f.Extension("Stash", keeps)),
            _ => null,
        };
        if(earlier is not null)
        {
            body.Add(HandBuiltRefinements.Evaluate(earlier));
        }

        body.Add(FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(b), f.V), f.Number(5L)));
        Statement? call = between switch
        {
            "call-writing-another-member" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(FactHolderFragments.Read(b), f.Writing("SetW", f.Box, f.W))),
            "call-writing-the-member" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(FactHolderFragments.Read(b), f.Writing("SetV", f.Box, f.V))),
            "borrowed-argument" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Lend", f.Pair, null, null,
                FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed)), FactHolderFragments.Read(b))),
            "call-that-cannot-reach-it" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Print", f.Pair, null))),
            "ref-argument-a-call-on-it-hands-back" => HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Call(FactHolderFragments.Read(b), f.Referencing("RefV", f.Box, f.H.String)))),
            _ => null,
        };
        if(call is not null)
        {
            body.Add(call);
        }

        body.Add(f.Need(FactHolderFragments.Member(FactHolderFragments.Read(b), f.V, Last)));

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnObjectWhoseMethodLeavesAClosureOverItKeepsNoMemberFacts(bool closesOverIt)
    {
        var f = new FactHolderFragments();
        var b = f.Object("b", OwnershipKind.Owned);
        var count = new Field("Count", f.H.Int, default, new MemberModifiers { IsStatic = true }) { DeclaringType = f.Box };
        var arm = f.Method("Arm", f.Box, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(f.Closure(
            FactHolderFragments.Set(FactHolderFragments.Own(closesOverIt ? f.V : count), f.Number(1L))))));
        List<Statement> body = [f.Created(b), HandBuiltRefinements.Evaluate(FactHolderFragments.Call(FactHolderFragments.Read(b), arm)),
            FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(b), f.V), f.Number(5L)), HandBuiltRefinements.Evaluate(f.Delegate()),
            f.Need(FactHolderFragments.Member(FactHolderFragments.Read(b), f.V, Last))];

        AssertRefusedOnlyAfter(f, body, closesOverIt);
    }

    [Theory]
    [InlineData("a-local", false)]
    [InlineData("a-reference-a-call-hands-back", true)]
    [InlineData("a-ref-local-bound-to-such-a-reference", true)]
    [InlineData("a-ref-parameter", true)]
    [InlineData("a-member-of-an-object-the-body-does-not-hold", true)]
    public void ARefArgumentWhoseStorageTheFlowCannotNameMayWriteEveryField(string argument, bool refused)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var other = f.Object("other", OwnershipKind.Unrestricted);
        var refA = f.Referencing("RefA", f.Pair);
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(0L)), f.Created(other),
            FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, refA)), FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L))];
        Expression passed = argument switch
        {
            "a-local" => FactHolderFragments.Read(a),
            "a-reference-a-call-hands-back" => FactHolderFragments.Call(f.This, refA),
            "a-ref-local-bound-to-such-a-reference" => FactHolderFragments.Read(r),
            "a-ref-parameter" => FactHolderFragments.Read(f.ByRef("x")),
            "a-member-of-an-object-the-body-does-not-hold" => FactHolderFragments.Member(FactHolderFragments.Read(other), f.V),
            _ => throw new InvalidOperationException(argument),
        };
        body.Add(HandBuiltRefinements.Evaluate(f.Free(f.Bump(), passed)));
        body.Add(f.Need(FactHolderFragments.Own(f.A, Last)));

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("argument-the-callee-reads", false)]
    [InlineData("argument-the-callee-keeps", true)]
    [InlineData("extension-receiver-the-callee-reads", false)]
    [InlineData("extension-receiver-the-callee-keeps", true)]
    public void AFieldOfThisOutlivesACallHandedThisOnlyWhileTheCalleeKeepsNoReferenceToIt(string form, bool refused)
    {
        var f = new FactHolderFragments();
        Statement[] reads(LocalVariable x)
        {
            return [f.Touch(), f.Need(FactHolderFragments.Member(FactHolderFragments.Read(x), f.B))];
        }

        Statement[] keeps(LocalVariable x)
        {
            return [f.Touch(), FactHolderFragments.Keep(f.StoredPair, FactHolderFragments.Read(x))];
        }

        var call = form switch
        {
            "argument-the-callee-reads" => f.Free(f.Lender("Look", f.Pair, reads, OwnershipKind.Unrestricted).Method, f.This),
            "argument-the-callee-keeps" => f.Free(f.Lender("Keep", f.Pair, keeps, OwnershipKind.Unrestricted).Method, f.This),
            "extension-receiver-the-callee-reads" => FactHolderFragments.Call(f.This, f.Extension("Peek", reads, f.Pair)),
            "extension-receiver-the-callee-keeps" => FactHolderFragments.Call(f.This, f.Extension("Stash", keeps, f.Pair)),
            _ => throw new InvalidOperationException(form),
        };
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)), HandBuiltRefinements.Evaluate(call),
            f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACallOnThisEndsTheFieldItsBodyWritesThroughARefLocal(bool writesA)
    {
        var f = new FactHolderFragments();
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var setter = f.Method("Set", f.Pair, HandBuiltModel.Block(FactHolderFragments.Declare(r, FactHolderFragments.Own(writesA ? f.A : f.B)),
            FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L))));
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)),
            HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, setter)), f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, writesA);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACallOnThisEndsOnlyTheFieldsItsBodyWrites(bool writesA)
    {
        var f = new FactHolderFragments();
        var setter = writesA ? f.Writing("SetA", f.Pair, f.A) : f.Writing("SetB", f.Pair, f.B);
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)),
            HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, setter)), f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, writesA);
    }

    [Fact]
    public void ACallOnThisWhoseBodyRunsCodeThatMayDoAnythingEndsEveryField()
    {
        var f = new FactHolderFragments();
        var print = f.Method("Print", f.Pair, null);
        var setter = f.Method("SetB", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Own(f.B), f.Number(1L)),
            HandBuiltRefinements.Evaluate(f.Free(print))));
        List<Statement> body = [FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)),
            HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, setter)), f.Need(FactHolderFragments.Own(f.A, Last))];

        AssertRefusedOnlyAfter(f, body, true);
    }

    private static IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Analyze(FactHolderFragments f, List<Statement> body)
    {
        return f.H.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block([.. body]));
    }

    private static void AssertRefusedOnlyAfter(FactHolderFragments f, List<Statement> body, bool refused)
    {
        var findings = Analyze(f, body);
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