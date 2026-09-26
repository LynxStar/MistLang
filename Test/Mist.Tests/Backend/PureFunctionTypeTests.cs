using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class PureFunctionTypeTests : ProgramModelProbe
{
    private const string Ops = "public class Ops { public static int Inc(int n) { return n + 1; } }";

    public PureFunctionTypeTests()
        : base("MistPureFunctionType_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void ProgramModel_ALambdaBoundAgainstAPureFunctionTypeHasThatType()
    {
        var lambda = InitializerOf<LambdaExpression>("pure func<int, int> f = n => n + 1;");

        var type = Assert.IsAssignableFrom<IFunctionType>(lambda.Type);
        Assert.True(type.IsPure);
        Assert.StartsWith("pure func<", type.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModel_APureLambdaHasAPureFunctionTypeWithoutATarget()
    {
        var lambda = InitializerOf<LambdaExpression>("var g = pure (int n) => n + 1;");

        Assert.True(lambda.IsPure);
        var type = Assert.IsAssignableFrom<IFunctionType>(lambda.Type);
        Assert.True(type.IsPure);
        Assert.StartsWith("pure func<", type.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModel_ALocalLambdaIsRecordedAsLocalAndItsTypeIsNotPure()
    {
        var lambda = InitializerOf<LambdaExpression>("var l = local (int a) => a;");

        Assert.True(lambda.IsLocal);
        Assert.False(lambda.IsPure);
        Assert.False(Assert.IsAssignableFrom<IFunctionType>(lambda.Type).IsPure);
    }

    [Fact]
    public void ProgramModelToAnalysis_APureFunctionConvertsToThePlainFunctionType()
    {
        Assert.Empty(ErrorsOf("func<int, int> h = f;", extraPreamble: "pure func<int, int> f = n => n + 1;"));
    }

    [Fact]
    public void ProgramModelToAnalysis_APlainFunctionDoesNotConvertToAPureFunctionType()
    {
        var error = Assert.Single(ErrorsOf("pure func<int, int> k = h;", extraPreamble: "func<int, int> h = n => n + 1;"));

        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", error.Id.Value);
        Assert.Contains("pure func<", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModelToAnalysis_ACastToAPureFunctionTypeIsRefused()
    {
        var error = Assert.Single(ErrorsOf("var k = (pure func<int, int>)h;", extraPreamble: "func<int, int> h = n => n + 1;"));

        Assert.Equal("MIST_PA_CAST_HAS_NO_CONVERSION", error.Id.Value);
        Assert.Contains("pure func<", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModel_ThePureFunctionTypeIsInternedApartFromThePlainOne()
    {
        var locals = BodyIn("pure func<int, int> a = n => n; pure func<int, int> b = n => n; func<int, int> c = n => n;")
            .Statements.OfType<LocalDeclarationStatement>().Select(statement => statement.Local).ToArray();

        Assert.Equal(3, locals.Length);
        Assert.Same(locals[0].Type, locals[1].Type);
        Assert.NotSame(locals[0].Type, locals[2].Type);
        Assert.Equal("pure " + locals[2].Type.Name, locals[0].Type.Name);
    }

    [Fact]
    public void ProgramModelToAnalysis_OverloadsDifferingOnlyInPurityAreDuplicateSignatures()
    {
        var error = Assert.Single(ErrorsOf("", "public class Twice { public static int A(pure func<int, int> f) { return 0; } public static int A(func<int, int> f) { return 0; } }"));

        Assert.Equal("MIST_PA_DUPLICATE_SIGNATURE", error.Id.Value);
    }

    [Fact]
    public void ProgramModel_AMethodGroupConvertsToAPureFunctionType()
    {
        var reference = InitializerOf<FunctionReferenceExpression>("pure func<int, int> m = Ops.Inc;", Ops);

        Assert.Equal("Inc", reference.Method.Name);
        Assert.True(Assert.IsAssignableFrom<IFunctionType>(reference.Type).IsPure);
    }

    [Fact]
    public void ProgramModel_AnAliasOfAPureFunctionTypeNamesThePureType()
    {
        var local = SingleLocal("Step s = n => n;", "type Step = pure func<int, int>;");

        var type = Assert.IsAssignableFrom<IFunctionType>(local.Type);
        Assert.True(type.IsPure);
        Assert.StartsWith("pure func<", type.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModel_PureBeforeAnAliasOfAPlainFunctionTypeMakesItPure()
    {
        var local = SingleLocal("pure Plain t = n => n;", "type Plain = func<int, int>;");

        var type = Assert.IsAssignableFrom<IFunctionType>(local.Type);
        Assert.True(type.IsPure);
        Assert.StartsWith("pure func<", type.Name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure Counter c = new Counter();", "Counter")]
    [InlineData("pure int n = 1;", "int")]
    [InlineData("var xs = new pure int[2];", "int")]
    public void ProgramModel_PureBeforeATypeThatIsNotAFunctionTypeIsRefused(string statement, string named)
    {
        var diagnostics = BuildRun(statement, "public class Counter { public Counter() { } }");

        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MIST_PM_PURE_REQUIRES_FUNCTION_TYPE", error.Id.Value);
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pure func<int, int> f = local n => n + 1;")]
    [InlineData("var g = pure local (int n) => n + 1;")]
    public void ProgramModel_ALocalLambdaWhereAPureOneIsNeededIsTheWrittenConflict(string statement)
    {
        var error = Assert.Single(BuildRun(statement), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MIST_PM_PURE_AND_LOCAL", error.Id.Value);
    }

    private Diagnostic[] ErrorsOf(string statement, string extraTypes = "", string extraPreamble = "")
    {
        var model = BuildModel(statement, extraTypes, extraPreamble, out var diagnostics);

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return diagnostics.Concat(findings.ToImmutableArray())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }
}