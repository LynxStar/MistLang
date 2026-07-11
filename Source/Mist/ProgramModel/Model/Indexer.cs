using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An indexer declared on a type.
/// </summary>
internal sealed class Indexer(
    IMistType type,
    List<Parameter> parameters,
    MemberModifiers modifiers) : IIndexer, IHasAttributes
{
    public IMistType Type { get; } = type;

    public List<Parameter> ParametersList { get; } = parameters;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The attributes applied to this indexer.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// The getter accessor, or null if the indexer has no getter.
    /// </summary>
    public PropertyAccessor? Getter { get; init; }

    /// <summary>
    /// The setter accessor, or null if the indexer has no setter.
    /// </summary>
    public PropertyAccessor? Setter { get; init; }

    IReadOnlyList<IParameter> IIndexer.Parameters => ParametersList;
    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}