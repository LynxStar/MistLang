using System.Collections.Immutable;
using Mist.Generated;

namespace Mist.Parsing;

internal static class AccessModifierSyntax
{
    public static bool IsAccessModifier(ModifierNode modifier)
    {
        return modifier is PublicModifierModifierNode
            or PrivateModifierModifierNode
            or ProtectedModifierModifierNode
            or InternalModifierModifierNode;
    }

    /// <summary>
    /// Every access keyword is a standalone level: Mist has no combined levels, so <c>protected internal</c>
    /// counts two, as does a repeated keyword (full-accessibility-model Requirement 2.6).
    /// </summary>
    public static int CountAccessModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        return modifiers.IsDefault ? 0 : modifiers.Count(IsAccessModifier);
    }
}