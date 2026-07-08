using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
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
    /// Builds a <see cref="MemberModifiers"/> from the given modifier node list.
    /// Used for nodes that do not yet implement <see cref="IHasModifiersNode"/>
    /// (e.g. property declaration alternatives). Prefer the generic overload.
    /// Reports diagnostics for universally-invalid combinations (e.g. public + private,
    /// multiple substructural disciplines).
    /// Safe to call with a default (uninitialized) array.
    /// </summary>
    internal static MemberModifiers Build(ImmutableArray<ModifierNode> modifiers, DiagnosticBag diagnostics, SyntaxNode locationNode)
    {
        if(modifiers.IsDefault)
        {
            return default;
        }

        var result = new MemberModifiers();

        foreach(var mod in modifiers)
        {
            result = mod switch
            {
                PublicModifierModifierNode => result with { IsPublic = true },
                PrivateModifierModifierNode => result with { IsPrivate = true },
                StaticModifierModifierNode => result with { IsStatic = true },
                ReadonlyModifierModifierNode => result with { IsReadonly = true },
                AsyncModifierModifierNode => result with { IsAsync = true },
                IntrinsicModifierModifierNode => result with { IsIntrinsic = true },
                OwnedModifierModifierNode => result with { IsOwned = true },
                RefModifierModifierNode => result with { IsRef = true },
                AffineModifierModifierNode => result with { Discipline = SubstructuralDiscipline.Affine },
                LinearModifierModifierNode => result with { Discipline = SubstructuralDiscipline.Linear },
                RelevantModifierModifierNode => result with { Discipline = SubstructuralDiscipline.Relevant },
                _ => result,
            };
        }

        ValidateUniversalConflicts(result, diagnostics, locationNode, modifiers);

        return result;
    }

    /// <summary>
    /// Reports diagnostics for modifier combinations that are universally invalid
    /// regardless of declaration context.
    /// </summary>
    private static void ValidateUniversalConflicts(
        MemberModifiers result,
        DiagnosticBag diagnostics,
        SyntaxNode locationNode,
        ImmutableArray<ModifierNode> modifiers)
    {
        if(result.IsPublic && result.IsPrivate)
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