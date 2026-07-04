using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An indexer declared on a type.
/// </summary>
public interface IIndexer
{
    /// <summary>The resolved return type of the indexer.</summary>
    IMistType Type { get; }

    /// <summary>The indexer parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>The modifiers declared on this indexer.</summary>
    MemberModifiers Modifiers { get; }
}