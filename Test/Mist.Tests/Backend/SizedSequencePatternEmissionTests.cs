using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class SizedSequencePatternEmissionTests
{
    private static readonly ConstructedType Three = ArrayOf(IntegerType, 3);

    [Theory]
    [InlineData(3L, "five is int[] { Length: 3 } bound")]
    [InlineData(0L, "five is int[] { Length: 0 } bound")]
    [InlineData(null, "five is int[] bound")]
    public void ATypeTest_CarriesTheLengthItsBindingPromises(long? length, string expected)
    {
        var tested = ArrayOf(IntegerType, length);

        Assert.Equal(expected, Emit(Is(Reference("five", UnknownType.Instance), TypeTest(tested, "bound"))));
    }

    [Fact]
    public void AnAliasTest_CarriesTheLengthOfWhatItNames()
    {
        Assert.Equal("five is int[] { Length: 3 } three",
            Emit(Is(Reference("five", UnknownType.Instance), TypeTest(AliasOf("Three", Three), "three"))));
    }

    [Theory]
    [InlineData(true, "System.Span<int>")]
    [InlineData(false, "System.ReadOnlySpan<int>")]
    public void ASpanTest_CarriesTheLengthOverAnUnknownLengthSpan(bool mutable, string spelled)
    {
        var scrutinee = mutable ? SpanOf(IntegerType, null) : ReadOnlySpanOf(IntegerType, null);
        var tested = mutable ? SpanOf(IntegerType, 3) : ReadOnlySpanOf(IntegerType, 3);

        Assert.Equal($"five is {spelled} {{ Length: 3 }} three", Emit(Is(Reference("five", scrutinee), TypeTest(tested, "three"))));
    }

    [Fact]
    public void DistinctSizedArms_StayDistinctAndLeaveTheUnsizedFallbackReachable()
    {
        var five = Reference("five", UnknownType.Instance);
        var switched = new SwitchExpression(null!, five,
        [
            Arm(TypeTest(Three, "three"), 3),
            Arm(TypeTest(ArrayOf(IntegerType, 5), "actual"), 5),
            Arm(TypeTest(ArrayOf(IntegerType, null), "any"), 0),
            Arm(new CatchAllPattern(null!, UnknownType.Instance, null), -1),
        ], IntegerType);

        Assert.Equal(
            "five switch { int[] { Length: 3 } three => 3, int[] { Length: 5 } actual => 5, int[] any => 0, var _ => -1 }",
            Emit(switched));
    }

    [Fact]
    public void SpanArms_StayDistinct()
    {
        var switched = new SwitchExpression(null!, Reference("five", SpanOf(IntegerType, null)),
        [
            Arm(TypeTest(SpanOf(IntegerType, 3), "a"), 3),
            Arm(TypeTest(SpanOf(IntegerType, 5), "b"), 5),
            Arm(new CatchAllPattern(null!, SpanOf(IntegerType, null), null), 0),
        ], IntegerType);

        Assert.Equal("five switch { System.Span<int> { Length: 3 } a => 3, System.Span<int> { Length: 5 } b => 5, var _ => 0 }", Emit(switched));
    }

    [Fact]
    public void ATupleTypeTest_ChecksEachSizedElementAndKeepsTheWholeBinding()
    {
        Assert.Equal("pair is System.ValueTuple<int[], string> { Item1: { Length: 3 } } whole",
            Emit(Is(Reference("pair", UnknownType.Instance), TypeTest(TupleOf(Three, StringType), "whole"))));
        Assert.Equal("nested is System.ValueTuple<(int[], string), int> { Item1: { Item1: { Length: 3 } } } whole",
            Emit(Is(Reference("nested", UnknownType.Instance), TypeTest(TupleOf(TupleOf(Three, StringType), IntegerType), "whole"))));
    }

    [Fact]
    public void APositionalPattern_ChecksTheLengthOnTheElementThatBindsIt()
    {
        var positional = new TuplePattern(null!, [TypeTest(Three, "three"), TypeTest(StringType, "s")],
            TupleOf(Three, StringType), TupleOf(Three, StringType), null);
        var nested = new TuplePattern(null!, [positional, TypeTest(IntegerType, "c")],
            TupleOf(TupleOf(Three, StringType), IntegerType), TupleOf(TupleOf(Three, StringType), IntegerType), null);
        var statically = new TuplePattern(null!, [TypeTest(Three, "three"), TypeTest(StringType, "s")], TupleOf(Three, StringType), null, null);

        Assert.Equal("pair is System.ValueTuple<int[], string>(int[] { Length: 3 } three, string s)",
            Emit(Is(Reference("pair", UnknownType.Instance), positional)));
        Assert.Equal(
            "pair is System.ValueTuple<(int[], string), int>(System.ValueTuple<int[], string>(int[] { Length: 3 } three, string s), int c)",
            Emit(Is(Reference("pair", UnknownType.Instance), nested)));
        Assert.Equal("pair is (int[] { Length: 3 } three, string s)",
            Emit(Is(Reference("pair", TupleOf(ArrayOf(IntegerType, null), StringType)), statically)));
    }

    [Fact]
    public void ACheckedAs_TestsTheErasedLengthAndAnswersAbsenceOtherwise()
    {
        Assert.Equal("(five is int[] { Length: 3 } __as7 ? __as7 : default(int[]))",
            Emit(As(Reference("five", UnknownType.Instance), Three)));
        Assert.Equal(
            "(pair is System.ValueTuple<int[], string> { Item1: { Length: 3 } } __as7 ? __as7 : default((int[], string)?))",
            Emit(As(Reference("pair", UnknownType.Instance), TupleOf(Three, StringType))));
    }

    [Fact]
    public void ACheckedTupleAs_NamesTheValueTupleRatherThanDeconstructingAnObject()
    {
        Assert.Equal("(pair is System.ValueTuple<int, string> __as7 ? __as7 : default((int, string)?))",
            Emit(As(Reference("pair", UnknownType.Instance), TupleOf(IntegerType, StringType))));
        Assert.Equal("(pair is System.ValueTuple<int, string> __as7 ? __as7 : default((int, string)?))",
            Emit(As(Reference("pair", UnknownType.Instance), AliasOf("Pair", TupleOf(IntegerType, StringType)))));
    }

    [Fact]
    public void AnUnsizedOrNominalAs_KeepsItsOrdinarySpelling()
    {
        var dog = PropertyPatternEmissionTests.Type("Dog");

        Assert.Equal("(animal as Dog)", Emit(As(Reference("animal", UnknownType.Instance), dog)));
        Assert.Equal("(five as int[])", Emit(As(Reference("five", UnknownType.Instance), ArrayOf(IntegerType, null))));
    }

    [Fact]
    public void ACheckedAs_EvaluatesItsOperandOnce()
    {
        var producer = new MemberAccessExpression(null!, Reference("source", PropertyPatternEmissionTests.Type("Source")), "Next", UnknownType.Instance);
        var emitted = Emit(As(producer, TupleOf(Three, StringType)));

        _ = Assert.Single(System.Text.RegularExpressions.Regex.Matches(emitted, "source\\.Next"));
    }

    [Fact]
    public void ARefinedTestUnderBooleanComposition_KeepsTheErasedLength()
    {
        var predicate = new PropertyPattern(HandBuiltModel.Node(), TupleOf(Three, StringType),
            [new PropertySubpattern(null!, "Item2", new RegexPattern(null!, StringType, "^y$"), StringType, new NotFoundResult())]);
        var refined = HandBuiltModel.Refined(TupleOf(Three, StringType), "Labelled");
        var negated = new NegatedPattern(null!, UnknownType.Instance,
            new TypeTestPattern(HandBuiltModel.Node(), refined, null, refinement: predicate));

        Assert.Contains("__e1 is System.ValueTuple<int[], string> { Item1: { Length: 3 } } __e2 && ",
            Emit(Is(Reference("value", UnknownType.Instance), negated)));
    }

    [Theory]
    [InlineData("is")]
    [InlineData("as")]
    [InlineData("property")]
    [InlineData("boolean")]
    public void ATestOfSizedElements_IsRefusedAtEveryRuntimeTestSite(string site)
    {
        var sequence = ArrayOf(TupleOf(Three, StringType), null);
        var value = Reference("value", UnknownType.Instance);
        Expression expression = site switch
        {
            "is" => Is(value, new TypeTestPattern(HandBuiltModel.Node(), ArrayOf(Three, 2), null)),
            "as" => new AsExpression(HandBuiltModel.Node(), value, sequence, AsConversionKind.Narrowing, null, OptionalOf(sequence)),
            "property" => Is(value, new PropertyPattern(null!, ArrayOf(Three, 2), [], null, new TypeTestPattern(HandBuiltModel.Node(), ArrayOf(Three, 2), null))),
            _ => Is(value, new NegatedPattern(null!, UnknownType.Instance, new TypeTestPattern(HandBuiltModel.Node(),
                HandBuiltModel.Refined(sequence, "Labelled"), null,
                refinement: new RegexPattern(null!, sequence, "^y$")))),
        };
        var diagnostics = new DiagnosticBag();

        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, new CSharpWriter(), diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST", refusal.Id.Value);
    }

    private static string Emit(Expression expression)
    {
        return PropertyPatternEmissionTests.Emit(expression);
    }

    private static IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(null!, scrutinee, pattern, BooleanType);
    }

    private static AsExpression As(Expression operand, IMistType target)
    {
        return new AsExpression(HandBuiltModel.Node(7), operand, target, AsConversionKind.Narrowing, null, OptionalOf(target));
    }

    private static TypeTestPattern TypeTest(IMistType tested, string binding)
    {
        return new TypeTestPattern(null!, tested, new LocalVariable(binding, tested));
    }

    private static SwitchArm Arm(Pattern pattern, int result)
    {
        return new SwitchArm(null!, pattern, null, PropertyPatternEmissionTests.Number(result));
    }

    private static LocalReferenceExpression Reference(string name, IMistType type)
    {
        return PropertyPatternEmissionTests.Reference(name, type);
    }
}