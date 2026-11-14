using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A <c>with Type name</c> clause on a method or constructor signature: the value is peeked from the
/// structured context stack at call entry and bound to the name.
/// </summary>
public interface IContextBinding
{
    IMistType ContextType { get; }

    string Identifier { get; }
}