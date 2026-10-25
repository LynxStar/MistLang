// Tier 3 (Component): the writes AccessorAnalyzer deliberately admits. Every fixture is analyzed in
// every row: a get-only auto-property in its own constructor, an init-only one on this in a
// constructor or init accessor of its type or derivation, and a private setter from its own type,
// lambda or nested type. AccessorAnalyzer states the access and initialization privileges.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AccessorDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_NO_GET_ACCESSOR", "MIST_PA_NO_SET_ACCESSOR", "MIST_PA_INACCESSIBLE_ACCESSOR", "MIST_PA_INIT_ONLY_ASSIGNMENT",
        "MIST_PA_INACCESSIBLE_MEMBER",
    ];

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

        public class Jar
        {
            public int Size { get; init; }

            public int? Spare { get; init; }

            public int Twice
            {
                get { return Size * 2; }
                init { Size = value / 2; this.Spare = value; }
            }

            public Jar()
            {
                Size = 3;
                this.Size = 4;
                Spare ??= 1;
            }
        }

        public class Crate : Jar
        {
            public int Depth
            {
                get { return Size; }
                init { Size = value; }
            }

            public Crate()
            {
                this.Size = 20;
                Size = 21;
            }
        }

        public class Counter
        {
            public int Count { get; private set; }

            public int this[int slot]
            {
                get { return Count + slot; }
                private set { Count = value; }
            }

            public string this[string key]
            {
                get { return ""; }
                set { Count = 0; }
            }

            public void Bump()
            {
                Count = Count + 1;
                this.Count = this.Count + 1;
                this[0] = this[0] + 1;
                func<int> later = () => { Count = 5; return Count; };
            }

            public static Counter Made()
            {
                var made = new Counter { Count = 40 };
                made.Count = made.Count + 1;
                made[1] = 2;
                return made;
            }

            public class Helper
            {
                public static void Poke(Counter counter)
                {
                    counter.Count = counter.Count + 100;
                    counter[2] = 3;
                }
            }
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

    [Fact]
    public void AnElementAccessTheTargetSupplies_IsNotJudged()
    {
        AssertSilent("int[] cells = [1]; cells[0] = 2;");
    }

    [Theory]
    [InlineData("var stamped = new Stamped { Serial = 2 };")]
    [InlineData("var jar = new Jar { Size = 1, Spare = 2 };")]
    [InlineData("var twice = new Jar { Twice = 8 };")]
    [InlineData("var crate = new Crate { Depth = 2, Size = 3 };")]
    [InlineData("func<Jar> make = () => new Jar { Size = 5 };")]
    public void AnInitOnlyPropertyWrittenInConstruction_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var stamped = new Stamped { Serial = 2 }; int serial = stamped.Serial;")]
    [InlineData("var jar = new Jar(); bool three = jar is { Size: 3 };")]
    public void AnInitOnlyPropertyReadAfterConstruction_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void APrivateSetterUsedInsideItsTypeAndANestedOne_IsAdmitted()
    {
        AssertSilent("var made = Counter.Made(); made.Bump(); Counter.Helper.Poke(made);");
    }

    [Theory]
    [InlineData("var counter = new Counter(); int count = counter.Count;")]
    [InlineData("var counter = new Counter(); int slot = counter[2];")]
    [InlineData("var counter = new Counter(); bool fresh = counter is { Count: 0 };")]
    public void APublicReadThroughAPrivateSetter_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void AnIndexerWhoseSiblingHasAPrivateSetter_IsSelectedAndWritten()
    {
        AssertSilent("var counter = new Counter(); counter[\"a\"] = \"b\";");
    }

    private void AssertSilent(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(findings.ToImmutableArray().Where(d => Ids.Contains(d.Number.Value)));
    }
}