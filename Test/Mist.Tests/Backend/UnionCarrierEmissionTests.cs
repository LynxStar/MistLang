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