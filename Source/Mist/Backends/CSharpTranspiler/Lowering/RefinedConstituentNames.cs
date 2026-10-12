using System.Globalization;
using System.Text;
using Mist.Common.Source;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Neither the name nor the predicate is unique alone: every refinement in one union alias carries the
/// alias's name, and two declarations may state the same predicate.
/// </summary>
internal static class RefinedConstituentNames
{
    public static string Of(RefinedType refined)
    {
        var name = refined.IsAnonymous ? $"Where{refined.PredicateNode.Span.Start}" : Identifier(refined.Name);
        return $"{name}_{Readable(refined.Predicate) ?? Digest(refined)}";
    }

    private static string? Readable(Pattern? predicate)
    {
        return predicate switch
        {
            ConjunctionPattern conjunction => Joined(conjunction.Operands, "And"),
            DisjunctionPattern disjunction => Joined(disjunction.Operands, "Or"),
            _ => Operand(predicate),
        };
    }

    private static string? Joined(IReadOnlyList<Pattern> operands, string separator)
    {
        var parts = operands.Select(Operand).ToList();
        return parts.Contains(null) ? null : string.Join(separator, parts);
    }

    private static string? Operand(Pattern? operand)
    {
        return operand switch
        {
            NegatedPattern { Operand: ComparisonPattern comparison } => Compared(comparison) is { } spelled ? $"Not{spelled}" : null,
            ComparisonPattern comparison => Compared(comparison),
            _ => null,
        };
    }

    private static string? Compared(ComparisonPattern comparison)
    {
        var kind = comparison.Kind switch
        {
            ComparisonKind.Equal => "Eq",
            ComparisonKind.LessThan => "Lt",
            ComparisonKind.LessThanOrEqual => "Le",
            ComparisonKind.GreaterThan => "Gt",
            _ => "Ge",
        };

        return Constant(comparison.Bound) is { } value ? kind + value : null;
    }

    private static string? Constant(Expression bound)
    {
        return bound switch
        {
            LiteralExpression { Value: bool truth } => truth ? "True" : "False",
            LiteralExpression { Value: char character } => $"Char{(int)character}",
            LiteralExpression { Value: float or double or decimal } literal => Number(literal.Value),
            _ when IntegralConstant.Fold(bound) is { } integral => Number(integral),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: float or double or decimal } literal }
                => Number(literal.Value) is { } magnitude ? $"Neg{magnitude}" : null,
            _ => null,
        };
    }

    private static string? Number(object value)
    {
        var text = value switch
        {
            Int128 integral => integral.ToString(CultureInfo.InvariantCulture),
            float single when float.IsFinite(single) => single.ToString("R", CultureInfo.InvariantCulture),
            double real when double.IsFinite(real) => real.ToString("R", CultureInfo.InvariantCulture),
            decimal exact => exact.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

        return text?.Replace("-", "Neg", StringComparison.Ordinal).Replace("+", "Pos", StringComparison.Ordinal).Replace(".", "p", StringComparison.Ordinal);
    }

    private static string Digest(RefinedType refined)
    {
        var node = refined.PredicateNode;
        var source = node.Origin is ParsedOrigin { Source.Contents: var contents } && node.Span.End <= contents.Length
            ? string.Concat(contents.AsSpan(node.Span.Start, node.Span.Length).ToString().Where(character => !char.IsWhiteSpace(character)))
            : string.Create(CultureInfo.InvariantCulture, $"@{node.Span.Start}+{node.Span.Length}");
        var closing = string.Join(",", refined.Closing
            .OrderBy(pair => pair.Key.Name, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Name}={UnionCarrierNames.Spelled(pair.Value, keepsRefinements: true)}"));

        var hash = 2166136261u;

        foreach(var octet in Encoding.UTF8.GetBytes($"{refined.Name}|{source}|{closing}"))
        {
            hash = unchecked((hash ^ octet) * 16777619u);
        }

        return $"X{hash:X8}";
    }

    private static string Identifier(string name)
    {
        return string.Concat(name.Where(character => char.IsAsciiLetterOrDigit(character) || character == '_'));
    }
}