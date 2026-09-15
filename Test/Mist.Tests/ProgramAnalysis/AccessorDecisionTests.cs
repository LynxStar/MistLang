// Tier 3 (Component): what AccessorAnalyzer deliberately does NOT report. Every fixture type is under analysis
// in every row, so a get-only auto-property assigned in its own constructor is a silence proven by each of
// them. WHY each is admitted is recorded on order 9 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AccessorDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids = ["MIST_PA_NO_GET_ACCESSOR", "MIST_PA_NO_SET_ACCESSOR"];

    public AccessorDecisionTests()
        : base("MistAccessor_")
    {
    }

    protected override string Fixtures { get; } = """
        public class ReadWrite
        {
            public int stored;
            public int? spare;

            public int this[int slot] { get { return stored + slot; } set { stored = value; } }

            public int? this[string key] { get { return spare; } set { spare = value; } }

            public int Level { get { return stored; } set { stored = value; } }

            public int? Spare { get { return spare; } set { spare = value; } }
        }

        public class GetOnly
        {
            public int this[int slot] { get { return slot; } }
        }

        public class SetOnly
        {
            public int last;

            public int this[int slot] { set { last = value; } }
        }

        public class Catalog
        {
            public string title = "";

            public int this[int slot] { get { return slot; } }

            public string this[string key] { get { return title; } set { title = value; } }
        }

        public class Seeded
        {
            public static int Made { get; }

            public int Count { get; }

            static Seeded()
            {
                Made = 1;
            }

            public Seeded()
            {
                Count = 3;
                this.Count = 4;
            }
        }

        public class Stamped
        {
            public int Serial { get; init; }
        }

        public class Row<params TArgs>
        {
            public TArgs... Slot { get; }

            public Row(params TArgs values)
            {
                Slot... = values...;
            }
        }

        public struct Framed<Type[2]:TParts>
        {
            public TParts... Part { get; }

            public Framed(params TParts parts)
            {
                Part... = parts...;
            }

            public int Both()
            {
                var first = Part1;
                var second = Part2;
                return 2;
            }
        }
        """;

    protected override string Preamble { get; } = """
        var rw = new ReadWrite();
        """;

    [Theory]
    [InlineData("rw[1] = 4; int read = rw[1];")]
    [InlineData("rw.Level = 4; int level = rw.Level;")]
    public void AReadWriteSlot_IsReadAndWritten(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("rw[2] = rw[2] + 10;")]
    [InlineData("rw.Level = rw.Level + 10;")]
    [InlineData("rw[\"k\"] ??= 5;")]
    [InlineData("rw.Spare ??= 5;")]
    public void AReadWriteSlot_IsReadAndWrittenByOneStatement(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var getOnly = new GetOnly(); int read = getOnly[0];")]
    [InlineData("var setOnly = new SetOnly(); setOnly[0] = 1;")]
    public void ASlot_IsUsedInTheDirectionItsAccessorAdmits(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void AnIndexerWhoseSiblingLacksTheAccessor_IsSelectedAndWritten()
    {
        AssertSilent("var catalog = new Catalog(); catalog[\"a\"] = \"b\";");
    }

    [Fact]
    public void AGetOnlyAutoPropertyAssignedInItsConstructor_IsAdmitted()
    {
        AssertSilent("var seeded = new Seeded(); int count = seeded.Count + Seeded.Made;");
    }

    [Theory]
    [InlineData("var row = new Row<int, string>(7, \"z\"); int slot = row.Slot1;")]
    [InlineData("var framed = new Framed<int, string>(7, \"z\"); int both = framed.Both();")]
    public void AProjectionOfAGetOnlySpreadProperty_IsRead(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var stamped = new Stamped(); stamped.Serial = 2;")]
    [InlineData("int[] cells = [1]; cells[0] = 2;")]
    public void ASlotThisRuleDoesNotOwn_IsNotJudged(string statement)
    {
        AssertSilent(statement);
    }

    private void AssertSilent(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value)));
    }
}