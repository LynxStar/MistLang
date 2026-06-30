using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A property or indexer in the program model.
/// </summary>
public interface IPropertyDefinition
{
    /// <summary>
    /// The property name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The resolved type of this property.
    /// </summary>
    ITypeReference Type { get; }

    /// <summary>
    /// Whether this property has a getter.
    /// </summary>
    bool HasGetter { get; }

    /// <summary>
    /// Whether this property has a setter.
    /// </summary>
    bool HasSetter { get; }

    /// <summary>
    /// Whether this property is declared static.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// The type that declares this property.
    /// </summary>
    ITypeDefinition DeclaringType { get; }

    /// <summary>
    /// The source location where this property was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}