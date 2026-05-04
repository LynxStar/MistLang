namespace Mist.Semantics.Contracts;

/// <summary>
/// Extension methods for <see cref="IType"/> that provide well-known type identity checks.
/// These replace the old pattern of checking <c>is VoidType</c> or <c>is TypeType</c>
/// against singleton metatypes. Now that Void and Type are stdlib-backed types
/// (StructType/ClassType), identity is checked by display name.
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// Returns <c>true</c> if this type represents the Mist <c>void</c> type
    /// (either the stdlib-backed <c>Standard.Void</c> or the legacy fallback).
    /// </summary>
    public static bool IsVoid(this IType type)
    {
        return type.DisplayName is "void" or "Void";
    }

    /// <summary>
    /// Returns <c>true</c> if this type represents the Mist metatype <c>Type</c>
    /// (either the stdlib-backed <c>Standard.Type</c> or the legacy fallback).
    /// </summary>
    public static bool IsTypeType(this IType type)
    {
        // TypeKind audit (unknown-type feature): restricting to TypeKind.Class is correct after
        // Unknown is introduced — UnknownType has Kind == TypeKind.Unknown, so it naturally
        // returns false here, which is the desired behavior (`unknown` is not the Type metatype).
        return type.DisplayName == "Type" && type.Kind == TypeKind.Class;
    }
}
