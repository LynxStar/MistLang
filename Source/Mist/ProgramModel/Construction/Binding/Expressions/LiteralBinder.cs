using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds literal expressions (integer, string, bool, float, char, hex, array).
/// </summary>
internal static class LiteralBinder
{
    internal static Expression BindIntegerLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var (value, wellKnown) = IntegerValue(lexeme);
        return Numeric(node, lexeme, value, wellKnown, context);
    }

    internal static Expression BindHexLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        return Numeric(node, lexeme, HexValue(lexeme), WellKnownTypes.Int32, context);
    }

    internal static Expression BindFloatLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var (value, wellKnown) = RealValue(lexeme);
        return Numeric(node, lexeme, value, wellKnown, context);
    }

    internal static (object? Value, WellKnownType Type) IntegerValue(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();
        var wellKnown = upper switch
        {
            _ when upper.EndsWith("UL", StringComparison.Ordinal) || upper.EndsWith("LU", StringComparison.Ordinal) => WellKnownTypes.UInt64,
            _ when upper.EndsWith("L", StringComparison.Ordinal) => WellKnownTypes.Int64,
            _ when upper.EndsWith("U", StringComparison.Ordinal) => WellKnownTypes.UInt32,
            _ => WellKnownTypes.Int32,
        };

        var digits = Digits(lexeme.TrimEnd('u', 'U', 'l', 'L'));

        if(wellKnown == WellKnownTypes.UInt64)
        {
            return (ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var wide) ? unchecked((long)wide) : null, wellKnown);
        }

        return (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : null, wellKnown);
    }

    internal static object? HexValue(string lexeme)
    {
        var digits = Digits(lexeme[2..].TrimEnd('u', 'U', 'l', 'L'));
        return long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    internal static object? BinaryValue(string lexeme)
    {
        var digits = Digits(lexeme[2..].TrimEnd('u', 'U', 'l', 'L'));
        return long.TryParse(digits, NumberStyles.AllowBinarySpecifier, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Whether the literal is 9223372036854775808, unsuffixed or with an l suffix, which long cannot hold but
    /// whose negation is long's minimum: C# admits it, as long, only as the operand a unary minus is written
    /// directly before, so the negation is bound as the one constant rather than a minus over a literal.
    /// </summary>
    internal static bool NegatesToLongMinimum(string lexeme)
    {
        var magnitude = lexeme.TrimEnd('l', 'L');

        return magnitude.Length >= lexeme.Length - 1
            && ulong.TryParse(Digits(magnitude), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value == 1UL << 63;
    }

    internal static Expression BindLongMinimum(SyntaxNode node, BuilderContext context)
    {
        return new LiteralExpression(node, long.MinValue, context.Resolver.LookupWellKnown(WellKnownTypes.Int64));
    }

    internal static (object? Value, WellKnownType Type) RealValue(string lexeme)
    {
        var digits = Digits(lexeme);
        var number = digits.TrimEnd('m', 'M', 'f', 'F', 'd', 'D');

        return char.ToUpperInvariant(digits[^1]) switch
        {
            'M' => (decimal.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : null, WellKnownTypes.Decimal),
            'F' => (float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : null, WellKnownTypes.Single),
            _ => (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null, WellKnownTypes.Double),
        };
    }

    private static string Digits(string lexeme)
    {
        return lexeme.Replace("_", "", StringComparison.Ordinal);
    }

    private static Expression Numeric(SyntaxNode node, string lexeme, object? value, WellKnownType wellKnown, BuilderContext context)
    {
        if(value is null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.NumericLiteralOutOfRange, node, null, lexeme, wellKnown.Keyword ?? wellKnown.Name);
            return new ErrorExpression(node);
        }

        return new LiteralExpression(node, value, context.Resolver.LookupWellKnown(wellKnown));
    }

    internal static Expression BindCharLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var value = lexeme.Length >= 3 ? lexeme[1] : '\0';
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Char);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindBoolLiteral(SyntaxNode node, bool value, BuilderContext context)
    {
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Boolean);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindStringLiteral(
        SyntaxNode node, string lexeme, BuilderContext context, IMistType? targetType = null)
    {
        // Strip surrounding quotes to get the string value.
        var value = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;

        // The shorthand: a quoted pattern where a regex is expected IS a regex literal, and binding
        // it as one is what keeps the two spellings a single thing rather than a conversion the
        // target language has to be talked into.
        if(targetType is not null
            && targetType.FullyQualifiedName == WellKnownTypes.Regex.FullyQualifiedName)
        {
            return new RegexLiteralExpression(node, value, targetType);
        }

        var type = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindRegexLiteral(
        RegexLiteralExpressionPrimaryNode node, BuilderContext context)
    {
        var lexeme = node.RegexLiteral.Lexeme;
        var raw = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;

        return new RegexLiteralExpression(
            node, Quoted(raw), context.Resolver.LookupWellKnown(WellKnownTypes.Regex));
    }

    /// <summary>
    /// Raw text as a string literal's body: the two characters a reader of that body would
    /// otherwise take for syntax.
    /// </summary>
    internal static string Quoted(string raw)
    {
        return raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Binds a tuple literal (<c>(42, "text")</c>, <c>(x: 3, y: 4)</c>). Each element binds against the
    /// corresponding element of the target tuple, so an element flows into an <c>unknown</c> slot or
    /// widens numerically exactly as it would in an assignment. A target of another arity supplies no
    /// element target at all, since it says nothing about what any one element should be.
    /// </summary>
    internal static Expression BindTupleLiteral(
        TupleLiteralExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var targetElements = (ctx.TargetType as ITupleType)?.ElementTypes;
        var elements = new List<Expression>();
        var elementNames = new List<string?>();

        var elementNodes = GrammarRepetition.TwoLeadsAndRepeated(
            node.First, node.Rest, node.TupleLiteralElements);

        var targets = targetElements?.Count == elementNodes.Count ? targetElements : null;

        for(var i = 0; i < elementNodes.Count; i++)
        {
            var (value, name) = elementNodes[i] switch
            {
                NamedTupleLiteralElementTupleLiteralElementNode named => (named.Value, named.Name.Lexeme),
                UnnamedTupleLiteralElementTupleLiteralElementNode unnamed => (unnamed.Value, null),
                _ => (null, null),
            };

            elementNames.Add(name);

            if(value is null)
            {
                elements.Add(new UnresolvedExpression(elementNodes[i]));
                continue;
            }

            using(ctx.PushTargetType(targets?[i]))
            {
                elements.Add(ImplicitConversions.At(
                    ExpressionBinder.BindExpression(value, ctx, context), targets?[i], context.ProgramModel.TypeInterner));
            }
        }

        var tupleType = context.ProgramModel.TypeInterner.GetOrCreateTupleType(
            elements.ConvertAll(element => element.Type));

        return new TupleLiteralExpression(node, elements, elementNames, tupleType);
    }

    /// <summary>
    /// Binds an array literal, empty (<c>[]</c>) or not (<c>[a, b, c]</c>).
    /// <para>
    /// The element type comes from the target type when the surrounding construct
    /// supplies an array type (<c>int[] x = [1, 2];</c>), and otherwise from the common
    /// type of the elements (<c>var x = [1, 2];</c>). An empty literal has no elements to
    /// infer from, which is why it is the only form that requires a target type.
    /// </para>
    /// </summary>
    internal static Expression BindArrayLiteral(ArrayLiteralExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var targetElementType = ArrayElementType(ctx.TargetType);
        var elements = new List<Expression>();

        // Elements are bound against the target ELEMENT type, not the array target
        // itself: element `1` is an int, not an int[]. When there is no array target the
        // push clears whatever outer target was in effect, so a nested literal infers
        // from its own elements instead of inheriting an unrelated one.
        if(node.Elements is { } list)
        {
            using(ctx.PushTargetType(targetElementType))
            {
                var interner = context.ProgramModel.TypeInterner;

                elements.Add(ImplicitConversions.At(ExpressionBinder.BindExpression(list.First, ctx, context), targetElementType, interner));

                foreach(var expression in list.Expressions)
                {
                    elements.Add(ImplicitConversions.At(ExpressionBinder.BindExpression(expression, ctx, context), targetElementType, interner));
                }
            }
        }

        var elementType = targetElementType;

        if(elementType is null && elements.Count > 0)
        {
            elementType = CommonElementType(elements, node, context);
        }

        if(elementType is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.EmptyArrayLiteralNeedsTargetType, node, null);
            return new ErrorExpression(node);
        }

        // A literal's length is statically known: it is the element count.
        var arrayType = context.Resolver.ConstructArray(elementType, elements.Count);
        return new ArrayLiteralExpression(node, elementType, elements, arrayType);
    }

    /// <summary>
    /// The element type of <paramref name="type"/> when it is an array, otherwise null.
    /// Used to turn an array target type into the element target its elements bind
    /// against, and to supply the element type for <c>[]</c>, which has none of its own.
    /// </summary>
    private static IMistType? ArrayElementType(IMistType? type)
    {
        if(type is ConstructedType constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            && constructed.TypeArguments.Count > 0)
        {
            return constructed.TypeArguments[0];
        }

        return null;
    }

    /// <summary>
    /// The common type of an array literal's elements. Type identity in the program
    /// model is the identity of the interned type instance, so a single distinct
    /// element type is the common type. Differing types have no common type: there is
    /// no conversion or numeric promotion infrastructure to widen them yet, so this
    /// reports rather than guessing.
    /// </summary>
    private static IMistType CommonElementType(List<Expression> elements, SyntaxNode node, BuilderContext context)
    {
        var candidate = elements[0].Type;

        foreach(var element in elements)
        {
            if(!ReferenceEquals(element.Type, candidate))
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.ArrayLiteralNoCommonElementType, node, null, candidate.Name, element.Type.Name);
                return ErrorType.Instance;
            }
        }

        return candidate;
    }
}