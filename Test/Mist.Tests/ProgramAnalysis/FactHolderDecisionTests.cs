using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FactHolderDecisionTests
{
    [Theory]
    [InlineData("written-by-a-local-function", true)]
    [InlineData("passed-by-ref-in-a-local-function", true)]
    [InlineData("named-by-a-ref-local-in-a-local-function", true)]
    [InlineData("passed-by-ref", false)]
    [InlineData("named-by-a-ref-local", false)]
    [InlineData("read-by-a-local-function", false)]
    [InlineData("read-by-a-local-function-and-passed-by-ref", true)]
    [InlineData("read-by-a-local-function-and-named-by-a-ref-local", true)]
    public void OnlyALocalANestedFunctionWritesOrMayReachThroughARefIsOutsideTheFlow(string form, bool unseen)
    {
        var f = new FactHolderFragments();
        var d = FactHolderFragments.Int(f.H, "d");
        var alias = FactHolderFragments.Int(f.H, "r", isRef: true);
        var bump = HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(d)));
        Statement nested(params Statement[] statements)
        {
            return new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(statements)), []);
        }

        var read = nested(f.Need(FactHolderFragments.Read(d)));
        Statement[] statements = form switch
        {
            "written-by-a-local-function" => [nested(FactHolderFragments.Set(FactHolderFragments.Read(d), f.Number(6L)))],
            "passed-by-ref-in-a-local-function" => [nested(bump)],
            "named-by-a-ref-local-in-a-local-function" => [nested(FactHolderFragments.Declare(alias, FactHolderFragments.Read(d)))],
            "passed-by-ref" => [bump],
            "named-by-a-ref-local" => [FactHolderFragments.Declare(alias, FactHolderFragments.Read(d))],
            "read-by-a-local-function" => [read],
            "read-by-a-local-function-and-passed-by-ref" => [read, bump],
            "read-by-a-local-function-and-named-by-a-ref-local" => [read, FactHolderFragments.Declare(alias, FactHolderFragments.Read(d))],
            _ => throw new InvalidOperationException(form),
        };

        var holders = BodyHolders.Of(HandBuiltModel.Block([FactHolderFragments.Declare(d, f.Number(5L)), .. statements]));

        Assert.Equal(unseen, holders.Unseen.Contains(d));
    }

    [Fact]
    public void ARefLocalAndTheLocalItNamesAreOneStorage()
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var s = FactHolderFragments.Int(f.H, "s", isRef: true);

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(5L)),
            FactHolderFragments.Declare(r, FactHolderFragments.Read(a)), FactHolderFragments.Declare(s, FactHolderFragments.Read(r))));

        Assert.Equal([r, s], holders.AliasesOf(a).OrderBy(holder => ((LocalVariable)holder).Name));
        Assert.Equal([a, s], holders.AliasesOf(r).OrderBy(holder => ((LocalVariable)holder).Name));
        Assert.Empty(holders.Unseen);
    }

    [Fact]
    public void ARefLocalWhoseStorageTheFlowCannotNameStaysOutsideIt()
    {
        var f = new FactHolderFragments();
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(r, f.H.At(f.H.Array(3L), f.Number(0L)))));

        Assert.Contains(r, holders.Unseen);
        Assert.Empty(holders.AliasesOf(r));
    }

    [Theory]
    [InlineData("local", "")]
    [InlineData("by-value-parameter", "")]
    [InlineData("ref-parameter", "p,r,s")]
    [InlineData("field-of-this", "A,r,s")]
    public void ARefLocalNamingStorageABodyDoesNotFollowLeavesItsWholeAliasGroupOutsideTheFlow(string named, string unseen)
    {
        var f = new FactHolderFragments();
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var s = FactHolderFragments.Int(f.H, "s", isRef: true);
        var aliased = named switch
        {
            "local" => (Expression)FactHolderFragments.Read(FactHolderFragments.Int(f.H, "a")),
            "by-value-parameter" => FactHolderFragments.Read(new LocalVariable("p", f.H.Int) { Parameter = FactHolderFragments.Parameter("p", f.H.Int) }),
            "ref-parameter" => FactHolderFragments.Read(f.ByRef("p")),
            "field-of-this" => FactHolderFragments.Member(f.This, f.A),
            _ => throw new InvalidOperationException(named),
        };

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(r, aliased), FactHolderFragments.Declare(s, FactHolderFragments.Read(r))));

        Assert.Equal(unseen, Names(holders.Unseen));
        Assert.Equal(2, holders.AliasesOf(r).Count);
    }

    [Theory]
    [InlineData("local", "")]
    [InlineData("ref-local-of-a-local", "")]
    [InlineData("field", "A")]
    [InlineData("ref-parameter", "p")]
    [InlineData("member-of-another-object", "V")]
    public void ARefArgumentLeavesOutsideTheFlowOnlyStorageTheBodyDoesNotFollow(string argument, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var x = FactHolderFragments.Int(f.H, "x", isRef: true);
        Expression passed = argument switch
        {
            "local" => FactHolderFragments.Read(a),
            "ref-local-of-a-local" => FactHolderFragments.Read(x),
            "field" => FactHolderFragments.Own(f.A),
            "ref-parameter" => FactHolderFragments.Read(f.ByRef("p")),
            "member-of-another-object" => FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V),
            _ => throw new InvalidOperationException(argument),
        };

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(0L)), FactHolderFragments.Declare(x, FactHolderFragments.Read(a)),
            HandBuiltRefinements.Evaluate(f.Free(f.Bump(), passed))));

        Assert.Equal(unseen, Names(holders.Unseen));
    }

    [Theory]
    [InlineData("local", ByRefDirection.In, "")]
    [InlineData("local-a-nested-function-reads", ByRefDirection.In, "")]
    [InlineData("local-a-nested-function-reads", ByRefDirection.Ref, "a")]
    [InlineData("local-a-nested-function-reads", ByRefDirection.Out, "a")]
    [InlineData("field", ByRefDirection.In, "")]
    [InlineData("ref-parameter", ByRefDirection.In, "")]
    [InlineData("ref-parameter", ByRefDirection.Ref, "p")]
    public void AReadonlyRefArgumentLeavesItsHolderInTheFlow(string argument, ByRefDirection direction, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var reader = new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(a)))), []);
        Expression passed = argument switch
        {
            "local" or "local-a-nested-function-reads" => FactHolderFragments.Read(a),
            "field" => FactHolderFragments.Own(f.A),
            "ref-parameter" => FactHolderFragments.Read(f.ByRef("p")),
            _ => throw new InvalidOperationException(argument),
        };

        Statement read = argument == "local-a-nested-function-reads" ? reader : HandBuiltRefinements.Evaluate(f.Number(0L));
        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(5L)), read,
            HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, direction), passed))));

        Assert.Equal(unseen, Names(holders.Unseen));
    }

    [Theory]
    [InlineData("a-local", "a", "r")]
    [InlineData("a-ref-local-of-a-local", "a,x", "r")]
    [InlineData("a-reference-another-such-call-hands-back", "a", "r")]
    [InlineData("a-field", "A", "A,r")]
    [InlineData("a-field-of-another-object", "", "V,r")]
    public void ARefLocalBoundToARefReturningCallCarriesNoFactsAndEndsWhatTheCallWasHandedByRef(string argument, string named, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var x = FactHolderFragments.Int(f.H, "x", isRef: true);
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var id = f.Identity();
        Expression passed = argument switch
        {
            "a-local" => FactHolderFragments.Read(a),
            "a-ref-local-of-a-local" => FactHolderFragments.Read(x),
            "a-reference-another-such-call-hands-back" => f.Free(id, FactHolderFragments.Read(a)),
            "a-field" => FactHolderFragments.Own(f.A),
            "a-field-of-another-object" => FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V),
            _ => throw new InvalidOperationException(argument),
        };

        Statement alias = argument == "a-ref-local-of-a-local" ? FactHolderFragments.Declare(x, FactHolderFragments.Read(a)) : HandBuiltRefinements.Evaluate(f.Number(0L));
        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(0L)), alias, FactHolderFragments.Declare(r, f.Free(id, passed))));

        Assert.Equal(named, Names(holders.AliasesOf(r)));
        Assert.Equal(unseen, Names(holders.Unseen));
    }

    [Theory]
    [InlineData("local", ByRefDirection.Ref, true, "a,x")]
    [InlineData("local", ByRefDirection.In, true, "a,x")]
    [InlineData("local", ByRefDirection.Out, true, "a,x")]
    [InlineData("local", ByRefDirection.Ref, false, "")]
    [InlineData("ref-local", ByRefDirection.Ref, true, "a,x")]
    [InlineData("ref-parameter", ByRefDirection.Ref, true, "p")]
    [InlineData("reference-a-call-hands-back", ByRefDirection.Ref, true, "a,x")]
    public void WhatACallHandsToAResultThatMayKeepAReferenceStaysOutsideTheFlow(string argument, ByRefDirection direction, bool refLike, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var x = FactHolderFragments.Int(f.H, "x", isRef: true);
        Expression passed = argument switch
        {
            "local" => FactHolderFragments.Read(a),
            "ref-local" => FactHolderFragments.Read(x),
            "ref-parameter" => FactHolderFragments.Read(f.ByRef("p")),
            "reference-a-call-hands-back" => f.Free(f.Identity(), FactHolderFragments.Read(a)),
            _ => throw new InvalidOperationException(argument),
        };
        var view = f.Viewing(refLike ? f.Span : HandBuiltModel.Struct("Copy"), direction);

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(0L)), FactHolderFragments.Declare(x, FactHolderFragments.Read(a)),
            HandBuiltRefinements.Evaluate(f.Free(view, passed))));

        Assert.Equal(unseen, Names(holders.Unseen));
    }

    private static string Names(IEnumerable<object> holders)
    {
        return string.Join(",", holders.Select(holder => holder switch
        {
            LocalVariable local => local.Name,
            Field field => field.Name,
            _ => throw new InvalidOperationException(holder.GetType().Name),
        }).Order(StringComparer.Ordinal));
    }
}