// Tier 3 (Component): Requirement 8's inference, observed the only way it can be -- through a DECLARED
// pure caller, since an inferred promise is never reported on the member that keeps it (Requirement 3).
// Every callee here declares nothing; what it keeps is read off its body, its implementations, or its
// storage, as .kiro/specs/purity/design.md's PurityInference section states.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PurityInferenceTests : ProgramModelProbe
{
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";

    private static readonly string[] Ids =
    [
        "MIST_PA_PURE_SCOPE_WRITES", "MIST_PA_PURE_SCOPE_READS_MUTABLE", CallsImpure,
        "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED",
        "MIST_PA_PURE_FUNCTION_REQUIRED", "MIST_PA_DEFAULT_NOT_CONSISTENT",
    ];

    public PurityInferenceTests()
        : base("MistPurityInference_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
        }

        public class Cell
        {
            public int Fixed { get; } = 3;

            public int Level { get; set; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public static int Helper() { return Globals.Limit + 1; }")]
    [InlineData("public static int Helper() { var xs = new int[2]; xs[0] = 1; return xs[0]; }")]
    [InlineData("public static int Helper() { return Helper2(); } public static int Helper2() { return 1; }")]
    public void AHelperWhoseBodyKeepsPurity_IsCallableFromADeclaredPureMember(string helper)
    {
        Assert.Empty(Analyse($"public class Host {{ public static pure int Caller() {{ return Helper(); }} {helper} }}"));
    }

    [Theory]
    [InlineData("public static int Helper() { return Globals.Counter; }")]
    [InlineData("public static int Helper() { Globals.Counter = 1; return 1; }")]
    [InlineData("public static int Helper() { return Helper2(); } public static int Helper2() { return Globals.Counter; }")]
    public void AHelperWhoseBodyBreaksPurity_IsRefusedAtTheCallerNamingIt(string helper)
    {
        var finding = Assert.Single(Analyse($"public class Host {{ public static pure int Caller() {{ return Helper(); }} {helper} }}"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("Helper", finding.Message, StringComparison.Ordinal);
        Assert.Contains("'Caller'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MutuallyRecursiveUndeclaredMembers_KeepPurityAsAGreatestFixpoint()
    {
        Assert.Empty(Analyse("""
            public class Host
            {
                public static pure int Caller() { return Ping(3); }

                public static int Ping(int n) { if (n == 0) { return 0; } return Pong(n - 1); }

                public static int Pong(int n) { return Ping(n); }
            }
            """));
    }

    [Fact]
    public void ASelfRecursiveUndeclaredMember_KeepsPurity()
    {
        Assert.Empty(Analyse("""
            public class Host
            {
                public static pure int Caller() { return Fact(3); }

                public static int Fact(int n) { if (n <= 1) { return 1; } return n * Fact(n - 1); }
            }
            """));
    }

    [Fact]
    public void AnAbstractMemberWithAnImpureImplementation_IsRefusedThroughTheAbstract()
    {
        var finding = Assert.Single(Analyse("""
            public abstract Shape { int Area(); }

            public class Square : Shape { public int Area() { return 4; } }

            public class Noisy : Shape { public int Area() { Globals.Counter = 1; return 1; } }

            public class Host { public static pure int Caller(Shape s) { return s.Area(); } }
            """));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("Area", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractMemberWhoseEveryImplementationIsPure_IsAdmittedThroughTheAbstract()
    {
        Assert.Empty(Analyse("""
            public abstract Shape { int Area(); }

            public class Square : Shape { public int Area() { return 4; } }

            public class Round : Shape { public int Area() { return Globals.Limit; } }

            public class Host { public static pure int Caller(Shape s) { return s.Area(); } }
            """));
    }

    [Fact]
    public void AnAbstractMemberWithNoImplementationInTheProgram_KeepsEveryPromise()
    {
        Assert.Empty(Analyse("""
            public abstract Lone { int Value(); }

            public class Host { public static pure int Caller(Lone l) { return l.Value(); } }
            """));
    }

    [Fact]
    public void AnAbstractMemberDeclaredPure_IsAdmittedWhateverItsImplementationsDeclare()
    {
        Assert.Empty(Analyse("""
            public abstract Shape { pure int Area(); }

            public class Square : Shape { public pure int Area() { return 4; } }

            public class Host { public static pure int Caller(Shape s) { return s.Area(); } }
            """));
    }

    [Theory]
    [InlineData("public static pure int Caller(Cell c) { return c.Fixed; }")]
    [InlineData("public static pure int Caller(frozen Cell c) { return c.Level; }")]
    [InlineData("public static pure int Caller() { var c = new Cell(); return c.Level; }")]
    [InlineData("public static pure int Caller() { owned var c = new Cell(); c.Level = 2; return c.Level; }")]
    public void AnAutoGetter_IsPureOverStableStorageAndLocalReadonlyOverMutable(string caller)
    {
        Assert.Empty(Analyse($"public class Host {{ {caller} }}"));
    }

    [Fact]
    public void AnAutoGetterOverMutableStorage_IsRefusedOnAnExternalReceiver()
    {
        var finding = Assert.Single(Analyse("public class Host { public static pure int Caller(Cell c) { return c.Level; } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("Level", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AGetterWithABodyOverStableFields_IsInferredPure()
    {
        Assert.Empty(Analyse("""
            public class Jar
            {
                public readonly int A;

                public pure Jar(int a) { A = a; }

                public int Twice { get { return A * 2; } }
            }

            public class Host { public static pure int Caller(Jar j) { return j.Twice; } }
            """));
    }

    [Fact]
    public void AnUndeclaredConstructorThatOnlyAssignsItsOwnFields_IsInferredPure()
    {
        Assert.Empty(Analyse("""
            public class Point
            {
                public int X;

                public Point(int x) { X = x; }
            }

            public class Host { public static pure int Caller() { var p = new Point(1); return p.X; } }
            """));
    }

    [Fact]
    public void AnUndeclaredConstructorThatWritesAStatic_IsRefusedAtTheAllocation()
    {
        var finding = Assert.Single(Analyse("""
            public class Point
            {
                public int X;

                public Point(int x) { X = x; Globals.Counter = x; }
            }

            public class Host { public static pure int Caller() { var p = new Point(1); return p.X; } }
            """));

        Assert.Equal(CallsImpure, finding.Id.Value);
    }

    private List<Diagnostic> Analyse(string extraTypes)
    {
        var model = BuildModel("", extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}