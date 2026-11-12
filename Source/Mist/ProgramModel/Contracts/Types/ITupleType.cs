namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Purely structural: identity is the element types alone. Element names are use-site metadata on
/// <see cref="INamedTupleType"/>.
/// </summary>
public interface ITupleType : IMistType
{
    IReadOnlyList<IMistType> ElementTypes { get; }
}