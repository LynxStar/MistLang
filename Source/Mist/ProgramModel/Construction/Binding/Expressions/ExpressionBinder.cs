using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
        // Ahead of the assignment unwrap because a lambda is an Expression ALTERNATIVE rather than
        // something reached through the precedence chain -- the grammar puts it at the loosest binding
        // there is, so `x => a = b` is a lambda whose body assigns and not an assignment of a lambda.
        if(expression is LambdaFormExpressionNode { Lambda: { } lambda })
        {
            return LambdaBinder.Bind(lambda, ctx, context);
        }

        var assignment = expression.AsAssignmentOrNull();
        if(assignment is null)
        {
            return new UnresolvedExpression(expression);
        }

        if(assignment.Group0 is { } assignmentOp)
        {
            var isCoalescing = assignmentOp.Operator.Lexeme == "??=";

            if(assignmentOp.Operator.Lexeme == "=" || isCoalescing)
            {
                var target = OperatorBinder.BindVoidCoalescing(assignment.Target, ctx, context);

                // A coalescing assignment's value fills the target's ABSENCE, so it binds
                // against the non-void portion rather than the optional itself.
                var valueTarget = isCoalescing
                    ? OptionalLifting.NonVoidPortion(target.Type, context.ProgramModel.TypeInterner)
                    : target.Type;

                Expression value;
                using(ctx.PushTargetType(valueTarget))
                {
                    value = BindExpression(assignmentOp.Value, ctx, context);
                }

                return new AssignmentExpression(expression, target, value, value.Type, isCoalescing);
            }

            return new UnresolvedExpression(expression);
        }

        return OperatorBinder.BindVoidCoalescing(assignment.Target, ctx, context);
    }

    internal static Expression BindPrimary(PrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return node switch
        {
            IntegerLiteralExpressionPrimaryNode integer => LiteralBinder.BindIntegerLiteral(
                integer, integer.IntegerLiteral.Lexeme, context),
            HexLiteralExpressionPrimaryNode hex => LiteralBinder.BindHexLiteral(hex, hex.HexLiteral.Lexeme, context),
            FloatLiteralExpressionPrimaryNode floatLit => LiteralBinder.BindFloatLiteral(
                floatLit, floatLit.FloatLiteral.Lexeme, context),
            StringLiteralExpressionPrimaryNode stringLit => LiteralBinder.BindStringLiteral(
                stringLit, stringLit.StringLiteral.Lexeme, context, ctx.TargetType),
            CharLiteralExpressionPrimaryNode charLit => LiteralBinder.BindCharLiteral(
                charLit, charLit.CharLiteral.Lexeme, context),
            TrueLiteralExpressionPrimaryNode trueLit => LiteralBinder.BindBoolLiteral(trueLit, true, context),
            FalseLiteralExpressionPrimaryNode falseLit => LiteralBinder.BindBoolLiteral(falseLit, false, context),
            ArrayLiteralExpressionPrimaryNode arrayLiteral => LiteralBinder.BindArrayLiteral(arrayLiteral, ctx, context),
            TupleLiteralExpressionPrimaryNode tupleLiteral => LiteralBinder.BindTupleLiteral(tupleLiteral, ctx, context),
            AnonymousTypeExpressionPrimaryNode anonymous => AnonymousObjectBinder.Bind(anonymous, ctx, context),

            IdentifierExpressionPrimaryNode identifier => IdentifierBinder.BindIdentifier(identifier, ctx, context),
            GenericInvocationExpressionPrimaryNode genericInvocation => PostfixBinder.BindGenericInvocation(
                genericInvocation, ctx, context),

            ParenthesizedExpressionPrimaryNode paren => BindExpression(paren.Inner, ctx, context),
            ArrayCreationExpressionPrimaryNode arrayCreation => BindArrayCreation(arrayCreation, ctx, context),
            StackallocExpressionPrimaryNode allocation => BindStackalloc(allocation, ctx, context),
            InterpolatedStringExpressionPrimaryNode interpStr => BindInterpolatedString(interpStr, ctx, context),
            ThisExpressionPrimaryNode thisExpr => BindThis(thisExpr, ctx, context),
            ObjectCreationExpressionPrimaryNode objectCreation => BindObjectCreation(objectCreation, ctx, context),
            ObjectCreationExpressionNoArgsPrimaryNode noArgs => BindObjectCreationNoArgs(noArgs, ctx, context),
            TargetTypedNewExpressionPrimaryNode targetTypedNew => BindTargetTypedNew(targetTypedNew, ctx, context),
            FieldKeywordExpressionPrimaryNode fieldKeyword => BindFieldKeyword(fieldKeyword, ctx, context),
            DefaultExpressionPrimaryNode named => new DefaultExpression(
                named, context.Resolver.Resolve(named.Type, ctx.Scope)),
            DefaultLiteralExpressionPrimaryNode bare => BindBareDefault(bare, ctx, context),

            TypeLiteralExpressionPrimaryNode typeLiteral => BindTypeLiteral(typeLiteral, ctx, context),
            RegexLiteralExpressionPrimaryNode regexLiteral => LiteralBinder.BindRegexLiteral(
                regexLiteral, context),

            _ => new UnresolvedExpression(node),
        };
    }

    /// <summary>
    /// Binds a type spelled in value position. For <c>void</c> the target type decides among
    /// three readings -- the unit value where a void is expected, absence where an optional
    /// is, and the type literal everywhere else -- so one token never needs three spellings.
    /// </summary>
    private static Expression BindTypeLiteral(
        TypeLiteralExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var referenced = context.Resolver.Resolve(node.TypeRef, ctx.Scope);

        if(referenced is ErrorType)
        {
            return new ErrorExpression(node);
        }

        if(OptionalUnion.IsVoid(referenced) && ctx.TargetType is { } target)
        {
            if(OptionalUnion.IsVoid(target))
            {
                return new VoidLiteralExpression(node, VoidReading.Singleton, target);
            }

            if(OptionalUnion.ContainsVoid(target))
            {
                return new VoidLiteralExpression(node, VoidReading.Absence, target);
            }
        }

        return new TypeLiteralExpression(
            node, referenced, context.Resolver.LookupWellKnown(WellKnownTypes.Type));
    }

    /// <summary>
    /// Binds a bare <c>default</c>, whose type is the one the position expects.
    /// </summary>
    private static Expression BindBareDefault(
        DefaultLiteralExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        if(ctx.TargetType is not { } target || target is ErrorType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.DefaultNeedsTargetType, node, null);
            return new ErrorExpression(node);
        }

        return new DefaultExpression(node, target);
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
    /// Binds <c>new T(...)</c>, generic or not, selecting the constructor its arguments pick out.
    /// </summary>
    private static Expression BindObjectCreation(ObjectCreationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return BindNamedCreation(node, node.Type, node.TypeArgs, node.Arguments, node.Initializer, ctx, context);
    }

    /// <summary>
    /// Binds <c>new T { ... }</c>, whose omitted argument list is a separate grammar alternative
    /// rather than an empty one, so the two forms cannot share a node type.
    /// </summary>
    private static Expression BindObjectCreationNoArgs(
        ObjectCreationExpressionNoArgsPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        return BindNamedCreation(node, node.Type, node.TypeArgs, null, node.Initializer, ctx, context);
    }

    private static Expression BindNamedCreation(
        Storm.Runtime.SyntaxNode node,
        QualifiedIdentifierNode typeNode,
        TypeArgumentListNode? typeArgList,
        ArgumentListNode? argumentList,
        PropertyInitializerBlockNode? initializer,
        BindingContext ctx,
        BuilderContext context)
    {
        var typeName = QualifiedNameHelper.Build(typeNode);

        // The `new` grammar names the type as a qualified identifier rather than a type
        // reference, so the lookup here bypasses ScopeResolver.ResolveNamed and its alias
        // expansion; a constructible alias (`new IntBox(42)`) must therefore expand here.
        IMistType type;
        if(typeArgList is not null)
        {
            var arity = 1 + typeArgList.TypeArguments.Length;
            var definition = context.Resolver.ResolveGenericDefinition(typeName, arity, ctx.Scope, node);
            if(definition is ErrorType)
            {
                return new ErrorExpression(node);
            }

            var typeArgs = context.Resolver.BindPack(
                definition, context.Resolver.ResolveTypeArguments(typeArgList, ctx.Scope), node);

            if(definition is TypeAlias alias)
            {
                type = context.Resolver.ExpandAlias(alias, typeArgs);
            }
            else
            {
                context.ProgramModel.ConstructorConstraintClosures.RecordType(definition, typeArgs, node);
                type = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
            }
        }
        else
        {
            // Through the shared entry point, so a bare name reaches a variadic family the way a
            // written argument list does: `new Box()` is the arity-zero construction, not a
            // reference to an unconstructed definition.
            var definition = context.Resolver.ResolveGenericDefinition(typeName, arity: 0, ctx.Scope, node);

            if(definition is TypeAlias alias)
            {
                type = context.Resolver.ExpandAlias(
                    alias, ScopeResolver.Absorbs(alias) ? context.Resolver.BindPack(alias, [], node) : null);
            }
            else if(ScopeResolver.Absorbs(definition))
            {
                var typeArgs = context.Resolver.BindPack(definition, [], node);
                type = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
            }
            else
            {
                type = definition;
            }
        }

        return BindConstruction(node, type, argumentList, initializer, ctx, context);
    }

    /// <summary>
    /// Binds <c>new(...)</c>, which names no type and takes the one the position expects.
    /// </summary>
    private static Expression BindTargetTypedNew(
        TargetTypedNewExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        if(ctx.TargetType is not { } target || target is ErrorType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.TargetTypedNewNeedsTargetType, node, null);
            return new ErrorExpression(node);
        }

        if(Definition(target) is not ConcreteType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TargetTypeNotConstructible, node, null,
                MistType.StripArity(target.Name));

            return new ErrorExpression(node);
        }

        return BindConstruction(node, target, node.Arguments, node.Initializer, ctx, context);
    }

    private static Expression BindConstruction(
        Storm.Runtime.SyntaxNode node,
        IMistType type,
        ArgumentListNode? argumentList,
        PropertyInitializerBlockNode? initializer,
        BindingContext ctx,
        BuilderContext context)
    {
        var arguments = ArgumentBinder.Bind(
            argumentList, ConstructorsOf(type), ctx, context, type as ConstructedType);

        var choice = SelectConstructor(
            type, arguments, ArgumentBinder.RefFlags(argumentList), node, context);

        var settled = MethodGroupConversion.Settle(
            arguments, choice.Selected?.ParametersList, choice.Substitution, context);

        return new NewExpression(
            node,
            type,
            OwnedProvenance.AtParameters(settled, choice.Selected?.ParametersList),
            choice.Selected,
            ObjectInitializerBinder.Bind(initializer, type, ctx, context));
    }

    /// <summary>
    /// A constructed type unwrapped to the definition its members are declared on, and anything else
    /// returned as it is.
    /// </summary>
    private static IMistType Definition(IMistType type)
    {
        return (type as ConstructedType)?.Definition ?? type;
    }

    /// <summary>
    /// The type whose constructors a construction selects from: a newtype declares none of its
    /// own, so construction reaches through to its source's -- the emitted wrapper carries the
    /// source's constructors under the newtype's name. The expression's type stays the newtype.
    /// </summary>
    private static IMistType ConstructionTarget(IMistType type)
    {
        var definition = Definition(type);

        if(definition is Newtype { SourceType: { } newtypeSource })
        {
            return Definition(newtypeSource);
        }

        return definition;
    }

    private static IReadOnlyList<Constructor> ConstructorsOf(IMistType type)
    {
        return ConstructionTarget(type) is ConcreteType concrete ? concrete.ConstructorsList : [];
    }

    /// <summary>
    /// The constructor the arguments select, with the substitution its signature has to be read
    /// through. Nothing selected and no diagnostic whenever no constructor could be named at all --
    /// the definition declares none, or is not a concrete type to declare any -- because a construction
    /// that reaches neither is already being reported elsewhere or is a C# default with nothing to
    /// choose between.
    /// <para>
    /// A constructed type is unwrapped to reach the definition its constructors are declared on, and
    /// passed on as the source of the arguments to substitute in.
    /// </para>
    /// </summary>
    private static OverloadChoice<Constructor> SelectConstructor(
        IMistType type,
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<bool> argumentIsRef,
        Storm.Runtime.SyntaxNode node,
        BuilderContext context)
    {
        var constructed = type as ConstructedType;
        var definition = ConstructionTarget(type);

        if(definition is not ConcreteType { ConstructorsList.Count: > 0 } concrete)
        {
            return new OverloadChoice<Constructor> { Selected = null };
        }

        var choice = OverloadResolution.Select(
            concrete.ConstructorsList, arguments, constructed, null, context.ProgramModel.TypeInterner,
            argumentIsRef);

        if(choice.Selected is null)
        {
            var name = MistType.StripArity(definition.Name);

            OverloadFailure.Report(
                choice, concrete.ConstructorsList, arguments.Count,
                $"constructor '{name}'", node, context.Diagnostics);
        }

        return choice;
    }

    private static Expression BindArrayCreation(ArrayCreationExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var elementType = ResolveElementType(node.Type, ctx, context);
        var size = BindExpression(node.Size, ctx, context);
        // The size is an arbitrary expression, so the length is not statically known.
        var arrayType = context.Resolver.ConstructArray(elementType, null);
        return new ArrayCreationExpression(node, elementType, size, arrayType);
    }

    /// <summary>
    /// One syntax produces two types, and only the POSITION tells them apart: `stackalloc T[n]` says
    /// nothing about mutability, so a readonly-span binding is what makes the result a readonly span.
    /// Anything else -- including no target at all -- takes the mutable one, which converts to the
    /// readonly span anyway and so is the answer that closes off least.
    /// </summary>
    private static Expression BindStackalloc(
        StackallocExpressionPrimaryNode node, BindingContext ctx, BuilderContext context)
    {
        var elementType = ResolveElementType(node.Type, ctx, context);
        var size = BindExpression(node.Size, ctx, context);

        // Read off the DEFINITION rather than the target's own name, which carries its arguments and so
        // never equals the bare declaration's.
        var definition = ctx.TargetType is ConstructedType constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.ReadOnlySpan.FullyQualifiedName
                ? WellKnownTypes.ReadOnlySpan
                : WellKnownTypes.Span
                ;

        // The size is an arbitrary expression, so the length is not statically known.
        return new StackallocExpression(
            node, elementType, size, context.Resolver.ConstructLengthIndexed(definition, elementType, null));
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