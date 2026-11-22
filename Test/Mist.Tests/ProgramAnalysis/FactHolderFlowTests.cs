using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
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
    [InlineData("call-writing-another-member", OwnershipKind.Owned, false)]
    [InlineData("call-writing-the-member", OwnershipKind.Owned, true)]
    [InlineData("borrowed-argument", OwnershipKind.Owned, true)]
    [InlineData("unrestricted-argument-earlier", OwnershipKind.Owned, true)]
    [InlineData("call-that-cannot-reach-it", OwnershipKind.Owned, false)]
    public void AMemberOfAnOwnedObjectKeepsItsFactsUntilACallMayWriteIt(string between, OwnershipKind ownership, bool refused)
    {
        var f = new FactHolderFragments();
        var b = f.Object("b", ownership);
        List<Statement> body = [f.Created(b)];
        if(between == "unrestricted-argument-earlier")
        {
            body.Add(HandBuiltRefinements.Evaluate(f.Free(f.Method("Keep", f.Pair, null, null, FactHolderFragments.Parameter("x", f.Box)), FactHolderFragments.Read(b))));
        }

        body.Add(FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(b), f.V), f.Number(5L)));
        Statement? call = between switch
        {
            "call-writing-another-member" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(FactHolderFragments.Read(b), f.Writing("SetW", f.Box, f.W))),
            "call-writing-the-member" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(FactHolderFragments.Read(b), f.Writing("SetV", f.Box, f.V))),
            "borrowed-argument" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Lend", f.Pair, null, null,
                FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed)), FactHolderFragments.Read(b))),
            "call-that-cannot-reach-it" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Print", f.Pair, null))),
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