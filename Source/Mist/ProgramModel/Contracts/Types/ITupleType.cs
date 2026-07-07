namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A tuple type composed of ordered element types with optional element names
/// (e.g., <c>(int, bool)</c> or <c>(int x, bool flag)</c>).
/// Element names are cosmetic and do not affect type identity.
/// </summary>
public interface ITupleType : IMistType
{
    /// <summary>
    /// The resolved element types in positional order.
    /// </summary>
    IReadOnlyList<IMistType> ElementTypes { get; }

    /// <summary>
    /// Optional element names in positional order. Null entries indicate unnamed elements.
    /// </summary>
    IReadOnlyList<string?> ElementNames { get; }
}