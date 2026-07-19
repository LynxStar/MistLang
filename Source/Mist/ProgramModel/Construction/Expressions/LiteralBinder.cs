using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Binds literal expressions (integer, string, and future: bool, float, char).
/// </summary>
internal static class LiteralBinder
{
    internal static Expression BindIntegerLiteral(IntegerLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var (value, wellKnown) = ParseIntegerLiteral(node.IntegerLiteral.Lexeme);
        var type = context.Resolver.LookupWellKnown(wellKnown);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindStringLiteral(StringLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        // Strip surrounding quotes to get the string value.
        var lexeme = node.StringLiteral.Lexeme;
        var value = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        return new LiteralExpression(node, value, type);
    }

    /// <summary>
    /// Parses a decimal integer literal lexeme into its value and the well-known type
    /// it maps to, honouring the u/l suffixes.
    /// </summary>
    private static (object Value, WellKnownType Type) ParseIntegerLiteral(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();
        var wellKnown = upper switch
        {
            _ when upper.EndsWith("UL", StringComparison.Ordinal) || upper.EndsWith("LU", StringComparison.Ordinal) => WellKnownTypes.UInt64,
            _ when upper.EndsWith("L", StringComparison.Ordinal) => WellKnownTypes.Int64,
            _ when upper.EndsWith("U", StringComparison.Ordinal) => WellKnownTypes.UInt32,
            _ => WellKnownTypes.Int32,
        };

        var digits = lexeme.TrimEnd('u', 'U', 'l', 'L');
        var value = long.TryParse(digits, out var parsed) ? parsed : 0L;
        return (value, wellKnown);
    }
}