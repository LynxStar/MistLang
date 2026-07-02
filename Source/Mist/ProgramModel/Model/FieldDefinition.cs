using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A field declared on a type.
/// </summary>
internal sealed class FieldDefinition(
    string name,
    ITypeReference type,
    OwnershipKind ownership,
    bool isReadonly,
    bool isStatic) : IFieldDefinition
{
    public string Name { get; } = name;

    public ITypeReference Type { get; } = type;

    public OwnershipKind Ownership { get; } = ownership;

    public bool IsReadonly { get; } = isReadonly;

    public bool IsStatic { get; } = isStatic;
}