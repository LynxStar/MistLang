using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

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
            ArrayLiteralExpressionPrimaryNode arrayLiteral => LiteralBinder.BindArrayLiteral(arrayLiteral, ctx, context),

            IdentifierExpressionPrimaryNode identifier => IdentifierBinder.BindIdentifier(identifier, ctx, context),

            ParenthesizedExpressionPrimaryNode paren => BindExpression(paren.Inner, ctx, context),
            ArrayCreationExpressionPrimaryNode arrayCreation => BindArrayCreation(arrayCreation, ctx, context),
            InterpolatedStringExpressionPrimaryNode interpStr => BindInterpolatedString(interpStr, ctx, context),
            ThisExpressionPrimaryNode thisExpr => BindThis(thisExpr, ctx, context),
            ObjectCreationExpressionPrimaryNode objectCreation => BindObjectCreation(objectCreation, ctx, context),
            FieldKeywordExpressionPrimaryNode fieldKeyword => BindFieldKeyword(fieldKeyword, ctx, context),

            _ => new UnresolvedExpression(node),
        };
    }

    private static Expression BindFieldKeyword(FieldKeywordExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        if(ctx.EnclosingProperty is not { } property)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.FieldKeywordOutsideAccessor, node, null);
            return new ErrorExpression(node);
        }

        return new BackingFieldReferenceExpression(node, property);
    }

    /// <summary>
    /// Binds <c>this</c>. The grammar admits it in any expression position, so both invalid
    /// placements are rejected here rather than left for the C# backend -- but neither is checked
    /// for: the receiver is RESOLVED, like any other name, and the two failures are just the two
    /// ways that resolution can come back. A static member answers that it has no instance; a
    /// chain with no type in it at all runs out and answers nothing.
    /// </summary>
    private static Expression BindThis(ThisExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        switch(ctx.Scope.Lookup(LookupRequest.Receiver(node)))
        {
            case ReceiverResult receiver:
                return new ThisExpression(node, receiver.Type);

            case StaticReceiverResult:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ThisInStaticMember, node, null);
                return new ErrorExpression(node);

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ThisOutsideType, node, null);
                return new ErrorExpression(node);
        }
    }

    /// <summary>
    /// Binds <c>new T(...)</c> and <c>new T&lt;...&gt;(...)</c> with no initializer block.
    /// The arguments are bound but the constructor overload is not selected; see
    /// <see cref="NewExpression"/>.
    /// </summary>
    private static Expression BindObjectCreation(ObjectCreationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        // A property initializer block changes what the expression constructs, so binding
        // the creation while dropping the block would silently emit a different object.
        // Left unresolved until initializers are supported.
        if(node.Initializer is not null)
        {
            return new UnresolvedExpression(node);
        }

        var typeName = QualifiedNameHelper.Build(node.Type);

        IMistType type;
        if(node.TypeArgs is { } typeArgList)
        {
            var arity = 1 + typeArgList.TypeArguments.Length;
            var arityName = ScopeResolver.BuildArityName(typeName, arity);
            var definition = context.Resolver.ResolveNamedType(arityName, ctx.Scope, node);
            if(definition is ErrorType)
            {
                return new ErrorExpression(node);
            }

            var typeArgs = context.Resolver.ResolveTypeArguments(typeArgList, ctx.Scope);
            type = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
        }
        else
        {
            type = context.Resolver.ResolveNamedType(typeName, ctx.Scope, node);
        }

        var arguments = ArgumentBinder.Bind(node.Arguments, ctx, context);

        return new NewExpression(node, type, arguments);
    }

    private static Expression BindArrayCreation(ArrayCreationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var elementType = ResolveElementType(node.Type, ctx, context);
        var size = BindExpression(node.Size, ctx, context);
        // The size is an arbitrary expression, so the length is not statically known.
        var arrayType = context.Resolver.ConstructArray(elementType, null);
        return new ArrayCreationExpression(node, elementType, size, arrayType);
    }

    private static IMistType ResolveElementType(ElementTypeRefNode elementTypeRef, BindingContext ctx, BuilderContext context)
    {
        return elementTypeRef switch
        {
            PrimitiveElementTypeRefElementTypeRefNode primitiveRef => ResolvePrimitiveElementType(primitiveRef, context),
            UnknownElementTypeRefElementTypeRefNode => UnknownType.Instance,
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