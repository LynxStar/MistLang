using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A local variable or parameter introduced within a body. Holds the resolved
/// type of the binding.
/// </summary>
/// <remarks>
/// Body-binding skeleton: only the name and resolved type are modelled for now.
/// Ownership tier, ref-kind, and readonly-status will be added when the
/// corresponding binding features land.
/// </remarks>
internal sealed class LocalSymbol(string name, IMistType type)
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;
}