namespace Mist.Semantics.Contracts;

/// <summary>
/// Kinds of types in the Mist type system.
/// Concrete members deferred to the Mist-language spec.
/// </summary>
public enum TypeKind
{
    Primitive,
    Function,
    Record,
    Union,
    Unknown,
}

/// <summary>
/// A type in the Mist semantic model.
/// </summary>
public interface IType
{
    string DisplayName { get; }
    TypeKind Kind { get; }
}
