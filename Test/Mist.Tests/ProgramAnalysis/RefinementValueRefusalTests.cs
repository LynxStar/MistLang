using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefinementValueRefusalTests
{
    [Theory]
    [InlineData("real", "declared", true)]
    [InlineData("real", "intrinsic", false)]
    [InlineData("real", "absent", false)]
    [InlineData("string", "declared", true)]
    [InlineData("string", "intrinsic", false)]
    [InlineData("string", "absent", false)]
    public void LiteralPredicateTrustRequiresADeclaredUnaryReturnContract(string domain, string selection, bool expected)
    {
        var h = new HandBuiltRefinements();
        var target = domain == "real" ? h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "PositiveReal")
            : h.Refine(h.String, ComparisonKind.Equal, "x", "Named");
        var operand = HandBuiltRefinements.Local(target);
        var value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, operand, target)
        {
            Operator = selection == "absent" ? null : new Operator(OperatorKind.Minus, target,
                [new Parameter("value", target)], new MemberModifiers { IsStatic = true, IsIntrinsic = selection == "intrinsic" }),
        };

        Assert.Equal(expected, LiteralPredicates.Satisfied(target, value.Type, null, value));
    }

    [Theory]
    [InlineData("argument-literal-violates-constraint", "less100-literal200", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("argument-literal-violates-refined-parameter", "adult-literal3", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("argument-refinement-not-implied-by-parameter", "adult-to-drinking", "MIST_PA_REFINEMENT_NOT_IMPLIED")]
    [InlineData("argument-unproven-for-a-substituted-refinement", "not18-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("argument-unproven-for-constraint", "less100-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("argument-unproven-for-refined-parameter", "adult-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("argument-violates-a-substituted-refinement", "not18-literal18", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("argument-violates-an-inferred-refinement", "not18-literal18", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("bool-literal-violates-refinement", "true-literalfalse", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("bool-transformation-has-no-input-promise", "true-negated-parameter", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("byte-literal-violates-refinement", "small-literal200", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("cast-literal-violates-refined-operator-parameter", "adult-literal7", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("cast-literal-violates-refinement", "adult-literal7", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("chain-argument-violates-constraint", "positive-literal0", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("char-literal-violates-refinement", "upper-literala", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("compound-refinement-not-implied", "percentage-to-digit", "MIST_PA_REFINEMENT_NOT_IMPLIED")]
    [InlineData("compound-refinement-violates-its-base", "senior-literal5", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("compound-refinement-violates-its-own-predicate", "senior-literal30", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("constructor-argument-unproven-for-refined-parameter", "adult-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("constructor-argument-violates-constraint", "positive-literal0", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("culture-dependent-regex-remains-unproven", "culture-regex", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("decoded-string-literal-violates-refinement", "escaped-string", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("disjunctive-regex-dynamic-remains-unproven", "regex-or-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("disjunctive-regex-literal-is-violated", "regex-or-literal", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("extension-method-where-unproven", "three-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("extension-method-where-violated", "three-literal1", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("float-double-bound-literal-is-violated", "float-bound", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("generic-extension-method-where-violated", "not18-literal18", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("indexer-constraint-not-implied-by-refinement", "adult-to-drinking-constraint", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("indexer-constraint-unproven", "positive-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("indexer-constraint-unproven-through-a-conversion", "positive-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    [InlineData("indexer-constraint-violated-by-literal", "positive-literalminus1", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("indexer-constraint-violated-on-the-selected-overload", "positive-literalminus1", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("indexer-literal-violates-refined-parameter", "smalladult-literal5", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("literal-violates-compound-refinement", "percentage-literal101", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("literal-violates-disjunction", "even-literal3", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("literal-violates-every-union-constituent", "union-literal500", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("literal-violates-negation", "not0-literal0", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("literal-violates-refinement", "adult-literal7", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("integral-transformation-has-no-input-promise", "positive-negated-parameter", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("literal-violates-the-only-constituent-that-holds-it", "split-literal300", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("local-function-where-violated", "ten-literal50", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("narrowed-binding-not-implied", "adult-to-drinking", "MIST_PA_REFINEMENT_NOT_IMPLIED")]
    [InlineData("negated-regex-dynamic-remains-unproven", "regex-not-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("negated-regex-literal-is-violated", "regex-not-literal", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("negated-string-literal-violates-refinement", "string-not-literal", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("real-argument-violates-a-predicate", "real-positive-literalminus", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("real-decimal-literal-violates-refinement", "decimal-unit-literal", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("real-float-promotion-violates-double-bound", "float-promotion", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("real-literal-violates-refinement", "real-positive-literalminus", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("real-negation-of-a-refined-parameter-remains-unproven", "real-negated-parameter", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("real-refinement-does-not-imply-a-narrower-one", "real-positive-to-unit", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("refinement-not-implied", "adult-to-drinking", "MIST_PA_REFINEMENT_NOT_IMPLIED")]
    [InlineData("regex-alternation-requires-a-full-match", "regex-alternation", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("regex-literal-requires-a-full-match", "regex-full", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("regex-return-violates-a-predicate", "regex-return", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("returned-literal-violates-refinement", "adult-literal5", "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("unproven-assignment", "adult-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unproven-cast", "adult-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unproven-return", "adult-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unreadable-argument-unproven", "property-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unreadable-property-pattern-unproven", "property-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unreadable-real-unproven", "real-positive-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unreadable-regex-unproven", "email-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("unreadable-string-constant-unproven", "string-unknown", "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData("where-parameter-reassigned-outside-constraint", "ninety-literal5", "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData("abstract-constraint-unproven-at-call", "nonnegative-unknown", "MIST_PA_CONSTRAINT_UNPROVEN")]
    public void ValueMustProveTheRequiredRefinement(string name, string scenario, string expected)
    {
        _ = name;
        var h = new HandBuiltRefinements();
        var adult = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "AdultAge");
        var drinking = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 21L, "DrinkingAge");
        var percentage = h.Between(h.Int, 0L, 100L, "Percentage");
        var positive = h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, "Positive");
        IMistType target = adult;
        Expression value = HandBuiltRefinements.Local(h.Int);
        switch(scenario)
        {
            case "adult-literal3": value = h.Literal(3L); break;
            case "adult-literal5": value = h.Literal(5L); break;
            case "adult-literal7": value = h.Literal(7L); break;
            case "adult-unknown": break;
            case "adult-to-drinking":
                target = drinking; value = HandBuiltRefinements.Local(adult); break;
            case "adult-to-drinking-constraint":
                target = h.Between(h.Int, 0L, 4L, null); value = HandBuiltRefinements.Local(h.Between(h.Int, 0L, 9L, "Digit")); break;
            case "less100-literal200":
            case "less100-unknown":
                target = h.Refine(h.Int, ComparisonKind.LessThan, 100L, null);
                if(scenario.EndsWith("200", StringComparison.Ordinal)) { value = h.Literal(200L); }

                break;
            case "not18-literal18":
            case "not18-unknown":
                target = HandBuiltRefinements.RefinePattern(h.Int, new NegatedPattern(HandBuiltModel.Node(), h.Int, h.Compare(h.Int, ComparisonKind.Equal, 18L)), null);
                if(scenario.EndsWith("18", StringComparison.Ordinal)) { value = h.Literal(18L); }

                break;
            case "true-literalfalse":
                target = h.Refine(h.Bool, ComparisonKind.Equal, true); value = h.Literal(false); break;
            case "true-negated-parameter":
                target = h.Refine(h.Bool, ComparisonKind.Equal, true, "Yes");
                value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.LogicalNot, HandBuiltRefinements.Local(target), h.Bool); break;
            case "positive-negated-parameter":
                target = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
                value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, HandBuiltRefinements.Local(target), h.Int); break;
            case "small-literal200":
                target = h.Refine(h.Built.Type("Byte"), ComparisonKind.LessThanOrEqual, 10L); value = h.Literal(200L); break;
            case "smalladult-literal5":
                target = h.Refine(h.Built.Type("Byte"), ComparisonKind.GreaterThanOrEqual, 18L); value = h.Literal(5L); break;
            case "positive-literal0":
            case "positive-literalminus1":
            case "nonnegative-unknown":
            case "positive-unknown":
                target = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, null);
                if(scenario == "positive-literal0") { target = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, null); value = h.Literal(0L); }

                if(scenario == "positive-literalminus1")
                {
                    target = name == "indexer-constraint-violated-by-literal" ? h.Between(h.Int, 0L, 4L, null) : target;
                    value = h.Literal(name == "indexer-constraint-violated-by-literal" ? 7L : -1L);
                }

                if(name == "indexer-constraint-unproven") { target = h.Between(h.Int, 0L, 4L, null); }

                if(name == "indexer-constraint-unproven-through-a-conversion")
                {
                    target = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, null);
                    var counter = HandBuiltModel.Class("Counter");
                    var convert = HandBuiltModel.Conversion(counter, counter, h.Int);
                    value = new ImplicitConversionExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(counter), h.Int, [new ConversionFunction(convert, counter, h.Int)]);
                }

                break;
            case "upper-literala":
                target = h.Between(h.Built.Type("Char"), 'A', 'Z'); value = h.Literal('a'); break;
            case "percentage-to-digit": target = h.Between(h.Int, 0L, 9L); value = HandBuiltRefinements.Local(percentage); break;
            case "percentage-literal101": target = percentage; value = h.Literal(101L); break;
            case "senior-literal5":
            case "senior-literal30":
                target = h.Refine(adult, ComparisonKind.GreaterThanOrEqual, 65L, "Senior");
                value = h.Literal(scenario == "senior-literal5" ? 5L : 30L); break;
            case "even-literal3":
                target = HandBuiltRefinements.RefinePattern(h.Int, new DisjunctionPattern(HandBuiltModel.Node(), h.Int,
                    [h.Compare(h.Int, ComparisonKind.Equal, 2L), h.Compare(h.Int, ComparisonKind.Equal, 4L), h.Compare(h.Int, ComparisonKind.Equal, 6L)]));
                value = h.Literal(3L); break;
            case "union-literal500":
                target = h.Built.Interner.GetOrCreateUnionType([h.Between(h.Int, 3L, 8L), h.Between(h.Built.Type("Byte"), 0L, 63L)]);
                value = h.Literal(500L); break;
            case "split-literal300":
                target = h.Built.Interner.GetOrCreateUnionType([h.Refine(h.Int, ComparisonKind.LessThanOrEqual, 5L), h.Refine(h.Built.Type("Byte"), ComparisonKind.GreaterThanOrEqual, 200L)]);
                value = h.Literal(300L); break;
            case "not0-literal0":
                target = HandBuiltRefinements.RefinePattern(h.Int, new NegatedPattern(HandBuiltModel.Node(), h.Int, h.Compare(h.Int, ComparisonKind.Equal, 0L)));
                value = h.Literal(0L); break;
            case "three-literal1":
            case "three-unknown":
                target = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 3L, null);
                if(scenario == "three-literal1") { value = h.Literal(1L); }

                break;
            case "ten-literal50": target = h.Between(h.Int, 0L, 10L, null); value = h.Literal(50L); break;
            case "ninety-literal5": target = h.Refine(h.Int, ComparisonKind.GreaterThan, 90L, null); value = h.Literal(5L); break;
            case "float-bound": target = h.Refine(h.Built.Type("Single"), ComparisonKind.LessThanOrEqual, 0.1); value = h.Literal(0.1f); break;
            case "float-promotion": target = h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 0.1); value = h.Literal(0.1f); break;
            case "real-positive-literalminus": target = name == "real-argument-violates-a-predicate" ? h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, null) : positive; value = h.Literal(-2.5); break;
            case "decimal-unit-literal": target = h.Between(h.Built.Type("Decimal"), 0.0m, 1.0m); value = h.Literal(1.5m); break;
            case "real-positive-unknown": target = positive; value = HandBuiltRefinements.Local(h.Double); break;
            case "real-positive-to-unit": target = h.Between(h.Double, 0.0, 1.0); value = HandBuiltRefinements.Local(positive); break;
            case "real-negated-parameter": target = positive; value = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, HandBuiltRefinements.Local(positive), positive); break;
            case "culture-regex": target = h.Regex("(?i:i)"); value = h.Literal("I"); break;
            case "escaped-string": target = h.Refine(h.String, ComparisonKind.Equal, "a\\n"); value = h.Literal("a\\\\n"); break;
            case "string-unknown": target = h.Refine(h.String, ComparisonKind.Equal, "x"); value = HandBuiltRefinements.Local(h.String); break;
            case "string-not-literal": target = HandBuiltRefinements.RefinePattern(h.String, new NegatedPattern(HandBuiltModel.Node(), h.String, h.Compare(h.String, ComparisonKind.Equal, "x"))); value = h.Literal("x"); break;
            case "regex-or-unknown":
            case "regex-or-literal":
                target = HandBuiltRefinements.RefinePattern(h.String, new DisjunctionPattern(HandBuiltModel.Node(), h.String,
                    [new RegexPattern(HandBuiltModel.Node(), h.String, "[a-z]+"), new RegexPattern(HandBuiltModel.Node(), h.String, "\\\\d+")]));
                value = scenario == "regex-or-literal" ? h.Literal("abc1") : HandBuiltRefinements.Local(h.String); break;
            case "regex-not-unknown":
            case "regex-not-literal":
                target = HandBuiltRefinements.RefinePattern(h.String, new NegatedPattern(HandBuiltModel.Node(), h.String, new RegexPattern(HandBuiltModel.Node(), h.String, "[a-z]+")));
                value = scenario == "regex-not-literal" ? h.Literal("abc") : HandBuiltRefinements.Local(h.String); break;
            case "email-unknown": target = h.Regex("^[^@]+@[^@]+\\\\.[^@]+$"); value = HandBuiltRefinements.Local(h.String); break;
            case "regex-alternation": target = h.Regex("a|b"); value = h.Literal("ab"); break;
            case "regex-full": target = h.Regex("[a-z]+"); value = h.Literal("abc1"); break;
            case "regex-return": target = h.Regex("[a-z]+"); value = h.Literal("123"); break;
            case "property-unknown":
                var box = HandBuiltModel.Class("Box");
                var size = new Property("Size", h.Int, new MemberModifiers());
                target = HandBuiltRefinements.RefinePattern(box, new PropertyPattern(HandBuiltModel.Node(), box,
                    [new PropertySubpattern(HandBuiltModel.Node(), "Size", h.Compare(h.Int, ComparisonKind.LessThan, 3L), h.Int, new PropertyResult(size, box, []))]), "Small");
                value = new NewExpression(HandBuiltModel.Node(), box, [h.Literal(1L)], null, [], [h.Int]); break;
            default: throw new InvalidOperationException(scenario);
        }

        var known = Requirement.Constant(value) is { } constant ? Admitted.Exactly(constant) : Requirement.ValuesOf(value.Type) ?? Admitted.Everything;
        Assert.Equal(expected, RefinementDecision.Judge(value, target, known, LiteralValue.Of(value), value is UnaryExpression ? RealSet.Everything(RealKind.Double) : RealRequirement.OfType(value.Type),
            Requirement.IsAnonymous(target), Regex.InfiniteMatchTimeout)?.Number.Value);
    }
}