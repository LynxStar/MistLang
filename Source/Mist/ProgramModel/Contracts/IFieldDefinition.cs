using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A field in the program model.
/// </summary>
public interface IFieldDefinition
{
    /// <summary>
    /// The field name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The resolved type of this field.
    /// </summary>
    ITypeReference Type { get; }

    /// <summary>
    /// Whether this field is declared static.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// The ownership tier declared on this field.
    /// </summary>
    OwnershipKind Ownership { get; }

    /// <summary>
    /// The type that declares this field.
    /// </summary>
    ITypeDefinition DeclaringType { get; }

    /// <summary>
    /// The source location where this field was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}