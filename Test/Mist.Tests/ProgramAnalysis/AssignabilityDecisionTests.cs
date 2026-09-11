// Tier 3 (Component): what AssignabilityAnalyzer deliberately does NOT report, and the reach facts the
// corpus cannot phrase. The corpus holds each thing the rule refuses; a silence has no line to put a
// marker on, so each is pinned here instead -- the admits classic's probe established, the constant that
// fits its narrower target, the refinement left to order 12, and the divergences from classic. WHY each
// is silent is recorded once, on order 5 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AssignabilityDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids = ["MIST_PA_NO_IMPLICIT_CONVERSION", "MIST_PA_CAST_REQUIRED"];

    public AssignabilityDecisionTests()
        : base("MistAssignability_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Animal
        {
        }

        public class Dog : Animal
        {
        }

        public abstract IBox<T>
        {
        }

        public class Box<T> : IBox<T>
        {
            public int Count { get; set; }
        }

        public enum Color : byte { Red, Green }
        public enum Priority : int { Low = 1, High = 2 }

        type AdultAge = int >= 18;
        type DrinkingAge = int >= 21;
        type PositiveInt = int > 0;

        public class Sized<Count:PositiveInt>
        {
            public int GetCount() { return Count; }
        }

        public class Helpers
        {
            public static int Twice(int v) { return v * 2; }
            public static void Take(int v) { }
            public static void TakeFn(func<int, int> f) { }
            public static T Id<T>(T v) { return v; }
            public static Animal Up<T>(T v) where T : Animal { return v; }
            public static async int Async() { return 1; }
            public static bool Compare(int a, int b) { return a > b && a != b; }
            public static string Join(int a) { return "n=" + a; }
            public static int Outer() { var f = () => { return "a"; }; return f().Length; }
            public static int Wrapped() { var t = async { return "a"; }; return 1; }
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        bool flag = true;
        Dog dog = new Dog();
        Box<int> box = new Box<int>();
        Color color = Color.Red;
        Priority priority = Priority.Low;
        AdultAge adult = (AdultAge)20;
        DrinkingAge drinking = (DrinkingAge)21;
        int | void opt = 5;
        int | void o3 = void;
        int | string mixed = 1;
        (int px, int py) named = (1, 2);
        (int, int) pair = (1, 2);
        int[] arr = [1];
        func<int, int> fn = y => y + 1;
        """;

    [Theory]
    [InlineData("byte b = 1;")]
    [InlineData("sbyte s = -1;")]
    [InlineData("short sh = -1;")]
    [InlineData("ushort us = 1;")]
    [InlineData("uint u = 1;")]
    [InlineData("ulong ul = 1;")]
    [InlineData("ulong big = 1L;")]
    [InlineData("long l = 1;")]
    [InlineData("double d = 1;")]
    [InlineData("float f = 1;")]
    [InlineData("decimal m = 1;")]
    [InlineData("int c = 'a';")]
    [InlineData("double fd = 1.5f;")]
    public void AConstantOrWideningIntoAPrimitive_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int | string | bool wider = mixed;")]
    [InlineData("int | void o = 5;")]
    [InlineData("int | void o2 = void;")]
    [InlineData("int? o4 = void;")]
    [InlineData("Priority? pq = priority;")]
    [InlineData("unknown any = box;")]
    [InlineData("int c1 = opt ?? 0;")]
    [InlineData("int c2 = opt ?? o3 ?? 0;")]
    [InlineData("o3 ??= 1;")]
    public void AValueIntoAUnionOrOutOfAnOptional_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("Animal a = dog;")]
    [InlineData("IBox<int> ib = box;")]
    [InlineData("byte cb = color;")]
    [InlineData("int ci = color;")]
    [InlineData("long cl = color;")]
    [InlineData("Helpers.Take(color);")]
    [InlineData("var idv = Helpers.Id(1);")]
    [InlineData("Animal up = Helpers.Up(dog);")]
    [InlineData("int df = default;")]
    [InlineData("Box<int> tn = new();")]
    [InlineData("Box<int> ini = new Box<int> { Count = 1 };")]
    [InlineData("var anon = new { A = 1 }; var anon2 = new { A = 2 }; anon = anon2;")]
    [InlineData("bool cmp = x > 0;")]
    [InlineData("string cat = \"a\" + x;")]
    [InlineData("long prod = x * 2L;")]
    [InlineData("double avg = x / 2.0;")]
    public void AValueAlreadyWithinItsTarget_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("AdultAge age = 20;")]
    [InlineData("AdultAge | void maybe = 20;")]
    [InlineData("int back = adult;")]
    [InlineData("AdultAge fromDrinking = drinking;")]
    public void ATargetThatOnlyARefinementStandsBetween_IsLeftToTheProver(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("func<int, int> f2 = y => y + 1;")]
    [InlineData("func<int, int> mg = Helpers.Twice;")]
    [InlineData("func<int, int> same = fn;")]
    [InlineData("Helpers.TakeFn(y => y * 2);")]
    [InlineData("Helpers.TakeFn(Helpers.Twice);")]
    [InlineData("Helpers.TakeFn(fn);")]
    [InlineData("int awaited = Helpers.Async();")]
    [InlineData("Task<int> t = Helpers.Async();")]
    public void AFunctionValueOrATaskWhereOneIsWanted_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("(int, int) unnamed = named;")]
    [InlineData("(int qa, int qb) renamed = named;")]
    [InlineData("named = pair;")]
    [InlineData("(long la, long lb) = pair;")]
    [InlineData("var (da, db) = named;")]
    [InlineData("int[] xs = [1, 2];")]
    [InlineData("long[] ls = [1, 2];")]
    [InlineData("int[] fromNew = new int[3];")]
    [InlineData("int[3] sized = new int[3];")]
    [InlineData("int[] resized = new int[x];")]
    [InlineData("foreach(int e in arr) { }")]
    [InlineData("foreach(long lw in arr) { }")]
    [InlineData("long rl = flag switch { true => 1, false => 0 };")]
    [InlineData("int | string ru = flag switch { true => 1, false => \"a\" };")]
    [InlineData("unknown rk = flag switch { true => 1, false => \"a\" };")]
    public void ATupleArrayLoopOrSwitchWhoseElementsFit_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int z = neverDeclared;")]
    [InlineData("Nope n = 1;")]
    [InlineData("Helpers.Take(neverDeclared);")]
    public void AValueOrTargetThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("int y = \"a\";")]
    [InlineData("Helpers.TakeFn(y => { int z = \"a\"; return y; });")]
    [InlineData("var f = () => { string s = \"a\"; int z = s; return z; };")]
    public void AMismatchIsReportedOnceWhereverItSits(string statement)
    {
        _ = Assert.Single(Analyse(statement));
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