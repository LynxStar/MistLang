// Tier 3 (Component): what ConformanceAnalyzer deliberately does NOT report. The fixtures are themselves
// under analysis in every row, so a default left unfilled, a default a conformer restates, a generic
// slot closed by the base list, a slot filled by an inherited member, a diamond filled once, a with
// block answered by a mapping and one answered by the type's own member are silences proven by every
// row. WHY each is silent is recorded once, on order 7 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ConformanceDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_SLOT_NOT_FILLED", "MIST_PA_SLOT_SIGNATURE_MISMATCH", "MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING",
        "MIST_PA_CONFORMANCE_SLOT_UNANSWERED", "MIST_PA_REDUNDANT_CONFORMANCE", "MIST_PA_TYPE_SLOT_UNFILLED",
        "MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED",
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

    [Fact]
    public void AConformerNamingAnUnresolvedAbstract_IsNotReportedAgain()
    {
        AssertSilent("", "public class Lost : Nowhere { }");
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

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}
