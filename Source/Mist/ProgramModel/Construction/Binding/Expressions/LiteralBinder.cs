using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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

    internal static Expression BindFloatLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var (value, wellKnown) = RealValue(lexeme);
        return Numeric(node, lexeme, value, wellKnown, context);
    }

    internal static (object? Value, WellKnownType Type) IntegerValue(string lexeme)
    {
        return IntegerLiterals.Read(lexeme) is (var value, var wellKnown) ? (value, wellKnown) : (null, WellKnownTypes.UInt64);
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
            context.Diagnostics.ReportAtNodeTyped(
                ProgramModelDiagnostics.NumericLiteralOutOfRange, node, null, new TextValue(lexeme), new TypeValue(wellKnown.FullyQualifiedName, wellKnown.Keyword ?? wellKnown.Name));
            return new ErrorExpression(node);
        }

        return new LiteralExpression(node, value, context.Resolver.LookupWellKnown(wellKnown));
    }

    internal static Expression BindCharLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var value = CharacterLiterals.Decode(lexeme);
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
        // target language has to be talked into. A cast to regex and a regex construction expect
        // one too (AsRegexLiteral), so a pattern written in the source is checked at compile time
        // however it is spelled, and only a string read at run time can still fail to parse.
        if(targetType is not null && IsRegex(targetType))
        {
            return new RegexLiteralExpression(node, value, targetType);
        }

        var type = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        return new LiteralExpression(node, value, type);
    }

    internal static RegexLiteralExpression? AsRegexLiteral(Expression bound, IMistType target)
    {
        return bound is LiteralExpression { Value: string body } && IsRegex(target)
            ? new RegexLiteralExpression(bound.Node, body, target)
            : null;
    }

    private static bool IsRegex(IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.Regex.FullyQualifiedName;
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

        return new TupleLiteralExpression(node, elements, elementNames, tupleType) { IsTargetTyped = targets is not null };
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

        if(ctx.TargetTypeIsFailed)
        {
            return new ErrorExpression(node, elements);
        }

        var elementType = targetElementType;

        if(elementType is null && elements.Count > 0)
        {
            elementType = CommonElementType(elements, node, context);
            if(elementType is null || elements.All(PriorFailure.Of))
            {
                return new ErrorExpression(node, elements);
            }

            elements = elements.ConvertAll(element => ImplicitConversions.At(element, elementType, context.ProgramModel.TypeInterner));
        }

        if(elementType is null)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.EmptyArrayLiteralNeedsTargetType, node, null);
            return new ErrorExpression(node);
        }

        // A literal's length is statically known: it is the element count.
        var arrayType = context.Resolver.ConstructArray(elementType, elements.Count);
        return new ArrayLiteralExpression(node, elementType, elements, arrayType) { IsTargetTyped = targetElementType is not null };
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

    private static IMistType? CommonElementType(List<Expression> elements, SyntaxNode node, BuilderContext context)
    {
        var common = CommonTypeInference.Infer(elements, context.Resolver.LookupWellKnown, context.ProgramModel.TypeInterner);
        if(common is null)
        {
            var (first, other) = CommonTypeInference.Disagreement(elements);
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.ArrayLiteralNoCommonElementType, node, null, DiagnosticSnapshot.Type(first, first.Name), DiagnosticSnapshot.Type(other, other.Name));
        }

        return common;
    }
}