using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// The one mapping from parsed modifiers to <see cref="MemberModifiers"/>; every member builder
/// delegates here so the mapping cannot drift.
/// </summary>
internal static class ModifierBuilder
{
    public static MemberModifiers Build<TNode>(TNode node, DiagnosticBag diagnostics, MistType? declaringType)
        where TNode : SyntaxNode, IHasModifiersNode
    {
        var modifiers = node.Modifiers;

        if(modifiers.IsDefault)
        {
            return new MemberModifiers { Accessibility = DefaultAccessibility(node, declaringType) };
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
                case PureModifierModifierNode:
                    result = result with { IsPure = true, IsReadonly = true };
                    break;
                case LocalModifierModifierNode:
                    result = result with { IsLocal = true };
                    break;
                case StampableModifierModifierNode:
                    result = result with { IsStampable = true };
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
            result = result with { Accessibility = DefaultAccessibility(node, declaringType) };
        }

        ValidateUniversalConflicts(diagnostics, node, modifiers);

        if(result.IsStampable && (result.IsRef || node is not TypeBodyDeclarationNode { Keyword: StructKeywordTypeKeywordNode }))
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.StampableRequiresStruct, node, null);
        }

        ValidatePurity(node, diagnostics, modifiers, result);

        foreach(var refusal in DeclarationModifierPlacement.Refusals(node, modifiers))
        {
            diagnostics.ReportAtNodeTyped(refusal, node, null);
        }

        return result;
    }

    /// <summary>
    /// A MEMBER's modifiers, which inherit the declaring type's <c>readonly</c>: the readonly-contexts
    /// spec makes every member of a readonly type readonly (Req 8.1), and a member that did not record
    /// it could not be judged as the context it is. A pure type's <c>pure</c> and a local type's
    /// <c>local</c> reach members the same way (purity spec Req 15.2), combined with what the member
    /// wrote as though both were written on it. Kept apart from <see cref="Build"/> because that one
    /// also builds a nested TYPE's modifiers, where the same parameter is the ENCLOSING type and
    /// none of these reaches through it.
    /// </summary>
    public static MemberModifiers ForMember<TNode>(TNode node, DiagnosticBag diagnostics, MistType? declaringType)
        where TNode : SyntaxNode, IHasModifiersNode
    {
        var modifiers = Build(node, diagnostics, declaringType);

        if(declaringType is not MemberType type)
        {
            return modifiers;
        }

        if(type.Modifiers.IsPure && node is not FieldDeclarationNode)
        {
            if(modifiers.IsLocal && !modifiers.IsPure)
            {
                diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PureAndLocal, node, null);
            }
            else if(!modifiers.IsPure && Wrote<ReadonlyModifierModifierNode>(node.Modifiers))
            {
                diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.ReadonlyImpliedByPure, node, null);
            }

            return modifiers with { IsPure = true, IsReadonly = true };
        }

        if(type.IsReadonly)
        {
            modifiers = modifiers with { IsReadonly = true };
        }

        return type.Modifiers.IsLocal && !modifiers.IsStatic && !modifiers.IsPure && InheritsLocal(node)
            ? modifiers with { IsLocal = true }
            : modifiers;
    }

    private static bool InheritsLocal(SyntaxNode node)
    {
        return node is MethodDeclarationNode
            or BlockPropertyPropertyDeclarationNode
            or ExpressionBodiedPropertyPropertyDeclarationNode
            or IndexerDeclarationNode;
    }

    private static void ValidatePurity(
        SyntaxNode node, DiagnosticBag diagnostics, ImmutableArray<ModifierNode> modifiers, MemberModifiers result)
    {
        var wrotePure = Wrote<PureModifierModifierNode>(modifiers);
        var wroteLocal = Wrote<LocalModifierModifierNode>(modifiers);

        if(wrotePure && wroteLocal)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PureAndLocal, node, null);
        }

        if(wrotePure && Wrote<ReadonlyModifierModifierNode>(modifiers))
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.ReadonlyImpliedByPure, node, null);
        }

        if(wrotePure && Misplaced(node, isLocal: false) is { } purePlace)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PurityModifierPlacement, node, null, new EnumValue("pure"), new TextValue(purePlace), new TextValue(AliasHint(node)));
        }

        if(!wroteLocal)
        {
            return;
        }

        if(Misplaced(node, isLocal: true) is { } localPlace)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PurityModifierPlacement, node, null, new EnumValue("local"), new TextValue(localPlace), new TextValue(string.Empty));
        }
        else if(node is ConstructorDeclarationNode || result.IsStatic)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.LocalRequiresInstance, node, null);
        }
    }

    private static string? Misplaced(SyntaxNode node, bool isLocal)
    {
        return node switch
        {
            TypeBodyDeclarationNode
                or MethodDeclarationNode
                or ConstructorDeclarationNode
                or BlockPropertyPropertyDeclarationNode
                or ExpressionBodiedPropertyPropertyDeclarationNode
                or IndexerDeclarationNode
                or LocalFunctionDeclarationNode => null,
            OperatorDeclarationNode => isLocal ? "an operator" : null,
            CastOperatorDeclarationNode => isLocal ? "a cast operator" : null,
            FieldDeclarationNode => "a field",
            TypeAliasDeclarationNode => "a type alias",
            NewtypeDeclarationNode => "a newtype",
            EnumDeclarationNode => "an enum",
            AssociatedTypeSlotDeclarationNode => "an associated type",
            _ => "this declaration",
        };
    }

    private static string AliasHint(SyntaxNode node)
    {
        return node is FieldDeclarationNode or TypeAliasDeclarationNode
            ? " A pure function type is written after the type's name, 'type Step = pure func<int, int>;', and a field names the alias."
            : string.Empty;
    }

    private static bool Wrote<TModifier>(ImmutableArray<ModifierNode> modifiers)
        where TModifier : ModifierNode
    {
        return !modifiers.IsDefaultOrEmpty && modifiers.Any(modifier => modifier is TModifier);
    }

    /// <summary>
    /// A CONSTRUCTOR DEFAULTS TO PUBLIC, WHERE EVERY OTHER MEMBER OF A CLASS OR STRUCT IS PRIVATE, a
    /// departure from C#. The enclosing type is the same, so only the node separates them:
    /// <see cref="MemberModifiers"/> records the answer, not whether the author supplied one.
    /// </summary>
    private static Accessibility DefaultAccessibility(SyntaxNode node, MistType? declaringType)
    {
        return node is ConstructorDeclarationNode
            ? Accessibility.Public
            : GetScopeDefault(declaringType);
    }

    /// <summary>
    /// Mirrors C#'s defaults by enclosing type.
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

    private static void ValidateUniversalConflicts(
        DiagnosticBag diagnostics,
        SyntaxNode locationNode,
        ImmutableArray<ModifierNode> modifiers)
    {
        if(HasMultipleDisciplines(modifiers))
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.ConflictingDisciplineModifiers, locationNode, null);
        }
    }

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