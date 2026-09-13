using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Replaces each newtype over a class or struct declared in Mist with its renamed clone (newtypes spec
/// Requirement 3.2), between registration and the first resolved signature. Registration cannot build
/// the clone, because a clone's kind is its source's and the source is only a name until every file is
/// registered; and anything later would leave signatures already holding the placeholder.
/// </summary>
internal static class NewtypeCloner
{
    public static void CloneAll(BuilderContext context)
    {
        var clones = new Dictionary<Newtype, ConcreteType?>();
        var placeholders = context.ProgramModel.TypesByFQN.Values
            .OfType<Newtype>()
            .Where(newtype => newtype.Assembly.HasMistSource)
            .ToList();

        foreach(var newtype in placeholders)
        {
            _ = CloneOf(newtype, context, clones, []);
        }
    }

    public static void BindSources(IEnumerable<ConcreteType> clones, BuilderContext context)
    {
        var bound = new HashSet<ConcreteType>();

        foreach(var clone in clones)
        {
            _ = BindSource(clone, context, bound);
        }
    }

    private static IMistType BindSource(ConcreteType clone, BuilderContext context, HashSet<ConcreteType> bound)
    {
        if(!bound.Add(clone))
        {
            return clone.CloneSourceType ?? ErrorType.Instance;
        }

        var scope = (CloneScope)clone.Scope.Parent!;
        var interner = context.ProgramModel.TypeInterner;
        scope.Itself = clone.TypeParametersList.Count == 0 ? clone : interner.GetOrCreateConstructedType(clone, clone.TypeParametersList);

        var source = context.Resolver.Resolve(clone.NewtypeDeclaration!.SourceType, scope.Declaration) switch
        {
            ConcreteType { CloneSource: not null } other when !ReferenceEquals(other, clone) => BindSource(other, context, bound),
            ConstructedType { Definition: ConcreteType { CloneSource: not null } other } constructed
                => TypeSubstitution.ForReceiver(constructed).Apply(BindSource(other, context, bound), interner),
            var resolved => resolved,
        };

        clone.CloneSourceType = source;
        scope.Arguments = source is ConstructedType constructedSource && ReferenceEquals(constructedSource.Definition, clone.CloneSource)
            ? constructedSource.TypeArguments
            : [.. clone.CloneSource!.TypeParametersList.Select(_ => ErrorType.Instance)];
        scope.Enclosing = (source as ConstructedType)?.Enclosing;

        return source;
    }

    private static ConcreteType? CloneOf(
        Newtype newtype, BuilderContext context, Dictionary<Newtype, ConcreteType?> clones, HashSet<Newtype> visiting)
    {
        if(clones.TryGetValue(newtype, out var known))
        {
            return known;
        }

        if(!visiting.Add(newtype))
        {
            return null;
        }

        var clone = newtype.DeclarationNode.SourceType is { SingleTypeRefs.IsEmpty: true } reference
            && DefinitionOf(reference.First, newtype.Scope, context, clones, visiting, []) is { } source
            ? Replace(newtype, source, context.ProgramModel)
            : null;

        clones[newtype] = clone;
        return clone;
    }

    private static ConcreteType? DefinitionOf(
        SingleTypeRefNode reference,
        Scope scope,
        BuilderContext context,
        Dictionary<Newtype, ConcreteType?> clones,
        HashSet<Newtype> visiting,
        HashSet<TypeAlias> aliases)
    {
        var named = reference switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode { ArrayRank: null, Optional: null } keyword => context.Resolver.Resolve(keyword, scope),
            NamedSingleTypeRefSingleTypeRefNode { ArrayRank: null, Optional: null } name
                => context.Resolver.FindGenericDefinition(Stem(name.Name), Arity(name.TypeArgs), scope),
            NestedGenericSingleTypeRefSingleTypeRefNode { TypeArgs: null, ArrayRank: null, Optional: null, Nested.IDENTIFIERs.IsEmpty: true } nested
                => (context.Resolver.FindGenericDefinition(Stem(nested.Outer), Arity(nested.OuterArgs), scope) as MistType)?.NestedTypes
                    .Find(member => member.Name == nested.Nested.IDENTIFIER.Lexeme),
            ParenthesizedTypeRefSingleTypeRefNode { ArrayRank: null, Inner.SingleTypeRefs.IsEmpty: true } parenthesized
                => DefinitionOf(parenthesized.Inner.First, scope, context, clones, visiting, aliases),
            _ => null,
        };

        return named switch
        {
            Newtype other => CloneOf(other, context, clones, visiting)?.CloneSource,
            TypeAlias { DeclarationNode.Type: { RefinableSingleTypeExprs.IsEmpty: true, First.Refinement: null } aliased } alias
                when aliases.Add(alias) => DefinitionOf(aliased.First.Type, alias.Scope, context, clones, visiting, aliases),
            ConcreteType { CloneSource: { } root } => root,
            ConcreteType { DeclarationNode: BodyTypeTypeDeclarationNode body } concrete
                when Cloneable(body.TypeBody.Modifiers) => concrete,
            _ => null,
        };
    }

    /// <summary>
    /// An intrinsic source is cloned only when it is readonly, so that its clone is a copyable wrapper
    /// around one backing value (newtypes spec Requirement 5.2); an intrinsic with state of its own to
    /// share, Console or a Task, is left for the newtype builder to refuse.
    /// </summary>
    internal static bool Cloneable(IEnumerable<ModifierNode> modifiers)
    {
        return !modifiers.Any(modifier => modifier is IntrinsicModifierModifierNode)
            || modifiers.Any(modifier => modifier is ReadonlyModifierModifierNode);
    }

    private static string Stem(QualifiedIdentifierNode name)
    {
        return name.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(name) : name.IDENTIFIER.Lexeme;
    }

    private static int Arity(TypeArgumentListNode? typeArgs)
    {
        return typeArgs is null ? 0 : 1 + typeArgs.TypeArguments.Length;
    }

    private static ConcreteType Replace(Newtype newtype, ConcreteType source, Model.ProgramModel programModel)
    {
        var scope = new CloneScope(source.Scope.Parent, source, newtype.Scope);
        ConcreteType clone = source is StructType
            ? new StructType(newtype.Name, newtype.NamespaceName, scope, newtype.DeclaringType, source.DeclarationNode)
            : new ClassType(newtype.Name, newtype.NamespaceName, scope, newtype.DeclaringType, source.DeclarationNode);

        scope.Clone = clone;
        clone.CloneSource = source;
        clone.NewtypeDeclaration = newtype.DeclarationNode;
        clone.Assembly = newtype.Assembly;
        clone.TypeParametersList.AddRange(newtype.TypeParametersList);

        programModel.ReplaceType(newtype, clone);

        if(newtype.DeclaringType is { } declaring)
        {
            declaring.NestedTypes[declaring.NestedTypes.IndexOf(newtype)] = clone;
        }

        foreach(var unit in newtype.Assembly.CompilationUnits)
        {
            if(unit.Types.IndexOf(newtype) is var index and >= 0)
            {
                unit.Types[index] = clone;
            }
        }

        return clone;
    }
}