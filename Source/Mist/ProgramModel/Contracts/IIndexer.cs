using Mist.ProgramModel.Contracts.Types;

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

    /// <summary>Whether the indexer has a getter.</summary>
    bool HasGetter { get; }

    /// <summary>Whether the indexer has a setter.</summary>
    bool HasSetter { get; }
}