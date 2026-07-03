using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A property declared on a type.
/// </summary>
internal sealed class Property(
    string name,
    IMistType type,
    MemberModifiers modifiers) : IProperty
{
    public string Name { get; } = name;

    public IMistType Type { get; } = type;

    public MemberModifiers Modifiers { get; } = modifiers;

    /// <summary>
    /// The getter accessor, or null if the property has no getter.
    /// </summary>
    public PropertyAccessor? Getter { get; init; }

    /// <summary>
    /// The setter accessor, or null if the property has no setter.
    /// </summary>
    public PropertyAccessor? Setter { get; init; }

    /// <summary>
    /// The property initializer expression, or null if none.
    /// </summary>
    public Expression? Initializer { get; init; }
}