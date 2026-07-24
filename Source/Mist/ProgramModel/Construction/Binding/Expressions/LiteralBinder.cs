using Mist.Generated;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds literal expressions (integer, string, bool, float, char, hex).
/// </summary>
internal static class LiteralBinder
{
    internal static Expression BindIntegerLiteral(IntegerLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var (value, wellKnown) = ParseIntegerLiteral(node.IntegerLiteral.Lexeme);
        var type = context.Resolver.LookupWellKnown(wellKnown);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindHexLiteral(HexLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var lexeme = node.HexLiteral.Lexeme;
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? lexeme[2..] : lexeme;
        var value = long.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : 0L;
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Int32);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindFloatLiteral(FloatLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var lexeme = node.FloatLiteral.Lexeme;
        var upper = lexeme.ToUpperInvariant();

        if(upper.EndsWith("M", StringComparison.Ordinal))
        {
            var digits = lexeme[..^1];
            var value = decimal.TryParse(digits, out var parsed) ? parsed : 0m;
            var type = context.Resolver.LookupWellKnown(WellKnownTypes.Decimal);
            return new LiteralExpression(node, value, type);
        }

        if(upper.EndsWith("F", StringComparison.Ordinal))
        {
            var digits = lexeme[..^1];
            var value = float.TryParse(digits, out var parsed) ? parsed : 0f;
            var type = context.Resolver.LookupWellKnown(WellKnownTypes.Single);
            return new LiteralExpression(node, value, type);
        }

        // Default: double
        var dValue = double.TryParse(lexeme.TrimEnd('d', 'D'), out var dParsed) ? dParsed : 0.0;
        var dType = context.Resolver.LookupWellKnown(WellKnownTypes.Double);
        return new LiteralExpression(node, dValue, dType);
    }

    internal static Expression BindCharLiteral(CharLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var lexeme = node.CharLiteral.Lexeme;
        var value = lexeme.Length >= 3 ? lexeme[1] : '\0';
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Char);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindBoolLiteral(Storm.Runtime.SyntaxNode node, bool value, BuilderContext context)
    {
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Boolean);
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