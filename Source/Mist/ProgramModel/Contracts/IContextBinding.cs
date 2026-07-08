using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A context binding declared via a <c>with Type name</c> clause on a method
/// or constructor signature. The context value is peeked from the structured
/// context stack at call entry and bound to the declared identifier.
/// </summary>
public interface IContextBinding
{
    /// <summary>The resolved type of the context value.</summary>
    IMistType ContextType { get; }

    /// <summary>The name bound to the context value inside the callable body.</summary>
    string Identifier { get; }
}