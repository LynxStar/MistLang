// Tier 3 (Component): what the readonly-context rules deliberately do NOT report. The fixture types are
// under analysis in every row, so a constructor assigning its own fields, a readonly setter writing its
// backing field, a readonly method calling an inferred-readonly one, a readonly block reading into a
// local, and an allocation through a readonly constructor are silences proven by every row. WHY each is
// admitted is recorded on order 11 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ReadonlyContextDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY",
        "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK",
        "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER", "MIST_PA_READONLY_FIELD_ASSIGNED",
        "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE", "MIST_PA_READONLY_PARAMETER_SETS_PROPERTY",
        "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX",
    ];

    public ReadonlyContextDecisionTests()
        : base("MistReadonlyContext_")
    {
    }

    protected override string Fixtures { get; } = """
        public readonly class Frozen
        {
            public int v;

            public Frozen(int x)
            {
                v = x;
            }

            public int Get()
            {
                return v;
            }

            public Frozen Next()
            {
                return new Frozen(v + 1);
            }
        }

        public class Box
        {
            public int v;

            public readonly Box(int x)
            {
                v = x;
            }

            public int Pure(int x)
            {
                return x + 1;
            }

            public readonly int Uses()
            {
                return Pure(v);
            }

            public readonly int Score
            {
                get
                {
                    return field;
                }
                set
                {
                    field = value;
                }
            }

            public int Reader()
            {
                var r = 0;
                readonly
                {
                    r = v;
                }

                return r;
            }

            public readonly Box Copy()
            {
                return new Box(v);
            }

            public readonly int Scratch()
            {
                int[] tmp = [0, 0];
                tmp[0] = 1;
                return tmp[0];
            }
        }

        public class Ledger
        {
            public int cell;

            public int this[int i]
            {
                get
                {
                    return cell;
                }
                set
                {
                    cell = value;
                }
            }

            public readonly int First()
            {
                return this[0];
            }
        }

        public struct Vec
        {
            public int x;

            public Vec(int x)
            {
                this.x = x;
            }

            public static Vec operator +(Vec a, Vec b)
            {
                return new Vec(a.x + b.x);
            }

            public readonly int Doubled()
            {
                return (this + this).x;
            }
        }

        public class Gauge
        {
            public int n;

            public int Total { get; set; }

            public readonly int Peek()
            {
                return n;
            }

            public static int Through(readonly Gauge gauge)
            {
                return gauge.Peek() + gauge.Total + gauge.n;
            }

            public static void Fill(readonly int[] slots)
            {
                slots[0] = 1;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("var f = new Frozen(1).Next();")]
    [InlineData("var u = new Box(1).Uses();")]
    [InlineData("var s = new Box(1).Score;")]
    [InlineData("var r = new Box(1).Reader();")]
    [InlineData("var c = new Box(1).Copy();")]
    [InlineData("var s2 = new Box(1).Scratch();")]
    [InlineData("var l = new Ledger().First();")]
    [InlineData("var d = new Vec(1).Doubled();")]
    [InlineData("var t = Gauge.Through(new Gauge());")]
    [InlineData("Gauge.Fill([0]);")]
    public void APrivilegedOrReadonlyUse_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement, extraTypes: ""));
    }

    [Fact]
    public void AMutableCallIsReportedOnceAtTheCall()
    {
        var extraTypes = """
            public class Counter
            {
                public int n;

                public void Bump()
                {
                    n = n + 1;
                }

                public readonly int Peek()
                {
                    Bump();
                    Bump();
                    return n;
                }
            }
            """;

        Assert.Equal(2, Analyse("var c = new Counter();", extraTypes).Count);
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}