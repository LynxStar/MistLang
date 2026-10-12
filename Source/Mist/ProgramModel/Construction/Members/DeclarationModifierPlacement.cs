using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Refuses only placements a spec rules out: <c>async</c> on a type declaration (structured-concurrency
/// Requirement 2.3, read as <see cref="ProgramModelDiagnostics.AsyncOnlyOnMethods"/> reads it for members,
/// whose builders report it themselves), unsupported operator modifiers (standard-library Requirement 4.2),
/// and <c>ref</c> on a field (ref-parameters Requirement 6.5).
/// <c>static</c> on a type, <c>owned</c> or a discipline on an enum, alias or newtype, and <c>readonly</c>
/// on an alias are deliberately absent: no spec decides them, so refusing them would invent a rule.
/// </summary>
internal static class DeclarationModifierPlacement
{
    public static IEnumerable<DiagnosticDescriptor> Refusals(SyntaxNode declaration, ImmutableArray<ModifierNode> modifiers)
    {
        return modifiers.IsDefaultOrEmpty
            ? []
            : modifiers.Select(modifier => Refusal(declaration, modifier, modifiers)).OfType<DiagnosticDescriptor>().Distinct();
    }

    private static DiagnosticDescriptor? Refusal(SyntaxNode declaration, ModifierNode modifier, ImmutableArray<ModifierNode> modifiers)
    {
        return (declaration, modifier) switch
        {
            (EnumDeclarationNode or TypeAliasDeclarationNode or NewtypeDeclarationNode or TypeBodyDeclarationNode, AsyncModifierModifierNode)
                => ProgramModelDiagnostics.AsyncOnlyOnMethods,
            (OperatorDeclarationNode, RefModifierModifierNode) => DeclarationModifierDiagnostics.RefOnOperator,
            (OperatorDeclarationNode, SealedModifierModifierNode) => DeclarationModifierDiagnostics.SealedOnOperator,
            (OperatorDeclarationNode, ReadonlyModifierModifierNode) when !modifiers.Any(written => written is RefModifierModifierNode)
                => DeclarationModifierDiagnostics.ReadonlyOnOperator,
            (OperatorDeclarationNode, OwnedModifierModifierNode) => DeclarationModifierDiagnostics.OwnedOnOperator,
            (OperatorDeclarationNode, AffineModifierModifierNode) => DeclarationModifierDiagnostics.AffineOnOperator,
            (FieldDeclarationNode, RefModifierModifierNode) => DeclarationModifierDiagnostics.RefOnField,
            _ => null,
        };
    }
}