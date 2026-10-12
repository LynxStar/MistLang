using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinedPatternFlowTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [InlineData("a-chained-test-proves-the-whole-chain", null)]
    [InlineData("a-chained-test-proves-its-base", null)]
    [InlineData("the-outer-predicate-alone-proves-nothing", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("a-failed-chained-test-does-not-prove-the-base", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("the-fact-does-not-leave-the-branch", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("reassignment-loses-the-fact", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("a-matched-constituent-binds-its-refinement", null)]
    [InlineData("a-matched-constituent-is-not-another", "MIST_PA_REFINEMENT_NOT_IMPLIED")]
    [InlineData("a-constituent-of-another-base-binds-its-refinement", null)]
    public void APatternProvesOnlyWhatItsEmittedTestChecks(string name, string? expected)
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var smallPositive = h.Refine(positive, ComparisonKind.LessThan, 10L, "SmallPositive");
        var small = h.Refine(h.Int, ComparisonKind.LessThan, 10L, "Small");
        var big = h.Refine(h.Int, ComparisonKind.GreaterThan, 100L, "Big");
        var tiny = h.Refine(h.Built.Type("Byte"), ComparisonKind.LessThan, 10L, "Tiny");
        var input = HandBuiltRefinements.Local(h.Int, "input");
        var replacement = HandBuiltRefinements.Local(h.Int, "replacement");

        IsPatternExpression test(Expression scrutinee, TypeTestPattern pattern)
        {
            return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, h.Bool);
        }

        TypeTestPattern tested(IMistType type, LocalVariable? binding = null, IMistType? constituent = null)
        {
            return new TypeTestPattern(HandBuiltModel.Node(), type, binding, constituent,
                PatternBinder.ComposeRefinement(type, HandBuiltModel.Node(), refined => refined.Predicate));
        }

        Statement[] matched(IMistType union, IMistType constituent, IMistType required)
        {
            var bound = new LocalVariable("bound", constituent);
            return [HandBuiltRefinements.If(test(HandBuiltRefinements.Local(union), tested(constituent, bound, constituent)),
                HandBuiltRefinements.Need(HandBuiltRefinements.Read(bound), required))];
        }

        var negated = new NegatedPattern(HandBuiltModel.Node(), h.Int, tested(smallPositive));
        var body = name switch
        {
            "a-chained-test-proves-the-whole-chain"
                => [HandBuiltRefinements.If(test(input, tested(smallPositive)), HandBuiltRefinements.Need(input, smallPositive))],
            "a-chained-test-proves-its-base" => [HandBuiltRefinements.If(test(input, tested(smallPositive)), HandBuiltRefinements.Need(input, positive))],
            "the-outer-predicate-alone-proves-nothing" => [HandBuiltRefinements.If(test(input, tested(small)), HandBuiltRefinements.Need(input, smallPositive))],
            "a-failed-chained-test-does-not-prove-the-base"
                => [HandBuiltRefinements.If(new IsPatternExpression(HandBuiltModel.Node(), input, negated, h.Bool), HandBuiltRefinements.Need(input, positive))],
            "the-fact-does-not-leave-the-branch"
                => [HandBuiltRefinements.If(test(input, tested(smallPositive))), HandBuiltRefinements.Need(input, smallPositive)],
            "reassignment-loses-the-fact"
                => [HandBuiltRefinements.If(test(input, tested(smallPositive)),
                    HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(input, replacement)), HandBuiltRefinements.Need(input, smallPositive))],
            "a-matched-constituent-binds-its-refinement" => matched(new UnionType([big, small]), small, small),
            "a-matched-constituent-is-not-another" => matched(new UnionType([big, small]), small, big),
            "a-constituent-of-another-base-binds-its-refinement" => matched(new UnionType([tiny, positive]), positive, positive),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        var findings = h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(body));

        Assert.True(expected is null ? findings.Count == 0 : findings.Count == 1 && findings[0].Id.Value == expected,
            $"{name}: {string.Join(", ", findings.Select(finding => finding.Id.Value))}");
    }
}