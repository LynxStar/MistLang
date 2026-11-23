using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
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
    [InlineData(OwnershipKind.Borrowed, false, false)]
    [InlineData(OwnershipKind.Frozen, false, false)]
    [InlineData(OwnershipKind.Unrestricted, false, false)]
    [InlineData(OwnershipKind.Owned, true, false)]
    public void OnlyAnOwnedBindingHoldsItsObjectsMembers(OwnershipKind ownership, bool isRef, bool holds)
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
    [InlineData("a-local", "a", "r")]
    [InlineData("a-ref-local-of-a-local", "a,x", "r")]
    [InlineData("a-reference-another-such-call-hands-back", "a", "r")]
    [InlineData("a-field-of-another-object", "", "A,r")]
    public void ARefLocalBoundToARefReturningCallMayNameWhatTheCallWasHandedByRef(string argument, string named, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var x = FactHolderFragments.Int(f.H, "x", isRef: true);
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var other = f.Object("other", OwnershipKind.Unrestricted);
        var id = f.Identity();
        Expression passed = argument switch
        {
            "a-local" => FactHolderFragments.Read(a),
            "a-ref-local-of-a-local" => FactHolderFragments.Read(x),
            "a-reference-another-such-call-hands-back" => f.Free(id, FactHolderFragments.Read(a)),
            "a-field-of-another-object" => FactHolderFragments.Member(FactHolderFragments.Read(other), f.A),
            _ => throw new InvalidOperationException(argument),
        };
        static string names(IEnumerable<object> holders)
        {
            return string.Join(",", holders.Select(holder => holder switch
            {
                LocalVariable local => local.Name,
                Field field => field.Name,
                _ => throw new InvalidOperationException(holder.GetType().Name),
            }).Order(StringComparer.Ordinal));
        }

        Statement alias = argument == "a-ref-local-of-a-local" ? FactHolderFragments.Declare(x, FactHolderFragments.Read(a)) : f.Created(other);
        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(0L)), alias, FactHolderFragments.Declare(r, f.Free(id, passed))));

        Assert.Equal(named, names(holders.AliasesOf(r)));
        Assert.Equal(unseen, names(holders.Unseen));
    }

    [Theory]
    [InlineData("plain-local", true)]
    [InlineData("ref-local-of-a-field", true)]
    [InlineData("ref-local-of-a-ref-local-of-a-field", true)]
    [InlineData("ref-parameter", false)]
    [InlineData("ref-local-of-a-ref-parameter", false)]
    [InlineData("ref-local-of-a-reference-a-call-hands-back", false)]
    public void OnlyALocalTheFlowCanFollowToAHolderNamesItsStorage(string form, bool named)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var s = FactHolderFragments.Int(f.H, "s", isRef: true);
        var x = f.ByRef("x");
        var (subject, statements) = form switch
        {
            "plain-local" => (a, new Statement[] { FactHolderFragments.Declare(a, f.Number(5L)) }),
            "ref-local-of-a-field" => (r, [FactHolderFragments.Declare(r, FactHolderFragments.Own(f.A))]),
            "ref-local-of-a-ref-local-of-a-field" => (s, [FactHolderFragments.Declare(r, FactHolderFragments.Own(f.A)), FactHolderFragments.Declare(s, FactHolderFragments.Read(r))]),
            "ref-parameter" => (x, [HandBuiltRefinements.Evaluate(FactHolderFragments.Read(x))]),
            "ref-local-of-a-ref-parameter" => (r, [FactHolderFragments.Declare(r, FactHolderFragments.Read(x))]),
            "ref-local-of-a-reference-a-call-hands-back" => (r, [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, f.Referencing("RefA", f.Pair)))]),
            _ => throw new InvalidOperationException(form),
        };

        Assert.Equal(named, BodyHolders.Of(HandBuiltModel.Block(statements)).Names(subject));
    }

    [Theory]
    [InlineData("field-access", OwnershipKind.Owned, true)]
    [InlineData("field-access", OwnershipKind.Borrowed, false)]
    [InlineData("field-access", OwnershipKind.Unrestricted, false)]
    [InlineData("call-with-known-writes", OwnershipKind.Owned, true)]
    [InlineData("call-that-may-do-anything", OwnershipKind.Owned, false)]
    [InlineData("call-handing-back-a-reference-into-it", OwnershipKind.Owned, false)]
    [InlineData("borrowed-argument", OwnershipKind.Owned, true)]
    [InlineData("borrowed-argument-the-callee-keeps", OwnershipKind.Owned, false)]
    [InlineData("frozen-argument-the-callee-keeps", OwnershipKind.Owned, false)]
    [InlineData("ref-argument-the-callee-keeps", OwnershipKind.Owned, false)]
    [InlineData("borrowed-argument-the-callee-hands-to-one-that-keeps-it", OwnershipKind.Owned, false)]
    [InlineData("borrowed-argument-of-a-callee-with-no-body", OwnershipKind.Owned, false)]
    [InlineData("constructor-argument-the-constructor-keeps", OwnershipKind.Owned, false)]
    [InlineData("extension-receiver-the-callee-reads", OwnershipKind.Owned, true)]
    [InlineData("extension-receiver-the-callee-keeps", OwnershipKind.Owned, false)]
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

        var statement = use switch
        {
            "field-access" => FactHolderFragments.Set(FactHolderFragments.Member(read(), f.V), f.Number(5L)),
            "call-with-known-writes" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Writing("SetV", f.Box, f.V))),
            "call-that-may-do-anything" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Method("Run", f.Box, null))),
            "call-handing-back-a-reference-into-it" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Referencing("RefV", f.Box, f.H.String))),
            "borrowed-argument" => Lend(f, read(), x => [f.Need(FactHolderFragments.Member(FactHolderFragments.Read(x), f.V))]),
            "borrowed-argument-the-callee-keeps" => Lend(f, read(), x => [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(x))]),
            "frozen-argument-the-callee-keeps" => Lend(f, read(), x => [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(x))], OwnershipKind.Frozen),
            "ref-argument-the-callee-keeps" => Lend(f, read(), x => [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(x))],
                OwnershipKind.Unrestricted, ByRefDirection.Ref),
            "borrowed-argument-the-callee-hands-to-one-that-keeps-it" => Lend(f, read(), x =>
                [HandBuiltRefinements.Evaluate(f.Free(f.Lender("Keep", f.Box, y => [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(y))]).Method,
                    FactHolderFragments.Read(x)))]),
            "borrowed-argument-of-a-callee-with-no-body" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Lend", f.Pair, null, null,
                FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed)), read())),
            "constructor-argument-the-constructor-keeps" => HandBuiltRefinements.Evaluate(Created(f, read())),
            "extension-receiver-the-callee-reads" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Extension("Peek", self =>
                [f.Need(FactHolderFragments.Member(FactHolderFragments.Read(self), f.V))]))),
            "extension-receiver-the-callee-keeps" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(), f.Extension("Stash", self =>
                [FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(self))]))),
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
    [InlineData("writes-a-field-through-a-ref-local", "A")]
    [InlineData("writes-through-a-ref-local-of-a-ref-local", "A")]
    [InlineData("compounds-through-a-ref-local", "A")]
    [InlineData("passes-a-ref-local-by-ref", "A")]
    [InlineData("writes-only-its-ref-parameter", "")]
    [InlineData("a-closure-touches-only-statics", "Count")]
    [InlineData("writes-through-a-ref-local-it-cannot-name", null)]
    [InlineData("a-closure-writes-a-field", null)]
    [InlineData("a-local-function-reads-a-field", null)]
    [InlineData("hands-out-a-method-group-over-this", null)]
    [InlineData("declared-readonly", "")]
    [InlineData("hands-out-this", null)]
    [InlineData("hands-this-to-a-call-that-keeps-nothing", "B")]
    [InlineData("hands-out-a-property-over-this", null)]
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
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var s = FactHolderFragments.Int(f.H, "s", isRef: true);
        Method only(params Statement[] statements)
        {
            return f.Method("Touch", f.Pair, HandBuiltModel.Block(statements));
        }

        var aliasA = FactHolderFragments.Declare(r, FactHolderFragments.Own(f.A));
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
            "writes-a-field-through-a-ref-local" => only(aliasA, FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L))),
            "writes-through-a-ref-local-of-a-ref-local" => only(aliasA, FactHolderFragments.Declare(s, FactHolderFragments.Read(r)),
                FactHolderFragments.Set(FactHolderFragments.Read(s), f.Number(1L))),
            "compounds-through-a-ref-local" => only(aliasA, HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(),
                FactHolderFragments.Read(r), BinaryOperatorKind.Add, f.Number(1L), f.H.Int, null, IncrementForm.None))),
            "passes-a-ref-local-by-ref" => only(aliasA, HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(r)))),
            "writes-only-its-ref-parameter" => f.Bump(),
            "a-closure-touches-only-statics" => only(HandBuiltRefinements.Evaluate(f.Closure(FactHolderFragments.Set(FactHolderFragments.Own(count), f.Number(1L))))),
            "writes-through-a-ref-local-it-cannot-name" => only(FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, f.Referencing("RefA", f.Pair))),
                FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L))),
            "a-closure-writes-a-field" => only(HandBuiltRefinements.Evaluate(f.Closure(FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(1L))))),
            "a-local-function-reads-a-field" => body(new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(f.Need(FactHolderFragments.Own(f.A)))), [])),
            "hands-out-a-method-group-over-this" => body(HandBuiltRefinements.Evaluate(new MethodGroupExpression(HandBuiltModel.Node(), null, writeA.Name, [writeA], null))),
            "declared-readonly" => new Method("Touch", f.H.String, [], new MemberModifiers { IsReadonly = true }) { Body = HandBuiltModel.Block(), DeclaringType = f.Pair },
            "hands-out-this" => body(HandBuiltRefinements.Evaluate(f.Free(f.Lender("Keep", f.Pair,
                x => [FactHolderFragments.Keep(f.StoredPair, FactHolderFragments.Read(x))], OwnershipKind.Unrestricted).Method, f.This))),
            "hands-this-to-a-call-that-keeps-nothing" => body(HandBuiltRefinements.Evaluate(f.Free(f.Method("Keep", f.Pair, HandBuiltModel.Block(), null,
                FactHolderFragments.Parameter("x", f.Pair)), f.This))),
            "hands-out-a-property-over-this" => body(FactHolderFragments.Keep(f.StoredPair, new PropertyReferenceExpression(HandBuiltModel.Node(), Self(f)))),
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
    [InlineData("reads-a-field-of-it", false)]
    [InlineData("writes-a-field-of-it", false)]
    [InlineData("rebinds-it", false)]
    [InlineData("hands-it-to-a-callee-that-reads-it", false)]
    [InlineData("recurses-through-another-callee-keeping-nothing", false)]
    [InlineData("stores-it", true)]
    [InlineData("returns-it", true)]
    [InlineData("captures-it-in-a-closure", true)]
    [InlineData("hands-it-to-a-callee-that-keeps-it", true)]
    [InlineData("calls-a-method-on-it-that-keeps-its-receiver", true)]
    [InlineData("rebinds-it-through-a-ref-parameter", true)]
    [InlineData("has-no-body", true)]
    [InlineData("returns-a-reference-into-it", true)]
    [InlineData("returns-a-reference-through-a-ref-local-into-it", true)]
    [InlineData("returns-a-reference-into-something-else", false)]
    [InlineData("sets-an-auto-property-of-it", false)]
    [InlineData("sets-a-property-whose-setter-keeps-it", true)]
    [InlineData("compounds-a-property-whose-setter-keeps-it", true)]
    public void ACalleeKeepsAnArgumentWhenItMayStoreReturnCaptureOrHandItOnToOneThatDoes(string callee, bool keeps)
    {
        var f = new FactHolderFragments();
        Expression read(LocalVariable x)
        {
            return FactHolderFragments.Read(x);
        }

        Method lender(Func<LocalVariable, Statement[]> body, ByRefDirection direction = ByRefDirection.None)
        {
            return f.Lender("Lend", f.Box, body, direction: direction).Method;
        }

        var reader = lender(x => [f.Need(FactHolderFragments.Member(read(x), f.V))]);
        var keeper = lender(x => [FactHolderFragments.Keep(f.StoredBox, read(x))]);
        var stash = f.Method("Stash", f.Box, HandBuiltModel.Block(FactHolderFragments.Keep(f.StoredBox, new ThisExpression(HandBuiltModel.Node(), f.Box))));
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var method = callee switch
        {
            "reads-a-field-of-it" => reader,
            "writes-a-field-of-it" => lender(x => [FactHolderFragments.Set(FactHolderFragments.Member(read(x), f.V), f.Number(1L))]),
            "rebinds-it" => lender(x => [FactHolderFragments.Set(read(x), FactHolderFragments.Own(f.StoredBox))]),
            "hands-it-to-a-callee-that-reads-it" => lender(x => [HandBuiltRefinements.Evaluate(f.Free(reader, read(x)))]),
            "recurses-through-another-callee-keeping-nothing" => Recursion(f),
            "stores-it" => keeper,
            "returns-it" => lender(x => [new ReturnStatement(read(x), f.Box)]),
            "captures-it-in-a-closure" => lender(x => [HandBuiltRefinements.Evaluate(f.Closure(f.Need(FactHolderFragments.Member(read(x), f.V))))]),
            "hands-it-to-a-callee-that-keeps-it" => lender(x => [HandBuiltRefinements.Evaluate(f.Free(keeper, read(x)))]),
            "calls-a-method-on-it-that-keeps-its-receiver" => lender(x => [HandBuiltRefinements.Evaluate(FactHolderFragments.Call(read(x), stash))]),
            "rebinds-it-through-a-ref-parameter" => lender(x => [FactHolderFragments.Set(read(x), FactHolderFragments.Own(f.StoredBox))], ByRefDirection.Ref),
            "has-no-body" => f.Method("Lend", f.Pair, null, null, FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed)),
            "returns-a-reference-into-it" => f.Returning(x => [new ReturnStatement(FactHolderFragments.Member(read(x), f.V), f.H.Int, isRef: true)]),
            "returns-a-reference-through-a-ref-local-into-it" => f.Returning(x => [FactHolderFragments.Declare(r, FactHolderFragments.Member(read(x), f.V)),
                new ReturnStatement(read(r), f.H.Int, isRef: true)]),
            "returns-a-reference-into-something-else" => f.Returning(x => [f.Need(FactHolderFragments.Member(read(x), f.V)),
                new ReturnStatement(FactHolderFragments.Own(f.Count), f.H.Int, isRef: true)]),
            "sets-an-auto-property-of-it" => lender(x => [FactHolderFragments.Set(FactHolderFragments.Member(read(x), f.Settable("Q", autoSetter: true)), f.Number(2L))]),
            "sets-a-property-whose-setter-keeps-it" => lender(x => [FactHolderFragments.Set(FactHolderFragments.Member(read(x), f.Settable("P", autoSetter: false)), f.Number(2L))]),
            "compounds-a-property-whose-setter-keeps-it" => lender(x => [HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(),
                FactHolderFragments.Member(read(x), f.Settable("P", autoSetter: false)), BinaryOperatorKind.Add, f.Number(2L), f.H.Int, null, IncrementForm.None))]),
            _ => throw new InvalidOperationException(callee),
        };
        var b = f.Object("b", OwnershipKind.Owned);
        var lent = FactHolderFragments.Read(b);

        Assert.Equal(keeps, new MemberWrites(new FieldEffects()).Keeps(f.Free(method, lent), lent));
    }

    [Fact]
    public void AValueThatCannotChangeIsNeverKept()
    {
        var f = new FactHolderFragments();
        var point = HandBuiltModel.Struct("Point");
        var stored = new Field("Last", point, default, new MemberModifiers { IsStatic = true }) { DeclaringType = f.Pair };
        var keeper = f.Lender("Keep", point, x => [FactHolderFragments.Keep(stored, FactHolderFragments.Read(x))], OwnershipKind.Unrestricted).Method;
        var passed = FactHolderFragments.Read(new LocalVariable("p", point));

        Assert.False(new MemberWrites(new FieldEffects()).Keeps(f.Free(keeper, passed), passed));
    }

    [Theory]
    [InlineData("argument-the-callee-reads", "Count")]
    [InlineData("argument-the-callee-keeps", null)]
    [InlineData("this-argument-the-callee-keeps", null)]
    [InlineData("extension-receiver-the-callee-reads", "Count")]
    [InlineData("extension-receiver-the-callee-keeps", null)]
    public void ACallWhoseCalleeMayKeepAReferenceItIsHandedMayWriteAnything(string form, string? fields)
    {
        var f = new FactHolderFragments();
        var b = f.Object("b", OwnershipKind.Owned);
        Statement[] reads(LocalVariable x)
        {
            return [f.Touch(), f.Need(FactHolderFragments.Member(FactHolderFragments.Read(x), f.V))];
        }

        Statement[] keeps(LocalVariable x, Field stored)
        {
            return [f.Touch(), FactHolderFragments.Keep(stored, FactHolderFragments.Read(x))];
        }

        var call = form switch
        {
            "argument-the-callee-reads" => f.Free(f.Lender("Lend", f.Box, reads).Method, FactHolderFragments.Read(b)),
            "argument-the-callee-keeps" => f.Free(f.Lender("Lend", f.Box, x => keeps(x, f.StoredBox)).Method, FactHolderFragments.Read(b)),
            "this-argument-the-callee-keeps" => f.Free(f.Lender("Keep", f.Pair, x => keeps(x, f.StoredPair), OwnershipKind.Unrestricted).Method, f.This),
            "extension-receiver-the-callee-reads" => FactHolderFragments.Call(FactHolderFragments.Read(b), f.Extension("Peek", reads)),
            "extension-receiver-the-callee-keeps" => FactHolderFragments.Call(FactHolderFragments.Read(b), f.Extension("Stash", self => keeps(self, f.StoredBox))),
            _ => throw new InvalidOperationException(form),
        };

        var written = new MemberWrites(new FieldEffects()).Of(call);

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

    private static Method Recursion(FactHolderFragments f)
    {
        var (first, x) = f.Lender("Lend", f.Box, _ => []);
        var second = f.Lender("Again", f.Box, y => [HandBuiltRefinements.Evaluate(f.Free(first, FactHolderFragments.Read(y)))]).Method;
        first.Body!.Statements = [f.Need(FactHolderFragments.Member(FactHolderFragments.Read(x), f.V)), HandBuiltRefinements.Evaluate(f.Free(second, FactHolderFragments.Read(x)))];
        return first;
    }

    private static Property Self(FactHolderFragments f)
    {
        return new Property("Self", f.Pair, new MemberModifiers { IsReadonly = true })
        {
            Getter = new PropertyAccessor { ExpressionBody = f.This },
            DeclaringType = f.Pair,
        };
    }

    private static Statement Lend(FactHolderFragments f, Expression lent, Func<LocalVariable, Statement[]> body,
        OwnershipKind ownership = OwnershipKind.Borrowed, ByRefDirection direction = ByRefDirection.None)
    {
        return HandBuiltRefinements.Evaluate(f.Free(f.Lender("Lend", f.Box, body, ownership, direction).Method, lent));
    }

    private static NewExpression Created(FactHolderFragments f, Expression argument)
    {
        var x = new LocalVariable("x", f.Box) { Parameter = FactHolderFragments.Parameter("x", f.Box, OwnershipKind.Borrowed) };
        var constructor = new Constructor("Wrapper", f.Pair, [x.Parameter], new MemberModifiers())
        {
            Body = HandBuiltModel.Block(FactHolderFragments.Keep(f.StoredBox, FactHolderFragments.Read(x))),
            DeclaringType = f.Pair,
        };
        return new NewExpression(HandBuiltModel.Node(), f.Pair, [argument], constructor, [], [f.Box]);
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