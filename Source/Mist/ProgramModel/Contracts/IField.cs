using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A field declared on a type.
/// </summary>
public interface IField
{
    /// <summary>The field name.</summary>
    string Name { get; }

    /// <summary>The resolved type of the field.</summary>
    ITypeReference Type { get; }

    /// <summary>The ownership classification of this field.</summary>
    OwnershipKind Ownership { get; }

    /// <summary>Whether the field is readonly.</summary>
    bool IsReadonly { get; }

    /// <summary>Whether the field is static.</summary>
    bool IsStatic { get; }
}