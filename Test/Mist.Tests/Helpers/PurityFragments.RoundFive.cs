using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    public IReadOnlyList<Diagnostic> RoundFive(string name)
    {
        return name switch
        {
            "clone-returning-this-into-an-owned-binding" => InsertedClone(CloneShape.ReturnsThis, into: "binding"),
            "clone-returning-this-into-an-owned-parameter" => InsertedClone(CloneShape.ReturnsThis, into: "parameter"),
            "clone-taking-this-owned-of-a-frozen-source" => InsertedClone(CloneShape.TakesThisOwned, into: "binding"),
            "clone-returning-this-inside-a-readonly-member" => InsertedClone(CloneShape.ReturnsThis, into: "receiver"),
            "clone-returning-a-fresh-owned-copy" => InsertedClone(CloneShape.ReturnsFreshOwned, into: "binding"),
            "ref-local-aliasing-a-field-of-a-captured-struct" => CapturedStructAliasedThroughAField(byRefArgument: false),
            "ref-argument-aliasing-a-field-of-a-captured-struct" => CapturedStructAliasedThroughAField(byRefArgument: true),
            "write-through-a-ref-local-into-an-owned-field" => WriteThroughARefIntoAnOwnedSlot(throughCall: false),
            "write-through-a-ref-return-into-an-owned-field" => WriteThroughARefIntoAnOwnedSlot(throughCall: true),
            "unmarked-write-through-a-ref-local-into-an-owned-field" => WriteThroughARefIntoAnOwnedSlot(throughCall: false, unmarked: true),
            "readonly-base-narrowed-to-a-mutable-subclass" => ReadonlyBaseNarrowedToAMutableSubclass(),
            "with-owned-return-of-this-through-an-unrestricted-alias" => WithOwnedReturnOfThisThroughAnAlias(),
            "owned-field-read-into-a-local-then-parent-moved" => OwnedFieldReadThenParentMoved(restored: false),
            "owned-field-read-into-a-local-restored-before-the-move" => OwnedFieldReadThenParentMoved(restored: true),
            "owned-binding-stored-into-a-field-then-moved" => OwnedBindingStoredThenMoved(intoTuple: false),
            "owned-binding-stored-into-a-tuple-then-moved" => OwnedBindingStoredThenMoved(intoTuple: true),
            "owned-return-of-a-fresh-local-kept-by-a-field" => OwnedReturnOfAFreshLocalKeptByAField(),
            "struct-ref-return-written-in-a-pure-method" => StructRefReturnWritten(readonlyMember: false),
            "struct-ref-return-written-in-a-readonly-member" => StructRefReturnWritten(readonlyMember: true),
            "enum-variant-of-a-class-holding-mutable-state" => EnumVariantOfAClassHoldingMutableState(),
            "frozen-local-reassigned" => FrozenBindingReassigned(FrozenSource.Local),
            "frozen-parameter-reassigned" => FrozenBindingReassigned(FrozenSource.Parameter),
            "params-array-reassigned" => FrozenBindingReassigned(FrozenSource.Params),
            "construction-whose-constructor-hands-out-this" => ConstructionHandingOutThis(leaks: true),
            "construction-whose-constructor-keeps-this-to-itself" => ConstructionHandingOutThis(leaks: false),
            _ => RoundFiveSites(name),
        };
    }

    private enum CloneShape
    {
        ReturnsThis,
        TakesThisOwned,
        ReturnsFreshOwned,
    }

    private enum FrozenSource
    {
        Local,
        Parameter,
        Params,
    }

    private IReadOnlyList<Diagnostic> InsertedClone(CloneShape shape, string into)
    {
        var copied = HandBuiltModel.Class("Copied");
        var v = Field(copied, "V", integer);
        _ = PureConstructor(copied);
        var clone = new Method("Clone", copied, [], Pure)
        {
            DeclaringType = copied,
            Body = shape is CloneShape.ReturnsFreshOwned ? Returning(Fresh(copied)) : Returning(new ThisExpression(Node(), copied)),
            ReturnOwnership = shape is CloneShape.ReturnsThis ? OwnershipKind.Unrestricted : OwnershipKind.Owned,
        };

        if(shape is CloneShape.TakesThisOwned)
        {
            clone.DeclaredThisTier = ThisTier.Owned;
        }

        copied.MethodsList.Add(clone);
        var c = new LocalVariable("c", copied) { Ownership = OwnershipKind.Owned };

        if(into == "receiver")
        {
            var peek = Method(copied, "Peek", integer, Public with { IsReadonly = true }, HandBuiltModel.Block(
                new LocalDeclarationStatement(c, new CloneExpression(Node(37), new ThisExpression(Node(), copied))),
                new ReturnStatement(Access(Ref(c), v), integer)));
            return Judge(new OwnershipAnalyzer(), peek);
        }

        var b = Input("b", copied, OwnershipKind.Frozen);

        if(into == "parameter")
        {
            var taken = Input("t", copied, OwnershipKind.Owned);
            var take = Method(probe, "Take", integer, PureStatic, Returning(Access(Ref(taken), v)), taken);
            var via = Method(probe, "Via", integer, PureStatic, Returning(Call(take, null, 0, new CloneExpression(Node(37), Ref(b)))), b);
            return Judge(new OwnershipAnalyzer(), via);
        }

        var bump = Method(probe, "Bump", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(c, new CloneExpression(Node(37), Ref(b))),
            new ExpressionStatement(Assign(Access(Ref(c), v), Literal(integer, 1L))),
            new ReturnStatement(Access(Ref(c), v), integer)), b);
        return Judge(new OwnershipAnalyzer(), bump);
    }

    private IReadOnlyList<Diagnostic> CapturedStructAliasedThroughAField(bool byRefArgument)
    {
        var inner = HandBuiltModel.Struct("Inner");
        var x = Field(inner, "X", integer);
        var outer = HandBuiltModel.Struct("Outer");
        var held = Field(outer, "In", inner);
        var s = new LocalVariable("s", outer);
        var step = Function(pure: true, integer, integer);
        var argument = new LocalVariable("n", integer);
        var lambda = new LambdaExpression(Node(), [argument], Access(Access(Ref(s), held, 37), x), null, false, step);
        var bumped = Input("x", integer, isRef: true);
        var bump = Method(probe, "Bump", unit, Static, HandBuiltModel.Block(), bumped);
        var r = new LocalVariable("r", inner) { IsRef = true };

        Statement[] alias = byRefArgument
            ? [new ExpressionStatement(Call(bump, null, 0, Access(Access(Ref(s), held), x)))]
            : [new LocalDeclarationStatement(r, Access(Ref(s), held)), new ExpressionStatement(Assign(Access(Ref(r), x), Literal(integer, 10L)))];

        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block([
            new LocalDeclarationStatement(s, Fresh(outer)),
            new LocalDeclarationStatement(new LocalVariable("f", step), lambda),
            .. alias]));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> WriteThroughARefIntoAnOwnedSlot(bool throughCall, bool unmarked = false)
    {
        var holder = HandBuiltModel.Class("Holder");
        var held = Field(holder, "Held", box, ownership: OwnershipKind.Owned);
        _ = PureConstructor(holder);
        var heldRef = Method(holder, "HeldRef", box, Local with { IsRef = true }, Returning(new FieldReferenceExpression(Node(), held)));
        var ext = Input("ext", box);
        var h = new LocalVariable("h", holder);
        var r = new LocalVariable("r", box) { IsRef = true };

        Statement[] write = throughCall
            ? [new ExpressionStatement(Assign(Call(heldRef, Ref(h), 0), Ref(ext, 37)))]
            : [new LocalDeclarationStatement(r, Access(Ref(h), held)), new ExpressionStatement(Assign(Ref(r), Ref(ext, 37)))];

        var hijack = Method(probe, "Hijack", integer, unmarked ? Static : PureStatic, HandBuiltModel.Block([
            new LocalDeclarationStatement(h, Fresh(holder)),
            .. write,
            new ReturnStatement(Literal(integer, 0L), integer)]), ext);
        return unmarked ? Judge(new OwnershipAnalyzer(), hijack) : Judge(new PurityAnalyzer(), hijack);
    }

    private IReadOnlyList<Diagnostic> ReadonlyBaseNarrowedToAMutableSubclass()
    {
        var shape = HandBuiltModel.Class("Shape");
        shape.Modifiers = Public with { IsReadonly = true };
        _ = PureConstructor(shape);
        var blob = HandBuiltModel.Class("Blob", shape);
        var v = Field(blob, "V", integer);
        _ = PureConstructor(blob);
        var s = Input("s", shape);
        var o = new LocalVariable("o", shape) { Ownership = OwnershipKind.Owned };
        var b = new LocalVariable("b", blob);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(o, Ref(s)),
            new ExpressionStatement(new IsPatternExpression(Node(), Ref(o), new TypeTestPattern(Node(), blob, b), boolean)),
            new ExpressionStatement(Assign(Access(Ref(b), v, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), s);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> WithOwnedReturnOfThisThroughAnAlias()
    {
        var mine = new Method("Mine", box, [], Pure)
        {
            DeclaringType = box,
            Body = Returning(new ThisExpression(Node(), box)),
            ReturnOwnership = OwnershipKind.Owned,
            DeclaredThisTier = ThisTier.Owned,
        };
        box.MethodsList.Add(mine);
        var ext = Input("ext", box);
        var alias = new LocalVariable("alias", box);
        var taken = new LocalVariable("mine", box);
        var grab = Method(probe, "Grab", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(alias, Ref(ext)),
            new LocalDeclarationStatement(taken, Call(mine, Ref(alias), 0)),
            new ExpressionStatement(Assign(Access(Ref(taken), n, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), ext);
        return Judge(new PurityAnalyzer(), grab);
    }

    private IReadOnlyList<Diagnostic> OwnedFieldReadThenParentMoved(bool restored)
    {
        var tally = HandBuiltModel.Class("Tally");
        var items = Field(tally, "Items", box, ownership: OwnershipKind.Owned);
        _ = PureConstructor(tally);
        var moved = Input("t", tally, OwnershipKind.Owned);
        var addOne = Method(probe, "AddOne", integer, PureStatic, Returning(Literal(integer, 1L)), moved);
        var t = new LocalVariable("t", tally) { Ownership = OwnershipKind.Owned };
        var leak = new LocalVariable("leak", box);
        var statements = new List<Statement>
        {
            new LocalDeclarationStatement(t, Fresh(tally)),
            new LocalDeclarationStatement(leak, Access(Ref(t), items)),
        };

        if(restored)
        {
            statements.Add(new ExpressionStatement(Assign(Access(Ref(t), items), Fresh(box))));
        }

        statements.Add(new ExpressionStatement(Call(addOne, null, 0, Ref(t, 37))));
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block([.. statements]));
        return Judge(new OwnershipAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> OwnedBindingStoredThenMoved(bool intoTuple)
    {
        var shelf = HandBuiltModel.Class("Shelf");
        var plain = Field(shelf, "Plain", box);
        _ = PureConstructor(shelf);
        var x = Input("x", box, OwnershipKind.Owned);
        var bump = Method(probe, "Bump", integer, PureStatic, Returning(Literal(integer, 1L)), x);
        var sh = new LocalVariable("shelf", shelf);
        var o = new LocalVariable("o", box) { Ownership = OwnershipKind.Owned };
        var pair = model.Interner.GetOrCreateTupleType([box, integer]);
        var t = new LocalVariable("t", pair);

        Statement stored = intoTuple
            ? new LocalDeclarationStatement(t, new TupleLiteralExpression(Node(), [Ref(o), Literal(integer, 1L)], [null, null], pair))
            : new ExpressionStatement(Assign(Access(Ref(sh), plain), Ref(o)));

        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(sh, Fresh(shelf)),
            new LocalDeclarationStatement(o, Fresh(box)),
            stored,
            new ExpressionStatement(Call(bump, null, 0, Ref(o, 37)))));
        return Judge(new OwnershipAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> OwnedReturnOfAFreshLocalKeptByAField()
    {
        var nest = HandBuiltModel.Class("Nest");
        var last = Field(nest, "Last", box);
        _ = PureConstructor(nest);
        var b = new LocalVariable("b", box);
        var make = new Method("Make", box, [], Local)
        {
            DeclaringType = nest,
            ReturnOwnership = OwnershipKind.Owned,
            Body = HandBuiltModel.Block(
                new LocalDeclarationStatement(b, Fresh(box)),
                new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), last), Ref(b))),
                new ReturnStatement(Ref(b, 37), box)),
        };
        nest.MethodsList.Add(make);
        return Judge(new OwnershipAnalyzer(), make);
    }

    private IReadOnlyList<Diagnostic> StructRefReturnWritten(bool readonlyMember)
    {
        var slotted = HandBuiltModel.Struct("Slotted");
        var x = Field(slotted, "X", integer);
        var holder = HandBuiltModel.Class("Holder");
        var c = Field(holder, "C", slotted);
        _ = PureConstructor(holder);

        if(readonlyMember)
        {
            var slotRef = Method(holder, "SlotRef", slotted, Public with { IsRef = true }, Returning(new FieldReferenceExpression(Node(), c)));
            var peek = Method(holder, "Peek", integer, Public with { IsReadonly = true }, HandBuiltModel.Block(
                new ExpressionStatement(Assign(Access(Call(slotRef, new ThisExpression(Node(), holder), 0), x, 37), Literal(integer, 1L))),
                new ReturnStatement(Literal(integer, 0L), integer)));
            return Judge(new ReadonlyAnalyzer(), peek);
        }

        var lent = Input("h", holder, OwnershipKind.Frozen);
        var slot = Method(probe, "Slot", slotted, Static with { IsRef = true }, Returning(Access(Ref(lent), c)), lent);
        var h = Input("h", holder, OwnershipKind.Frozen);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Assign(Access(Call(slot, null, 0, Ref(h)), x, 37), Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), h);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> EnumVariantOfAClassHoldingMutableState()
    {
        var tray = HandBuiltModel.Class("Tray");
        tray.Modifiers = Public with { IsReadonly = true };
        var items = Field(tray, "Items", bag, ownership: OwnershipKind.Owned);
        _ = PureConstructor(tray);
        var slot = HandBuiltModel.Enum("Slot", tray);
        var a = new EnumMember("A", false, Fresh(tray));
        slot.MembersList.Add(a);
        var t = new LocalVariable("t", slot);
        var variant = new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), slot), "A", slot, member: new EnumMemberResult(a, slot));
        var add = bag.MethodsList.Find(method => method.Name == "Add")!;
        var fill = Method(probe, "Fill", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(t, variant),
            new ExpressionStatement(Call(add, Access(Ref(t), items), 37, Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new PurityAnalyzer(), fill);
    }

    private IReadOnlyList<Diagnostic> FrozenBindingReassigned(FrozenSource source)
    {
        var ext = Input("ext", box);

        if(source is FrozenSource.Params)
        {
            var outside = Input("outside", ArrayOf(integer));
            var xs = Input("xs", ArrayOf(integer));
            xs.Parameter!.IsFlattened = true;
            var sum = Method(probe, "Sum", integer, PureStatic, HandBuiltModel.Block(
                new ExpressionStatement(Assign(Ref(xs), Ref(outside))),
                new ReturnStatement(new IndexExpression(Node(37), Ref(xs), Literal(integer, 0L), integer), integer)), outside, xs);
            return Judge(new PurityAnalyzer(), sum);
        }

        if(source is FrozenSource.Parameter)
        {
            var lent = Input("f", box, OwnershipKind.Frozen);
            var reread = Method(probe, "Peek", integer, PureStatic, HandBuiltModel.Block(
                new ExpressionStatement(Assign(Ref(lent), Ref(ext))),
                new ReturnStatement(Access(Ref(lent), n, 37), integer)), lent, ext);
            return Judge(new PurityAnalyzer(), reread);
        }

        var mine = new LocalVariable("mine", box) { Ownership = OwnershipKind.Owned };
        var f = new LocalVariable("f", box) { Ownership = OwnershipKind.Frozen };
        var peek = Method(probe, "Peek", integer, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(mine, Fresh(box)),
            new LocalDeclarationStatement(f, Ref(mine)),
            new ExpressionStatement(Assign(Ref(f), Ref(ext))),
            new ReturnStatement(Access(Ref(f), n, 37), integer)), ext);
        return Judge(new PurityAnalyzer(), peek);
    }

    private IReadOnlyList<Diagnostic> ConstructionHandingOutThis(bool leaks)
    {
        var leaker = HandBuiltModel.Class("Leaker");
        var v = Field(leaker, "V", integer);
        var keep = Method(probe, "Keep", unit, Static, HandBuiltModel.Block(), Input("l", leaker));

        Statement body = leaks
            ? new ExpressionStatement(Call(keep, null, 0, new ThisExpression(Node(), leaker)))
            : new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), v), Literal(integer, 1L)));

        var constructor = new Constructor("Leaker", leaker, [], Public) { DeclaringType = leaker, Body = HandBuiltModel.Block(body) };
        leaker.ConstructorsList.Add(constructor);
        var x = new LocalVariable("x", leaker) { Ownership = OwnershipKind.Owned };
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(x, new NewExpression(Node(37), leaker, [], constructor, [], []))));
        return Judge(new OwnershipAnalyzer(), main);
    }
}