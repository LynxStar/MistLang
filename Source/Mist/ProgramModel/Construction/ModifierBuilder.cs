using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts;
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
    /// Builds a <see cref="MemberModifiers"/> from the given modifier node list.
    /// Reports diagnostics for universally-invalid combinations (e.g. public + private).
    /// Safe to call with a default (uninitialized) array.
    /// </summary>
    public static MemberModifiers Build(ImmutableArray<ModifierNode> modifiers, DiagnosticBag diagnostics, SyntaxNode locationNode)
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
                _ => result,
            };
        }

        if(result.IsPublic && result.IsPrivate)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.ConflictingAccessModifiers, locationNode, null);
        }

        return result;
    }
}