namespace Mist.Semantics.Contracts;

/// <summary>
/// Classifies type constructors at the kind level (tier 3 in the value → type → kind
/// hierarchy). Class denotes reference types; Struct denotes value types.
/// </summary>
public enum TypeKind
{
    Class,
    Struct,
}

/// <summary>
/// A type in the Mist semantic model.
/// </summary>
public interface IType
{
    string DisplayName { get; }
    TypeKind Kind { get; }
}
