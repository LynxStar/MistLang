// Tier 3 (Component): Requirement 16 -- a lambda converts to a pure function type only when it keeps
// Requirements 6 and 7 judged where it converts, a captured variable is readable only when nothing can
// assign it after the capture, a method group only when its method is pure, and a pure function value is
// callable from a pure scope where a plain one is not. Ops.Twice is under analysis in every row.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PureFunctionConversionTests : ProgramModelProbe
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string FunctionRequired = "MIST_PA_PURE_FUNCTION_REQUIRED";

    private static readonly string[] Ids =
    [
        Writes, ReadsMutable, CallsImpure, FunctionRequired,
        "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED", "MIST_PA_DEFAULT_NOT_CONSISTENT",
    ];

    public PureFunctionConversionTests()
        : base("MistPureFunctionConversion_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
            public static readonly int Limit = 3;
        }

        public class Ops
        {
            public static pure int Inc(int n) { return n + 1; }

            public static int Plain(int n) { return n + 1; }

            public static int Bump(int n) { Globals.Counter = n; return n; }

            public static pure int Twice(pure func<int, int> f, int x) { return f(f(x)); }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("pure func<int, int> f = n => n + 1;")]
    [InlineData("pure func<int, int> f = n => n + Globals.Limit;")]
    [InlineData("var g = pure (int n) => n + 1;")]
    [InlineData("pure func<int, int> f = n => { var xs = new int[1]; xs[0] = n; return xs[0]; };")]
    [InlineData("var r = Ops.Twice(n => n + 1, 3);")]
    public void ALambdaThatKeepsPurity_ConvertsToAPureFunctionType(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("pure func<int, int> f = n => { Globals.Counter = n; return n; };")]
    [InlineData("var g = pure (int n) => { Globals.Counter = n; return n; };")]
    [InlineData("var r = Ops.Twice(n => { Globals.Counter = n; return n; }, 3);")]
    public void ALambdaThatWrites_IsRefusedAtTheOperationInItsBody(string statement)
    {
        var finding = Assert.Single(Analyse(statement));

        Assert.Equal(Writes, finding.Id.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure func<int, int> f = n => n + Globals.Counter;")]
    [InlineData("var g = pure (int n) => n + Globals.Counter;")]
    public void ALambdaThatReadsAMutableStatic_IsRefused(string statement)
    {
        Assert.Equal(ReadsMutable, Assert.Single(Analyse(statement)).Id.Value);
    }

    [Theory]
    [InlineData("readonly var k = 1; pure func<int, int> f = n => n + k;")]
    [InlineData("var k = 1; pure func<int, int> f = n => n + k;")]
    [InlineData("readonly int k = 1; var g = pure (int n) => n + k;")]
    public void ALambdaCapturingALocalNothingReassigns_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("var k = 1; k = 2; pure func<int, int> f = n => n + k;")]
    [InlineData("var k = 1; pure func<int, int> f = n => n + k; k = 2;")]
    public void ALambdaCapturingAReassignedLocal_IsRefusedAsAMutableRead(string statement)
    {
        var finding = Assert.Single(Analyse(statement));

        Assert.Equal(ReadsMutable, finding.Id.Value);
        Assert.Contains("'k'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure func<int, int> m = Ops.Inc;")]
    [InlineData("pure func<int, int> m = Ops.Plain;")]
    [InlineData("var r = Ops.Twice(Ops.Inc, 3);")]
    public void AMethodGroupOfAPureMethod_ConvertsToAPureFunctionType(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("pure func<int, int> m = Ops.Bump;")]
    [InlineData("var r = Ops.Twice(Ops.Bump, 3);")]
    public void AMethodGroupOfAnImpureMethod_IsRefused(string statement)
    {
        var finding = Assert.Single(Analyse(statement));

        Assert.Equal(FunctionRequired, finding.Id.Value);
        Assert.Contains("Bump", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureScopeMayInvokeAPureFunctionValueItWasGiven()
    {
        Assert.Empty(Analyse("", "public class Host { public static pure int Twice(pure func<int, int> f, int x) { return f(f(x)); } }"));
    }

    [Fact]
    public void APureScopeMayNotInvokeAPlainFunctionValue()
    {
        var finding = Assert.Single(Analyse("", "public class Host { public static pure int Apply(func<int, int> f, int x) { return f(x); } }"));

        Assert.Equal(CallsImpure, finding.Id.Value);
        Assert.Contains("'Apply'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureFunctionValueIsCallableFromALocalReadonlyScopeToo()
    {
        Assert.Empty(Analyse("", "public class Host { public int n; public local readonly int Apply(pure func<int, int> f) { return f(n); } }"));
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes = "")
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}