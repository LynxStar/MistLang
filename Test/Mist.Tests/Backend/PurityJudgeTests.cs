// Tier 3 (Component): every row of the purity design's judge table, once per scope kind. The fixture
// types are under analysis in every row, so Bag's local members, Vec's inherited pure ones and Holder's
// storing members are silences proven by every row. WHY each row is admitted or refused is the table in
// .kiro/specs/purity/design.md, and the ids are its findings list.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PurityJudgeTests : ProgramModelProbe
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string ReadsArgument = "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";

    private static readonly string[] Ids =
    [
        Writes, ReadsMutable, CallsImpure, ReadsArgument, StoresUnowned,
        "MIST_PA_PURE_FUNCTION_REQUIRED", "MIST_PA_DEFAULT_NOT_CONSISTENT",
    ];

    private const string HostOpen = """
        public class Host
        {
            public int Count;
            public readonly int Fixed;
            public owned Bag Mine;
            public Bag Shared;

            public Host(int seed)
            {
                Count = 0;
                Fixed = seed;
                Mine = new Bag();
                Shared = new Bag();
            }

            public local void Add(int x) { Count = Count + 1; }

            public local readonly int Size() { return Count; }

            public readonly int Peek() { return Count + Globals.Counter; }

            public void Poke() { Count = 0; Globals.Counter = 1; }

        """;

    public PurityJudgeTests()
        : base("MistPurityJudge_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Bag
        {
            public int Count;

            public pure Bag() { }

            public local void Add(int x) { Count = Count + 1; }

            public local readonly int Size() { return Count; }

            public readonly int Peek() { return Count + Globals.Counter; }

            public void Poke() { Count = 0; Globals.Counter = 1; }
        }

        public pure class Vec
        {
            public readonly int X;

            public pure Vec(int x) { X = x; }

            public int Doubled() { return X * 2; }
        }

        public struct Pt
        {
            public int X;
        }

        public class Holder
        {
            public Bag Item;
            public string Text;
            public Pt P;
            public int N;

            public local void Keep(Bag b) { Item = b; }

            public local void Label(string s) { Text = s; }
        }

        public class Loud
        {
            public int V;

            public Loud() { Globals.Counter = 1; }
        }

        public class Res
        {
            public void Drop() { Globals.Counter = 1; }
        }

        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
            public static readonly Bag Pool = new Bag();
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public pure int M() { var b = new Bag(); b.Count = 1; return b.Count; }", null)]
    [InlineData("public pure int M() { owned var b = new Bag(); b.Count = 1; return b.Count; }", null)]
    [InlineData("public pure int M() { int[] xs = [0, 0]; xs[0] = 1; return xs[0]; }", null)]
    [InlineData("public pure int M() { Count = 1; return 1; }", Writes)]
    [InlineData("public pure int M() { Globals.Counter = 1; return 1; }", Writes)]
    [InlineData("public pure int M(frozen Bag f) { f.Count = 1; return 1; }", Writes)]
    [InlineData("public pure int M(Bag x) { x.Count = 1; return 1; }", Writes)]
    [InlineData("public pure int M() { return Fixed; }", null)]
    [InlineData("public pure int M() { return Globals.Limit; }", null)]
    [InlineData("public pure int M(Vec v) { return v.X; }", null)]
    [InlineData("public pure int M() { var b = new Bag(); return b.Count; }", null)]
    [InlineData("public pure int M() { int[] xs = [1]; return xs[0]; }", null)]
    [InlineData("public pure int M(frozen Bag f) { return f.Count; }", null)]
    [InlineData("public pure int M() { return Count; }", ReadsMutable)]
    [InlineData("public pure int M() { return Globals.Counter; }", ReadsMutable)]
    [InlineData("public pure int M(Bag x) { return x.Count; }", ReadsMutable)]
    [InlineData("public pure int M() { return Shared.Count; }", ReadsMutable)]
    [InlineData("public pure int M() { return Mine.Count; }", ReadsMutable)]
    [InlineData("public pure int M(Vec v) { return v.Doubled(); }", null)]
    [InlineData("public pure int M() { var b = new Bag(); return b.Size(); }", null)]
    [InlineData("public pure int M(frozen Bag f) { return f.Size(); }", null)]
    [InlineData("public pure int M(Bag x) { return x.Size(); }", CallsImpure)]
    [InlineData("public pure int M() { return Mine.Size(); }", CallsImpure)]
    [InlineData("public pure int M() { var b = new Bag(); b.Add(1); return b.Count; }", null)]
    [InlineData("public pure int M(frozen Bag f) { f.Add(1); return 1; }", CallsImpure)]
    [InlineData("public pure int M(Bag x) { x.Add(1); return 1; }", CallsImpure)]
    [InlineData("public pure int M() { var b = new Bag(); return b.Peek(); }", CallsImpure)]
    [InlineData("public pure int M(frozen Bag f) { return f.Peek(); }", CallsImpure)]
    [InlineData("public pure int M(Bag x) { return x.Peek(); }", CallsImpure)]
    [InlineData("public pure int M() { var b = new Bag(); b.Poke(); return 1; }", CallsImpure)]
    [InlineData("public pure int M(frozen Bag f) { f.Poke(); return 1; }", CallsImpure)]
    [InlineData("public pure int M(Bag x) { x.Poke(); return 1; }", CallsImpure)]
    [InlineData("public pure int M(frozen Vec v) { return v.Doubled(); }", null)]
    [InlineData("public pure int M() { var v = new Vec(1); return v.Doubled(); }", null)]
    [InlineData("public pure int M(pure func<int, int> f) { return f(1); }", null)]
    [InlineData("public pure int M(func<int, int> f) { return f(1); }", CallsImpure)]
    [InlineData("public pure int M() { var v = new Vec(1); return v.X; }", null)]
    [InlineData("public pure int M() { var h = new Holder(); return h.N; }", null)]
    [InlineData("public pure int M() { int[] xs = new int[2]; return xs[0]; }", null)]
    [InlineData("public pure int M() { var f = (int n) => n + 1; return 1; }", null)]
    [InlineData("public pure int M() { var l = new Loud(); return l.V; }", CallsImpure)]
    [InlineData("public pure int M(frozen Bag f) { var h = new Holder(); h.Item = f; return 1; }", StoresUnowned)]
    [InlineData("public pure int M(frozen Bag f) { var h = new Holder(); h.Keep(f); return 1; }", StoresUnowned)]
    [InlineData("public pure int M() { var h = new Holder(); h.Item = Globals.Pool; return 1; }", StoresUnowned)]
    [InlineData("public pure int M(string s) { var h = new Holder(); h.Text = s; return 1; }", null)]
    [InlineData("public pure int M(Pt p) { var h = new Holder(); h.P = p; return 1; }", null)]
    [InlineData("public pure int M(string s) { var h = new Holder(); h.Label(s); return 1; }", null)]
    [InlineData("public pure int M() { var b = new Bag(); b.Add(Globals.Limit); return 1; }", null)]
    [InlineData("public pure int M() { int[] xs = [1, 2]; var t = 0; foreach (var x in xs) { t = t + x; } return t; }", null)]
    [InlineData("public pure int M(int[] xs) { var t = 0; foreach (var x in xs) { t = t + x; } return t; }", ReadsMutable)]
    [InlineData("public pure int M() { with (var r = new Res()) { } return 1; }", CallsImpure)]
    [InlineData("public pure int M() { var s = $\"limit={Globals.Limit}\"; return Globals.Limit; }", null)]
    public void APureMethod_IsJudgedByTheTable(string member, string? expected)
    {
        Judge(member, expected);
    }

    [Theory]
    [InlineData("public local void M() { Count = 1; }", null)]
    [InlineData("public local void M() { Mine.Count = 1; }", null)]
    [InlineData("public local void M() { int[] xs = [0, 0]; xs[0] = 1; }", null)]
    [InlineData("public local void M() { Shared.Count = 1; }", Writes)]
    [InlineData("public local void M() { Globals.Counter = 1; }", Writes)]
    [InlineData("public local void M(frozen Bag f) { f.Count = 1; }", Writes)]
    [InlineData("public local void M(Bag x) { x.Count = 1; }", Writes)]
    [InlineData("public local int M() { return Fixed; }", null)]
    [InlineData("public local int M() { return Globals.Limit; }", null)]
    [InlineData("public local int M(Vec v) { return v.X; }", null)]
    [InlineData("public local int M() { return Count; }", null)]
    [InlineData("public local int M() { return Mine.Count; }", null)]
    [InlineData("public local int M() { var b = new Bag(); return b.Count; }", null)]
    [InlineData("public local int M(Pt p) { return p.X; }", null)]
    [InlineData("public local int M(frozen Bag f) { return f.Count; }", ReadsArgument)]
    [InlineData("public local int M(Bag x) { return x.Count; }", ReadsArgument)]
    [InlineData("public local int M() { return Shared.Count; }", ReadsMutable)]
    [InlineData("public local int M() { return Globals.Counter; }", ReadsMutable)]
    [InlineData("public local int M(Vec v) { return v.Doubled(); }", null)]
    [InlineData("public local int M() { return Size(); }", null)]
    [InlineData("public local int M() { return Mine.Size(); }", null)]
    [InlineData("public local int M() { var b = new Bag(); return b.Size(); }", null)]
    [InlineData("public local int M(frozen Bag f) { return f.Size(); }", CallsImpure)]
    [InlineData("public local int M() { return Shared.Size(); }", CallsImpure)]
    [InlineData("public local void M() { Add(1); }", null)]
    [InlineData("public local void M() { Mine.Add(1); }", null)]
    [InlineData("public local void M() { var b = new Bag(); b.Add(1); }", null)]
    [InlineData("public local void M() { Shared.Add(1); }", CallsImpure)]
    [InlineData("public local int M() { return Peek(); }", CallsImpure)]
    [InlineData("public local int M() { return Shared.Peek(); }", CallsImpure)]
    [InlineData("public local int M() { var b = new Bag(); return b.Peek(); }", CallsImpure)]
    [InlineData("public local void M() { Poke(); }", CallsImpure)]
    [InlineData("public local void M() { Shared.Poke(); }", CallsImpure)]
    [InlineData("public local void M() { var b = new Bag(); b.Poke(); }", CallsImpure)]
    [InlineData("public local int M() { var v = new Vec(1); return v.Doubled(); }", null)]
    [InlineData("public local int M(pure func<int, int> f) { return f(1); }", null)]
    [InlineData("public local int M(func<int, int> f) { return f(1); }", CallsImpure)]
    [InlineData("public local int M() { var v = new Vec(1); return v.X; }", null)]
    [InlineData("public local int M() { var h = new Holder(); return h.N; }", null)]
    [InlineData("public local int M() { var l = new Loud(); return l.V; }", CallsImpure)]
    [InlineData("public local void M() { Shared = Globals.Pool; }", StoresUnowned)]
    [InlineData("public local void M(frozen Bag f) { var h = new Holder(); h.Keep(f); }", StoresUnowned)]
    [InlineData("public local void M(Bag other) { Shared = other; }", null)]
    [InlineData("public local void M(string s) { var h = new Holder(); h.Text = s; }", null)]
    [InlineData("public local void M(Pt p) { var h = new Holder(); h.P = p; }", null)]
    [InlineData("public local int M() { int[] xs = [1, 2]; var t = 0; foreach (var x in xs) { t = t + x; } return t; }", null)]
    [InlineData("public local int M(int[] xs) { var t = 0; foreach (var x in xs) { t = t + x; } return t; }", ReadsArgument)]
    [InlineData("public local int M() { with (var r = new Res()) { } return 1; }", CallsImpure)]
    [InlineData("public local int M() { var s = $\"limit={Globals.Limit}\"; return Globals.Limit; }", null)]
    public void ALocalMethod_IsJudgedByTheTable(string member, string? expected)
    {
        Judge(member, expected);
    }

    [Theory]
    [InlineData("public local readonly int M() { return Count; }", null)]
    [InlineData("public local readonly int M() { return Mine.Count; }", null)]
    [InlineData("public local readonly int M() { return Fixed; }", null)]
    [InlineData("public local readonly int M() { return Globals.Limit; }", null)]
    [InlineData("public local readonly int M(Vec v) { return v.X; }", null)]
    [InlineData("public local readonly int M(Pt p) { return p.X; }", null)]
    [InlineData("public local readonly int M() { var b = new Bag(); return b.Count; }", null)]
    [InlineData("public local readonly int M() { int[] xs = [1]; return xs[0]; }", null)]
    [InlineData("public local readonly void M() { Count = 1; }", Writes)]
    [InlineData("public local readonly void M() { Mine.Count = 1; }", Writes)]
    [InlineData("public local readonly void M() { var b = new Bag(); b.Count = 1; }", Writes)]
    [InlineData("public local readonly void M() { int[] xs = [0, 0]; xs[0] = 1; }", Writes)]
    [InlineData("public local readonly void M() { Globals.Counter = 1; }", Writes)]
    [InlineData("public local readonly void M(Bag x) { x.Count = 1; }", Writes)]
    [InlineData("public local readonly int M(frozen Bag f) { return f.Count; }", ReadsArgument)]
    [InlineData("public local readonly int M(Bag x) { return x.Count; }", ReadsArgument)]
    [InlineData("public local readonly int M() { return Shared.Count; }", ReadsMutable)]
    [InlineData("public local readonly int M() { return Globals.Counter; }", ReadsMutable)]
    [InlineData("public local readonly int M(Vec v) { return v.Doubled(); }", null)]
    [InlineData("public local readonly int M() { return Size(); }", null)]
    [InlineData("public local readonly int M() { return Mine.Size(); }", null)]
    [InlineData("public local readonly int M() { var b = new Bag(); return b.Size(); }", null)]
    [InlineData("public local readonly int M(frozen Bag f) { return f.Size(); }", CallsImpure)]
    [InlineData("public local readonly int M() { return Shared.Size(); }", CallsImpure)]
    [InlineData("public local readonly void M() { Add(1); }", CallsImpure)]
    [InlineData("public local readonly void M() { Mine.Add(1); }", CallsImpure)]
    [InlineData("public local readonly void M() { var b = new Bag(); b.Add(1); }", CallsImpure)]
    [InlineData("public local readonly int M() { return Peek(); }", CallsImpure)]
    [InlineData("public local readonly int M() { return Shared.Peek(); }", CallsImpure)]
    [InlineData("public local readonly int M() { var b = new Bag(); return b.Peek(); }", CallsImpure)]
    [InlineData("public local readonly void M() { Poke(); }", CallsImpure)]
    [InlineData("public local readonly void M() { Shared.Poke(); }", CallsImpure)]
    [InlineData("public local readonly int M() { var v = new Vec(1); return v.Doubled(); }", null)]
    [InlineData("public local readonly int M(pure func<int, int> f) { return f(1); }", null)]
    [InlineData("public local readonly int M(func<int, int> f) { return f(1); }", CallsImpure)]
    [InlineData("public local readonly int M() { var v = new Vec(1); return v.X; }", null)]
    [InlineData("public local readonly int M() { var h = new Holder(); return h.N; }", null)]
    [InlineData("public local readonly int M() { var l = new Loud(); return l.V; }", CallsImpure)]
    [InlineData("public local readonly int M() { int[] xs = [1, 2]; var t = 0; foreach (var x in xs) { t = t + x; } return t; }", null)]
    [InlineData("public local readonly int M(int[] xs) { var t = 0; foreach (var x in xs) { t = t + x; } return t; }", ReadsArgument)]
    [InlineData("public local readonly int M() { with (var r = new Res()) { } return 1; }", CallsImpure)]
    [InlineData("public local readonly int M() { var s = $\"limit={Globals.Limit}\"; return Globals.Limit; }", null)]
    public void ALocalReadonlyMethod_IsJudgedByTheTable(string member, string? expected)
    {
        Judge(member, expected);
    }

    private void Judge(string member, string? expected)
    {
        var model = BuildModel("", HostOpen + member + "\n}", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var reported = findings.ToImmutableArray()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))
            .ToList();

        if(expected is null)
        {
            Assert.Empty(reported);
            return;
        }

        var finding = Assert.Single(reported);
        Assert.Equal(expected, finding.Id.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }
}