// Tier 3 (Component): how a cast to an enum lowers its operand, asserted on EnumEmitter.LookupArgument over
// a real model. No sample can hold these shapes: the baseline is shared with classic, which emits every one
// that needs the conversion as a single C# cast and fails CS0030 on it.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class EnumCastLoweringTests : ProgramModelProbe
{
    public EnumCastLoweringTests()
        : base("MistEnumCastLowering_")
    {
    }

    protected override string Fixtures { get; } = """
        public enum Priority : int { Low = 1, High = 2 }
        public enum Rival : int { One = 1 }
        public enum Wide : long { One = 1 }
        public enum Color : byte { Red, Green }
        public enum Mixed : int | string { Num = 1, Text = "x" }

        public class Counter
        {
            public int Count;

            public Counter(int count)
            {
                this.Count = count;
            }

            public static implicit operator int(Counter c)
            {
                return c.Count;
            }
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        Priority priority = Priority.Low;
        Counter counter = new Counter(2);
        """;

    [Theory]
    [InlineData("var r = (Priority)Rival.One;", "(int)(operand)")]
    [InlineData("var r = (Wide)priority;", "(long)(operand)")]
    [InlineData("var r = (Priority)counter;", "(int)(operand)")]
    [InlineData("var r = (Color)1;", "(byte)(operand)")]
    [InlineData("var r = (Mixed)\"x\";", "(Union_Int32_String)(operand)")]
    public void AnOperandThatIsNotTheUnderlyingType_IsConvertedToItFirst(string statement, string expected)
    {
        Assert.Equal(expected, Lowered(statement));
    }

    [Theory]
    [InlineData("var r = (Priority)x;")]
    [InlineData("var r = (Priority)priority;")]
    public void AnOperandAlreadyOfTheParameterType_IsLeftAsWritten(string statement)
    {
        Assert.Equal("operand", Lowered(statement));
    }

    private string Lowered(string statement)
    {
        return EnumEmitter.LookupArgument(InitializerOf<CastExpression>(statement), "operand");
    }
}