using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves an AST modifier list into a <see cref="MemberModifiers"/> value with
/// scope-based default accessibility already applied. This is the single point of
/// truth for mapping parsed modifiers to the program model — all member builders
/// delegate here to avoid drift.
/// </summary>
internal static class ModifierBuilder
{
    /// <summary>
    /// Builds a <see cref="MemberModifiers"/> from a node that implements
    /// <see cref="IHasModifiersNode"/>. Applies the scope-based default accessibility
    /// when no explicit access modifier is present. The node itself is used as the
    /// diagnostic location. Reports diagnostics for universally-invalid combinations
    /// (e.g. public + private, multiple substructural disciplines).
    /// </summary>
    public static MemberModifiers Build<TNode>(TNode node, DiagnosticBag diagnostics, MistType? declaringType)
        where TNode : SyntaxNode, IHasModifiersNode
    {
        var modifiers = node.Modifiers;

        if(modifiers.IsDefault)
        {
            return new MemberModifiers { Accessibility = GetScopeDefault(declaringType) };
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
                    result = result with { IsSealed = true };
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
        else if(accessModifierCount == 0)
        {
            result = result with { Accessibility = GetScopeDefault(declaringType) };
        }

        ValidateUniversalConflicts(accessModifierCount, diagnostics, node, modifiers);

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
    internal static Accessibility GetScopeDefault(MistType? declaringType)
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