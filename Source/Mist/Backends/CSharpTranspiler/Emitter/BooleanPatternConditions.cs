using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class BooleanPatternConditions
{
    public static bool Requires(Pattern pattern)
    {
        return pattern switch
        {
            ComparisonPattern comparison => PromotesSingle(comparison),
            NegatedPattern negated => ContainsCondition(negated.Operand),
            DisjunctionPattern disjunction => disjunction.Operands.Any(ContainsCondition),
            _ => false,
        };
    }

    private static bool ContainsCondition(Pattern pattern)
    {
        return pattern switch
        {
            RegexPattern => true,
            ComparisonPattern comparison => PromotesSingle(comparison),
            NegatedPattern negated => ContainsCondition(negated.Operand),
            ConjunctionPattern conjunction => conjunction.Operands.Any(ContainsCondition),
            DisjunctionPattern disjunction => disjunction.Operands.Any(ContainsCondition),
            TypeTestPattern { Refinement: { } refinement } => ContainsCondition(refinement),
            PropertyPattern property => property.Subpatterns.Any(subpattern => ContainsCondition(subpattern.Pattern))
                || (property.TypeTest?.Refinement is { } refinement && ContainsCondition(refinement)),
            TuplePattern tuple => tuple.Elements.Any(ContainsCondition),
            ListPattern list => list.Elements.Any(element => element.Subpattern is { } nested && ContainsCondition(nested)),
            _ => false,
        };
    }

    private static bool PromotesSingle(ComparisonPattern comparison)
    {
        return Unrefined(comparison.NarrowedType).FullyQualifiedName == WellKnownTypes.Single.FullyQualifiedName
            && Unrefined(comparison.Bound.Type).FullyQualifiedName == WellKnownTypes.Double.FullyQualifiedName;
    }

    private static IMistType Unrefined(IMistType type)
    {
        return TypeNameEmitter.Closed(type) switch
        {
            RefinedType refined => Unrefined(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Unrefined(aliased),
            var closed => closed,
        };
    }

    // Boolean composition keeps a hoisted predicate inside its own not/or branch and preserves short circuiting.
    public static string Render(Pattern pattern, string value, Func<Pattern, string, string> structural, Func<Expression, string> expression)
    {
        return pattern switch
        {
            RegexPattern regex => RegexLowering.IsFullMatch(value, RegexLowering.Quote(regex.Pattern)),
            ComparisonPattern comparison when PromotesSingle(comparison) => $"({value} {Symbol(comparison.Kind)} {expression(comparison.Bound)})",
            NegatedPattern negated => $"!({Render(negated.Operand, value, structural, expression)})",
            ConjunctionPattern conjunction => $"({string.Join(" && ", conjunction.Operands.Select(operand => Render(operand, value, structural, expression)))})",
            DisjunctionPattern disjunction => $"({string.Join(" || ", disjunction.Operands.Select(operand => Render(operand, value, structural, expression)))})",
            TypeTestPattern { Refinement: { } refinement } tested => Refined(tested, refinement, value, structural, expression),
            _ => structural(pattern, value),
        };
    }

    private static string Refined(TypeTestPattern tested, Pattern refinement, string value, Func<Pattern, string, string> structural, Func<Expression, string> expression)
    {
        var narrowed = BodyEmitter.TemporaryName();
        var binding = tested.Binding is { } local ? $" && {narrowed} is var {LocalNames.Of(local)}" : "";
        return $"({value} is {TypeNameEmitter.EmitAsPatternType(tested.NarrowedType)} {narrowed} && {Render(refinement, narrowed, structural, expression)}{binding})";
    }

    private static string Symbol(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.Equal => "==",
            ComparisonKind.LessThan => "<",
            ComparisonKind.LessThanOrEqual => "<=",
            ComparisonKind.GreaterThan => ">",
            ComparisonKind.GreaterThanOrEqual => ">=",
            _ => throw new InvalidOperationException(),
        };
    }
}