namespace Mist.Semantics.Contracts;

/// <summary>
/// Classifies type constructors at the kind level (tier 3 in the value → type → kind
/// hierarchy). Class denotes reference types; Struct denotes value types.
/// </summary>
public enum TypeKind
{
    Class,
    Struct,
    Interface,
    Trait,
    Enum,

    /// <summary>
    /// The user-facing <c>unknown</c> top type. Every non-<c>void</c> value is assignable
    /// to a location of this kind, but <c>unknown</c> is forbidden as the declared type of
    /// writable storage (fields, properties with backing fields, indexer elements,
    /// <c>new</c> expressions, generic-construction type arguments). Lowered to C#
    /// <c>object</c> by the emitter.
    /// </summary>
    Unknown,

    /// <summary>
    /// An abstract type slot declared in an interface or trait. Implementors must
    /// provide a concrete type member satisfying the slot's constraints.
    /// </summary>
    AssociatedTypeSlot,
}

/// <summary>
/// A type in the Mist semantic model.
/// </summary>
public interface IType
{
    string DisplayName { get; }
    TypeKind Kind { get; }
}
