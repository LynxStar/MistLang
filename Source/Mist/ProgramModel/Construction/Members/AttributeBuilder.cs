using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves the attribute lists on a declaration into <see cref="DeclaredAttribute"/> instances.
/// <para>
/// BRACKET GROUPING IS DELIBERATELY NOT PRESERVED. <c>[A, B]</c> and <c>[A][B]</c> apply the same two
/// attributes, so a model that told them apart would be recording how the author typed rather than
/// what they meant, and emission would then be reproducing a source file instead of stating a
/// program.
/// </para>
/// <para>
/// THE SUFFIX CONVENTION IS A FILTER, NOT A FALLBACK. <c>[Range(0, 100)]</c> must reach
/// <c>RangeAttribute</c> even though <c>Range</c> also resolves -- to the slicing struct, which is
/// not an attribute class at all. Trying the written name first and stopping at the first hit binds
/// that struct, and the mistake survives emission unseen: the name goes out as written and C#, which
/// does apply the filter, then binds the attribute the author meant. Only the model is wrong, and
/// nothing downstream would ever say so.
/// </para>
/// </summary>
internal sealed class AttributeBuilder(BuilderContext context)
{
    private const string ConventionSuffix = "Attribute";
    private const string AttributeBaseName = "System.Attribute";

    public void Build(ImmutableArray<AttributeListNode> lists, List<DeclaredAttribute> target, Scope scope)
    {
        if(lists.IsDefaultOrEmpty)
        {
            return;
        }

        foreach(var list in lists)
        {
            foreach(var attribute in AllAttributes(list))
            {
                target.Add(Resolve(attribute, scope));
            }
        }
    }

    private static IEnumerable<AttributeNode> AllAttributes(AttributeListNode list)
    {
        yield return list.Attribute;

        foreach(var attribute in list.Attributes)
        {
            yield return attribute;
        }
    }

    private static IEnumerable<AttributeArgumentNode> AllArguments(AttributeArgumentListNode list)
    {
        yield return list.AttributeArgument;

        foreach(var argument in list.AttributeArguments)
        {
            yield return argument;
        }
    }

    private DeclaredAttribute Resolve(AttributeNode node, Scope scope)
    {
        var attributeType = ResolveAttributeType(node, scope);
        var positional = new List<Expression>();
        var named = new List<(string, Expression)>();

        if(node.Arguments?.ArgList is { } argumentList)
        {
            foreach(var argument in AllArguments(argumentList))
            {
                switch(argument)
                {
                    case NamedAttributeArgumentAttributeArgumentNode namedArgument:
                        named.Add((namedArgument.Name.Lexeme, BindValue(namedArgument.Value, scope)));
                        break;
                    case PositionalAttributeArgumentAttributeArgumentNode positionalArgument:
                        positional.Add(BindValue(positionalArgument.Value, scope));
                        break;
                }
            }
        }

        return new DeclaredAttribute(attributeType, positional, named);
    }

    private IMistType ResolveAttributeType(AttributeNode node, Scope scope)
    {
        var written = QualifiedNameHelper.Build(node.Name);
        var arity = node.TypeArgs is null ? 0 : 1 + node.TypeArgs.TypeArguments.Length;

        var writtenName = WithArity(written, arity);
        var definition = PickAttributeClass(
            ProbeWithoutReporting(writtenName, scope),
            ProbeWithoutReporting(WithArity(written + ConventionSuffix, arity), scope));

        if(definition is null)
        {
            return context.Resolver.ResolveNamedType(writtenName, scope, node);
        }

        context.ProgramModel.TypeNamings.Record(definition, scope, node);

        if(node.TypeArgs is null)
        {
            return definition;
        }

        var typeArguments = context.Resolver.ResolveTypeArguments(node.TypeArgs, scope);
        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArguments);
    }

    private static IMistType? PickAttributeClass(IMistType? written, IMistType? suffixed)
    {
        if(DerivesFromAttribute(written))
        {
            return written;
        }

        if(DerivesFromAttribute(suffixed))
        {
            return suffixed;
        }

        return written ?? suffixed;
    }

    private static bool DerivesFromAttribute(IMistType? type)
    {
        for(var current = type; current is not null; current = (current as ConcreteType)?.BaseType)
        {
            if(current.FullyQualifiedName == AttributeBaseName)
            {
                return true;
            }
        }

        return false;
    }

    private static string WithArity(string name, int arity)
    {
        return arity == 0 ? name : ScopeResolver.BuildArityName(name, arity);
    }

    private static IMistType? ProbeWithoutReporting(string name, Scope scope)
    {
        return scope.Lookup(new LookupRequest(name, MemberVisibility.None, LocationNode: null)) is TypeResult found
            ? found.Type
            : null;
    }

    private Expression BindValue(AttributeExpressionNode value, Scope scope)
    {
        return value switch
        {
            GenericTypeLiteralExpressionAttributeExpressionNode generic => BindGenericTypeLiteral(generic, scope),
            RegularExpressionAttributeExpressionNode regular => AsValue(BindOrdinary(regular.Expr, scope)),
            _ => new ErrorExpression(value),
        };
    }

    private Expression BindGenericTypeLiteral(
        GenericTypeLiteralExpressionAttributeExpressionNode node, Scope scope)
    {
        var name = QualifiedNameHelper.Build(node.Name);
        var arity = 1 + node.TypeArgs.TypeArguments.Length;
        var definition = context.Resolver.ResolveNamedType(WithArity(name, arity), scope, node);

        if(definition is ErrorType)
        {
            return new ErrorExpression(node);
        }

        var typeArguments = context.Resolver.ResolveTypeArguments(node.TypeArgs, scope);
        var referenced = context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArguments);

        return new TypeLiteralExpression(node, referenced, context.Resolver.LookupWellKnown(WellKnownTypes.Type));
    }

    private Expression BindOrdinary(ExpressionNode node, Scope scope)
    {
        var bindingContext = new BindingContext(new BodyScope(new InitializerScope(scope)), returnType: null);

        return ExpressionBinder.BindExpression(node, bindingContext, context);
    }

    /// <summary>
    /// A bare type name is a VALUE here, where in an ordinary expression it is nearly always the
    /// RECEIVER of a static member access. Binding cannot tell the two apart and settles on the
    /// receiver reading, which emits the name unadorned. Left alone that puts <c>[Proxy(Widget)]</c>
    /// into the output as C# that does not compile -- and nothing upstream reports it, because
    /// every step of the binding succeeded.
    /// </summary>
    private static Expression AsValue(Expression bound)
    {
        return bound is TypeReferenceExpression reference
            ? new TypeLiteralExpression(reference.Node, reference.ReferencedType, reference.Type)
            : bound;
    }
}