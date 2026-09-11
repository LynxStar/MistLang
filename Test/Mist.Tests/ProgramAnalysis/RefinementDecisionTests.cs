// Tier 3 (Component): what order 12's prover deliberately does NOT report -- the constants that
// satisfy, the implications that hold, the narrowings the flow establishes, the indices it proves,
// and the call arguments classic leaves to a runtime guard -- and the reach facts the corpus cannot
// phrase. WHY each is silent, and which of them classic refuses, is recorded once, on order 12 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinementDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_REFINEMENT_VIOLATED", "MIST_PA_REFINEMENT_UNPROVEN", "MIST_PA_REFINEMENT_NOT_IMPLIED",
        "MIST_PA_INDEX_OUT_OF_BOUNDS", "MIST_PA_INDEX_UNPROVEN", "MIST_PA_CONSTRAINT_VIOLATED",
        "MIST_PA_CONSTRAINT_UNPROVEN", "MIST_PA_DEPENDENT_ARGUMENT_VIOLATED", "MIST_PA_DEPENDENT_FORWARD_UNPROVEN",
        "MIST_PA_CONSTRAINT_STRENGTHENED",
    ];

    public RefinementDecisionTests()
        : base("MistRefinement_")
    {
    }

    protected override string Fixtures { get; } = """
        type AdultAge = int >= 18;
        type DrinkingAge = int >= 21;
        type Percentage = int >= 0 and <= 100;
        type Digit = int >= 0 and <= 9;
        type Even = int (2 or 4 or 6);
        type NotZero = int not 0;
        type Upper = char >= 'A' and <= 'Z';
        type Flag = bool true;
        type Small = byte <= 10;
        type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
        type Named = string "x";

        public class Buffer
        {
            int[5] items = new int[5];

            public int this[int index] where index >= 0 and < 5
            {
                get { return items[index]; }
                set { items[index] = value; }
            }
        }

        public class FixedArray<TValue, Size:int> where Size >= 0
        {
            public int Count()
            {
                return Size;
            }
        }

        public class Wrapper<N:int> : FixedArray<string, N> where N >= 0
        {
        }

        public abstract IValidator
        {
            string Validate(int value) where value >= 0;
        }

        public class Same : IValidator
        {
            public string Validate(int value) where value >= 0
            {
                return "same";
            }
        }

        public class Lax : IValidator
        {
            public string Validate(int value) where value >= -5
            {
                return "lax";
            }
        }

        public class Lenient : IValidator
        {
            public string Validate(int value)
            {
                return "lenient";
            }
        }

        public class Helpers
        {
            public static int Twice(AdultAge a) { return a + a; }
            public static string Process(int x) where x < 100 { return "ok"; }
            public static AdultAge Grown() { return 30; }
            public static int Sum(int[] items) { int s = 0; for(int i = 0; i < items.Length; i = i + 1) { s = s + items[i]; } return s; }
            public static int Sized(int[] items, int i) { if(i >= 0 && i < items.Length) { return items[i]; } return 0; }
            public static int Narrowed(int x) where x >= 0 { Percentage p = 0; if(x <= 100) { p = x; } return p; }
        }
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        int x = 20;
        AdultAge adult = 30;
        DrinkingAge drinking = 40;
        Digit digit = 5;
        int[] items = [1, 2, 3];
        """;

    [Theory]
    [InlineData("AdultAge a = 18;")]
    [InlineData("AdultAge a = 65;")]
    [InlineData("Percentage p = 0; Percentage q = 100;")]
    [InlineData("Even e = 4;")]
    [InlineData("NotZero n = -3;")]
    [InlineData("Upper u = 'Q';")]
    [InlineData("Flag f = true;")]
    [InlineData("Small s = 10;")]
    [InlineData("AgeRange r = 5; AgeRange b = 50;")]
    [InlineData("Named n = \"anything\";")]
    [InlineData("AdultAge a = adult;")]
    [InlineData("AdultAge a = drinking;")]
    [InlineData("Percentage p = digit;")]
    [InlineData("int i = adult;")]
    [InlineData("AdultAge a = Helpers.Grown();")]
    [InlineData("int y = 40; AdultAge a = y;")]
    public void AValueThatSatisfiesTheRefinement_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("if(x >= 18) { AdultAge a = x; }")]
    [InlineData("if(x > 17) { AdultAge a = x; }")]
    [InlineData("if(x >= 21) { AdultAge a = x; }")]
    [InlineData("if(18 <= x) { AdultAge a = x; }")]
    [InlineData("if(x is >= 18) { AdultAge a = x; }")]
    [InlineData("if(x is AdultAge a) { AdultAge b = x; DrinkingAge c = 21; }")]
    [InlineData("if(x < 18) { return; } AdultAge a = x;")]
    [InlineData("if(x >= 0 && x <= 100) { Percentage p = x; }")]
    [InlineData("if(x >= 0) { if(x <= 100) { Percentage p = x; } }")]
    [InlineData("if(x is >= 0 and <= 100) { Percentage p = x; }")]
    [InlineData("if(x >= 18 || x >= 30) { AdultAge a = x; }")]
    [InlineData("while(x >= 18) { AdultAge a = x; x = x - 1; }")]
    [InlineData("if(!(x < 18)) { AdultAge a = x; }")]
    public void ANarrowingTheFlowEstablishes_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var a = new int[3]; Console.WriteLine(a[0] + a[2]);")]
    [InlineData("var a = new int[3]; for(var i = 0; i < 3; i = i + 1) { Console.WriteLine(a[i]); }")]
    [InlineData("var a = new int[3]; for(var i = 0; i < a.Length; i = i + 1) { Console.WriteLine(a[i]); }")]
    [InlineData("var a = new int[3]; for(var i = 2; i >= 0; i = i - 1) { Console.WriteLine(a[i]); }")]
    [InlineData("Console.WriteLine(items[0]);")]
    [InlineData("Console.WriteLine(Helpers.Sum(items) + Helpers.Sized(items, 1));")]
    [InlineData("var a = new int[10]; Console.WriteLine(a[digit]);")]
    [InlineData("var b = new Buffer(); Console.WriteLine(b[4]); b[0] = 1;")]
    [InlineData("var b = new Buffer(); if(x >= 0 && x < 5) { Console.WriteLine(b[x]); }")]
    [InlineData("Console.WriteLine(items[^1]);")]
    [InlineData("var a = new int[3]; if(x >= 0) { if(x < 3) { Console.WriteLine(a[x]); } }")]
    [InlineData("var d = [[1, 2], [3, 4]]; for(int i = 0; i < d.Length; i = i + 1) { Console.WriteLine(d[i][0]); }")]
    [InlineData("var d = [[1, 2], [3, 4]]; int s = 0; for(int i = 0; i < d.Length; i = i + 1) { for(int j = 0; j < d[i].Length; j = j + 1) { s = s + d[i][j]; } }")]
    [InlineData("var d = [1, 2]; for(int i = 0; i < d.Length; i = i + 1) { Console.WriteLine(d[i]); }")]
    public void AnIndexTheFlowProves_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("Console.WriteLine(Helpers.Twice(3));")]
    [InlineData("Console.WriteLine(Helpers.Twice(x));")]
    [InlineData("Console.WriteLine(Helpers.Process(200));")]
    [InlineData("Console.WriteLine(Helpers.Process(x));")]
    [InlineData("var a = (AdultAge)7; var b = (AdultAge)x;")]
    [InlineData("var f = new FixedArray<int, 3>(); var w = new Wrapper<2>(); Console.WriteLine(f.Count() + w.Count());")]
    [InlineData("Console.WriteLine(Helpers.Narrowed(5));")]
    public void WhatClassicLeavesToTheCallee_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("AdultAge a = 7; AdultAge b = 7;")]
    [InlineData("var a = new int[3]; Console.WriteLine(a[5] + a[5]);")]
    public void AFindingIsReportedPerSite(string statement)
    {
        Assert.Equal(2, Analyse(statement).Count);
    }

    [Theory]
    [InlineData("AdultAge a = nothing;")]
    [InlineData("var a = new int[3]; Console.WriteLine(a[nothing]);")]
    public void AValueThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}