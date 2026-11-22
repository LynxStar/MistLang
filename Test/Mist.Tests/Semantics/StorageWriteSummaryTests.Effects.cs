using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.StorageWrites;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed partial class StorageWriteSummaryTests
{
    [Fact]
    public void ACompoundOrCoalescingAssignmentReadsItsPropertyTargetAndAPlainOneDoesNot()
    {
        var box = new Generic("Box", "T");
        var lazy = box.Property("Lazy", Int32,
            getter: Body(Assign(box.ValueOf(This(box.Type)), box.ValueOf(This(box.Type)))),
            setter: Body(Assign(box.CountOf(This(box.Type)), Local("value", Int32))));
        var eager = box.Property("Eager", Int32,
            getter: HandBuiltModel.Block(new ReturnStatement(box.CountOf(This(box.Type)), Int32)),
            setter: Body(Assign(box.ValueOf(This(box.Type)), Local("seed", box.T))));
        var bumps = box.Method("Bumps", [], Body(Compound(PropertyOf(This(box.Type), lazy, box.Type), One())));
        var fills = box.Method("Fills", [], Body(Coalesce(PropertyOf(This(box.Type), lazy, box.Type), One())));
        var raises = box.Method("Raises", [], Body(Compound(PropertyOf(This(box.Type), eager, box.Type), One())));
        var bareBumps = box.Method("BareBumps", [], Body(Compound(new PropertyReferenceExpression(Node(), lazy), One())));
        var bareFills = box.Method("BareFills", [], Body(Coalesce(new PropertyReferenceExpression(Node(), lazy), One())));
        var bareAssigns = box.Method("BareAssigns", [], Body(Assign(new PropertyReferenceExpression(Node(), lazy), One())));
        var bareReads = box.Method("BareReads", [], Body(new PropertyReferenceExpression(Node(), lazy)));
        var bareStores = box.Method("BareStores", [], Body(Assign(new PropertyReferenceExpression(Node(), eager), One())));

        var writes = Solve([.. box.AccessorRoots(lazy), .. box.AccessorRoots(eager), .. box.Roots(bumps, fills, raises, bareBumps, bareFills, bareAssigns, bareReads, bareStores)]);

        Assert.Equal(Set(box.T), writes[(bumps, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(fills, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(raises, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(bareBumps, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(bareFills, StorageAccess.Read)]);
        Assert.Empty(writes[(bareAssigns, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(bareReads, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(bareStores, StorageAccess.Read)]);
    }

    [Fact]
    public void ACompoundOrCoalescingAssignmentReadsItsIndexerTargetAndAPlainOneDoesNot()
    {
        var box = new Generic("Box", "T");
        var indexer = box.Indexer(
            getter: HandBuiltModel.Block(new ExpressionStatement(Assign(box.ValueOf(This(box.Type)), box.ValueOf(This(box.Type)))), new ReturnStatement(box.ValueOf(This(box.Type)), box.T)),
            setter: Body(Assign(box.CountOf(This(box.Type)), Local("value", Int32))));
        var bumps = box.Method("Bumps", [], Body(Compound(Index(This(box.Type), indexer, box.T), Local("step", box.T))));
        var fills = box.Method("Fills", [], Body(Coalesce(Index(This(box.Type), indexer, box.T), Local("seed", box.T))));
        var stores = box.Method("Stores", [], Body(Assign(Index(This(box.Type), indexer, box.T), Local("seed", box.T))));

        var writes = Solve([.. box.AccessorRoots(indexer), .. box.Roots(bumps, fills, stores)]);

        Assert.Equal(Set(box.T), writes[(bumps, StorageAccess.Read)]);
        Assert.Equal(Set(box.T), writes[(fills, StorageAccess.Read)]);
        Assert.Empty(writes[(stores, StorageAccess.Read)]);
    }

    [Fact]
    public void AnExtensionPropertyTargetIsReadOnlyWhereTheAssignmentReadsIt()
    {
        var box = new Generic("Box", "T");
        var block = new ExtensionBlock("self", "Probe", new ExtensionScope(null), null);
        var e = new TypeParameter("E", 0, block.Scope, null);
        block.TypeParametersList.Add(e);
        var receiver = Interner.GetOrCreateConstructedType(box.Type, [e]);
        block.ResolvedReceiverType = receiver;
        var stored = new MemberAccessExpression(Node(), Local("self", receiver), box.Value.Name, e, member: new FieldResult(box.Value, box.Type, []));
        var peek = new Property("Peek", Int32, DeclarationFragments.Public)
        {
            Getter = new PropertyAccessor { Body = Body(Assign(stored, Local("seed", e))) },
            Setter = new PropertyAccessor { Body = Body() },
        };
        block.PropertiesList.Add(peek);
        fragments.Built.Model.GetOrAddNamespace(block.NamespaceName).Extensions[box.Type] = [block];
        var wrapper = new Generic("Wrapper", "V");
        var held = Interner.GetOrCreateConstructedType(box.Type, [wrapper.T]);
        var reads = wrapper.Method("Reads", [], Body(Peek(held, peek)));
        var assigns = wrapper.Method("Assigns", [], Body(Assign(Peek(held, peek), One())));
        var bumps = wrapper.Method("Bumps", [], Body(Compound(Peek(held, peek), One())));
        var fills = wrapper.Method("Fills", [], Body(Coalesce(Peek(held, peek), One())));
        var scope = Set(e);

        var writes = Solve([
            new StorageWriteRoot(peek, StorageAccess.Read, peek.Getter.Body, null, scope),
            new StorageWriteRoot(peek, StorageAccess.Write, peek.Setter.Body, null, scope),
            .. wrapper.Roots(reads, assigns, bumps, fills)]);

        Assert.Equal(Set(e), writes[(peek, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(reads, StorageAccess.Read)]);
        Assert.Empty(writes[(assigns, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(bumps, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(fills, StorageAccess.Read)]);
    }

    [Fact]
    public void AUnaryOrCompoundOperatorIsAnEdgeToTheSelectedDeclaration()
    {
        var box = new Generic("Box", "T");
        var self = Interner.GetOrCreateConstructedType(box.Type, [box.T]);
        var negate = box.Operator(OperatorKind.Minus, self, [new Parameter("operand", self)],
            Body(Assign(FieldOf(Local("operand", self), box.Value, box.Type), FieldOf(Local("operand", self), box.Value, box.Type))));
        var plus = box.Operator(OperatorKind.Plus, self, [new Parameter("left", self), new Parameter("right", self)],
            Body(Assign(FieldOf(Local("left", self), box.Value, box.Type), FieldOf(Local("right", self), box.Value, box.Type))));
        var wrapper = new Generic("Wrapper", "V");
        var inner = Interner.GetOrCreateConstructedType(box.Type, [wrapper.T]);
        var flips = wrapper.Method("Flips", [],
            Body(new UnaryExpression(Node(), UnaryOperatorKind.Negate, Local("inner", inner), inner) { Operator = negate }));
        var accumulates = wrapper.Method("Accumulates", [],
            Body(new CompoundAssignmentExpression(Node(), Local("total", inner), BinaryOperatorKind.Add, Local("other", inner), inner, plus, IncrementForm.None)));

        var writes = Solve([.. box.Roots(negate, plus), .. wrapper.Roots(flips, accumulates)]);

        Assert.Equal(Set(wrapper.T), writes[(flips, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(accumulates, StorageAccess.Read)]);
    }

    /// <summary>
    /// A conversion into a box of T takes a bare T, so only the type converted to says how the site
    /// closes the operator's owner; a conversion with no body writes what its operand mentions.
    /// </summary>
    [Fact]
    public void AConversionIsAnEdgeClosedByWhicheverSideNamesItsOwner()
    {
        var box = new Generic("Box", "T");
        var self = Interner.GetOrCreateConstructedType(box.Type, [box.T]);
        var wrap = box.Conversion(box.T, self, isImplicit: true,
            Body(Assign(FieldOf(Local("made", self), box.Value, box.Type), Local("value", box.T))));
        var unwrap = box.Conversion(self, Int32, isImplicit: false,
            Body(Assign(FieldOf(Local("value", self), box.Value, box.Type), FieldOf(Local("value", self), box.Value, box.Type))));
        var native = HandBuiltModel.Conversion(box.Type, box.T, Int32, isIntrinsic: true);
        var wrapper = new Generic("Wrapper", "V");
        var inner = Interner.GetOrCreateConstructedType(box.Type, [wrapper.T]);
        var wraps = wrapper.Method("Wraps", [],
            Body(new ImplicitConversionExpression(Node(), Local("seed", wrapper.T), inner, [new ConversionFunction(wrap, wrapper.T, inner)])));
        var unwraps = wrapper.Method("Unwraps", [],
            Body(new CastExpression(Node(), Local("inner", inner), Int32, [new ConversionFunction(unwrap, inner, Int32)], Int32)));
        var lowers = wrapper.Method("Lowers", [],
            Body(new CastExpression(Node(), Local("seed", wrapper.T), Int32, [new ConversionFunction(native, wrapper.T, Int32)], Int32)));

        var writes = Solve([.. box.Roots(wrap, unwrap), .. wrapper.Roots(wraps, unwraps, lowers)]);

        Assert.Equal(Set(wrapper.T), writes[(wraps, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(unwraps, StorageAccess.Read)]);
        Assert.Equal(Set(wrapper.T), writes[(lowers, StorageAccess.Read)]);
    }

    [Fact]
    public void AConstructionWithoutABodyWritesWhatItsArgumentsMention()
    {
        var native = new Generic("Native", "N");
        var seeded = native.Constructor([new Parameter("seed", native.T)], body: null);
        var sized = native.Constructor([new Parameter("size", Int32)], body: null);
        var outer = new Generic("Outer", "U");
        var created = Interner.GetOrCreateConstructedType(native.Type, [outer.T]);
        var seeds = outer.Method("Seeds", [], Body(new NewExpression(Node(), created, [Local("seed", outer.T)], seeded, [], parameterTypes: null)));
        var counts = outer.Method("Counts", [], Body(new NewExpression(Node(), created, [One()], sized, [], parameterTypes: null)));

        var writes = Solve(outer.Roots(seeds, counts));

        Assert.Equal(Set(outer.T), writes[(seeds, StorageAccess.Read)]);
        Assert.Empty(writes[(counts, StorageAccess.Read)]);
    }

    [Fact]
    public void AnInitializerEntryWritesThroughTheSlotItAssigns()
    {
        var cell = new Generic("Cell", "T");
        var eager = cell.Property("Eager", Int32,
            getter: HandBuiltModel.Block(new ReturnStatement(cell.CountOf(This(cell.Type)), Int32)),
            setter: Body(Assign(cell.ValueOf(This(cell.Type)), Local("seed", cell.T))));
        var lazy = cell.Property("Lazy", Int32,
            getter: Body(Assign(cell.ValueOf(This(cell.Type)), cell.ValueOf(This(cell.Type)))),
            setter: Body(Assign(cell.CountOf(This(cell.Type)), Local("value", Int32))));
        var add = cell.Method("Add", [new Parameter("item", cell.T)], Body(Assign(cell.ValueOf(This(cell.Type)), Local("item", cell.T))));
        var native = new Generic("Native", "N");
        var append = new Method("Add", Int32, [new Parameter("item", native.T)], DeclarationFragments.Public) { DeclaringType = native.Type };
        native.Type.MethodsList.Add(append);
        var outer = new Generic("Outer", "U");
        var filled = Interner.GetOrCreateConstructedType(cell.Type, [outer.T]);
        var listed = Interner.GetOrCreateConstructedType(native.Type, [outer.T]);
        var field = outer.Method("Field", [], Body(Initialized(filled, Entry(cell.Value.Name, Local("seed", outer.T), outer.T, new FieldResult(cell.Value, cell.Type, [])))));
        var count = outer.Method("Count", [], Body(Initialized(filled, Entry(cell.Count.Name, One(), Int32, new FieldResult(cell.Count, cell.Type, [])))));
        var setter = outer.Method("Setter", [], Body(Initialized(filled, Entry(eager.Name, One(), Int32, new PropertyResult(eager, cell.Type, [])))));
        var getter = outer.Method("Getter", [], Body(Initialized(filled, Entry(lazy.Name, One(), Int32, new PropertyResult(lazy, cell.Type, [])))));
        var element = outer.Method("Element", [], Body(Initialized(filled, new ObjectInitializerAssignment(Node(), null, Local("seed", outer.T), null, add))));
        var foreign = outer.Method("Foreign", [], Body(Initialized(listed, new ObjectInitializerAssignment(Node(), null, Local("seed", outer.T), null, append))));

        var writes = Solve([.. cell.AccessorRoots(eager), .. cell.AccessorRoots(lazy), .. cell.Roots(add), .. outer.Roots(field, count, setter, getter, element, foreign)]);

        Assert.Equal(Set(outer.T), writes[(field, StorageAccess.Read)]);
        Assert.Empty(writes[(count, StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(setter, StorageAccess.Read)]);
        Assert.Empty(writes[(getter, StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(element, StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(foreign, StorageAccess.Read)]);
    }

    [Fact]
    public void AForEachInheritsTheWritesOfEachMemberTheLoopCalls()
    {
        var plain = Cursor("PlainCursor", moveNextWrites: false, currentWrites: false);
        var moving = Cursor("MovingCursor", moveNextWrites: true, currentWrites: false);
        var reading = Cursor("ReadingCursor", moveNextWrites: false, currentWrites: true);
        var outer = new Generic("Outer", "U");
        var roots = new List<StorageWriteRoot>();
        var loops = new List<Method>();

        foreach(var (name, cursor, getEnumeratorWrites) in new[] { ("Plain", plain, false), ("Enumerating", plain, true), ("Moving", moving, false), ("Reading", reading, false) })
        {
            var bag = new Generic($"{name}Bag", "B");
            var getEnumerator = bag.Method("GetEnumerator", [], getEnumeratorWrites ? Body(Assign(bag.ValueOf(This(bag.Type)), bag.ValueOf(This(bag.Type)))) : Body(),
                returns: Interner.GetOrCreateConstructedType(cursor.Type.Type, [bag.T]));
            var loop = new ForEachStatement(new LocalVariable("item", outer.T), Local("items", Interner.GetOrCreateConstructedType(bag.Type, [outer.T])), HandBuiltModel.Block())
            {
                GetEnumerator = getEnumerator,
                MoveNext = cursor.MoveNext,
                Current = cursor.Current,
            };
            loops.Add(outer.Method($"Loops{name}", [], HandBuiltModel.Block(loop)));
            roots.AddRange(bag.Roots(getEnumerator));
        }

        foreach(var (type, moveNext, current) in new[] { plain, moving, reading })
        {
            roots.AddRange(type.Roots(moveNext));
            roots.AddRange(type.AccessorRoots(current));
        }

        var writes = Solve([.. roots, .. outer.Roots([.. loops])]);

        Assert.Empty(writes[(loops[0], StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(loops[1], StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(loops[2], StorageAccess.Read)]);
        Assert.Equal(Set(outer.T), writes[(loops[3], StorageAccess.Read)]);
    }

    private static (Generic Type, Method MoveNext, Property Current) Cursor(string name, bool moveNextWrites, bool currentWrites)
    {
        var cursor = new Generic(name, "C");
        var touch = Assign(cursor.ValueOf(This(cursor.Type)), cursor.ValueOf(This(cursor.Type)));
        var moveNext = cursor.Method("MoveNext", [], moveNextWrites ? Body(touch) : Body());
        var current = cursor.Property("Current", cursor.T,
            getter: currentWrites ? Body(touch) : HandBuiltModel.Block(new ReturnStatement(cursor.ValueOf(This(cursor.Type)), cursor.T)),
            setter: Body());

        return (cursor, moveNext, current);
    }

    private LiteralExpression One()
    {
        return HandBuiltModel.Literal(Int32, 1);
    }

    private static CompoundAssignmentExpression Compound(Expression target, Expression value)
    {
        return new CompoundAssignmentExpression(Node(), target, BinaryOperatorKind.Add, value, target.Type, @operator: null, IncrementForm.None);
    }

    private static AssignmentExpression Coalesce(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type, isCoalescing: true);
    }

    private static ExtensionPropertyAccessExpression Peek(IMistType receiver, Property property)
    {
        return new ExtensionPropertyAccessExpression(Node(), Local("held", receiver), property, "BoxExtensions", property.Type);
    }

    private static NewExpression Initialized(IMistType created, ObjectInitializerAssignment entry)
    {
        return new NewExpression(Node(), created, [], constructor: null, [entry], parameterTypes: null);
    }

    private static ObjectInitializerAssignment Entry(string name, Expression value, IMistType slot, LookupResult member)
    {
        return new ObjectInitializerAssignment(Node(), name, value, slot, member: member);
    }
}