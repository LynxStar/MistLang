using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
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

internal sealed partial class PurityFragments
{
    public IReadOnlyList<Diagnostic> PatternReads(string name)
    {
        return name switch
        {
            "counted-list-pattern-slices-through-an-impure-slice-method" => CountedListPatternSlice(impureCount: false, SliceThrough.ImpureMethod),
            "counted-list-pattern-slices-through-an-impure-range-indexer" => CountedListPatternSlice(impureCount: false, SliceThrough.ImpureRangeIndexer),
            "counted-list-pattern-slices-through-pure-members" => CountedListPatternSlice(impureCount: false, SliceThrough.PureMethod),
            "counted-list-pattern-with-an-impure-count-slices-through-an-impure-slice-method" => CountedListPatternSlice(impureCount: true, SliceThrough.ImpureMethod),
            "disjunction-reads-a-mutable-field-in-its-second-operand" => DisjunctionReadsAMutableFieldInItsSecondOperand(),
            "property-pattern-names-a-static-field-the-binder-refused" => PropertyPatternNamesAStaticFieldTheBinderRefused(),
            "pure-getter-reads-the-backing-field-of-a-get-only-property" => PureGetterReadsItsBackingField(setter: null),
            "pure-getter-reads-the-backing-field-of-an-init-only-property" => PureGetterReadsItsBackingField(new PropertyAccessor { IsInit = true }),
            "pure-getter-reads-the-backing-field-of-a-settable-property" => PureGetterReadsItsBackingField(new PropertyAccessor()),
            "pure-method-indexes-a-readonly-value-held-in-a-stable-field" => PureMethodIndexesAReadonlyValueHeldInAStableField(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    public IReadOnlyList<Diagnostic> NoisyTriggers(string name)
    {
        return name switch
        {
            "allocation" => NoisyTriggeredBy(NoisyTrigger.Allocation),
            "bare-static-field" => NoisyTriggeredBy(NoisyTrigger.BareStaticField),
            "bare-static-property" => NoisyTriggeredBy(NoisyTrigger.BareStaticProperty),
            "unary-operator" => NoisyTriggeredBy(NoisyTrigger.UnaryOperator),
            "explicit-conversion" => NoisyTriggeredBy(NoisyTrigger.ExplicitConversion),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    public bool FormatsByType(string shape)
    {
        var clr = HandBuiltModel.Class("Lazy");
        var suit = new EnumType("Suit", "Probe", new Scope(null), null, null) { UnderlyingType = integer };
        IMistType type = shape switch
        {
            "named-tuple-of-an-int-and-a-string" => NamedPair(text),
            "named-tuple-of-an-int-and-a-clr-type" => NamedPair(clr),
            "alias-of-an-enum" => new TypeAlias("Hand", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = suit },
            "alias-of-a-clr-type" => new TypeAlias("Later", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = clr },
            "enum" => suit,
            "newtype" => new Newtype("Meters", "Probe", new Scope(null), null, new NewtypeDeclarationNode()),
            "function-type" => Function(pure: false, integer, integer),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        return PurityJudge.FormatsByType(type);
    }

    private enum SliceThrough
    {
        PureMethod,
        ImpureMethod,
        ImpureRangeIndexer,
    }

    private enum NoisyTrigger
    {
        Allocation,
        BareStaticField,
        BareStaticProperty,
        UnaryOperator,
        ExplicitConversion,
    }

    private NamedTupleType NamedPair(IMistType second)
    {
        return new NamedTupleType(model.Interner.GetOrCreateTupleType([integer, second]), ["n", "s"]);
    }

    private PropertyAccessor Accessor(bool impure)
    {
        return impure
            ? new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 2L), integer)) }
            : new PropertyAccessor { Body = Returning(Literal(integer, 2L)) };
    }

    private IReadOnlyList<Diagnostic> CountedListPatternSlice(bool impureCount, SliceThrough through)
    {
        var row = HandBuiltModel.Class("Row");
        _ = PureConstructor(row);
        var count = new Property("Count", integer, Public) { DeclaringType = row, Getter = Accessor(impureCount) };
        row.PropertiesList.Add(count);
        var element = new Indexer(integer, [new Parameter("i", integer)], Public) { DeclaringType = row, Getter = Accessor(impure: false) };
        row.IndexersList.Add(element);
        CountedSlice slice;

        if(through is SliceThrough.ImpureRangeIndexer)
        {
            var range = new Indexer(integer, [new Parameter("r", model.Type("Range"))], Public) { DeclaringType = row, Getter = Accessor(impure: true) };
            row.IndexersList.Add(range);
            slice = new CountedSlice(count, range, null);
        }
        else
        {
            var start = Input("start", integer);
            var length = Input("length", integer);
            var body = through is SliceThrough.ImpureMethod
                ? HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Ref(length), integer))
                : Returning(Ref(length));
            slice = new CountedSlice(count, null, Method(row, "Slice", integer, Public, body, start, length));
        }

        var r = new LocalVariable("r", row);
        var elements = new List<ListPatternElement>
        {
            new(new CatchAllPattern(Node(), integer, null), IsSlice: false, integer),
            new(new CatchAllPattern(Node(), integer, new LocalVariable("rest", integer)), IsSlice: true, integer),
        };
        var pattern = new ListPattern(Node(37), elements, row) { Reads = new CountedIndexer(element, integer, count), Slice = slice };
        var starts = Method(probe, "Starts", boolean, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(r, Fresh(row)),
            new ReturnStatement(new IsPatternExpression(Node(), Ref(r), pattern, boolean), boolean)));
        return Judge(new PurityAnalyzer(), starts);
    }

    private IReadOnlyList<Diagnostic> DisjunctionReadsAMutableFieldInItsSecondOperand()
    {
        var gauge = HandBuiltModel.Class("Gauge");
        var fixedLevel = Field(gauge, "Fixed", integer, isReadonly: true);
        var level = Field(gauge, "Level", integer);
        var g = Input("g", gauge);
        var either = new DisjunctionPattern(Node(), gauge,
        [
            new PropertyPattern(Node(), gauge, [Subpattern(Node(), fixedLevel, gauge)]),
            new PropertyPattern(Node(), gauge, [Subpattern(Node(37), level, gauge)]),
        ]);
        var test = Method(probe, "Test", boolean, PureStatic, Returning(new IsPatternExpression(Node(), Ref(g), either, boolean)), g);
        return Judge(new PurityAnalyzer(), test);
    }

    private IReadOnlyList<Diagnostic> PropertyPatternNamesAStaticFieldTheBinderRefused()
    {
        var gauge = HandBuiltModel.Class("Gauge");
        _ = PureConstructor(gauge);
        var shared = Field(gauge, "Shared", integer, isStatic: true);
        var g = new LocalVariable("g", gauge);
        var refused = new PropertySubpattern(Node(37), shared.Name, new ComparisonPattern(Node(), ErrorType.Instance, ComparisonKind.Equal, Literal(integer, 1L)), ErrorType.Instance, new FieldResult(shared, gauge, []))
        {
            IsMemberFailed = true,
        };
        var pattern = new PropertyPattern(Node(), ErrorType.Instance, [refused]) { IsFailed = true };
        var test = Method(probe, "Test", boolean, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(g, Fresh(gauge)),
            new ReturnStatement(new IsPatternExpression(Node(), Ref(g), pattern, boolean), boolean)));
        return Judge(new PurityAnalyzer(), test);
    }

    private PropertySubpattern Subpattern(Storm.Runtime.SyntaxNode at, Field field, ConcreteType holder)
    {
        return new PropertySubpattern(at, field.Name, new ComparisonPattern(Node(), integer, ComparisonKind.Equal, Literal(integer, 1L)), field.Type, new FieldResult(field, holder, []));
    }

    private IReadOnlyList<Diagnostic> PureGetterReadsItsBackingField(PropertyAccessor? setter)
    {
        var meter = HandBuiltModel.Class("Meter");
        var getter = new PropertyAccessor();
        var reading = new Property("Reading", integer, Pure) { DeclaringType = meter, Getter = getter, Setter = setter, UsesBackingField = true };
        getter.ExpressionBody = new BackingFieldReferenceExpression(Node(37), reading);
        meter.PropertiesList.Add(reading);
        return model.Analyze(new PurityAnalyzer(), getter.ExpressionBody, [meter], meter, reading);
    }

    private IReadOnlyList<Diagnostic> PureMethodIndexesAReadonlyValueHeldInAStableField()
    {
        var frame = new StructType("Frame", "System.Collections.Immutable", new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true },
        };
        var holder = HandBuiltModel.Class("Holder");
        var held = Field(holder, "Frame", frame, isReadonly: true);
        var h = Input("h", holder);
        var first = new IndexExpression(Node(37), Access(Ref(h), held), Literal(integer, 0L), integer);
        return Judge(new PurityAnalyzer(), Method(probe, "First", integer, PureStatic, Returning(first), h));
    }

    private IReadOnlyList<Diagnostic> NoisyTriggeredBy(NoisyTrigger trigger)
    {
        var noisy = trigger is NoisyTrigger.UnaryOperator or NoisyTrigger.ExplicitConversion ? (ConcreteType)HandBuiltModel.Struct("Noisy") : HandBuiltModel.Class("Noisy");

        switch(trigger)
        {
            case NoisyTrigger.Allocation:
                StaticConstructorWritesTicks(noisy);
                var made = new LocalVariable("made", noisy);
                return Judge(new PurityAnalyzer(), Method(probe, "Make", integer, PureStatic, HandBuiltModel.Block(
                    new LocalDeclarationStatement(made, new NewExpression(Node(37), noisy, [], PureConstructor(noisy), [], [])),
                    new ReturnStatement(Literal(integer, 0L), integer))));

            case NoisyTrigger.BareStaticField:
                var seed = NoisyStaticInitializer(noisy);
                return Judge(new PurityAnalyzer(), Method(noisy, "Read", integer, PureStatic, Returning(new FieldReferenceExpression(Node(37), seed))));

            case NoisyTrigger.BareStaticProperty:
                _ = NoisyStaticInitializer(noisy);
                var level = new Property("Level", integer, Static) { DeclaringType = noisy, Getter = new PropertyAccessor { IsAuto = true } };
                noisy.PropertiesList.Add(level);
                return Judge(new PurityAnalyzer(), Method(noisy, "Read", integer, PureStatic, Returning(new PropertyReferenceExpression(Node(37), level))));

            case NoisyTrigger.UnaryOperator:
                StaticConstructorWritesTicks(noisy);
                var negate = new Operator(OperatorKind.Minus, integer, [new Parameter("a", noisy)], PureStatic) { DeclaringType = noisy, Body = Returning(Literal(integer, 0L)) };
                noisy.OperatorsList.Add(negate);
                var a = Input("a", noisy);
                var negated = new UnaryExpression(Node(37), UnaryOperatorKind.Negate, Ref(a), integer) { Operator = negate };
                return Judge(new PurityAnalyzer(), Method(probe, "Negate", integer, PureStatic, Returning(negated), a));

            default:
                StaticConstructorWritesTicks(noisy);
                var conversion = new CastOperator(integer, new Parameter("d", noisy), false, PureStatic) { DeclaringType = noisy, Body = Returning(Literal(integer, 0L)) };
                noisy.CastOperatorsList.Add(conversion);
                var d = Input("d", noisy);
                var cast = new CastExpression(Node(37), Ref(d), integer, [new ConversionFunction(conversion, noisy, integer)], integer);
                return Judge(new PurityAnalyzer(), Method(probe, "Convert", integer, PureStatic, Returning(cast), d));
        }
    }

    private void StaticConstructorWritesTicks(ConcreteType noisy)
    {
        noisy.ConstructorsList.Add(new Constructor("Noisy", noisy, [], Static) { DeclaringType = noisy, Body = HandBuiltModel.Block(WriteStatic(ticks)) });
    }

    private Field NoisyStaticInitializer(ConcreteType noisy)
    {
        var tally = HandBuiltModel.Class("Tally");
        var bump = Method(tally, "Bump", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 1L), integer)));
        var seed = Field(noisy, "Seed", integer, isStatic: true, isReadonly: true);
        seed.Initializer = Call(bump, null, 0);
        return seed;
    }
}