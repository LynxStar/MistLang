// Tier 3: an abstract return promise survives only a supported subset proof, integral member lift or refined identity.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinedReturnContractDecisionTests
{
    [Theory]
    [InlineData("int", "> 0", "> 0")]
    [InlineData("int", ">= 0", "> 0")]
    [InlineData("int", ">= 0 and <= 100", ">= 1 and <= 9")]
    [InlineData("int", "not 0", "> 0")]
    [InlineData("double", "> 0.0", "> 0.0")]
    [InlineData("double", ">= 0.0", "> 0.0")]
    [InlineData("double", ">= 0.0 and <= 1.0", "> 0.0 and < 1.0")]
    [InlineData("double", "not > 0.0", "<= 0.0")]
    [InlineData("double", "not > 0.0", "not > 0.0")]
    [InlineData("double", "not <= 0.0", "> 0.0")]
    [InlineData("float", ">= 0.0f", "> 0.0f")]
    [InlineData("float", "not > 0.0f", "<= 0.0f")]
    [InlineData("float", "<= 0.1f", "<= 0.1")]
    [InlineData("decimal", ">= 0.0m", "> 0.0m")]
    [InlineData("decimal", "<= 0.0m", "not > 0.0m")]
    public void ASameOrStrongerDeclaredReturn_IsAdmitted(string domain, string promised, string produced)
    {
        Assert.Empty(AnalyseFactory(domain, promised, produced));
    }

    [Theory]
    [InlineData("int", "> 0", ">= 0")]
    [InlineData("int", ">= 0 and <= 9", ">= 0 and <= 10")]
    [InlineData("int", "> 0", "")]
    [InlineData("double", "> 0.0", ">= 0.0")]
    [InlineData("double", ">= 0.0 and < 1.0", ">= 0.0 and <= 1.0")]
    [InlineData("double", "<= 0.0", "not > 0.0")]
    [InlineData("double", "> 0.0", "not <= 0.0")]
    [InlineData("double", "> 0.0", "")]
    [InlineData("float", "> 0.0f", ">= 0.0f")]
    [InlineData("float", "<= 0.0f", "not > 0.0f")]
    [InlineData("float", "<= 0.1", "<= 0.1f")]
    [InlineData("float", "> 0.0f", "")]
    [InlineData("decimal", "> 0.0m", ">= 0.0m")]
    [InlineData("decimal", ">= 0.0m and < 1.0m", ">= 0.0m and <= 1.0m")]
    [InlineData("decimal", "> 0.0m", "")]
    public void AWeakerOrUnrefinedDeclaredReturn_IsRefused(string domain, string promised, string produced)
    {
        Assert.Equal("MIST_PA_REFINEMENT_RETURN_UNPROVEN", Assert.Single(AnalyseFactory(domain, promised, produced)).Id.Value);
    }

    [Theory]
    [InlineData("Letters", "Letters")]
    [InlineData("Letters", "NonEmptyLetters")]
    [InlineData("BelowLimit", "BelowLimit")]
    [InlineData("BelowLimit", "NonNegativeBelowLimit")]
    public void AnUnreadablePredicatePreservedByRefinedIdentity_IsAdmitted(string promised, string produced)
    {
        Assert.Empty(AnalyseOpaqueFactory(promised, produced));
    }

    [Theory]
    [InlineData("Letters", "SameLetters")]
    [InlineData("Letters", "Digits")]
    [InlineData("Letters", "string")]
    [InlineData("BelowLimit", "OtherBelowLimit")]
    [InlineData("BelowLimit", "int")]
    public void AnUnreadablePredicateWithoutRefinedIdentity_IsRefused(string promised, string produced)
    {
        Assert.Equal("MIST_PA_REFINEMENT_RETURN_UNPROVEN", Assert.Single(AnalyseOpaqueFactory(promised, produced)).Id.Value);
    }

    [Theory]
    [InlineData("int", "public int Make() { return 1; }")]
    [InlineData("int", "public bool Flag; public int Make() { if(Flag) { return 1; } return 2; }")]
    [InlineData("int", "public int Make() { return Keep(); } public Positive Keep() { return 1; }")]
    public void ASupportedIntegralLiftMeetsTheReturnContract(string domain, string member)
    {
        Assert.Empty(AnalyseMember(domain, member));
    }

    [Theory]
    [InlineData("int", "public int Make() { return -1; }")]
    [InlineData("int", "public bool Flag; public int Make() { if(Flag) { return 1; } return -1; }")]
    [InlineData("int", "public int Make() { int held = 1; return held; }")]
    [InlineData("int", "public int Make() { return 1 + 1; }")]
    [InlineData("double", "public double Make() { return -1.0; }")]
    [InlineData("double", "public double Make() { return 1.0; }")]
    [InlineData("float", "public float Make() { return 1.0f; }")]
    [InlineData("decimal", "public decimal Make() { return 1.0m; }")]
    public void AnInsufficientOrUnsupportedBodyLift_IsRefused(string domain, string member)
    {
        Assert.Equal("MIST_PA_REFINEMENT_RETURN_UNPROVEN", Assert.Single(AnalyseMember(domain, member)).Id.Value);
    }

    private static ImmutableArray<Diagnostic> AnalyseFactory(string domain, string promised, string produced)
    {
        var producedDeclaration = produced.Length == 0 ? "" : $"type Produced = {domain} {produced};";
        var producedType = produced.Length == 0 ? domain : "Produced";
        return Analyse($$"""
            type Promised = {{domain}} {{promised}};
            {{producedDeclaration}}
            public abstract IFactory { Promised Make({{producedType}} value); }
            public class Factory : IFactory
            {
                public {{producedType}} Make({{producedType}} value) { return value; }
            }
            """);
    }

    private static ImmutableArray<Diagnostic> AnalyseOpaqueFactory(string promised, string produced)
    {
        return Analyse($$"""
            public class Limits { public static readonly int Bound = 10; }
            type Letters = string `[a-z]+`;
            type NonEmptyLetters = Letters and not "";
            type SameLetters = string `[a-z]+`;
            type Digits = string `[0-9]+`;
            type BelowLimit = int < Limits.Bound;
            type NonNegativeBelowLimit = BelowLimit and >= 0;
            type OtherBelowLimit = int < Limits.Bound;
            public abstract IFactory { {{promised}} Make({{produced}} value); }
            public class Factory : IFactory
            {
                public {{produced}} Make({{produced}} value) { return value; }
            }
            """);
    }

    private static ImmutableArray<Diagnostic> AnalyseMember(string domain, string member)
    {
        var zero = domain switch { "double" => "0.0", "float" => "0.0f", "decimal" => "0.0m", _ => "0" };
        return Analyse($$"""
            type Positive = {{domain}} > {{zero}};
            public abstract IFactory { Positive Make(); }
            public class Factory : IFactory { {{member}} }
            """);
    }

    internal static ImmutableArray<Diagnostic> Analyse(string source)
    {
        var model = StandardModelProbe.Build("", out var buildDiagnostics, source);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var nodes = SourceBodies.Of(model)
            .Where(root => root.Owner?.FullyQualifiedName.StartsWith("Probe.", StringComparison.Ordinal) == true)
            .SelectMany(root => BodyWalker.DescendantsAndSelf(root.Root));
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}