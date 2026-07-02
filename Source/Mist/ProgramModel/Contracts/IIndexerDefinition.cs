using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An indexer declared on a type.
/// </summary>
public interface IIndexerDefinition
{
    /// <summary>The resolved return type of the indexer.</summary>
    ITypeReference Type { get; }

    /// <summary>The indexer parameters.</summary>
    IReadOnlyList<IParameterDefinition> Parameters { get; }

    /// <summary>Whether the indexer has a getter.</summary>
    bool HasGetter { get; }

    /// <summary>Whether the indexer has a setter.</summary>
    bool HasSetter { get; }
}