using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySoundnessFragments
{
    private IReadOnlyList<Diagnostic> StructLocalReadonlyMemberReadsThroughAnOwnedFieldOfACopy()
    {
        var (cell, inner) = OwnedCell();
        var look = Method(cell, "Look", integer, LocalReadonly, Returning(Access(Bare(inner), n)));
        return Judge(new PurityAnalyzer(), look);
    }

    private IReadOnlyList<Diagnostic> CallsAnUnmarkedStructMemberWritingThroughAnOwnedField(IProgramAnalyzer analyzer, MemberModifiers caller)
    {
        var (cell, inner) = OwnedCell();
        var bump = Method(cell, "Bump", unit, Public, HandBuiltModel.Block(Set(Access(Bare(inner), n), Lit(42))));
        var c = Input("c", cell);
        var poke = Method(probe, "Poke", integer, caller, HandBuiltModel.Block(Statement(Call(bump, Ref(c))), Return(Lit(0))), c);
        return Judge(analyzer, poke);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAStructsLocalMembersOnAParameter()
    {
        var (cell, bump, look) = CountingCell();
        var c = Input("c", cell);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(Statement(Call(bump, Ref(c))), Return(Call(look, Ref(c)))), c);
        return Judge(new PurityAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMethodReadsAnAbstractAutoGetterAnsweredByAnImpureImplementation()
    {
        var (shape, area) = Shape(settable: false);
        var square = Implementer("Sq", shape);
        square.PropertiesList.Add(new Property("Area", integer, Public)
        {
            DeclaringType = square,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1)), Return(StaticRead(ticks))) },
        });
        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, ReadonlyStatic, Returning(AccessProperty(Ref(s), area)), s);
        return Judge(new ReadonlyAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> PureMethodSetsAnAbstractAutoSetterAnsweredByAnImpureImplementation()
    {
        var (shape, area) = Shape(settable: true);
        var square = Implementer("Sq", shape);
        var made = Constructor(square, Pure);
        square.PropertiesList.Add(new Property("Area", integer, Public)
        {
            DeclaringType = square,
            Getter = new PropertyAccessor { Body = Returning(Lit(1)) },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block(Set(StaticRead(ticks), Lit(1))) },
        });
        var s = new LocalVariable("s", shape) { Ownership = OwnershipKind.Owned };
        var fill = Method(probe, "Fill", integer, PureStatic, HandBuiltModel.Block(
            Declare(s, New(square, made)),
            Set(AccessProperty(Ref(s), area), Lit(5)),
            Return(AccessProperty(Ref(s), area))));
        return Judge(new PurityAnalyzer(), fill, shape, square);
    }

    private IReadOnlyList<Diagnostic> PureMethodReadsAnAbstractAutoGetterAnsweredByPureImplementations()
    {
        var (shape, area) = Shape(settable: false);
        var square = Implementer("Sq", shape);
        var side = Field(square, "side", integer, Readonly);
        var given = Input("s", integer);
        _ = Constructor(square, Pure, HandBuiltModel.Block(Set(Bare(side), Ref(given))), given);
        square.PropertiesList.Add(new Property("Area", integer, Public)
        {
            DeclaringType = square,
            Getter = new PropertyAccessor { Body = Returning(Sum(Bare(side), Bare(side))) },
        });
        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, PureStatic, Returning(AccessProperty(Ref(s), area)), s);
        return Judge(new PurityAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> PureMethodCallsAnAbstractDefaultBody(bool replaced)
    {
        var shape = Contract("IShape");
        var slot = Method(shape, "Area", integer, Public, Returning(Lit(1)));
        var square = Implementer("Sq", shape);

        if(replaced)
        {
            _ = Method(square, "Area", integer, Public, Returning(Lit(4)));
        }

        var s = Input("s", shape);
        var measure = Method(probe, "Measure", integer, PureStatic, Returning(Call(slot, Ref(s))), s);
        return Judge(new PurityAnalyzer(), measure, shape, square);
    }

    private IReadOnlyList<Diagnostic> OwnedSlotAnsweredByAnOwnedReturn()
    {
        var (contract, _, maker) = OwnedMaker();
        return model.Analyze(new ConformanceAnalyzer(), types: [contract, maker]);
    }

    private IReadOnlyList<Diagnostic> PureMethodWritesTheOwnedResultOfAnOwnedSlot()
    {
        var (contract, slot, maker) = OwnedMaker();
        var m = Input("m", contract);
        var b = new LocalVariable("b", box);
        var poke = Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
            Declare(b, Call(slot, Ref(m))),
            Set(Access(Ref(b), n), Sum(Access(Ref(b), n), Lit(1))),
            Return(Access(Ref(b), n))), m);
        return Judge(new PurityAnalyzer(), poke, contract, maker);
    }

    private IReadOnlyList<Diagnostic> GenericClosing(bool asExpression, IMistType argument)
    {
        var generic = Method(probe, "Coerce", UnknownType.Instance, Static, null);
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        generic.TypeParametersList.Add(parameter);
        var u = Input("u", UnknownType.Instance);
        Expression test = asExpression
            ? new AsExpression(Node(), Ref(u), parameter, AsConversionKind.Narrowing, null, parameter)
            : new IsPatternExpression(Node(), Ref(u), new TypeTestPattern(Node(), parameter, new LocalVariable("t", parameter)), boolean);
        model.Model.ConstructorConstraintClosures.RecordMethod(generic, [argument], TypeSubstitution.Empty, Node());
        return model.Analyze(new PatternAnalyzer(), test, owner: probe, member: generic);
    }

    private IReadOnlyList<Diagnostic> ReadonlyTypeFieldBackedSettablePropertyReadThroughAnUnrestrictedReference()
    {
        var getter = new PropertyAccessor();
        var setter = new PropertyAccessor();
        var (frozenType, stored) = ReadonlyType(getter, setter);
        stored.UsesBackingField = true;
        getter.ExpressionBody = new BackingFieldReferenceExpression(Node(), stored);
        setter.ExpressionBody = new AssignmentExpression(Node(), new BackingFieldReferenceExpression(Node(), stored), Ref(new LocalVariable("value", integer)), integer);
        var r = Input("r", frozenType);
        var peek = Method(probe, "Peek", integer, PureStatic, Returning(AccessProperty(Ref(r), stored)), r);
        return Judge(new PurityAnalyzer(), peek, frozenType);
    }

    private IReadOnlyList<Diagnostic> ReadonlyTypePropertyRead(PropertyAccessor setter, OwnershipKind ownership)
    {
        var (frozenType, stored) = ReadonlyType(new PropertyAccessor { IsAuto = true }, setter);
        var r = Input("r", frozenType, ownership);
        var peek = Method(probe, "Peek", integer, PureStatic, Returning(AccessProperty(Ref(r), stored)), r);
        return Judge(new PurityAnalyzer(), peek, frozenType);
    }

    private IReadOnlyList<Diagnostic> AbstractLocalIndexerImplemented(Block? setterBody)
    {
        var contract = Contract("IGrid");
        contract.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Local) { DeclaringType = contract, Getter = new PropertyAccessor(), Setter = new PropertyAccessor() });
        var grid = Implementer("Grid", contract, declared: true);
        var cell = Field(grid, "cell", integer, Public);
        grid.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Public)
        {
            DeclaringType = grid,
            Getter = new PropertyAccessor { Body = Returning(Bare(cell)) },
            Setter = new PropertyAccessor { Body = setterBody ?? HandBuiltModel.Block(Set(Bare(cell), Ref(new LocalVariable("value", integer)))) },
        });
        return model.Analyze(new ConformanceAnalyzer(), types: [contract, grid]);
    }

    private IReadOnlyList<Diagnostic> AbstractPureIndexerImplementedPurely()
    {
        var contract = Contract("IGrid");
        contract.IndexersList.Add(new Indexer(integer, [new Parameter("i", integer)], Pure) { DeclaringType = contract, Getter = new PropertyAccessor() });
        var grid = Implementer("Grid", contract, declared: true);
        var i = Input("i", integer);
        grid.IndexersList.Add(new Indexer(integer, [i.Parameter!], Public) { DeclaringType = grid, Getter = new PropertyAccessor { Body = Returning(Sum(Ref(i), Ref(i))) } });
        return model.Analyze(new ConformanceAnalyzer(), types: [contract, grid]);
    }

    private (StructType Cell, Field Inner) OwnedCell()
    {
        var cell = HandBuiltModel.Struct("Cell");
        var inner = Field(cell, "Inner", box, Public, OwnershipKind.Owned);
        var given = Input("inner", box, OwnershipKind.Owned);
        _ = Constructor(cell, Pure, HandBuiltModel.Block(Set(Bare(inner), Ref(given))), given);
        return (cell, inner);
    }

    private (StructType Cell, Method Bump, Method Look) CountingCell()
    {
        var cell = HandBuiltModel.Struct("Cell");
        var tally = Field(cell, "Count", integer, Public);
        var given = Input("c", integer);
        _ = Constructor(cell, Pure, HandBuiltModel.Block(Set(Bare(tally), Ref(given))), given);
        var bump = Method(cell, "Bump", unit, Local, HandBuiltModel.Block(Set(Bare(tally), Sum(Bare(tally), Lit(1)))));
        var look = Method(cell, "Look", integer, LocalReadonly, Returning(Bare(tally)));
        return (cell, bump, look);
    }

    private (AbstractType Shape, Property Area) Shape(bool settable)
    {
        var shape = Contract("IShape");
        var area = new Property("Area", integer, Public)
        {
            DeclaringType = shape,
            Getter = new PropertyAccessor(),
            Setter = settable ? new PropertyAccessor() : null,
        };
        shape.PropertiesList.Add(area);
        return (shape, area);
    }

    private (AbstractType Contract, Method Slot, ClassType Maker) OwnedMaker()
    {
        var contract = Contract("IMaker");
        var slot = new Method("Make", box, [], Pure) { DeclaringType = contract, ReturnOwnership = OwnershipKind.Owned };
        contract.MethodsList.Add(slot);
        var maker = Implementer("Maker", contract, declared: true);
        maker.MethodsList.Add(new Method("Make", box, [], Pure)
        {
            DeclaringType = maker,
            ReturnOwnership = OwnershipKind.Owned,
            Body = Returning(New(box, boxConstructor)),
        });
        return (contract, slot, maker);
    }

    private (ClassType Type, Property X) ReadonlyType(PropertyAccessor getter, PropertyAccessor? setter)
    {
        var frozenType = HandBuiltModel.Class("R");
        frozenType.Modifiers = Public with { IsReadonly = true };
        var stored = new Property("X", integer, Public with { IsReadonly = true }) { DeclaringType = frozenType, Getter = getter, Setter = setter };
        frozenType.PropertiesList.Add(stored);
        return (frozenType, stored);
    }

    private static AbstractType Contract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static ClassType Implementer(string name, AbstractType contract, bool declared = false)
    {
        var type = declared
            ? new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(37))
            : HandBuiltModel.Class(name);
        type.BaseAbstractTypes.Add(contract);
        return type;
    }
}