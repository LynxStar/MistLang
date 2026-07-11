using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A parameter of a method, constructor, indexer, or operator.
/// </summary>
internal sealed class Parameter(
    string name,
    IMistType type) : IParameter, IHasAttributes
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    /// <summary>
    /// The attributes applied to this parameter.
    /// </summary>
    public List<AttributeData> AttributesList { get; } = [];

    /// <summary>
    /// The adhoc refinement predicate AST node for this parameter, if any
    /// (from a <c>where paramName >= 0</c> clause). Null until resolved.
    /// Interpretation deferred to the pattern resolution phase.
    /// </summary>
    public Storm.Runtime.SyntaxNode? RefinementNode { get; internal set; }

    IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
}