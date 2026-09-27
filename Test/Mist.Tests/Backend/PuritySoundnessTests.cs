// Tier 3 (Component): the silences the purity review found in the judge, the inference and the readonly
// amendment, each pinned by the probe that found it. Every fixture type is under analysis in every row,
// so an unmarked setter that writes a static, a linear Drop that writes one and a Clone that returns
// this are silences the rows prove -- until a scope that declares a promise reaches them.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PuritySoundnessTests : ProgramModelProbe
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string StoresUnowned = "MIST_PA_PURE_SCOPE_STORES_UNOWNED";
    private const string FunctionRequired = "MIST_PA_PURE_FUNCTION_REQUIRED";
    private const string SetsProperty = "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY";
    private const string PureScope = "MIST_PA_PURE_SCOPE_";

    private static readonly string[] PurityIds =
    [
        Writes, ReadsMutable, CallsImpure, StoresUnowned, FunctionRequired,
        "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_DEFAULT_NOT_CONSISTENT",
    ];

    private static readonly string[] ReadonlyIds =
    [
        SetsProperty, "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE",
        "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER",
        "MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK", "MIST_PA_READONLY_FIELD_ASSIGNED",
        "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE", "MIST_PA_READONLY_PARAMETER_SETS_PROPERTY",
        "MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER",
    ];

    private const string HostOpen = """
        public class Host
        {
            public owned Bag Mine;
            public Bag Shared;

            public Host()
            {
                Mine = new Bag();
                Shared = new Bag();
            }

        """;

    private const string Wrap = "public struct Wrap { public owned Bag Inner; }";

    private const string Handle = """
        public linear class Handle
        {
            public int Fd;

            public Handle(int fd) { Fd = fd; }

            public void Drop() { Globals.Counter = 1; }
        }
        """;

    private const string LevelExtension = """
        extension(Bag self)
        {
            public int Level
            {
                get { return self.Count; }
                set { Globals.Counter = value; }
            }
        }
        """;

    public PuritySoundnessTests()
        : base("MistPuritySoundness_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
        }

        public class Clock
        {
            public static int Ticks;
        }

        public class Bag
        {
            public int Count;

            public pure Bag() { }
        }

        public struct Pt
        {
            public int X;

            public Pt(int x) { X = x; }
        }

        public class Twin
        {
            public int N;

            public Twin Clone() { return this; }
        }

        public class Cold
        {
            public int Count;

            public pure int Peek() with frozen { return Count; }
        }

        public class Loud
        {
            public int lvl;

            public int Level
            {
                get { return lvl; }
                set { lvl = value; Globals.Counter = 1; }
            }
        }

        public class LoudGrid
        {
            public int cell;

            public int this[int i]
            {
                get { return cell; }
                set { cell = value; Globals.Counter = 1; }
            }
        }

        public class Cell
        {
            public int Level { get; set; }
        }

        public class Plain
        {
            public int V;
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AnOwnedFieldBehindAStructCopy_IsNotTheCopys()
    {
        var finding = Assert.Single(AnalyseMember("public pure int M(Wrap w) { w.Inner.Count = 1; return 1; }", Wrap, PurityIds));

        Assert.Equal(Writes, finding.Id.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACloneThatReturnsThis_DoesNotMakeItsResultOwned()
    {
        var finding = SinglePureScopeFinding(AnalyseMember("public pure int M(Twin t) { var c = t.Clone(); c.N = 1; return 1; }", "", PurityIds));

        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldInitializer_IsJudgedUnderAPureConstructor()
    {
        var finding = SinglePureScopeFinding(Analyse("", "public class Box { public int N = Clock.Ticks; public pure Box(int k) { } }", PurityIds));

        Assert.Contains("Box", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmarkedConstructorWhoseInitializerReadsAStatic_IsImpureToItsCaller()
    {
        var finding = Assert.Single(Analyse(
            "",
            "public class Box { public int N = Clock.Ticks; public Box(int k) { } } "
            + "public class Maker { public static pure int Make() { var b = new Box(1); return b.N; } }",
            PurityIds));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'Make'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteLaunderedThroughNestedLambdas_IsRefusedWhereThePureScopeInvokesTheOuter()
    {
        var finding = SinglePureScopeFinding(AnalyseMember(
            "public pure int M(Twin t) { func<void> inner = () => { t.N = 1; }; func<void> outer = () => { inner(); }; outer(); return 1; }",
            "",
            PurityIds));

        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureLocalFunction_TreatsItsCapturesAsInputs()
    {
        var finding = Assert.Single(AnalyseMember("public int M() { var b = new Bag(); pure int F() { return b.Count; } return F(); }", "", PurityIds));

        Assert.Equal(ReadsMutable, finding.Id.Value);
        Assert.Contains("'F'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalFunctionReadingACapturedMutableObject_DoesNotConvertToAPureFunctionType()
    {
        var finding = Assert.Single(AnalyseMember("public int M() { var b = new Bag(); int F() { return b.Count; } pure func<int> g = F; return g(); }", "", PurityIds));

        Assert.Contains(finding.Id.Value, new[] { FunctionRequired, ReadsMutable });
    }

    [Fact]
    public void AMethodGroupOverAWithFrozenMethod_DoesNotConvertToAPureFunctionType()
    {
        var finding = Assert.Single(AnalyseMember("public int M() { owned var c = new Cold(); pure func<int> f = c.Peek; return f(); }", "", PurityIds));

        Assert.Equal(FunctionRequired, finding.Id.Value);
        Assert.Contains("Peek", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var p = new Pt(1); pure func<int> f = () => p.X; p.X = 5;")]
    [InlineData("var p = new Pt(1); p.X = 5; pure func<int> f = () => p.X;")]
    public void APureLambdaCapturingAStructWhoseFieldIsAssigned_IsRefused(string statement)
    {
        Assert.Equal(ReadsMutable, Assert.Single(Analyse(statement, "", PurityIds)).Id.Value);
    }

    [Fact]
    public void APureLambdaCapturingAStructNothingAssigns_IsAdmitted()
    {
        Assert.Empty(Analyse("var p = new Pt(1); pure func<int> f = () => p.X;", "", PurityIds));
    }

    [Fact]
    public void ALinearValueWithAnImpureDrop_IsRefusedInAPureScope()
    {
        var finding = Assert.Single(AnalyseMember("public pure int M() { var h = new Handle(1); return h.Fd; }", Handle, PurityIds));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtensionSetterThatWritesAStatic_IsRefusedInAPureScope()
    {
        var finding = SinglePureScopeFinding(AnalyseMember("public pure int M() { var b = new Bag(); b.Level = 1; return 1; }", LevelExtension, PurityIds));

        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public readonly int Build() { var l = new Loud(); l.Level = 2; return l.Level; }")]
    [InlineData("public readonly int Build() { var g = new LoudGrid(); g[0] = 1; return g[0]; }")]
    public void AReadonlyMember_MayNotCallASideEffectingSetterOnWhatItOwns(string member)
    {
        Assert.Equal(SetsProperty, Assert.Single(AnalyseMember(member, "", ReadonlyIds)).Id.Value);
    }

    [Fact]
    public void AReadonlyMember_MayStillCallAnAutoSetterOnWhatItOwns()
    {
        Assert.Empty(AnalyseMember("public readonly int Build() { var c = new Cell(); c.Level = 2; return c.Level; }", "", ReadonlyIds));
    }

    [Fact]
    public void ALocalMember_MayNotStoreAnArgumentIntoAnOwnedField()
    {
        var finding = Assert.Single(AnalyseMember("public local void M(Bag other) { Mine = other; }", "", PurityIds));

        Assert.Equal(StoresUnowned, finding.Id.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalMember_MayStoreAnArgumentIntoAnUnownedField()
    {
        Assert.Empty(AnalyseMember("public local void M(Bag other) { Shared = other; }", "", PurityIds));
    }

    [Fact]
    public void AnAllocationThroughAnImplicitConstructor_KeepsTheAllocatingMemberPure()
    {
        Assert.Empty(Analyse(
            "",
            "public class Maker { public static int ViaImplicit() { var p = new Plain(); return p.V; } public static pure int Use() { return ViaImplicit(); } }",
            PurityIds));
    }

    [Fact]
    public void AReadonlyBlockInsideAPureBody_IsReportedOnce()
    {
        var finding = Assert.Single(AnalyseMember("public pure int M() { readonly { Globals.Counter = 1; } return 0; }", "", [.. PurityIds, .. ReadonlyIds]));

        Assert.Equal(Writes, finding.Id.Value);
    }

    private static Diagnostic SinglePureScopeFinding(List<Diagnostic> findings)
    {
        var finding = Assert.Single(findings);
        Assert.StartsWith(PureScope, finding.Id.Value, StringComparison.Ordinal);
        return finding;
    }

    private List<Diagnostic> AnalyseMember(string member, string extraTypes, string[] ids)
    {
        return Analyse("", HostOpen + member + "\n}\n" + extraTypes, ids);
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes, string[] ids)
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && ids.Contains(diagnostic.Id.Value))];
    }
}