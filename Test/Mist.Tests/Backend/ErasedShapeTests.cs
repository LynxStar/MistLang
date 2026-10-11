using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ErasedShapeTests
{
    public static TheoryData<string, string> TestedSpellings()
    {
        return new TheoryData<string, string>
        {
            { "int[3]", "int[] { Length: 3 }" },
            { "int[0]", "int[] { Length: 0 }" },
            { "int[]", "int[]" },
            { "Span<int, 3>", "System.Span<int> { Length: 3 }" },
            { "Span<int>", "System.Span<int>" },
            { "ReadOnlySpan<int, 3>", "System.ReadOnlySpan<int> { Length: 3 }" },
            { "ReadOnlySpan<int>", "System.ReadOnlySpan<int>" },
            { "Three", "int[] { Length: 3 }" },
            { "Refined int[3]", "int[] { Length: 3 }" },
            { "(int[3], string)", "System.ValueTuple<int[], string> { Item1: { Length: 3 } }" },
            { "(string, int[3], int[5])", "System.ValueTuple<string, int[], int[]> { Item2: { Length: 3 }, Item3: { Length: 5 } }" },
            { "((int[3], string), int)", "System.ValueTuple<(int[], string), int> { Item1: { Item1: { Length: 3 } } }" },
            { "(int[3]?, string)", "System.ValueTuple<int[]?, string> { Item1: null or { Length: 3 } }" },
            { "Pair", "System.ValueTuple<int[], string> { Item1: { Length: 3 } }" },
            { "(int, string)", "System.ValueTuple<int, string>" },
            { "(int[], string)", "System.ValueTuple<int[], string>" },
            { "Eight", "System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int>>" },
            { "Nine", "System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int[], string>> { Rest.Item1: { Length: 3 } }" },
            { "Fifteen",
                "System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int[]>>>"
                + " { Rest.Rest.Item1: { Length: 2 } }" },
            { "(Nine, int)", "System.ValueTuple<(int, int, int, int, int, int, int, int[], string), int> { Item1: { Rest.Item1: { Length: 3 } } }" },
        };
    }

    [Theory]
    [MemberData(nameof(TestedSpellings))]
    public void TestedType_StatesEveryLengthItsSpellingErases(string described, string expected)
    {
        Assert.Equal(expected, ErasedShape.Tested(Described(described)));
    }

    [Fact]
    public void Members_AreClauseMembersSoACallerCanMergeItsOwn()
    {
        Assert.Equal(["Length: 3"], ErasedShape.Members(ArrayOf(IntegerType, 3)));
        Assert.Equal(["Item2: { Length: 5 }"], ErasedShape.Members(TupleOf(StringType, ArrayOf(IntegerType, 5))));
        Assert.Equal(["Item1: { Length: 3 }", "Rest.Item2: { Length: 5 }"],
            ErasedShape.Members(TupleOf([ArrayOf(IntegerType, 3), .. Enumerable.Repeat<IMistType>(IntegerType, 7), ArrayOf(IntegerType, 5)])));
        Assert.Empty(ErasedShape.Members(ArrayOf(IntegerType, null)));
        Assert.Empty(ErasedShape.Members(OptionalOf(ArrayOf(IntegerType, 3))));
        Assert.Empty(ErasedShape.Members(BoxOf(3)));
        Assert.Empty(ErasedShape.Members(UnknownType.Instance));
    }

    [Theory]
    [InlineData("int[3][2]", "int[3][2]")]
    [InlineData("int[3][]", "int[3][]")]
    [InlineData("(int[3], string)[]", "(int[3], string)[]")]
    [InlineData("(int[3][2], string)", "int[3][2]")]
    [InlineData("int[3][][]", "int[3][]")]
    [InlineData("int[3]?[2]", "int[3]?[2]")]
    public void UnstatedElements_NamesTheSequenceWhoseElementsCarryALength(string described, string sequence)
    {
        var found = ErasedShape.UnstatedElements(Described(described));

        Assert.NotNull(found);
        Assert.Equal(TypeDisplay.Mist(Described(sequence)), TypeDisplay.Mist(found));
    }

    [Theory]
    [InlineData("int[3]")]
    [InlineData("int[][2]")]
    [InlineData("(int[3], string)")]
    [InlineData("Span<int, 3>")]
    [InlineData("(int, string)")]
    public void UnstatedElements_IsNullWhereEveryLengthIsStatable(string described)
    {
        Assert.Null(ErasedShape.UnstatedElements(Described(described)));
    }

    [Fact]
    public void ErasedLength_IsTheLiftedLengthOfALengthIndexedConstructionOnly()
    {
        Assert.Equal(3, LengthErasure.ErasedLength(ArrayOf(IntegerType, 3)));
        Assert.Equal(0, LengthErasure.ErasedLength(ReadOnlySpanOf(IntegerType, 0)));
        Assert.Null(LengthErasure.ErasedLength(SpanOf(IntegerType, null)));
        Assert.Null(LengthErasure.ErasedLength(BoxOf(3)));
    }

    private static IMistType Described(string described)
    {
        var three = ArrayOf(IntegerType, 3);

        return described switch
        {
            "int[3]" => three,
            "int[0]" => ArrayOf(IntegerType, 0),
            "int[]" => ArrayOf(IntegerType, null),
            "Span<int, 3>" => SpanOf(IntegerType, 3),
            "Span<int>" => SpanOf(IntegerType, null),
            "ReadOnlySpan<int, 3>" => ReadOnlySpanOf(IntegerType, 3),
            "ReadOnlySpan<int>" => ReadOnlySpanOf(IntegerType, null),
            "Three" => AliasOf("Three", three),
            "Refined int[3]" => HandBuiltModel.Refined(three, "NonEmpty"),
            "(int[3], string)" => TupleOf(three, StringType),
            "(string, int[3], int[5])" => TupleOf(StringType, three, ArrayOf(IntegerType, 5)),
            "((int[3], string), int)" => TupleOf(TupleOf(three, StringType), IntegerType),
            "(int[3]?, string)" => TupleOf(OptionalOf(three), StringType),
            "Pair" => AliasOf("Pair", TupleOf(three, StringType)),
            "(int, string)" => TupleOf(IntegerType, StringType),
            "(int[], string)" => TupleOf(ArrayOf(IntegerType, null), StringType),
            "int[3][2]" => ArrayOf(three, 2),
            "int[3][]" => ArrayOf(three, null),
            "(int[3], string)[]" => ArrayOf(TupleOf(three, StringType), null),
            "(int[3][2], string)" => TupleOf(ArrayOf(three, 2), StringType),
            "int[3][][]" => ArrayOf(ArrayOf(three, null), null),
            "int[3]?[2]" => ArrayOf(OptionalOf(three), 2),
            "int[][2]" => ArrayOf(ArrayOf(IntegerType, null), 2),
            "Eight" => TupleOf([.. Enumerable.Repeat<IMistType>(IntegerType, 8)]),
            "Nine" => TupleOf([.. Enumerable.Repeat<IMistType>(IntegerType, 7), three, StringType]),
            "Fifteen" => TupleOf([.. Enumerable.Repeat<IMistType>(IntegerType, 14), ArrayOf(IntegerType, 2)]),
            "(Nine, int)" => TupleOf(Described("Nine"), IntegerType),
            _ => throw new ArgumentOutOfRangeException(nameof(described), described, null),
        };
    }
}