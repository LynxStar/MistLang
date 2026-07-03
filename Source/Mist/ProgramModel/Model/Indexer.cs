using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An indexer declared on a type.
/// </summary>
internal sealed class Indexer(
    ITypeReference type,
    IReadOnlyList<IParameter> parameters,
    bool hasGetter,
    bool hasSetter) : IIndexer
{
    public ITypeReference Type { get; } = type;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public bool HasGetter { get; } = hasGetter;

    public bool HasSetter { get; } = hasSetter;
}