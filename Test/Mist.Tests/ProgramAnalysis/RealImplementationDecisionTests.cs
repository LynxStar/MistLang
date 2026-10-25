// Tier 3: an implementation accepts every real value promised by its abstract slot, including NaN where admitted.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealImplementationDecisionTests
{
    [Theory]
    [InlineData("double", ">= 0.0 and <= 1.0", ">= 0.0 and <= 1.0")]
    [InlineData("double", ">= 0.0 and <= 1.0", ">= 0.0")]
    [InlineData("double", "> 0.0", ">= 0.0")]
    [InlineData("double", "> 0.0", "")]
    [InlineData("double", "", "")]
    [InlineData("double", "<= 0.0", "not > 0.0")]
    [InlineData("double", "not > 0.0", "not > 0.0")]
    [InlineData("float", ">= 0.0f and <= 1.0f", ">= 0.0f and <= 1.0f")]
    [InlineData("float", ">= 0.0f and <= 1.0f", ">= 0.0f")]
    [InlineData("float", "> 0.0f", "")]
    [InlineData("float", "<= 0.1", "<= 0.1f")]
    [InlineData("decimal", ">= 0.0m and <= 1.0m", ">= 0.0m and <= 1.0m")]
    [InlineData("decimal", "> 0.0m and < 1.0m", ">= 0.0m and <= 1.0m")]
    [InlineData("decimal", "> 0.0m", "")]
    [InlineData("decimal", "not > 0.0m", "<= 0.0m")]
    public void ASameLooserOrUnconstrainedRealImplementation_IsAdmitted(string type, string promised, string demanded)
    {
        Assert.Empty(Analyse(type, promised, demanded));
    }

    [Theory]
    [InlineData("double", ">= 0.0", "> 0.0")]
    [InlineData("double", ">= 0.0 and <= 1.0", ">= 0.0 and < 1.0")]
    [InlineData("double", "", ">= 0.0")]
    [InlineData("double", "not > 0.0", "<= 0.0")]
    [InlineData("double", "not <= 0.0", "> 0.0")]
    [InlineData("float", ">= 0.0f", "> 0.0f")]
    [InlineData("float", "<= 0.1f", "<= 0.1")]
    [InlineData("float", "not > 0.0f", "<= 0.0f")]
    [InlineData("float", "", ">= 0.0f and <= 1.0f")]
    [InlineData("decimal", ">= 0.0m", "> 0.0m")]
    [InlineData("decimal", ">= 0.0m and <= 1.0m", ">= 0.0m and < 1.0m")]
    [InlineData("decimal", "", ">= 0.0m")]
    public void AStricterRealImplementation_IsRefused(string type, string promised, string demanded)
    {
        Assert.Equal("MIST0283", Assert.Single(Analyse(type, promised, demanded)).Number.Value);
    }

    private static ImmutableArray<Diagnostic> Analyse(string type, string promised, string demanded)
    {
        var slotWhere = promised.Length == 0 ? "" : $"where value {promised}";
        var implementationWhere = demanded.Length == 0 ? "" : $"where value {demanded}";
        var source = $$"""
            public abstract IRealContract
            {
                {{type}} Keep({{type}} value) {{slotWhere}};
            }

            public class RealImplementation : IRealContract
            {
                public {{type}} Keep({{type}} value) {{implementationWhere}}
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