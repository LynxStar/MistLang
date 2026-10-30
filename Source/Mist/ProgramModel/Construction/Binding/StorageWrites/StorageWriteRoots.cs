using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.StorageWrites;

/// <summary>
/// One bound body a member's classification reads: the member, the direction the body answers for, the
/// tree, the type the body was written inside, and the type parameters in scope there.
/// </summary>
internal readonly record struct StorageWriteRoot(
    object Member, StorageAccess Side, object? Root, MistType? Owner, HashSet<TypeParameter> Scope);

/// <summary>
/// Every body a type parameter is in scope of, across the compilation's Mist source, as trees. A
/// member the binder has finished is read as it stands; one it has not reached, or is in the middle
/// of, is bound here in a trial whose diagnostics are withheld, whose registry records are discarded,
/// and whose trees are detached before the member's slots are restored -- the real binding fills them
/// later, and must find them as it would have.
/// </summary>
internal static class StorageWriteRoots
{
    public static IReadOnlyList<StorageWriteRoot> Gather(BuilderContext context, ISet<object> complete)
    {
        var roots = new List<StorageWriteRoot>();

        context.Diagnostics.Withhold(() => context.ProgramModel.Discarding(() =>
        {
            foreach(var type in SourceMemberTypes(context.ProgramModel))
            {
                GatherType(type, context, complete, roots);
            }

            foreach(var block in context.ProgramModel.Namespaces.Values.SelectMany(ns => ns.Extensions.Values).SelectMany(blocks => blocks))
            {
                if(block.DeclarationNode is not null)
                {
                    GatherBlock(block, context, complete, roots);
                }
            }

            return 0;
        })).Discard();

        return roots;
    }

    private static IEnumerable<MemberType> SourceMemberTypes(Model.ProgramModel model)
    {
        var seen = new HashSet<MistType>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<MistType>(model.TypesByFQN.Values.OfType<MistType>().Where(type => type.Assembly.HasMistSource));

        while(pending.Count > 0)
        {
            var type = pending.Pop();

            if(!seen.Add(type))
            {
                continue;
            }

            foreach(var nested in type.NestedTypes)
            {
                pending.Push(nested);
            }

            if(type is MemberType member)
            {
                yield return member;
            }
        }
    }

    private static HashSet<TypeParameter> ParametersInScope(MistType? type)
    {
        var scope = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);

        for(var current = type; current is not null; current = current.DeclaringType)
        {
            if(current is IHasTypeParameters generic)
            {
                scope.UnionWith(generic.TypeParametersList);
            }
        }

