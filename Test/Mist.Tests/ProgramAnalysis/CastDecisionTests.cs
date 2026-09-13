// Tier 3 (Component): the casts to an enum that CastAnalyzer deliberately does NOT refuse. A cast is sugar
// for a conversion function, so an enum cast admits exactly what could be passed to a parameter of the
// underlying type -- enums spec, Requirement 15.3 -- and each row is one kind of argument conversion. The
// corpus holds what the rule refuses; a silence has no line to put a marker on.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class CastDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH", "MIST_PA_CAST_HAS_NO_CONVERSION", "MIST_PA_NO_IMPLICIT_CONVERSION",
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
        byte b = 1;
        Priority priority = Priority.Low;
        Counter counter = new Counter(2);
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

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}