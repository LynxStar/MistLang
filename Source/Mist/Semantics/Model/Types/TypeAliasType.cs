using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A transparent type alias that resolves to its underlying type during type checking.
/// The alias is erased after resolution — using an alias is semantically identical to
/// using the underlying type directly.
/// </summary>
public sealed class TypeAliasType : IType
{
    public string Name { get; }
    public IType UnderlyingType { get; }
    public IReadOnlyList<IType> TypeParameters { get; }
    public string DisplayName => Name;
    public TypeKind Kind => UnderlyingType.Kind;

    /// <summary>
    /// The resolved accessibility of this type alias.
    /// </summary>
    public Accessibility Accessibility { get; internal set; }

    public TypeAliasType(
        string name,
        IType underlyingType,
        IReadOnlyList<IType> typeParameters)
    {
        Name = name;
        UnderlyingType = underlyingType;
        TypeParameters = typeParameters;
    }
}