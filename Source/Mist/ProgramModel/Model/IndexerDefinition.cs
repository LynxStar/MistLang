using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An indexer declared on a type.
/// </summary>
internal sealed class IndexerDefinition(
    ITypeReference type,
    IReadOnlyList<IParameterDefinition> parameters,
    bool hasGetter,
    bool hasSetter) : IIndexerDefinition
{
    public ITypeReference Type { get; } = type;

    public IReadOnlyList<IParameterDefinition> Parameters { get; } = parameters;

    public bool HasGetter { get; } = hasGetter;

    public bool HasSetter { get; } = hasSetter;
}