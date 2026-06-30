using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An extension block in the program model. Declares methods, properties,
/// or other members that extend an existing type without modifying its definition.
/// </summary>
public interface IExtensionDefinition
{
    /// <summary>
    /// The receiver type this extension applies to.
    /// </summary>
    ITypeReference ReceiverType { get; }

    /// <summary>
    /// Methods declared in this extension block.
    /// </summary>
    IReadOnlyList<IMethodDefinition> Methods { get; }

    /// <summary>
    /// Properties declared in this extension block.
    /// </summary>
    IReadOnlyList<IPropertyDefinition> Properties { get; }

    /// <summary>
    /// Whether this is a universal extension (applies to all types).
    /// </summary>
    bool IsUniversal { get; }

    /// <summary>
    /// The source location where this extension was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}