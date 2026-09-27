// Tier 3 (Component): purity Requirement 12.1 and defaultability Requirement 6 -- a field initializer of a
// DEFAULTABLE type and a default-block entry are pure scopes with no inputs, and a type with no default
// (an argument-taking constructor and no block) leaves its initializers as ordinary construction-time
// code. The fixture types are under analysis in every row, so Globals' static initializers are silences.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DefaultConsistencyTests : ProgramModelProbe
{
    private const string NotConsistent = "MIST_PA_DEFAULT_NOT_CONSISTENT";

    private static readonly string[] Ids =
    [
        NotConsistent, "MIST_PA_PURE_SCOPE_WRITES", "MIST_PA_PURE_SCOPE_READS_MUTABLE", "MIST_PA_PURE_SCOPE_CALLS_IMPURE",
        "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED", "MIST_PA_PURE_FUNCTION_REQUIRED",
    ];

    public DefaultConsistencyTests()
        : base("MistDefaultConsistency_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
        }

        public class Bag
        {
            public int Count;

            public pure Bag() { }
        }

        public pure class Vec
        {
            public readonly int X;

            public pure Vec(int x) { X = x; }
        }

        public class Loud
        {
            public int V;

            public Loud() { Globals.Counter = 1; }
        }

        public class Quiet
        {
            public int V;

            public Quiet(int v) { V = v; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Box { public Vec V = new Vec(7); }")]
    [InlineData("public class Box { public Bag B = new Bag(); }")]
    [InlineData("public class Box { public Quiet Q = new Quiet(7); }")]
    [InlineData("public class Box { public int N = Globals.Limit; }")]
    [InlineData("public class Box { public int N = 1 + 2; }")]
    [InlineData("public class Box { public int[] Xs = [1, 2]; }")]
    [InlineData("public struct Box { public int N = Globals.Limit; }")]
    [InlineData("public class Box { public int N; public Box(int n) { N = n; } default { N = Globals.Limit } }")]
    [InlineData("public class Box { public Vec V; public Box(Vec v) { V = v; } default { V = new Vec(7) } }")]
    public void AConsistentInitializerOrEntry_IsAdmitted(string declaration)
    {
        Assert.Empty(Analyse(declaration));
    }

    [Theory]
    [InlineData("public class Box { public int N = Globals.Counter; }", "N")]
    [InlineData("public class Box { public Loud L = new Loud(); }", "L")]
    [InlineData("public struct Box { public int N = Globals.Counter; }", "N")]
    [InlineData("public class Box { public int N { get; set; } = Globals.Counter; }", "N")]
    [InlineData("public class Box { public int N; public Box(int n) { N = n; } default { N = Globals.Counter } }", "N")]
    [InlineData("public class Box { public Loud L; public Box(Loud l) { L = l; } default { L = new Loud() } }", "L")]
    public void AnInconsistentInitializerOrEntry_IsRefusedNamingTheFieldAndTheType(string declaration, string field)
    {
        var finding = Assert.Single(Analyse(declaration));

        Assert.Equal(NotConsistent, finding.Id.Value);
        Assert.Contains($"'{field}'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("Box", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Box { public int N = Globals.Counter; public Box(int n) { N = n; } }")]
    [InlineData("public class Box { public Loud L = new Loud(); public Box(int n) { } }")]
    [InlineData("public struct Box { public int N = Globals.Counter; public Box(int n) { N = n; } }")]
    [InlineData("public class Box { public static int N = Globals.Counter; }")]
    public void ATypeWithNoDefault_LeavesItsInitializersUnconstrained(string declaration)
    {
        Assert.Empty(Analyse(declaration));
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