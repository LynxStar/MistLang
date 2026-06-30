namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A tuple type (e.g., (int, string)).
/// </summary>
public interface ITupleTypeReference : ITypeReference
{
    /// <summary>
    /// The element types of the tuple, in order.
    /// </summary>
    IReadOnlyList<ITypeReference> ElementTypes { get; }
}