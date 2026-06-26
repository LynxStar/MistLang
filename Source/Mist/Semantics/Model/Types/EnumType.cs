using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A user-defined enum type. Enums are nominal collections of static values
/// that wrap an underlying type.
/// </summary>
public sealed class EnumType : IType
{
    public string DisplayName { get; }
    public TypeKind Kind => TypeKind.Enum;
    public IType UnderlyingType { get; internal set; }
    public IReadOnlyList<EnumMemberInfo> Members { get; internal set; }
    public IScope MemberScope { get; }
    public string? ContainingNamespace { get; }

    /// <summary>The defaultability status of this enum type.</summary>
    public DefaultabilityStatus Defaultability { get; internal set; } = DefaultabilityStatus.Unknown;

    /// <summary>The designated default variant, if any.</summary>
    public EnumMemberInfo? DefaultMember { get; internal set; }

    public EnumType(
        string displayName,
        IScope memberScope,
        string? containingNamespace)
    {
        DisplayName = displayName;
        MemberScope = memberScope;
        ContainingNamespace = containingNamespace;
        UnderlyingType = null!;
        Members = Array.Empty<EnumMemberInfo>();
    }
}

/// <summary>
/// Describes a single member within an enum declaration.
/// </summary>
public sealed class EnumMemberInfo
{
    public string Name { get; }
    public object? ResolvedValue { get; internal set; }
    public IType? ValueType { get; internal set; }

    /// <summary>Whether this member is designated as the enum's default variant.</summary>
    public bool IsDefault { get; internal set; }

    public EnumMemberInfo(string name)
    {
        Name = name;
    }
}