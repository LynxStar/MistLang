namespace Mist.ProgramModel.Contracts;

/// <summary>
/// The fully resolved semantic representation of a Mist program.
/// Contains all declared types, namespaces, and their relationships —
/// navigable without reference to syntax trees or the legacy semantic model.
/// </summary>
// CA1040: Intentionally empty during incremental development — members will be added
// as the contract solidifies.
#pragma warning disable CA1040
public interface IProgramModel
{
}
#pragma warning restore CA1040