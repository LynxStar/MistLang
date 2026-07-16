using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves an AST modifier list into a <see cref="MemberModifiers"/> value.
/// This is the single point of truth for mapping parsed modifiers to the
/// program model — all member builders delegate here to avoid drift.
/// </summary>
internal static class ModifierBuilder
{
    /// <summary>
    /// Builds a <see cref="MemberModifiers"/> directly from a node that implements
    /// <see cref="IHasModifiersNode"/>. The node itself is used as the diagnostic location.
    /// </summary>
    public static MemberModifiers Build<TNode>(TNode node, DiagnosticBag diagnostics)
        where TNode : SyntaxNode, IHasModifiersNode
    {
        return Build(node.Modifiers, diagnostics, node);
    }

    /// <summary>
    /// Builds a <see cref="MemberModifiers"/> directly from a node that implements
    /// <see cref="IHasModifiersNode"/>, additionally returning whether the <c>sealed</c>
    /// modifier was present. The node itself is used as the diagnostic location.
    /// </summary>
    public static MemberModifiers Build<TNode>(TNode node, DiagnosticBag diagnostics, out bool hasSealed)
        where TNode : SyntaxNode, IHasModifiersNode
    {
        return Build(node.Modifiers, diagnostics, node, out hasSealed);
    }

    /// <summary>
    /// Builds a <see cref="MemberModifiers"/> from the given modifier node list.
    /// Used for nodes that do not yet implement <see cref="IHasModifiersNode"/>
    /// (e.g. property declaration alternatives). Prefer the generic overload.
    /// Reports diagnostics for universally-invalid combinations (e.g. public + private,
    /// multiple substructural disciplines).
    /// Safe to call with a default (uninitialized) array.
    /// </summary>
    internal static MemberModifiers Build(ImmutableArray<ModifierNode> modifiers, DiagnosticBag diagnostics, SyntaxNode locationNode)
    {
        return Build(modifiers, diagnostics, locationNode, out _);
    }

    /// <summary>
    /// Core build overload that also reports whether the <c>sealed</c> modifier was present.
    /// </summary>
    internal static MemberModifiers Build(
        ImmutableArray<ModifierNode> modifiers,
        DiagnosticBag diagnostics,
        SyntaxNode locationNode,
        out bool hasSealed)
    {
        hasSealed = false;

        if(modifiers.IsDefault)
        {
            return default;
        }

        var result = new MemberModifiers();
        var accessModifierCount = 0;
        Accessibility accessibility = default;

        foreach(var mod in modifiers)
        {
            switch(mod)
            {
                case PublicModifierModifierNode:
                    accessibility = Accessibility.Public;
                    accessModifierCount++;
                    break;
                case PrivateModifierModifierNode:
                    accessibility = Accessibility.Private;
                    accessModifierCount++;
                    break;
                case ProtectedModifierModifierNode:
                    accessibility = Accessibility.Protected;
                    accessModifierCount++;
                    break;
                case InternalModifierModifierNode:
                    accessibility = Accessibility.Internal;
                    accessModifierCount++;
                    break;
                case SealedModifierModifierNode:
                    hasSealed = true;
                    break;
                case StaticModifierModifierNode:
                    result = result with { IsStatic = true };
                    break;
                case ReadonlyModifierModifierNode:
                    result = result with { IsReadonly = true };
                    break;
                case AsyncModifierModifierNode:
                    result = result with { IsAsync = true };
                    break;
                case IntrinsicModifierModifierNode:
                    result = result with { IsIntrinsic = true };
                    break;
                case OwnedModifierModifierNode:
                    result = result with { IsOwned = true };
                    break;
                case RefModifierModifierNode:
                    result = result with { IsRef = true };
                    break;
                case AffineModifierModifierNode:
                    result = result with { Discipline = SubstructuralDiscipline.Affine };
                    break;
                case LinearModifierModifierNode:
                    result = result with { Discipline = SubstructuralDiscipline.Linear };
                    break;
                case RelevantModifierModifierNode:
                    result = result with { Discipline = SubstructuralDiscipline.Relevant };
                    break;
            }
        }

        if(accessModifierCount == 1)
        {
            result = result with { Accessibility = accessibility };
        }

        ValidateUniversalConflicts(accessModifierCount, diagnostics, locationNode, modifiers);

        return result;
    }

    /// <summary>
    /// Returns the scope-kind default accessibility for a declaration based on
    /// its declaring (enclosing) type. This mirrors the C# defaults:
    /// <list type="bullet">
    ///   <item>Top-level (no declaring type) → <see cref="Accessibility.Internal"/></item>
    ///   <item>Inside a class or struct → <see cref="Accessibility.Private"/></item>
    ///   <item>Inside an interface (abstract) or enum → <see cref="Accessibility.Public"/></item>
    /// </list>
    /// Nested types inside a class/struct default to <see cref="Accessibility.Private"/>.
    /// </summary>
    public static Accessibility GetScopeDefault(MistType? declaringType)
    {
        return declaringType switch
        {
            null => Accessibility.Internal,
            ConcreteType => Accessibility.Private,
            AbstractType => Accessibility.Public,
            EnumType => Accessibility.Public,
            _ => Accessibility.Private,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the modifier list contains at least one explicit access
    /// modifier (<c>public</c>, <c>private</c>, <c>protected</c>, or <c>internal</c>).
    /// </summary>
    public static bool HasExplicitAccessModifier(ImmutableArray<ModifierNode> modifiers)
    {
        if(modifiers.IsDefault)
        {
            return false;
        }

        foreach(var mod in modifiers)
        {
            if(mod is PublicModifierModifierNode
                or PrivateModifierModifierNode
                or ProtectedModifierModifierNode
                or InternalModifierModifierNode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies the scope-based default accessibility to a <see cref="MemberModifiers"/> value
    /// when no explicit access modifier was present in the modifier list. Returns the
    /// modifiers unchanged when an explicit access modifier was specified.
    /// </summary>
    public static MemberModifiers ApplyDefault(MemberModifiers modifiers, ImmutableArray<ModifierNode> rawModifiers, MistType? declaringType)
    {
        if(HasExplicitAccessModifier(rawModifiers))
        {
            return modifiers;
        }

        return modifiers with { Accessibility = GetScopeDefault(declaringType) };
    }

    /// <summary>
    /// Reports diagnostics for modifier combinations that are universally invalid
    /// regardless of declaration context.
    /// </summary>
    private static void ValidateUniversalConflicts(
        int accessModifierCount,
        DiagnosticBag diagnostics,
        SyntaxNode locationNode,
        ImmutableArray<ModifierNode> modifiers)
    {
        if(accessModifierCount > 1)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.ConflictingAccessModifiers, locationNode, null);
        }

        if(HasMultipleDisciplines(modifiers))
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.ConflictingDisciplineModifiers, locationNode, null);
        }
    }

    /// <summary>
    /// Returns true if more than one substructural discipline modifier is present.
    /// </summary>
    private static bool HasMultipleDisciplines(ImmutableArray<ModifierNode> modifiers)
    {
        var count = 0;

        foreach(var mod in modifiers)
        {
            if(mod is AffineModifierModifierNode or LinearModifierModifierNode or RelevantModifierModifierNode)
            {
                count++;

                if(count > 1)
                {
                    return true;
                }
            }
        }

        return false;
    }
}