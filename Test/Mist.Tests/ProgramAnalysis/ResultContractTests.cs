using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ResultContractTests
{
    private readonly ResultModel shapes = new();

    [Fact]
    public void PropagationNeedsTheNearestCallablesResultDestination()
    {
        Assert.Same(ProgramAnalysisDiagnostics.ResultPropagationDestination, ResultAnalyzer.JudgePropagation(shapes.Text, null, shapes.Built.Interner));
        Assert.Same(ProgramAnalysisDiagnostics.ResultPropagationDestination, ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Void, shapes.Built.Interner));
    }

    [Fact]
    public void ErrorUnionsAdmitConstituentsAndIncompatibleErrorsRequireMapping()
    {
        var union = shapes.Built.Interner.GetOrCreateUnionType([shapes.Text, shapes.Boolean]);
        Assert.Null(ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Result(shapes.Integer, union), shapes.Built.Interner));
        Assert.Same(ProgramAnalysisDiagnostics.ResultPropagationError, ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Result(shapes.Integer, shapes.Boolean), shapes.Built.Interner));
    }

    [Fact]
    public void AnOrdinaryErrorOrErrorUnionCanBeTheReturnDestination()
    {
        var union = shapes.Built.Interner.GetOrCreateUnionType([shapes.Text, shapes.Boolean]);
        Assert.Null(ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Text, shapes.Built.Interner));
        Assert.Null(ResultAnalyzer.JudgePropagation(shapes.Text, union, shapes.Built.Interner));
        Assert.Same(ProgramAnalysisDiagnostics.ResultPropagationError, ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Integer, shapes.Built.Interner));
    }

    [Fact]
    public void PropagationExplicitlySelectsFailureEvenWhenTheDestinationPayloadsOverlap()
    {
        Assert.Null(ResultAnalyzer.JudgePropagation(shapes.Text, shapes.Result(shapes.Text, shapes.Text), shapes.Built.Interner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATagPatternAdmitsOnlyTheCorrespondingPayload(bool success)
    {
        Assert.True(TupleCorrelation.MayMatch(shapes.Tag(success), success ? shapes.True : shapes.False));
        Assert.False(TupleCorrelation.MayMatch(shapes.Tag(success), success ? shapes.False : shapes.True));
    }
}