using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
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

        var clone = SourceOf(newtype, context, clones, visiting) is { } source
            ? Replace(newtype, source, context.ProgramModel)
            : null;

        clones[newtype] = clone;
        return clone;
    }

    private static ConcreteType? SourceOf(
        Newtype newtype, BuilderContext context, Dictionary<Newtype, ConcreteType?> clones, HashSet<Newtype> visiting)
    {
        if(newtype.TypeParametersList.Count > 0 || newtype.DeclarationNode.SourceType is not { SingleTypeRefs.IsEmpty: true } reference)
        {
            return null;
        }

        var named = reference.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode { ArrayRank: null, Optional: null } keyword => context.Resolver.Resolve(keyword, newtype.Scope),
            NamedSingleTypeRefSingleTypeRefNode { TypeArgs: null, ArrayRank: null, Optional: null } name
                => newtype.Scope.Lookup(new LookupRequest(Stem(name), MemberVisibility.None, name)) is TypeResult found ? found.Type : null,
            _ => null,
        };

        return named switch
        {
            Newtype other => CloneOf(other, context, clones, visiting)?.CloneSource,
            ConcreteType { CloneSource: { } root } => root,
            ConcreteType { DeclarationNode: BodyTypeTypeDeclarationNode body, TypeParametersList.Count: 0 } concrete
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

    private static string Stem(NamedSingleTypeRefSingleTypeRefNode named)
    {
        return named.Name.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(named.Name) : named.Name.IDENTIFIER.Lexeme;
    }

    private static ConcreteType Replace(Newtype newtype, ConcreteType source, Model.ProgramModel programModel)
    {
        var scope = new CloneScope(source.Scope.Parent, source);
        ConcreteType clone = source is StructType
            ? new StructType(newtype.Name, newtype.NamespaceName, scope, newtype.DeclaringType, source.DeclarationNode)
            : new ClassType(newtype.Name, newtype.NamespaceName, scope, newtype.DeclaringType, source.DeclarationNode);

        scope.Clone = clone;
        clone.CloneSource = source;
        clone.NewtypeDeclaration = newtype.DeclarationNode;
        clone.Assembly = newtype.Assembly;

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