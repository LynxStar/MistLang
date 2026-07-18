using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A single member (variant) of an enum type.
/// </summary>
internal sealed class EnumMember(string name, bool isDefault, Expression? value) : IEnumMember
{
    /// <summary>The variant name.</summary>
    public string Name { get; } = name;

    /// <summary>Whether this variant is marked as the enum's default.</summary>
    public bool IsDefault { get; } = isDefault;

    /// <summary>
    /// The value expression for this variant, or null for name-only variants.
    /// Stashed for later evaluation; not resolved during Phase 2.
    /// </summary>
    public Expression? Value { get; } = value;
}