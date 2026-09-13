// Tier 3 (Component): how a cast lowers its operand, asserted on CastLowering.Argument over a real model. No
// sample can hold these shapes: the baseline is shared with classic, which emits every one that needs the
// conversion as a single C# cast and fails CS0030 on it.

using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class CastLoweringTests : ProgramModelProbe
{
    public CastLoweringTests()
        : base("MistCastLowering_")
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

        public class Meters
        {
            public Meters() { }
        }

        public class Reading
        {
            public Reading() { }

            public static implicit operator Meters(Reading r) { return new Meters(); }
        }

        public class Feet
        {
            public Feet() { }

            public static explicit operator Feet(Meters m) { return new Feet(); }
        }

        public class Fathoms
        {
            public Fathoms() { }

            public static explicit operator Fathoms(double depth) { return new Fathoms(); }

            public static explicit operator Fathoms(string depth) { return new Fathoms(); }
        }

        public class Tag
        {
            public Tag() { }

            public static explicit operator Tag(int | string key) { return new Tag(); }
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        double d = 1.0;
        Priority priority = Priority.Low;
        Counter counter = new Counter(2);
        Meters meters = new Meters();
        Reading reading = new Reading();
        """;

    [Theory]
    [InlineData("var r = (Priority)Rival.One;", "(int)(operand)")]
    [InlineData("var r = (Wide)priority;", "(long)(operand)")]
    [InlineData("var r = (Priority)counter;", "(int)(operand)")]
    [InlineData("var r = (Color)1;", "(byte)(operand)")]
    [InlineData("var r = (Mixed)\"x\";", "(Union_Int32_String)(operand)")]
    [InlineData("var r = (Feet)reading;", "(Probe.Meters)(operand)")]
    [InlineData("var r = (Fathoms)x;", "(double)(operand)")]
    [InlineData("var r = (Fathoms)priority;", "(double)(operand)")]
    [InlineData("var r = (Fathoms)counter;", "(double)(operand)")]
    [InlineData("var r = (Tag)x;", "(Union_Int32_String)(operand)")]
    public void AnOperandThatIsNotTheParameterType_IsConvertedToItFirst(string statement, string expected)
    {
        Assert.Equal(expected, Lowered(statement));
    }

    [Theory]
    [InlineData("var r = (Priority)x;")]
    [InlineData("var r = (Priority)priority;")]
    [InlineData("var r = (Feet)meters;")]
    [InlineData("var r = (Fathoms)d;")]
    public void AnOperandAlreadyOfTheParameterType_IsLeftAsWritten(string statement)
    {
        Assert.Equal("operand", Lowered(statement));
    }

    private string Lowered(string statement)
    {
        return CastLowering.Argument(InitializerOf<CastExpression>(statement), "operand");
    }
}