using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

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
    /// (e.g. multiple substructural disciplines).
    /// </summary>
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
            diagnostics.ReportAtNode(ProgramModelDiagnostics.StampableRequiresStruct, node, null);
        }

        ValidatePurity(node, diagnostics, modifiers, result);

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
                diagnostics.ReportAtNode(ProgramModelDiagnostics.PureAndLocal, node, null);
            }
            else if(!modifiers.IsPure && Wrote<ReadonlyModifierModifierNode>(node.Modifiers))
            {
                diagnostics.ReportAtNode(ProgramModelDiagnostics.ReadonlyImpliedByPure, node, null);
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
            diagnostics.ReportAtNode(ProgramModelDiagnostics.PureAndLocal, node, null);
        }

        if(wrotePure && Wrote<ReadonlyModifierModifierNode>(modifiers))
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.ReadonlyImpliedByPure, node, null);
        }

        if(wrotePure && Misplaced(node, isLocal: false) is { } purePlace)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.PurityModifierPlacement, node, null, "pure", purePlace, AliasHint(node));
        }

        if(!wroteLocal)
        {
            return;
        }

        if(Misplaced(node, isLocal: true) is { } localPlace)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.PurityModifierPlacement, node, null, "local", localPlace, string.Empty);
        }
        else if(node is ConstructorDeclarationNode || result.IsStatic)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.LocalRequiresInstance, node, null);
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
    /// The accessibility a declaration takes when it names none.
    /// <para>
    /// A CONSTRUCTOR IS PUBLIC, WHERE EVERY OTHER MEMBER OF A CLASS OR STRUCT IS PRIVATE. Mist parts
    /// from C# here, and the two are not reconcilable by scope: the enclosing type is the same, so
    /// only the declaration kind separates them. Branching on the node rather than on the resolved
    /// modifiers is what makes it decidable at all -- <see cref="MemberModifiers"/> records the
    /// answer, not whether the author supplied one, so by the time Build returns, a defaulted
    /// private and a written private are the same value.
    /// </para>
    /// </summary>
    private static Accessibility DefaultAccessibility(SyntaxNode node, MistType? declaringType)
    {
        return node is ConstructorDeclarationNode
            ? Accessibility.Public
            : GetScopeDefault(declaringType);
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
        DiagnosticBag diagnostics,
        SyntaxNode locationNode,
        ImmutableArray<ModifierNode> modifiers)
    {
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