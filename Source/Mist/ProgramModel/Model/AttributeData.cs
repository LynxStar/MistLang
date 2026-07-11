using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Represents a single applied attribute with its resolved type.
/// Argument resolution is deferred until invocation expression handling is implemented.
/// </summary>
public sealed class AttributeData(
    IMistType attributeType,
    AttributeNode? attributeNode)
{
    /// <summary>The resolved attribute class type.</summary>
    public IMistType AttributeType { get; } = attributeType;

    /// <summary>
    /// The AST node for this attribute. Contains the argument list for source attributes.
    /// Null for assembly-projected attributes (which have no source AST).
    /// </summary>
    public AttributeNode? AttributeNode { get; } = attributeNode;
}
