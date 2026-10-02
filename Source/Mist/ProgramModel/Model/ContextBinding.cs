using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Storm.Runtime;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A resolved context binding declared via a <c>with Type name</c> clause on a
/// method or constructor. At call entry the context value is peeked from the
/// structured context stack and bound to the declared identifier.
/// </summary>
internal sealed class ContextBinding : IContextBinding
{
    public ContextBinding(IMistType contextType, string identifier)
    {
        ContextType = contextType;
        Identifier = identifier;
    }

    /// <summary>The resolved type of the context value.</summary>
    public IMistType ContextType { get; }

    /// <summary>The name bound to the peeked context value inside the callable body.</summary>
    public string Identifier { get; }

    public SyntaxNode? Node { get; init; }

    internal static IReadOnlyList<IMistType> TypesOf(Method? callee, Func<IMistType, IMistType>? closing = null)
    {
        return callee is { ContextBindingsList.Count: > 0 }
            ? [.. callee.ContextBindingsList.Select(binding => closing is null ? binding.ContextType : closing(binding.ContextType))]
            : [];
    }
}