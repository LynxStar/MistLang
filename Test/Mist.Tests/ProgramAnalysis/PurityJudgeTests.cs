// Tier 2 (Functional): every row of the purity design's judge table, once per scope kind, over a hand-built Host
// world whose Bag, Vec, Holder, Loud, Res and Globals are consulted only as callees. WHY each row is admitted or
// refused is the table in specs/purity/design.md, and the ids are its findings list.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PurityJudgeTests
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string ReadsArgument = "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";

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
    [InlineData("public local void M(Bag other) { Mine = other; }", StoresUnowned)]
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

    private static void Judge(string member, string? expected)
    {
        var findings = new PurityJudgeFragments().Analyze(member);

        if(expected is null)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.True(finding.Number.Value == expected, $"{member}: expected {expected}, got {finding.Number.Value}");
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }
}