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
    public static void CloneAll(Model.ProgramModel programModel)
    {
        var clones = new Dictionary<Newtype, ConcreteType?>();
        var placeholders = programModel.TypesByFQN.Values
            .OfType<Newtype>()
            .Where(newtype => newtype.Assembly.HasMistSource)
            .ToList();

        foreach(var newtype in placeholders)
        {
            _ = CloneOf(newtype, programModel, clones, []);
        }
    }

    private static ConcreteType? CloneOf(
        Newtype newtype, Model.ProgramModel programModel, Dictionary<Newtype, ConcreteType?> clones, HashSet<Newtype> visiting)
    {
        if(clones.TryGetValue(newtype, out var known))
        {
            return known;
        }

        if(!visiting.Add(newtype))
        {
            return null;
        }

        var clone = SourceOf(newtype, programModel, clones, visiting) is { } source
            ? Replace(newtype, source, programModel)
            : null;

        clones[newtype] = clone;
        return clone;
    }

    private static ConcreteType? SourceOf(
        Newtype newtype, Model.ProgramModel programModel, Dictionary<Newtype, ConcreteType?> clones, HashSet<Newtype> visiting)
    {
        if(newtype.TypeParametersList.Count > 0
            || newtype.DeclarationNode.SourceType is not { SingleTypeRefs.IsEmpty: true, First: NamedSingleTypeRefSingleTypeRefNode named }
            || named is not { TypeArgs: null, ArrayRank: null, Optional: null })
        {
            return null;
        }

        var name = named.Name.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(named.Name) : named.Name.IDENTIFIER.Lexeme;

        return newtype.Scope.Lookup(new LookupRequest(name, MemberVisibility.None, named)) switch
        {
            TypeResult { Type: Newtype other } => CloneOf(other, programModel, clones, visiting)?.CloneSource,
            TypeResult { Type: ConcreteType { CloneSource: { } root } } => root,
            TypeResult { Type: ConcreteType { DeclarationNode: BodyTypeTypeDeclarationNode body, TypeParametersList.Count: 0 } concrete }
                when !body.TypeBody.Modifiers.Any(modifier => modifier is IntrinsicModifierModifierNode) => concrete,
            _ => null,
        };
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