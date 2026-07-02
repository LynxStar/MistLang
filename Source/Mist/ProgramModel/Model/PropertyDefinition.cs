using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A property declared on a type.
/// </summary>
internal sealed class PropertyDefinition(
    string name,
    ITypeReference type,
    bool hasGetter,
    bool hasSetter,
    bool isStatic) : IPropertyDefinition
{
    public string Name { get; } = name;

    public ITypeReference Type { get; } = type;

    public bool HasGetter { get; } = hasGetter;

    public bool HasSetter { get; } = hasSetter;

    public bool IsStatic { get; } = isStatic;
}