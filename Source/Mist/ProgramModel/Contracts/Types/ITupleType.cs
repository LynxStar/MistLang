namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A tuple type composed of ordered element types (e.g., <c>(int, bool)</c>).
/// Tuple types are purely structural: identity depends on element types only.
/// Element names, when present, are use-site metadata exposed through
/// <see cref="INamedTupleType"/> rather than being part of the type itself.
/// </summary>
public interface ITupleType : IMistType
{
    /// <summary>
    /// The resolved element types in positional order.
    /// </summary>
    IReadOnlyList<IMistType> ElementTypes { get; }
}