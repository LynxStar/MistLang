using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnionCarrierEmissionTests
{
    private const string Sized = "Union_Int32Array3_Int32Array5";
    private static readonly ConstructedType Three = ArrayOf(IntegerType, 3);
    private static readonly ConstructedType Five = ArrayOf(IntegerType, 5);
    private static readonly UnionType SizedUnion = new([Three, Five]);
    private static readonly UnionType Wide = new([Three, Five, StringType]);

    [Fact]
    public void ACarrierOfSharedSpellings_EntersThemThroughFactoriesAndNamesEachAccessorForItsLength()
    {
        var emitted = Carrier(Wide, SizedUnion);

        Assert.Contains($"public static {Sized}_String FromInt32Array3(int[] value)", emitted);
        Assert.Contains($"    => new {Sized}_String(1, value);", emitted);
        Assert.Contains($"public static {Sized}_String FromInt32Array5(int[] value)", emitted);
        Assert.Contains($"    => new {Sized}_String(2, value);", emitted);
        Assert.Contains($"public static implicit operator {Sized}_String(string value)", emitted);
        Assert.DoesNotContain($"implicit operator {Sized}_String(int[] value)", emitted);
        Assert.Contains("public bool IsInt32Array3 => _tag == 1;", emitted);
        Assert.Contains("public bool IsInt32Array5 => _tag == 2;", emitted);
        Assert.Contains("public int[] AsInt32Array5OrNull => _tag == 2 ? (int[])_value : null;", emitted);
        Assert.Contains("public string AsStringOrNull => _tag == 3 ? (string)_value : null;", emitted);
    }

    [Fact]
    public void WideningFromANarrowerSizedCarrier_KeepsEachConstituentsTag()
    {
        Assert.Contains(
            $"public static implicit operator {Sized}_String({Sized} value)\n"
            + $"        => value.IsInt32Array3 ? new {Sized}_String(1, value.AsInt32Array3) : new {Sized}_String(2, value.AsInt32Array5);",
            Carrier(Wide, SizedUnion));
    }

    [Fact]
    public void ACarrierWithoutSharedSpellings_KeepsAnImplicitOperatorPerConstituent()
    {
        var emitted = Carrier(new UnionType([IntegerType, StringType]));

        Assert.Contains("public static implicit operator Union_Int32_String(int value)", emitted);
        Assert.Contains("public static implicit operator Union_Int32_String(string value)", emitted);
        Assert.DoesNotContain(" From", emitted);
    }

    [Fact]
    public void EveryAssignmentShape_EntersThroughTheConstituentTheValueIs()
    {
        var target = Reference("value", SizedUnion);

        Assert.Equal($"{Sized} value = {Sized}.FromInt32Array3(three);",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", SizedUnion), Reference("three", Three))));
        Assert.Equal($"value = {Sized}.FromInt32Array5(five)",
            PropertyPatternEmissionTests.Emit(new AssignmentExpression(null!, target, Reference("five", Five), SizedUnion)));
        Assert.Equal($"return {Sized}.FromInt32Array3(three);",
            Statement(new ReturnStatement(Reference("three", Three), SizedUnion)));
        Assert.Equal($"{Sized} value = {Sized}.FromInt32Array5(five);",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", AliasOf("Sized", SizedUnion)), Reference("five", Five))));
    }

    [Fact]
    public void AnExplicitEntry_UsesTheFactoryWhereACastCannot()
    {
        Assert.Equal($"{Sized}.FromInt32Array5(five)",
            PropertyPatternEmissionTests.Emit(new CastExpression(null!, Reference("five", Five), SizedUnion, [], SizedUnion)));
        Assert.Equal($"{Sized}.FromInt32Array3(three)",
            PropertyPatternEmissionTests.Emit(new AsExpression(
                HandBuiltModel.Node(), Reference("three", Three), SizedUnion, AsConversionKind.Widening, null, SizedUnion)));
    }

    [Fact]
    public void ElementsAndArms_EnterTheTypeTheyAreGatheredInto()
    {
        Assert.Equal($"new {Sized}[] {{ {Sized}.FromInt32Array3(three), {Sized}.FromInt32Array5(five) }}",
            PropertyPatternEmissionTests.Emit(new ArrayLiteralExpression(
                null!, SizedUnion, [Reference("three", Three), Reference("five", Five)], ArrayOf(SizedUnion, 2))));

        var picked = new SwitchExpression(null!, Reference("flag", BooleanType),
        [
            new SwitchArm(null!, new CatchAllPattern(null!, BooleanType, null), null, Reference("five", Five)),
        ], SizedUnion);

        Assert.Equal($"flag switch {{ var _ => {Sized}.FromInt32Array5(five) }}", PropertyPatternEmissionTests.Emit(picked));
    }

    [Fact]
    public void ATupleLiteral_EntersEachElementIntoItsOwnSlot()
    {
        var literal = new TupleLiteralExpression(null!, [Reference("three", Three), Reference("label", StringType)], [null, null],
            TupleOf(Three, StringType));

        Assert.Equal($"({Sized}, string) pair = ({Sized}.FromInt32Array3(three), label);",
            Statement(new LocalDeclarationStatement(new LocalVariable("pair", TupleOf(SizedUnion, StringType)), literal)));
    }

    [Fact]
    public void ATupleValue_IsBoundOnceAndRebuiltAroundItsEnteringElement()
    {
        Assert.Equal($"({Sized}, string) pair = (held is var __e1 ? ({Sized}.FromInt32Array3(__e1.Item1), __e1.Item2) : default);",
            Statement(new LocalDeclarationStatement(
                new LocalVariable("pair", TupleOf(SizedUnion, StringType)), Reference("held", TupleOf(Three, StringType)))));
    }

    [Fact]
    public void AnOrdinaryOrUnclaimedEntry_IsLeftToCSharp()
    {
        Assert.Equal("Union_Int32_String value = 3;",
            Statement(new LocalDeclarationStatement(
                new LocalVariable("value", new UnionType([IntegerType, StringType])), PropertyPatternEmissionTests.Number(3))));
        Assert.Equal($"{Sized} value = numbers;",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", SizedUnion), Reference("numbers", ArrayOf(IntegerType, null)))));
    }

    [Fact]
    public void ACarrierOfRefinementsOverOneBase_GivesEachItsOwnTagAccessorsAndFactory()
    {
        var (small, big) = Refinements();
        var emitted = Carrier(new UnionType([big, small]));
        const string name = "Union_Int32_Big_Gt100_Int32_Small_Lt10";

        Assert.Contains("public bool IsInt32_Big_Gt100 => _tag == 1;", emitted);
        Assert.Contains("public bool IsInt32_Small_Lt10 => _tag == 2;", emitted);
        Assert.Contains(" AsInt32_Small_Lt10OrNull => _tag == 2 ? (int)_value : null;", emitted);
        Assert.Contains($"public static {name} FromInt32_Big_Gt100(int value)\n        => new {name}(1, value);", emitted);
        Assert.Contains($"public static {name} FromInt32_Small_Lt10(int value)\n        => new {name}(2, value);", emitted);
        Assert.DoesNotContain("implicit operator", emitted);
    }

    [Fact]
    public void WideningARefinedCarrier_KeepsEachRefinementsTag_EvenWhereTheyShareADeclaredName()
    {
        var (small, big) = Refinements();
        var h = new HandBuiltRefinements();
        var below = h.Refine(IntegerType, ComparisonKind.LessThan, -100L, "Split");
        var above = h.Refine(IntegerType, ComparisonKind.GreaterThan, 100L, "Split");

        Assert.Contains(
            "=> value.IsInt32_Big_Gt100 ? new Union_Int32_Big_Gt100_Int32_Small_Lt10_String(1, value.AsInt32_Big_Gt100) "
            + ": new Union_Int32_Big_Gt100_Int32_Small_Lt10_String(2, value.AsInt32_Small_Lt10);",
            Carrier(new UnionType([big, small, StringType]), new UnionType([big, small])));
        Assert.Contains(
            "=> value.IsInt32_Split_LtNeg100 ? new Union_Int32_Split_Gt100_Int32_Split_LtNeg100_String(2, value.AsInt32_Split_LtNeg100) "
            + ": new Union_Int32_Split_Gt100_Int32_Split_LtNeg100_String(1, value.AsInt32_Split_Gt100);",
            Carrier(new UnionType([above, below, StringType]), new UnionType([below, above])));
    }

    [Fact]
    public void ARefinedValue_EntersAndIsRetrievedThroughItsOwnRefinement()
    {
        var (small, big) = Refinements();
        var either = new UnionType([big, small]);
        var bound = new LocalVariable("s", small);
        var narrowed = new TypeTestPattern(HandBuiltModel.Node(), small, bound, small, small.Predicate);
        const string name = "Union_Int32_Big_Gt100_Int32_Small_Lt10";

        Assert.Equal($"{name} value = {name}.FromInt32_Small_Lt10(small);",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", either), Reference("small", small))));
        Assert.Equal($"return {name}.FromInt32_Big_Gt100(big);", Statement(new ReturnStatement(Reference("big", big), either)));
        Assert.Equal("value.AsInt32_Small_Lt10OrNull is int and < 10 and var s",
            PropertyPatternEmissionTests.Emit(new IsPatternExpression(HandBuiltModel.Node(), Reference("value", either), narrowed, BooleanType)));
        Assert.Equal("value.AsInt32_Small_Lt10OrNull",
            PropertyPatternEmissionTests.Emit(new AsExpression(HandBuiltModel.Node(), Reference("value", either), small, AsConversionKind.UnionExtraction, small, OptionalOf(small))));
    }

    [Fact]
    public void AnEnum_EntersAnotherCarrierAsItsUnderlyingValue_AndItsOwnThroughItsOperator()
    {
        var pair = new UnionType([IntegerType, StringType]);
        var color = HandBuiltModel.Enum("Color", IntegerType);
        var mixed = HandBuiltModel.Enum("Mixed", pair);

        Assert.Equal("Union_Int32_String value = ((int)color);",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", pair), Reference("color", color))));
        Assert.Equal("Union_Boolean_Int32_String value = ((Union_Int32_String)mixed);",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", new UnionType([BooleanType, IntegerType, StringType])), Reference("mixed", mixed))));
        Assert.Equal("Union_Int32_String value = mixed;",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", pair), Reference("mixed", mixed))));
        Assert.Equal("return ((int)color);", Statement(new ReturnStatement(Reference("color", color), pair)));
        Assert.Equal("Union_Color_Int32 value = color;",
            Statement(new LocalDeclarationStatement(new LocalVariable("value", new UnionType([color, IntegerType])), Reference("color", color))));
    }

    private static (RefinedType Small, RefinedType Big) Refinements()
    {
        var h = new HandBuiltRefinements();
        return (h.Refine(IntegerType, ComparisonKind.LessThan, 10L, "Small"), h.Refine(IntegerType, ComparisonKind.GreaterThan, 100L, "Big"));
    }

    private static string Carrier(UnionType union, params UnionType[] narrower)
    {
        var carriers = narrower.Append(union).ToDictionary(UnionCarrierNames.CarrierName);
        var writer = new CSharpWriter();
        using var generated = TypeNameEmitter.Oblivious();

        UnionCarrierEmitter.Emit(UnionCarrierNames.CarrierName(union), union, carriers, writer);

        return writer.ToString().ReplaceLineEndings("\n");
    }

    private static string Statement(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);

        Assert.False(diagnostics.HasErrors);

        return writer.ToString().Trim();
    }

    private static LocalReferenceExpression Reference(string name, IMistType type)
    {
        return PropertyPatternEmissionTests.Reference(name, type);
    }
}