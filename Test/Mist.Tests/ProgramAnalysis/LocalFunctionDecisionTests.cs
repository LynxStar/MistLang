// Tier 3 (Component): what a local function binds to, and the shapes the local-functions spec
// deliberately admits -- a call before the declaration, recursion, captures, the definite-assignment
// admits -- with the refusals kept in refusals/. A write inside the function does not count as
// assigning the local at the call, as a write inside a lambda does not (spec Req 5.4), so the read
// after such a call is the one finding pinned here. WHY each is admitted is recorded on
// .kiro/specs/local-functions/design.md and on orders 9, 10 and 11 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class LocalFunctionDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT",
        "MIST_PA_STATIC_LOCAL_FUNCTION_CAPTURE", "MIST_PA_CAPTURED_BINDING",
    ];

    public LocalFunctionDecisionTests()
        : base("MistLocalFunction_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Counter
        {
            public int count = 1;

            public int Read()
            {
                int Peek()
                {
                    return count + this.count;
                }

                return Peek();
            }
        }
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        """;

    [Fact]
    public void ACallBindsToTheFunctionAsAMethodWithNoDeclaringType()
    {
        var body = BodyIn("int Twice(int x) { return x * 2; } var r = Twice(2);");
        var function = Assert.Single(body.Statements.OfType<LocalFunctionStatement>());
        var call = Assert.IsType<InvocationExpression>(DeclarationIn("int Twice(int x) { return x * 2; } var r = Twice(2);", "").Initializer);

        Assert.Equal("Twice", function.Function.Name);
        Assert.Null(function.Function.DeclaringType);
        _ = Assert.Single(function.Parameters);
        Assert.Equal("Twice", Assert.IsType<MethodReferenceExpression>(call.Receiver).Method.Name);
        Assert.NotNull(call.Method);
        Assert.Equal(function.Function.ParametersList[0].Type.Name, Assert.Single(call.ParameterTypes!).Name);
    }

    [Fact]
    public void AnAsyncFunctionReturnsATaskAndASyncContextAwaitsItImplicitly()
    {
        var statement = "async int Fetch() { return 1; } var t = Fetch();";
        var body = BodyIn(statement);
        var function = Assert.Single(body.Statements.OfType<LocalFunctionStatement>());

        Assert.NotNull(TaskResult.Of(function.Function.ReturnType));
        Assert.True(InitializerOf<AwaitExpression>(statement).IsImplicit);
    }

    [Fact]
    public void AnAsyncContextHoldsTheTaskItCalls()
    {
        var body = BodyIn("async int Fetch() { return 1; } async int Sum() { var t = Fetch(); return await t; } var s = Sum();");
        var sum = body.Statements.OfType<LocalFunctionStatement>().Single(function => function.Function.Name == "Sum");
        var held = sum.Body.Statements.OfType<LocalDeclarationStatement>().Single();

        _ = Assert.IsType<InvocationExpression>(held.Initializer);
        Assert.True(TaskResult.IsTask(held.Local.Type));
    }

    [Fact]
    public void TheNameConvertsToAFunctionValue()
    {
        var reference = InitializerOf<FunctionReferenceExpression>("int Twice(int x) { return x * 2; } func<int, int> f = Twice;");

        Assert.Equal("Twice", reference.Method.Name);
        Assert.Null(reference.Receiver);
    }

    [Fact]
    public void AParameterWhereIsTheParametersType()
    {
        var body = BodyIn("int Half(int x) where x > 0 { return x / 2; } var h = Half(8);");
        var function = Assert.Single(body.Statements.OfType<LocalFunctionStatement>());

        _ = Assert.IsType<RefinedType>(function.Function.ParametersList[0].Type);
    }

    [Theory]
    [InlineData("var r = Twice(2); int Twice(int x) { return x * 2; } var s = r;")]
    [InlineData("int Fact(int n) { if(n <= 1) { return 1; } return n * Fact(n - 1); } var r = Fact(3);")]
    [InlineData("bool IsEven(int n) { if(n == 0) { return true; } return IsOdd(n - 1); } bool IsOdd(int n) { if(n == 0) { return false; } return IsEven(n - 1); } var r = IsEven(2);")]
    [InlineData("var seed = 1; int Grow() { return seed + 1; } var r = Grow();")]
    [InlineData("var count = 0; void Bump() { count = count + 1; } Bump(); var r = count;")]
    [InlineData("public static int Sq(int x) { return x * x; } var r = Sq(2);")]
    [InlineData("private int Sq(int x) { return x * x; } var r = Sq(2);")]
    [InlineData("static int Sq(int x) { return x * x; } var r = Sq(2);")]
    [InlineData("int x; void Show() { var y = x; } x = 1; Show();")]
    [InlineData("int x; x = 1; Show(); void Show() { var y = x; }")]
    [InlineData("int x; void Set() { x = 1; } Set(); x = 2; var r = x;")]
    [InlineData("int x; void Show() { var y = x; } if(flag) { x = 1; } else { x = 2; } Show();")]
    [InlineData("int x; int Peek() { return x; } x = 1; func<int> f = Peek; var r = f();")]
    [InlineData("int Outer(int x) { int Inner(int y) { return y + x; } return Inner(1); } var r = Outer(1);")]
    [InlineData("func<int, int> g = v => { int L(int w) { return w * 3; } return L(v); }; var r = g(1);")]
    [InlineData("int Twice(int x) { func<int, int> d = y => y * x; return d(2); } var r = Twice(3);")]
    [InlineData("var n = 1; { int n(int q) { return q; } var r = n(2); } var s = n;")]
    [InlineData("async int Fetch() { return 1; } int Doubled() { var v = Fetch(); return v * 2; } var r = Doubled();")]
    [InlineData("var r = new Counter().Read();")]
    [InlineData("with var w = 1; int Twice(int x) { return x * w; } var r = Twice(2);")]
    public void AShapeTheSpecAdmits_IsAdmitted(string statement)
    {
        AssertAdmitted(statement);
    }

    [Fact]
    public void AnAssignmentInsideTheFunctionDoesNotCountAtTheCall()
    {
        var finding = Assert.Single(Analyse("int x; void Set() { x = 1; } Set(); var r = x;"));

        Assert.Equal("MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT", finding.Id.Value);
    }

    [Fact]
    public void AFunctionAfterAWithDeclarationBelongsToItsTail()
    {
        var body = BodyIn("var before = 0; with var w = 1; int Twice(int x) { return x * w; } var r = Twice(2);");
        var tail = Assert.IsType<WithBlockStatement>(body.Statements.Last());

        Assert.DoesNotContain(body.Statements, statement => statement is LocalFunctionStatement);
        _ = Assert.Single(tail.Body.Statements.OfType<LocalFunctionStatement>());
    }

    [Fact]
    public void AnUnassignedCaptureIsReportedOncePerPath()
    {
        _ = Assert.Single(Analyse("int x; void Show() { var y = x; } Show(); Show(); x = 1;"));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertAdmitted(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(findings.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}