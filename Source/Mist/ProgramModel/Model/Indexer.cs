using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An indexer declared on a type.
/// </summary>
internal sealed class Indexer(
    IMistType type,
    IReadOnlyList<IParameter> parameters,
    bool isStatic) : IIndexer
{
    public IMistType Type { get; } = type;

    public IReadOnlyList<IParameter> Parameters { get; } = parameters;

    public bool IsStatic { get; } = isStatic;

    /// <summary>
    /// The getter accessor, or null if the indexer has no getter.
    /// </summary>
    public PropertyAccessor? Getter { get; init; }

    /// <summary>
    /// The setter accessor, or null if the indexer has no setter.
    /// </summary>
    public PropertyAccessor? Setter { get; init; }
}