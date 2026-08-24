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
        var (value, wellKnown) = ParseIntegerLiteral(lexeme);
        var type = context.Resolver.LookupWellKnown(wellKnown);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindHexLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? lexeme[2..] : lexeme;
        var value = long.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var parsed) ? parsed : 0L;
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.Int32);
        return new LiteralExpression(node, value, type);
    }

    internal static Expression BindFloatLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
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

    internal static Expression BindStringLiteral(SyntaxNode node, string lexeme, BuilderContext context)
    {
        // Strip surrounding quotes to get the string value.
        var value = lexeme.Length >= 2 ? lexeme[1..^1] : lexeme;
        var type = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        return new LiteralExpression(node, value, type);
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
                elements.Add(ExpressionBinder.BindExpression(value, ctx, context));
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
                elements.Add(ExpressionBinder.BindExpression(list.First, ctx, context));

                foreach(var expression in list.Expressions)
                {
                    elements.Add(ExpressionBinder.BindExpression(expression, ctx, context));
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