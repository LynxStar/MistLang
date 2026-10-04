// Tier 3 (Component): switch shapes ExhaustivenessAnalyzer deliberately leaves unreported. A tuple
// may need range reasoning to prove coverage, guards are not evaluated, and enum variants form a
// closed domain. The analyzer reports only missing cases it proves; switch severity remains MIST-106
// in backlog/issues.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ExhaustivenessDecisionTests : ProgramModelProbe
{
    private const string NotExhaustive = "MIST_PA_SWITCH_NOT_EXHAUSTIVE";

    public ExhaustivenessDecisionTests()
        : base("MistExhaustiveness_")
    {
    }

    protected override string Fixtures { get; } = """
        public enum Color {
            Red,
            Green,
            Blue
        }
        """;

    protected override string Preamble { get; } = """
        int x = 1;
        int y = 2;
        bool flag = true;
        unknown u = 1;
        """;

    [Fact]
    public void ATupleOverRelationalArms_IsNotJudged()
    {
        AssertSilent(
            "var q = (x, y) switch { (0, 0) => 0, (> 0, > 0) => 1, (< 0, > 0) => 2, (< 0, < 0) => 3, "
            + "(> 0, < 0) => 4, (0, _) => 5, (_, 0) => 6 };");
    }

    [Fact]
    public void RelationalArmsOverAnInt_AreNotJudged()
    {
        AssertSilent("var sign = x switch { < 0 => -1, 0 => 0, > 0 => 1 };");
    }

    [Fact]
    public void TypeTestsOverAnUnknown_AreNotJudged()
    {
        AssertSilent("var kind = u switch { int n => 1, string s => 2 };");
    }

    [Theory]
    [InlineData("var a = x switch { var n => n };")]
    [InlineData("var b = x switch { _ => 0 };")]
    [InlineData("var c = x switch { int n => n };")]
    [InlineData("var d = flag switch { true or false => 1 };")]
    [InlineData("var e = flag switch { true => 1, false => 0 };")]
    public void AnArmSetThatCoversEverything_IsExhaustive(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void AUnionWithEveryConstituentTested_IsExhaustive()
    {
        AssertSilent("int | string m = 1; var r = m switch { int n => 1, string s => 2 };");
    }

    [Fact]
    public void AConstituentReachedOnlyThroughAGuard_IsNotReported()
    {
        AssertSilent("int | string m = 1; var r = m switch { int n when n > 0 => 1, string s => 2 };");
    }

    [Fact]
    public void AnEnumWithEveryVariantMatched_IsExhaustive()
    {
        AssertSilent(
            "Color c = Color.Red; var n = c switch { Color.Red => 1, Color.Green => 2, Color.Blue => 3 };");
    }

    [Theory]
    [InlineData("var z = neverDeclared switch { 1 => 2 };")]
    [InlineData("var z = neverDeclared switch { int n when n > 0 => 2 };")]
    public void AScrutineeThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    [Fact]
    public void AFindingDeepInsideAnExpressionIsReached()
    {
        var reported = Analyse(
            "var f = (bool b) => $\"{(b switch { true => x switch { var n => (flag switch { true => n }) }, false => 0 })}\";");

        _ = Assert.Single(reported);
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

        return [.. findings.ToImmutableArray().Where(d => d.Id.Value == NotExhaustive)];
    }
}