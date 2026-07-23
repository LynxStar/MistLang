using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Expressions;

/// <summary>
/// Entry point for expression binding. Unwraps the assignment layer and dispatches
/// to the precedence chain, ultimately reaching a primary expression.
/// </summary>
internal static class ExpressionBinder
{
    public static Expression BindExpression(ExpressionNode expression, BindingContext ctx, BuilderContext context)
    {
        var assignment = expression.AsAssignmentOrNull();
        if(assignment is null)
        {
            return new UnresolvedExpression(expression);
        }

        if(assignment.Group0 is { } assignmentOp)
        {
            if(assignmentOp.Operator.Lexeme == "=")
            {
                var target = OperatorBinder.BindVoidCoalescing(assignment.Target, ctx, context);
                var value = BindExpression(assignmentOp.Value, ctx, context);
                return new AssignmentExpression(expression, target, value, value.Type);
            }

            return new UnresolvedExpression(expression);
        }

        return OperatorBinder.BindVoidCoalescing(assignment.Target, ctx, context);
    }

    internal static Expression BindPrimary(PrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            IntegerLiteralExpressionPrimaryNode integer => LiteralBinder.BindIntegerLiteral(integer, context),
            HexLiteralExpressionPrimaryNode hex => LiteralBinder.BindHexLiteral(hex, context),
            FloatLiteralExpressionPrimaryNode floatLit => LiteralBinder.BindFloatLiteral(floatLit, context),
            StringLiteralExpressionPrimaryNode stringLit => LiteralBinder.BindStringLiteral(stringLit, context),
            CharLiteralExpressionPrimaryNode charLit => LiteralBinder.BindCharLiteral(charLit, context),
            TrueLiteralExpressionPrimaryNode trueLit => LiteralBinder.BindBoolLiteral(trueLit, true, context),
            FalseLiteralExpressionPrimaryNode falseLit => LiteralBinder.BindBoolLiteral(falseLit, false, context),
            IdentifierExpressionPrimaryNode identifier => IdentifierBinder.BindIdentifier(identifier, ctx, context),
            ParenthesizedExpressionPrimaryNode paren => BindExpression(paren.Inner, ctx, context),
            ArrayCreationExpressionPrimaryNode arrayCreation => BindArrayCreation(arrayCreation, ctx, context),
            InterpolatedStringExpressionPrimaryNode interpStr => BindInterpolatedString(interpStr, ctx, context),
            _ => new UnresolvedExpression(node),
        };
    }

    private static Expression BindArrayCreation(ArrayCreationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var elementType = ResolveElementType(node.Type, ctx, context);
        var size = BindExpression(node.Size, ctx, context);
        return new ArrayCreationExpression(node, elementType, size, elementType);
    }

    private static IMistType ResolveElementType(ElementTypeRefNode elementTypeRef, BindingContext ctx, BuilderContext context)
    {
        return elementTypeRef switch
        {
            PrimitiveElementTypeRefElementTypeRefNode primitiveRef => ResolvePrimitiveElementType(primitiveRef, context),
            NamedElementTypeRefElementTypeRefNode namedRef => context.Resolver.ResolveNamedType(namedRef.Name.IDENTIFIER.Lexeme, ctx.Scope, namedRef),
            _ => ErrorType.Instance,
        };
    }

    private static IMistType ResolvePrimitiveElementType(PrimitiveElementTypeRefElementTypeRefNode node, BuilderContext context)
    {
        var wellKnown = node.PrimitiveType switch
        {
            IntTypePrimitiveTypeNode => WellKnownTypes.Int32,
            UintTypePrimitiveTypeNode => WellKnownTypes.UInt32,
            LongTypePrimitiveTypeNode => WellKnownTypes.Int64,
            UlongTypePrimitiveTypeNode => WellKnownTypes.UInt64,
            ShortTypePrimitiveTypeNode => WellKnownTypes.Int16,
            UshortTypePrimitiveTypeNode => WellKnownTypes.UInt16,
            ByteTypePrimitiveTypeNode => WellKnownTypes.Byte,
            SbyteTypePrimitiveTypeNode => WellKnownTypes.SByte,
            FloatTypePrimitiveTypeNode => WellKnownTypes.Single,
            DoubleTypePrimitiveTypeNode => WellKnownTypes.Double,
            DecimalTypePrimitiveTypeNode => WellKnownTypes.Decimal,
            BoolTypePrimitiveTypeNode => WellKnownTypes.Boolean,
            CharTypePrimitiveTypeNode => WellKnownTypes.Char,
            StringTypePrimitiveTypeNode => WellKnownTypes.String,
            _ => null,
        };

        if(wellKnown is null)
        {
            return ErrorType.Instance;
        }

        return context.Resolver.LookupWellKnown(wellKnown);
    }

    private static Expression BindInterpolatedString(InterpolatedStringExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var parts = new List<InterpolatedStringPart>();

        foreach(var part in node.InterpolatedString.Parts)
        {
            switch(part)
            {
                case TextPartInterpolatedPartNode textPart:
                    parts.Add(new InterpolatedStringText(textPart.Text.Lexeme));
                    break;

                case ExpressionPartInterpolatedPartNode exprPart:
                    var expr = BindExpression(exprPart.Expression, ctx, context);
                    parts.Add(new InterpolatedStringHole(expr));
                    break;
            }
        }

        var type = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        return new InterpolatedStringExpression(node, parts, type);
    }
}