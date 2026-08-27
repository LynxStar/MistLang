using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A local variable or parameter introduced within a body. Holds the resolved
/// type of the binding.
/// </summary>
/// <remarks>
/// Body-binding skeleton: ownership tier and readonly-status will be added when the corresponding
/// binding features land. Ref-ness is recorded already, but only so the backend can REFUSE it --
/// a binding whose alias is dropped computes a wrong answer silently, which is worth catching
/// before the aliasing itself is built.
/// </remarks>
internal sealed class LocalVariable(string name, IMistType type)
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public bool IsRef { get; init; }
}