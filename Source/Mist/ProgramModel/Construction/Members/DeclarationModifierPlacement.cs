using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Refuses only placements a spec rules out: <c>async</c> on a type declaration (structured-concurrency
/// Requirement 2.3, read as <see cref="ProgramModelDiagnostics.AsyncOnlyOnMethods"/> reads it for members,
/// whose builders report it themselves) and <c>ref</c> on an operator (standard-library Requirement 4.2).
/// <c>static</c> on a type, <c>owned</c> or a discipline on an enum, alias or newtype, and <c>readonly</c>
/// on an alias are deliberately absent: no spec decides them, so refusing them would invent a rule.
/// </summary>
internal static class DeclarationModifierPlacement
{
    public static IEnumerable<DiagnosticDescriptor> Refusals(SyntaxNode declaration, ImmutableArray<ModifierNode> modifiers)
    {
        return modifiers.IsDefaultOrEmpty
            ? []
            : modifiers.Select(modifier => Refusal(declaration, modifier)).OfType<DiagnosticDescriptor>().Distinct();
    }

    private static DiagnosticDescriptor? Refusal(SyntaxNode declaration, ModifierNode modifier)
    {
        return (declaration, modifier) switch
        {
            (EnumDeclarationNode or TypeAliasDeclarationNode or NewtypeDeclarationNode or TypeBodyDeclarationNode, AsyncModifierModifierNode)
                => ProgramModelDiagnostics.AsyncOnlyOnMethods,
            (OperatorDeclarationNode, RefModifierModifierNode) => DeclarationModifierDiagnostics.RefOnOperator,
            _ => null,
        };
    }
}