        return scope;
    }

    private static void GatherType(MemberType type, BuilderContext context, ISet<object> complete, List<StorageWriteRoot> roots)
    {
        var typeScope = ParametersInScope(type);

        foreach(var method in type.EnumerateDelegates())
        {
            if(method.Body is null || method is CastOperator { ClonesThrough: not null })
            {
                continue;
            }

            var scope = new HashSet<TypeParameter>(typeScope, ReferenceEqualityComparer.Instance);
            scope.UnionWith(method.TypeParametersList);

            if(scope.Count > 0)
            {
                GatherDelegate(method, type, scope, context, complete, roots);
            }
        }

        if(typeScope.Count == 0)
        {
            return;
        }

        foreach(var property in type.PropertiesList)
        {
            GatherAccessors(property, property.Getter, property.Setter, property.Type, [], property.Modifiers.IsStatic, property, type, typeScope, context, complete, roots);
        }

        foreach(var indexer in type.IndexersList)
        {
            GatherAccessors(indexer, indexer.Getter, indexer.Setter, indexer.Type, indexer.ParametersList, indexer.Modifiers.IsStatic, owningProperty: null, type, typeScope, context, complete, roots);
        }

        if(type is not ConcreteType concrete)
        {
            return;
        }

        foreach(var field in concrete.FieldsList)
        {
            GatherInitializer(field, field.Initializer, field.Type, concrete, typeScope, context, complete, roots);
        }

        foreach(var property in type.PropertiesList)
        {
            GatherInitializer(property, property.Initializer, property.Type, concrete, typeScope, context, complete, roots);
        }
    }

    private static void GatherBlock(ExtensionBlock block, BuilderContext context, ISet<object> complete, List<StorageWriteRoot> roots)
    {
        var blockScope = new HashSet<TypeParameter>(block.TypeParametersList, ReferenceEqualityComparer.Instance);

        foreach(var method in block.MethodsList)
        {
            var scope = new HashSet<TypeParameter>(blockScope, ReferenceEqualityComparer.Instance);
            scope.UnionWith(method.TypeParametersList);

            if(method.Body is not null && scope.Count > 0)
            {
                GatherDelegate(method, owner: null, scope, context, complete, roots);
            }
        }

        if(blockScope.Count == 0)
        {
            return;
        }

        foreach(var property in block.PropertiesList)
        {
            GatherAccessors(property, property.Getter, property.Setter, property.Type, [], property.Modifiers.IsStatic, property, owner: null, blockScope, context, complete, roots, block.Scope);
        }
    }

    private static void GatherDelegate(
        Method method, MistType? owner, HashSet<TypeParameter> scope, BuilderContext context, ISet<object> complete, List<StorageWriteRoot> roots)
    {
        if(complete.Contains(method))
        {
            roots.Add(new StorageWriteRoot(method, StorageAccess.Read, method.Body, owner, scope));

            if(method is Constructor bound)
            {
                roots.AddRange(bound.ChainArgumentsList.Select(argument => new StorageWriteRoot(method, StorageAccess.Read, argument, owner, scope)));
            }

            return;
        }

        var body = method.Body!;
        var statements = body.Statements;
        var constructor = method as Constructor;
        (List<Expression> Arguments, Constructor? Chained, IReadOnlyList<IMistType>? ParameterTypes)? chain = constructor is null
            ? null
            : (constructor.ChainArgumentsList.ToList(), constructor.ChainedConstructor, constructor.ChainParameterTypes);
        constructor?.ChainArgumentsList.Clear();

        BodyBinder.BindDelegate(method, context);

        roots.Add(new StorageWriteRoot(method, StorageAccess.Read, new Block(body.Node) { Statements = body.Statements }, owner, scope));

        if(constructor is not null)
        {
            roots.AddRange(constructor.ChainArgumentsList.Select(argument => new StorageWriteRoot(method, StorageAccess.Read, argument, owner, scope)));
            constructor.ChainArgumentsList.Clear();
            constructor.ChainArgumentsList.AddRange(chain!.Value.Arguments);
            constructor.ChainedConstructor = chain.Value.Chained;
            constructor.ChainParameterTypes = chain.Value.ParameterTypes;
        }

        body.Statements = statements;
    }

    /// <summary>
    /// An initializer runs in every constructor, so its tree is a root of each; a type declaring none
    /// has no member the initializer could be reached through.
    /// </summary>
    private static void GatherInitializer(
        object member,
        Expression? parked,
        IMistType memberType,
        ConcreteType owner,
        HashSet<TypeParameter> scope,
        BuilderContext context,
        ISet<object> complete,
        List<StorageWriteRoot> roots)
    {
        if(parked is null || owner.ConstructorsList.Count == 0)
        {
            return;
        }

        var bound = complete.Contains(member) ? parked : BodyBinder.BindInitializer(parked, memberType, owner.Scope, context);

        if(bound is null)
        {
            return;
        }

        roots.AddRange(owner.ConstructorsList.Select(constructor => new StorageWriteRoot(constructor, StorageAccess.Read, bound, owner, scope)));
    }

    private static void GatherAccessors(
        object member,
        PropertyAccessor? getter,
        PropertyAccessor? setter,
        IMistType valueType,
        IReadOnlyList<Parameter> parameters,
        bool isStatic,
        Property? owningProperty,
        MemberType? owner,
        HashSet<TypeParameter> scope,
        BuilderContext context,
        ISet<object> complete,
        List<StorageWriteRoot> roots,
        Scope? blockScope = null)
    {
        if(complete.Contains(member))
        {
            Add(member, StorageAccess.Read, getter, owner, scope, roots);
            Add(member, StorageAccess.Write, setter, owner, scope, roots);
            return;
        }

        var saved = (Snapshot(getter), Snapshot(setter));

        BodyBinder.BindAccessors(getter, setter, valueType, parameters, isStatic, owningProperty, blockScope ?? owner!.Scope, context);

        AddDetached(member, StorageAccess.Read, getter, owner, scope, roots);
        AddDetached(member, StorageAccess.Write, setter, owner, scope, roots);

        Restore(getter, saved.Item1);
        Restore(setter, saved.Item2);
    }

    private static void Add(object member, StorageAccess side, PropertyAccessor? accessor, MistType? owner, HashSet<TypeParameter> scope, List<StorageWriteRoot> roots)
    {
        if(accessor is { IsAuto: false } && (accessor.Body ?? (object?)accessor.ExpressionBody) is { } root)
        {
            roots.Add(new StorageWriteRoot(member, side, root, owner, scope));
        }
    }

    private static void AddDetached(object member, StorageAccess side, PropertyAccessor? accessor, MistType? owner, HashSet<TypeParameter> scope, List<StorageWriteRoot> roots)
    {
        if(accessor is not { IsAuto: false })
        {
            return;
        }

        object? root = accessor.Body is { } body ? new Block(body.Node) { Statements = body.Statements } : accessor.ExpressionBody;

        if(root is not null)
        {
            roots.Add(new StorageWriteRoot(member, side, root, owner, scope));
        }
    }

    private static (IReadOnlyList<Statement>? Statements, Expression? ExpressionBody) Snapshot(PropertyAccessor? accessor)
    {
        return (accessor?.Body?.Statements, accessor?.ExpressionBody);
    }

    private static void Restore(PropertyAccessor? accessor, (IReadOnlyList<Statement>? Statements, Expression? ExpressionBody) saved)
    {
        if(accessor is null)
        {
            return;
        }

        if(accessor.Body is { } body && saved.Statements is { } statements)
        {
            body.Statements = statements;
        }

        accessor.ExpressionBody = saved.ExpressionBody;
    }
}