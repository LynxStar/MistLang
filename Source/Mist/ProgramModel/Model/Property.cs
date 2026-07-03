using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A property declared on a type.
/// </summary>
internal sealed class Property(
    string name,
    IMistType type,
    bool hasGetter,
    bool hasSetter,
    bool isStatic) : IProperty
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public bool HasGetter { get; } = hasGetter;

    public bool HasSetter { get; } = hasSetter;

    public bool IsStatic { get; } = isStatic;
}