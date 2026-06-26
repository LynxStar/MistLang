using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A nominally distinct type that is a structural clone of its source type.
/// Unlike a type alias (which is transparent), a newtype is a separate type
/// in the type system — assignment between the newtype and its source requires
/// an explicit cast.
/// </summary>
public sealed class NewtypeType : IType
{
    public string Name { get; }
    public IType SourceType { get; }
    public IScope MemberScope { get; }
    public string DisplayName => Name;
    public TypeKind Kind => SourceType.Kind;

    public NewtypeType(string name, IType sourceType)
    {
        Name = name;
        SourceType = sourceType;
        MemberScope = new Scope(parent: null);
    }
}