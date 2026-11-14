using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// Static storage of a value-routed type over hand-built declarations (runtime-dependent-values design, <em>Identity by
/// value: static storage</em>): the holder that keeps one storage for each value of the slots and the table that
/// finds it, how a closing written with a literal, a capture or a dependent parameter reaches it, how the type's own
/// members reach it by a bare name, and the runtime that builds a holder once for a value. A carrier-routed type keeps
/// its C# statics.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteStaticStorageEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType counter;
    private readonly TypeParameter n;
    private readonly Field hits;

    public ValueRouteStaticStorageEmissionTests()
    {
        counter = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Counter<>"), "N", slots.Int);
        counter.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        n = counter.TypeParametersList[0];
        hits = Static("hits");
        counter.FieldsList.Add(hits);
    }

    [Fact]
    public void AFieldAnAutoPropertyAndAnInitializerReadingTheSlotLiveInTheHolderItsTableFinds()
    {
        var doubled = Static("doubled");
        doubled.Initializer = new BinaryExpression(HandBuiltModel.Node(), Slot(), BinaryOperatorKind.Add, Slot(), slots.Int);
        counter.FieldsList.Add(doubled);
        counter.PropertiesList.Add(new Property("Next", slots.Int, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = counter,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Private },
        });
        using var routes = SlotRoutes.Enter([n]);

        var lines = TypeBody(counter);

        Assert.Contains("public sealed class __Statics", lines);
        Assert.Contains("public int hits;", lines);
        Assert.Contains("public int doubled;", lines);
        Assert.Contains("public int Next { get; internal set; }", lines);
        Assert.Contains("public __Statics(int __N)", lines);
        Assert.Contains("__statics.Enter(__N, this);", lines);
        Assert.Contains("this.doubled = __N + __N;", lines);
        Assert.Contains("private static readonly MistStatics.Table<int, __Statics> __statics = new MistStatics.Table<int, __Statics>(static key => new __Statics(key));", lines);
        Assert.Contains("public static __Statics __StaticsOf(int __N)", lines);
        Assert.Contains("return __statics.Of(__N);", lines);
        Assert.DoesNotContain(lines, line => line.Contains("static int hits", StringComparison.Ordinal) || line.Contains("static int Next", StringComparison.Ordinal));
        Assert.True(lines.IndexOf("__statics.Enter(__N, this);") < lines.IndexOf("this.doubled = __N + __N;"));
    }

    [Fact]
    public void TheHolderOfATypeWithTwoSlotsIsKeyedByBothValues()
    {
        var grid = (ClassType)HandBuiltSlots.Generic(HandBuiltModel.Class("Grid<>"), "W", slots.Int);
        var h = new TypeParameter("H", 1, grid.Scope, grid) { IsDependent = true, Domain = (MistType)slots.Int };
        grid.TypeParametersList.Add(h);
        grid.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        grid.FieldsList.Add(new Field("visits", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = grid });
        using var routes = SlotRoutes.Enter([grid.TypeParametersList[0], h]);

        var lines = TypeBody(grid);

        Assert.Contains("__statics.Enter((__W, __H), this);", lines);
        Assert.Contains("private static readonly MistStatics.Table<(int, int), __Statics> __statics = new MistStatics.Table<(int, int), __Statics>(static key => new __Statics(key.Item1, key.Item2));", lines);
        Assert.Contains("return __statics.Of((__W, __H));", lines);
    }

    [Fact]
    public void AStaticConstructorRunsInTheHolderAfterTheInitializersAndEveryConstructionReachesTheHolder()
    {
        var initialized = Static("initialized");
        initialized.Initializer = HandBuiltModel.Literal(slots.Int, 1L);
        counter.FieldsList.Add(initialized);
        var assigned = new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), hits, slots.Int), Slot(), slots.Int);
        counter.ConstructorsList.Add(new Constructor("Counter", counter, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = counter,
            Body = HandBuiltModel.Block(new ExpressionStatement(assigned)),
        });
        using var routes = SlotRoutes.Enter([n]);

        var lines = TypeBody(counter);

        Assert.True(lines.IndexOf("this.initialized = 1;") < lines.IndexOf("this.hits = __N;"));
        Assert.DoesNotContain(lines, line => line.StartsWith("static Counter(", StringComparison.Ordinal));
        Assert.Contains("_ = Probe.Counter.__StaticsOf(__N);", lines);
    }

    [Fact]
    public void AReadAndAWriteThroughAClosingFindTheHolderForTheClosingsValue()
    {
        var k = slots.Dependent("K");
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([n, k]);

        Assert.Equal("Probe.Counter.__StaticsOf(4).hits", Emit(Through(slots.Literal(4))));
        Assert.Equal("Probe.Counter.__StaticsOf(n).hits", Emit(Through(capture.Value, capture)));
        Assert.Equal("Probe.Counter.__StaticsOf(__K).hits", Emit(Through(k)));
        Assert.Equal(
            "Probe.Counter.__StaticsOf(4).hits = 5",
            Emit(new AssignmentExpression(HandBuiltModel.Node(), Through(slots.Literal(4)), HandBuiltModel.Literal(slots.Int, 5L), slots.Int)));
        Assert.Equal(
            "Probe.Counter.__StaticsOf(__K).hits = 5",
            Emit(new AssignmentExpression(HandBuiltModel.Node(), Through(k), HandBuiltModel.Literal(slots.Int, 5L), slots.Int)));
    }

    [Fact]
    public void ACaptureThatRunsCodeIsEvaluatedOnceWhereTheHolderIsFound()
    {
        var capture = Capture(slots, Count(slots));
        using var routes = SlotRoutes.Enter([n]);

        Assert.Equal("Probe.Counter.__StaticsOf(p.Count).hits", Emit(Through(capture.Value, capture)));
        Assert.Equal(
            "Probe.Counter.__StaticsOf(p.Count).hits = 5",
            Emit(new AssignmentExpression(HandBuiltModel.Node(), Through(capture.Value, capture), HandBuiltModel.Literal(slots.Int, 5L), slots.Int)));
        Assert.Equal(
            "Probe.Counter.__StaticsOf(p.Count).hits += 5",
            Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), Through(capture.Value, capture), BinaryOperatorKind.Add, HandBuiltModel.Literal(slots.Int, 5L), slots.Int, null, IncrementForm.None)));
    }

    [Fact]
    public void ABareNameInAStaticAndInAnInstanceMemberReadsTheHolderForTheHiddenValue()
    {
        var read = HandBuiltModel.Block(new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), hits, slots.Int), slots.Int));
        counter.MethodsList.Add(new Method("Bump", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = counter, Scope = counter.Scope, Body = read });
        counter.MethodsList.Add(new Method("Seen", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = counter, Scope = counter.Scope, Body = read });
        using var routes = SlotRoutes.Enter([n]);

        var lines = TypeBody(counter);

        Assert.Contains("public static int Bump(int __N)", lines);
        Assert.Contains("public int Seen()", lines);
        Assert.Equal(2, lines.Count(line => line == "return Probe.Counter.__StaticsOf(__N).hits;"));
    }

    [Fact]
    public void ALambdaReadsTheHolderThroughTheHiddenValueAndAStaticOneDropsTheModifierToCaptureIt()
    {
        var read = new FieldReferenceExpression(HandBuiltModel.Node(), hits, slots.Int);
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], read, null, isStatic: true, new FunctionType([], slots.Int));
        counter.MethodsList.Add(new Method("Later", new FunctionType([], slots.Int), [], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = counter,
            Scope = counter.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(lambda, lambda.Type)),
        });
        using var routes = SlotRoutes.Enter([n]);

        var lines = TypeBody(counter);

        Assert.Contains(lines, line => line.Contains("() => Probe.Counter.__StaticsOf(__N).hits", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("static ()", StringComparison.Ordinal));
    }

    [Fact]
    public void AStaticHoldingAValueRoutedStructStartsFromItsStampInTheHolderAndInAnOrdinaryType()
    {
        var cell = Cell(slots, out var m);
        counter.FieldsList.Add(new Field("cell", slots.Closed(cell, n), OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = counter });
        var plain = HandBuiltModel.Class("Plain");
        plain.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        plain.FieldsList.Add(new Field("seven", slots.Closed(cell, slots.Literal(7)), OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = plain });
        using var routes = SlotRoutes.Enter([n, m]);

        Assert.Contains("this.cell = Probe.Cell.__CreateDefault(__N);", TypeBody(counter));
        Assert.Contains("public static Probe.Cell seven = Probe.Cell.__CreateDefault(7);", TypeBody(plain));
    }

    [Fact]
    public void ACarrierRoutedTypeKeepsItsCSharpStatics()
    {
        using var routes = SlotRoutes.Enter([]);

        var lines = TypeBody(counter);

        Assert.Contains("public static int hits;", lines);
        Assert.DoesNotContain(lines, line => line.Contains("__Statics", StringComparison.Ordinal));
        Assert.Equal("Probe.Counter<Const_4>.hits", Emit(Through(slots.Literal(4))));
    }

    [Fact]
    public void TheRuntimeBuildsAHolderOnceForAValueAndIsWrittenOnlyWhenAHolderIs()
    {
        StaticHolders.Reset();
        Assert.False(StaticHolders.Used());

        using(SlotRoutes.Enter([n]))
        {
            _ = TypeBody(counter);
        }

        var runtime = new CSharpWriter();
        StaticHolders.Emit(runtime);
        var lines = Lines(runtime);

        Assert.True(StaticHolders.Used());
        Assert.Contains("if (made.TryGetValue(key, out var held))", lines);
        Assert.Contains("lock (gate)", lines);
        Assert.Contains("if (made.TryGetValue(key, out held) || making.TryGetValue(key, out held))", lines);
        Assert.Contains("held = create(key);", lines);
        Assert.Contains("made[key] = held;", lines);
        Assert.Contains("making[key] = holder;", lines);
        Assert.True(lines.IndexOf("lock (gate)") < lines.IndexOf("held = create(key);"));
        Assert.True(lines.IndexOf("held = create(key);") < lines.IndexOf("made[key] = held;"));

        StaticHolders.Reset();
        Assert.False(StaticHolders.Used());
    }

    private Field Static(string name)
    {
        return new Field(name, slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = counter };
    }

    private TypeReferenceExpression Slot()
    {
        return new TypeReferenceExpression(HandBuiltModel.Node(), n);
    }

    private MemberAccessExpression Through(IMistType argument, params SlotCapture[] captures)
    {
        var closing = slots.Closed(counter, argument);
        return new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), closing), "hits", slots.Int,
            member: new FieldResult(hits, closing, []))
        {
            Captures = captures,
        };
    }

    private List<string> TypeBody(MemberType type)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return Lines(writer);
    }

    private static List<string> Lines(CSharpWriter writer)
    {
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}