// Tier 3 (Component): what the binder records for a pure function type -- the type a lambda, a method
// group and an alias take, and the spellings it refuses -- read off the real model builder over
// Standard's shells; what the rules make of the type is PureFunctionTypeDecisionTests' and
// PureFunctionTypeRefusalTests' claim.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
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
}