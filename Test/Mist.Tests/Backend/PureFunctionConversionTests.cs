// Tier 2 (Functional): Requirement 16 -- a lambda converts to a pure function type only when it keeps
// Requirements 6 and 7 judged where it converts, a captured variable is readable only when nothing can
// assign it after the capture, a method group only when its method is pure, and a pure function value is
// callable from a pure scope where a plain one is not. The purity rule judges each hand-built body.

using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class PureFunctionConversionTests
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string FunctionRequired = "MIST_PA_PURE_FUNCTION_REQUIRED";

    [Theory]
    [InlineData("lambda-adding-one-to-its-parameter")]
    [InlineData("lambda-reading-a-readonly-static")]
    [InlineData("pure-lambda-adding-one-to-its-parameter")]
    [InlineData("lambda-writing-an-array-it-allocated")]
    [InlineData("lambda-passed-to-a-pure-function-parameter")]
    public void ALambdaThatKeepsPurity_ConvertsToAPureFunctionType(string statement)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(statement));
    }

    [Theory]
    [InlineData("lambda-writing-a-static")]
    [InlineData("pure-lambda-writing-a-static")]
    [InlineData("lambda-passed-to-a-pure-function-parameter-writing-a-static")]
    public void ALambdaThatWrites_IsRefusedAtTheOperationInItsBody(string statement)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(statement));

        Assert.Equal(Writes, finding.Number.Value);
        Assert.Contains("lambda", finding.Message, StringComparison.Ordinal);
        Assert.Equal(38, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData("lambda-reading-a-mutable-static")]
    [InlineData("pure-lambda-reading-a-mutable-static")]
    public void ALambdaThatReadsAMutableStatic_IsRefused(string statement)
    {
        Assert.Equal(ReadsMutable, Assert.Single(new PuritySpecFragments().Analyze(statement)).Number.Value);
    }

    [Theory]
    [InlineData("lambda-capturing-a-readonly-local")]
    [InlineData("lambda-capturing-a-local-nothing-reassigns")]
    [InlineData("pure-lambda-capturing-a-readonly-local")]
    public void ALambdaCapturingALocalNothingReassigns_IsAdmitted(string statement)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(statement));
    }

    [Theory]
    [InlineData("lambda-capturing-a-local-reassigned-before-the-capture")]
    [InlineData("lambda-capturing-a-local-reassigned-after-the-capture")]
    public void ALambdaCapturingAReassignedLocal_IsRefusedAsAMutableRead(string statement)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(statement));

        Assert.Equal(ReadsMutable, finding.Number.Value);
        Assert.Contains("'k'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("method-group-of-a-declared-pure-method")]
    [InlineData("method-group-of-an-inferred-pure-method")]
    [InlineData("method-group-passed-to-a-pure-function-parameter")]
    public void AMethodGroupOfAPureMethod_ConvertsToAPureFunctionType(string statement)
    {
        Assert.Empty(new PuritySpecFragments().Analyze(statement));
    }

    [Theory]
    [InlineData("method-group-of-an-impure-method")]
    [InlineData("method-group-of-an-impure-method-passed-to-a-pure-function-parameter")]
    public void AMethodGroupOfAnImpureMethod_IsRefused(string statement)
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze(statement));

        Assert.Equal(FunctionRequired, finding.Number.Value);
        Assert.Contains("Bump", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureScopeMayInvokeAPureFunctionValueItWasGiven()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("pure-method-invoking-a-pure-function-value"));
    }

    [Fact]
    public void APureScopeMayNotInvokeAPlainFunctionValue()
    {
        var finding = Assert.Single(new PuritySpecFragments().Analyze("pure-method-invoking-a-plain-function-value"));

        Assert.Equal(CallsImpure, finding.Number.Value);
        Assert.Contains("'Apply'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureFunctionValueIsCallableFromALocalReadonlyScopeToo()
    {
        Assert.Empty(new PuritySpecFragments().Analyze("local-readonly-method-invoking-a-pure-function-value"));
    }
}