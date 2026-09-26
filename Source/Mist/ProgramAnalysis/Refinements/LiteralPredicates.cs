using System.Text.RegularExpressions;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static class LiteralPredicates
{
    private static readonly TimeSpan MatchLimit = TimeSpan.FromMilliseconds(100);

    public static bool Applies(IMistType target)
    {
        var name = Requirement.Unrefined(target).FullyQualifiedName;
        return name == WellKnownTypes.String.FullyQualifiedName || name == WellKnownTypes.Single.FullyQualifiedName
            || name == WellKnownTypes.Double.FullyQualifiedName || name == WellKnownTypes.Decimal.FullyQualifiedName;
    }

    public static bool IsRefused(IMistType target)
    {
        return Requirement.Refined(target) is RefinedType refined
            && (refined.Predicate is null || Comparability.IsRefused(refined) || IsRefused(refined.BaseType));
    }

    public static bool Satisfied(IMistType target, IMistType source, LiteralValue? value, Expression? expression)
    {
        return IsRefused(target) || (Decide(target, value)
            ?? (TrustsType(expression) && ReferenceEquals(Requirement.Refined(source), Requirement.Refined(target))));
    }

    private static bool TrustsType(Expression? expression)
    {
        return expression switch
        {
            UnaryExpression => false,
            CloneExpression clone => TrustsType(clone.Source),
            _ => true,
        };
    }

    public static bool? Decide(IMistType target, LiteralValue? value)
    {
        if(value is null || (value.Value is double number && !double.IsFinite(number))
            || (value.Value is float single && !float.IsFinite(single)))
        {
            return null;
        }

        return target switch
        {
            TypeAlias { AliasedType: { } aliased } => Decide(aliased, value),
            RefinedType refined when value.ConvertedTo(refined.BaseType) is { } converted =>
                And(DecideBase(refined.BaseType, converted), Decide(refined.Predicate, converted)),
            _ => null,
        };
    }

    private static bool? DecideBase(IMistType type, LiteralValue value)
    {
        return Requirement.Refined(type) is null ? true : Decide(type, value);
    }

    private static bool? Decide(Pattern? predicate, LiteralValue value)
    {
        return predicate switch
        {
            ComparisonPattern comparison when Comparability.Misordered(comparison) is null => Compare(value, comparison),
            RegexPattern regex when value.Value is string input => Match(input, regex.Pattern),
            ConjunctionPattern conjunction => All(conjunction.Operands.Select(operand => Decide(operand, value))),
            DisjunctionPattern disjunction => Any(disjunction.Operands.Select(operand => Decide(operand, value))),
            NegatedPattern negated => Decide(negated.Operand, value) is { } inner ? !inner : null,
            TypeTestPattern { Refinement: { } refinement } => Decide(refinement, value),
            CatchAllPattern => true,
            _ => null,
        };
    }

    private static bool? Compare(LiteralValue value, ComparisonPattern comparison)
    {
        var bound = LiteralValue.Of(comparison.Bound);

        if(bound is null)
        {
            return null;
        }

        if(value.Value is string text)
        {
            return comparison.Kind == ComparisonKind.Equal && bound.Value is string other ? StringComparer.Ordinal.Equals(text, other) : null;
        }

        var limit = bound.ConvertedTo(comparison.NarrowedType);

        if(value.Value is float && bound.Value is double && value.ConvertedTo(comparison.Bound.Type) is { } promoted)
        {
            value = promoted;
            limit = bound;
        }

        if(limit is null)
        {
            return null;
        }

        var order = (value.Value, limit.Value) switch
        {
            (float left, float right) => (int?)left.CompareTo(right),
            (double left, double right) => left.CompareTo(right),
            (decimal left, decimal right) => left.CompareTo(right),
            _ => null,
        };

        return order is { } compared ? comparison.Kind switch
        {
            ComparisonKind.Equal => compared == 0,
            ComparisonKind.LessThan => compared < 0,
            ComparisonKind.LessThanOrEqual => compared <= 0,
            ComparisonKind.GreaterThan => compared > 0,
            ComparisonKind.GreaterThanOrEqual => compared >= 0,
            _ => null,
        } : null;
    }

    private static bool? Match(string input, string body)
    {
        if(LiteralValue.Decoded(body) is not { } pattern)
        {
            return null;
        }

        // The timeout bounds compiler work; options and anchoring retain the emitted runtime test's semantics.
        try
        {
            if(Regex.IsMatch(pattern, @"\(\?[imnsx-]*i", RegexOptions.CultureInvariant, MatchLimit))
            {
                return null;
            }

            return Regex.IsMatch(input, $"^(?:{pattern})$", RegexOptions.None, MatchLimit);
        }
        catch(ArgumentException)
        {
            return null;
        }
        catch(RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static bool? And(bool? left, bool? right)
    {
        return left == false || right == false ? false : left == true && right == true ? true : null;
    }

    private static bool? All(IEnumerable<bool?> operands)
    {
        foreach(var operand in operands)
        {
            if(operand != true)
            {
                return operand;
            }
        }

        return true;
    }

    private static bool? Any(IEnumerable<bool?> operands)
    {
        foreach(var operand in operands)
        {
            if(operand != false)
            {
                return operand;
            }
        }

        return false;
    }
}