using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealImplementationDecisionTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Double", "closed unit", "closed unit")]
    [InlineData("Double", "closed unit", "nonnegative")]
    [InlineData("Double", "positive", "nonnegative")]
    [InlineData("Double", "positive", "")]
    [InlineData("Double", "", "")]
    [InlineData("Double", "complement of positive", "complement of positive")]
    [InlineData("Single", "closed unit", "closed unit")]
    [InlineData("Single", "closed unit", "nonnegative")]
    [InlineData("Single", "positive", "")]
    [InlineData("Single", "at most double tenth", "at most float tenth")]
    [InlineData("Decimal", "closed unit", "closed unit")]
    [InlineData("Decimal", "open unit", "closed unit")]
    [InlineData("Decimal", "positive", "")]
    public void ASameLooserOrUnconstrainedRealImplementation_IsAdmitted(string type, string promised, string demanded)
    {
        Assert.Null(Finding(type, promised, demanded));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Double", "nonnegative", "positive")]
    [InlineData("Double", "closed unit", "half-open unit")]
    [InlineData("Double", "", "nonnegative")]
    [InlineData("Double", "complement of nonpositive", "positive")]
    [InlineData("Single", "nonnegative", "positive")]
    [InlineData("Single", "complement of positive", "nonpositive")]
    [InlineData("Single", "", "closed unit")]
    [InlineData("Decimal", "nonnegative", "positive")]
    [InlineData("Decimal", "closed unit", "half-open unit")]
    [InlineData("Decimal", "", "nonnegative")]
    public void AStricterRealImplementation_IsRefused(string type, string promised, string demanded)
    {
        Assert.Equal("MIST0283", Finding(type, promised, demanded)?.Number.Value);
    }

    private static MistDiagnostic? Finding(string domain, string promised, string demanded)
    {
        var contract = new NumericContract(domain);
        return RefinementContracts.ParameterFinding(contract.TypeFor(promised), contract.TypeFor(demanded));
    }

    [Theory]
    [Trait("Tier", "4")]
    [InlineData("double", "<= 0.0", "not > 0.0", null)]
    [InlineData("double", "not > 0.0", "<= 0.0", "MIST0283")]
    [InlineData("float", "<= 0.1f", "<= 0.1", "MIST0283")]
    [InlineData("decimal", "not > 0.0m", "<= 0.0m", null)]
    public void BoundWhereClausesRetainTheirNumericDomain(string type, string promised, string demanded, string? expected)
    {
        var findings = Analyse(type, promised, demanded);
        if(expected is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(expected, Assert.Single(findings).Number.Value);
        }
    }

    private static ImmutableArray<Diagnostic> Analyse(string type, string promised, string demanded)
    {
        var source = $$"""
            public abstract IRealContract
            {
                {{type}} Keep({{type}} value) where value {{promised}};
            }

            public class RealImplementation : IRealContract
            {
                public {{type}} Keep({{type}} value) where value {{demanded}}
                {
                    return value;
                }
            }
            """;
        var model = StandardModelProbe.Build("", out var buildDiagnostics, source);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var implementation = Assert.IsType<ClassType>(model.TypesByFQN["Probe.RealImplementation"]);
        var nodes = implementation.MethodsList.SelectMany(method => BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(method.Body))).ToList();
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}