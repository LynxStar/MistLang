using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FactHolderDecisionTests
{
    [Theory]
    [InlineData(OwnershipKind.Owned, false, true)]
    [InlineData(OwnershipKind.Borrowed, false, true)]
    [InlineData(OwnershipKind.Frozen, false, false)]
    [InlineData(OwnershipKind.Unrestricted, false, false)]
    [InlineData(OwnershipKind.Owned, true, false)]
    public void OnlyAnOwnedOrBorrowedBindingHoldsItsObjectsMembers(OwnershipKind ownership, bool isRef, bool holds)
    {
        var f = new FactHolderFragments();

        Assert.Equal(holds, BodyHolders.HoldsMembers(new LocalVariable("b", f.Box) { Ownership = ownership, IsRef = isRef }));
    }

    [Theory]
    [InlineData("written-by-a-local-function", true)]
    [InlineData("passed-by-ref-in-a-local-function", true)]
    [InlineData("named-by-a-ref-local-in-a-local-function", true)]
    [InlineData("passed-by-ref", false)]
    [InlineData("named-by-a-ref-local", false)]
    [InlineData("read-by-a-local-function", false)]
    public void OnlyALocalANestedFunctionWritesIsOutsideTheFlow(string form, bool unseen)
    {
        var f = new FactHolderFragments();
        var d = FactHolderFragments.Int(f.H, "d");
        var alias = FactHolderFragments.Int(f.H, "r", isRef: true);
        var bump = HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(d)));
        Statement nested(params Statement[] statements)
        {
            return new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(statements)), []);
        }

        var statement = form switch
        {
            "written-by-a-local-function" => nested(FactHolderFragments.Set(FactHolderFragments.Read(d), f.Number(6L))),
            "passed-by-ref-in-a-local-function" => nested(bump),
            "named-by-a-ref-local-in-a-local-function" => nested(FactHolderFragments.Declare(alias, FactHolderFragments.Read(d))),
            "passed-by-ref" => bump,
            "named-by-a-ref-local" => FactHolderFragments.Declare(alias, FactHolderFragments.Read(d)),
            "read-by-a-local-function" => nested(f.Need(FactHolderFragments.Read(d))),
            _ => throw new InvalidOperationException(form),
        };

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(d, f.Number(5L)), statement));

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
    [InlineData("field-access", OwnershipKind.Owned, true)]
    [InlineData("field-access", OwnershipKind.Borrowed, true)]
    [InlineData("field-access", OwnershipKind.Unrestricted, false)]
    [InlineData("call-with-known-writes", OwnershipKind.Owned, true)]
    [InlineData("call-that-may-do-anything", OwnershipKind.Owned, false)]
    [InlineData("borrowed-argument", OwnershipKind.Owned, true)]
    [InlineData("unrestricted-argument", OwnershipKind.Owned, false)]
    [InlineData("moved", OwnershipKind.Owned, true)]
    [InlineData("lent-to-a-borrowed-local", OwnershipKind.Owned, false)]
    [InlineData("captured", OwnershipKind.Owned, false)]
    [InlineData("named-by-a-ref-local-member", OwnershipKind.Owned, false)]
    public void AReceiverHoldsItsMembersOnlyWhileEveryUseIsOneTheFlowSees(string use, OwnershipKind ownership, bool held)
    {
        var f = new FactHolderFragments();
        var b = f.Object("b", ownership);
        Expression read()
        {
            return FactHolderFragments.Read(b);
        }

        Statement statement = use switch
        {
            "field-access" => FactHolderFragments.Set(FactHolderFragments.Member(read(), f.V), f.Number(5L)),
            "call-with-known-writes" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Writing("SetV", f.Box, f.V))),
            "call-that-may-do-anything" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Method("Run", f.Box, null))),
            "borrowed-argument" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Lend", f.Pair, null, null,
                FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed)), read())),
            "unrestricted-argument" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Keep", f.Pair, null, null, FactHolderFragments.Parameter("x", f.Box)), read())),
            "moved" => FactHolderFragments.Declare(f.Object("c", OwnershipKind.Unrestricted), read()),
            "lent-to-a-borrowed-local" => FactHolderFragments.Declare(f.Object("c", OwnershipKind.Borrowed), read()),
            "captured" => new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(f.Need(FactHolderFragments.Member(read(), f.V)))), []),
            "named-by-a-ref-local-member" => FactHolderFragments.Declare(FactHolderFragments.Int(f.H, "r", isRef: true), FactHolderFragments.Member(read(), f.V)),
            _ => throw new InvalidOperationException(use),
        };

        var holders = BodyHolders.Of(HandBuiltModel.Block(f.Created(b), statement));

        Assert.Equal(held, holders.Held.Contains(b));
    }

    [Theory]
    [InlineData("writes-one-field", "B")]
    [InlineData("calls-a-method-on-this-that-writes-another", "A,B")]
    [InlineData("calls-itself-through-another", "A,B")]
    [InlineData("writes-a-static", "Count")]
    [InlineData("passes-a-field-by-ref", "A")]
    [InlineData("declared-readonly", "")]
    [InlineData("hands-out-this", null)]
    [InlineData("calls-what-may-do-anything", null)]
    [InlineData("writes-another-object", null)]
    [InlineData("has-no-body", null)]
    [InlineData("is-an-abstract-slot", null)]
    public void ACallWritesOnlyTheFieldsItsBodyAndTheBodiesItCallsOnThisWrite(string callee, string? fields)
    {
        var f = new FactHolderFragments();
        var count = new Field("Count", f.H.Int, default, new MemberModifiers { IsStatic = true }) { DeclaringType = f.Pair };
        var writeA = f.Writing("SetA", f.Pair, f.A);
        var other = f.Object("other", OwnershipKind.Unrestricted);
        Method body(params Statement[] statements)
        {
            return f.Method("Touch", f.Pair, HandBuiltModel.Block([FactHolderFragments.Set(FactHolderFragments.Own(f.B), f.Number(1L)), .. statements]));
        }

        var method = callee switch
        {
            "writes-one-field" => body(),
            "calls-a-method-on-this-that-writes-another" => body(HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, writeA))),
            "calls-itself-through-another" => Cycle(f, writeA),
            "writes-a-static" => f.Method("Touch", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Own(count), f.Number(1L)))),
            "passes-a-field-by-ref" => f.Method("Touch", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Own(f.A))))),
            "declared-readonly" => new Method("Touch", f.H.String, [], new MemberModifiers { IsReadonly = true }) { Body = HandBuiltModel.Block(), DeclaringType = f.Pair },
            "hands-out-this" => body(HandBuiltRefinements.Evaluate(f.Free(f.Method("Keep", f.Pair, HandBuiltModel.Block(), null, FactHolderFragments.Parameter("x", f.Pair)), f.This))),
            "calls-what-may-do-anything" => body(HandBuiltRefinements.Evaluate(f.Free(f.Method("Print", f.Pair, null)))),
            "writes-another-object" => body(FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(other), f.V), f.Number(1L))),
            "has-no-body" => f.Method("Touch", f.Pair, null),
            "is-an-abstract-slot" => new Method("Touch", f.H.String, [], new MemberModifiers()) { Body = HandBuiltModel.Block(), DeclaringType = new AbstractType("Slot", "Probe", null, null, null) },
            _ => throw new InvalidOperationException(callee),
        };

        var written = new MemberWrites(new FieldEffects()).Of(FactHolderFragments.Call(f.This, method));

        Assert.Equal(fields, written is null ? null : string.Join(",", written.Select(field => field.Name).Order(StringComparer.Ordinal)));
    }

    [Theory]
    [InlineData("delegate-call")]
    [InlineData("object-creation")]
    public void WhatRunsNoBodyTheFlowCanReadMayWriteAnything(string form)
    {
        var f = new FactHolderFragments();
        Expression node = form == "delegate-call"
            ? new InvocationExpression(HandBuiltModel.Node(), f.Number(0L), [], f.H.Int, null, [])
            : new NewExpression(HandBuiltModel.Node(), f.Box, [], null, [], []);

        Assert.Null(new MemberWrites(new FieldEffects()).Of(node));
    }

    private static Method Cycle(FactHolderFragments f, Method writeA)
    {
        var first = f.Writing("Touch", f.Pair, f.B);
        var second = f.Method("Again", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, first)),
            HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, writeA))));
        first.Body!.Statements = [.. first.Body.Statements, HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, second))];
        return first;
    }
}