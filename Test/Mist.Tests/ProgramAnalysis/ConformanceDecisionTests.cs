// Tier 3 (Component): the conformance shapes ConformanceAnalyzer deliberately admits. Fixtures pin
// defaults left unfilled or restated by a conformer, generic slots closed by the base list,
// inherited fillers, diamonds filled once, and with blocks answered by mappings or the type's own
// members. The analyzer states the slot/default/filler contract.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ConformanceDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST0183", "MIST0184", "MIST0188",
        "MIST0189", "MIST0190", "MIST0191",
        "MIST0192", "MIST0187",
    ];

    public ConformanceDecisionTests()
        : base("MistConformance_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IDescribable
        {
            string Describe();
        }

        public abstract IFull : IDescribable
        {
            string Detail();
        }

        public abstract IGreeting
        {
            string Name();
            string Greet() { return "hi " + this.Name(); }
        }

        public abstract ILabeled
        {
            string Label { get; set; }
        }

        public abstract IBox<T>
        {
            T Get();
            void Put(T value);
        }

        public abstract ILeft : IDescribable
        {
        }

        public abstract IRight : IDescribable
        {
        }

        public abstract IHasKind
        {
            type Kind : IDescribable;
            Kind Make();
        }

        public class Fine : IDescribable, IGreeting, ILabeled
        {
            public string Describe() { return "fine"; }
            public string Name() { return "fine"; }
            public string Label { get; set; }
        }

        public class OwnGreet : IGreeting
        {
            public string Name() { return "own"; }
            public string Greet() { return "own greet"; }
        }

        public class IntBox : IBox<int>
        {
            public int Get() { return 1; }
            public void Put(int value) { }
        }

        public class Base : IDescribable
        {
            public string Describe() { return "base"; }
        }

        public class Inherits : Base, IFull
        {
            public string Detail() { return "inherits"; }
        }

        public class Both : ILeft, IRight
        {
            public string Describe() { return "both"; }
        }

        public class Plain
        {
            public string Text() { return "plain"; }
        }

        with Plain : IDescribable
        {
            Text as Describe;
        }

        public class HasIt
        {
            public string Describe() { return "own"; }
            public string Text() { return "detail"; }
        }

        with HasIt : IFull
        {
            Text as Detail;
        }

        public class Kinded : IHasKind
        {
            public class Kind : IDescribable
            {
                public string Describe() { return "kind"; }
            }

            public Kind Make() { return new Kind(); }
        }
        """;

    protected override string Preamble { get; } = """
        Fine fine = new Fine();
        """;

    [Theory]
    [InlineData("string d = fine.Describe();")]
    [InlineData("IBox<int> box = new IntBox();")]
    [InlineData("IFull full = new Inherits();")]
    [InlineData("IDescribable both = new Both();")]
    [InlineData("IDescribable plain = new Plain();")]
    [InlineData("IFull hasIt = new HasIt();")]
    public void AConformerThatFillsEverySlot_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("public class Missing : IDescribable { }")]
    [InlineData("public class Wrong : IDescribable { public string Describe(int n) { return \"x\"; } }")]
    [InlineData("public class GetOnly : ILabeled { public string Label { get; } }")]
    public void AConformerThatLeavesASlot_IsReportedOnce(string extraType)
    {
        _ = Assert.Single(Analyse("", extraType));
    }

    [Theory]
    [InlineData("public readonly abstract IRead { string Read(); int Count { get; } } public class Page : IRead { public string Text = \"a\"; public int Held = 1; public readonly string Read() { return Text; } public int Count { get { return Held; } } public void Clear() { Text = \"\"; Held = 0; } }")]
    [InlineData("public readonly abstract IRead { int Peek(); } public class Page : IRead { int n; public int Peek() { return Look(); } int Look() { return n; } public void Bump() { n = n + 1; } }")]
    [InlineData("public abstract IWrite { int Poke(); int Size { get; } } public class Page : IWrite { int n; public int Poke() { n = n + 1; return n; } public int Size { get { n = n + 1; return n; } } }")]
    [InlineData("public readonly abstract IRead { int Peek(); } public class Shelf { int n; public int Peek() { return n; } public void Bump() { n = n + 1; } } public class Page : Shelf, IRead { }")]
    public void AMemberThatWritesNothing_MeetsAReadonlyContractOnAMutableType(string extraType)
    {
        AssertSilent("", extraType);
    }

    [Fact]
    public void AConformerNamingAnUnresolvedAbstract_IsNotReportedAgain()
    {
        AssertSilent("", "public class Lost : Nowhere { }");
    }

    [Theory]
    [InlineData("T Read();", "public int Number() { return 1; }", "Number as Read;")]
    [InlineData("T Value { get; set; }", "public int Number { get; set; }", "Number as Value;")]
    public void MappingOneConstruction_LeavesTheOtherConstructionUnanswered(string slot, string member, string mapping)
    {
        var findings = Analyse("", $$"""
            public abstract IGeneric<T> { {{slot}} }
            public abstract IBoth : IGeneric<int>, IGeneric<string> { }
            public class Partial { {{member}} }
            with Partial : IBoth { {{mapping}} }
            """);

        var finding = Assert.Single(findings);
        Assert.Equal("MIST0189", finding.Number.Value);
        Assert.Contains("string", finding.Message);
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement, string extraTypes = "")
    {
        Assert.Empty(Analyse(statement, extraTypes));
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes = "")
    {
        var model = BuildModel(statement, extraTypes, extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Number.Value))];
    }
}