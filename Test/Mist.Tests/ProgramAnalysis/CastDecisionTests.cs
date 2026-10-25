// Tier 3 (Component): the casts CastAnalyzer deliberately does NOT refuse. A cast is sugar for a conversion
// function, so a cast admits exactly what could be passed to the function it calls -- an enum's lookup
// (enums spec, Requirement 15.3) or a declared cast operator (cast-operators spec, Requirement 8) -- or,
// for a value already of the target type, calls nothing (Requirement 8.8); each row is one kind of argument
// conversion.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class CastDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH", "MIST_PA_CAST_HAS_NO_CONVERSION", "MIST_PA_NO_IMPLICIT_CONVERSION",
        "MIST_PA_CAST_ARGUMENT_MISMATCH", "MIST_PA_AMBIGUOUS_CAST", "MIST_PA_CAST_REQUIRED",
    ];

    public CastDecisionTests()
        : base("MistCastDecision_")
    {
    }

    protected override string Fixtures { get; } = """
        public enum Priority : int { Low = 1, High = 2 }
        public enum Rival : int { One = 1 }
        public enum Wide : long { One = 1 }
        public enum Color : byte { Red, Green }
        public enum Mixed : int | string { Num = 1, Text = "x" }

        type SmallAdult = byte >= 18;

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

        public class Yards
        {
            public Yards() { }

            public static implicit operator Yards(Meters m) { return new Yards(); }
        }

        public class Fathoms
        {
            public Fathoms() { }

            public static explicit operator Fathoms(double depth) { return new Fathoms(); }
        }

        public class Crate
        {
            public Crate() { }

            public static explicit operator Crate(byte size) { return new Crate(); }
        }

        public class Bin
        {
            public Bin() { }

            public static explicit operator Bin(string label) { return new Bin(); }

            public static explicit operator Bin(byte size) { return new Bin(); }
        }

        public class Tally
        {
            public Tally() { }

            public static implicit operator int(Tally t) { return 1; }

            public static implicit operator long(Tally t) { return 2L; }
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        byte b = 1;
        Priority priority = Priority.Low;
        Counter counter = new Counter(2);
        Reading reading = new Reading();
        Tally tally = new Tally();
        """;

    [Theory]
    [InlineData("Priority? fromUnderlying = (Priority)x;")]
    [InlineData("Priority? fromConstant = (Priority)7;")]
    [InlineData("Priority? fromWidening = (Priority)b;")]
    [InlineData("Color? fromFittingConstant = (Color)1;")]
    [InlineData("Priority? fromAnotherEnum = (Priority)Rival.One;")]
    [InlineData("Wide? fromAnEnumOverANarrowerBase = (Wide)priority;")]
    [InlineData("Priority? fromDeclaredImplicit = (Priority)counter;")]
    [InlineData("Mixed? fromUnionConstituent = (Mixed)\"x\";")]
    [InlineData("Priority fromItself = (Priority)priority;")]
    [InlineData("Priority narrowedByCoalescing = (Priority)x ?? Priority.Low;")]
    public void ACastToAnEnumWhoseOperandCouldBePassedToItsUnderlyingType_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("Feet throughADeclaredImplicit = (Feet)reading;")]
    [InlineData("Yards anImplicitOperatorCalledExplicitly = (Yards)reading;")]
    [InlineData("Fathoms throughADeclaredImplicitThenAWidening = (Fathoms)counter;")]
    [InlineData("Fathoms throughAnEnumsBackingValue = (Fathoms)priority;")]
    [InlineData("Crate aFittingConstant = (Crate)2;")]
    [InlineData("Bin aFittingConstantAmongOperators = (Bin)2;")]
    [InlineData("long theOperatorAnsweringTheWrittenType = (long)tally;")]
    public void ACastWhoseOperandCouldBePassedToTheOperatorItCalls_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("byte aFittingConstant = (byte)2;")]
    [InlineData("sbyte aNegativeConstantASignedTypeHolds = (sbyte)-1;")]
    [InlineData("ulong anIntConstantAnUnsignedLongHolds = (ulong)2;")]
    [InlineData("ulong aNonNegativeLongConstant = (ulong)2L;")]
    [InlineData("SmallAdult aConstantTheRefinedBaseHolds = (SmallAdult)20;")]
    [InlineData("byte? aConstantAnOptionalsInnerTypeHolds = (byte?)2;")]
    public void ACastOfAnIntegerConstantTheTargetTypeHolds_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Number.Value))];
    }
}