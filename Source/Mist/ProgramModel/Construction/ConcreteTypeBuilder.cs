using System.Collections.Immutable;
using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for concrete types (classes and structs). Resolves the type's
/// declaration-level information (base types, generics, modifiers) and its member
/// signatures (fields, properties, methods, etc.) from the stashed AST.
/// </summary>
internal sealed class ConcreteTypeBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly BaseTypeResolver baseTypeResolver;

    public ConcreteTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        baseTypeResolver = new BaseTypeResolver(context, typeResolver);
    }

    /// <summary>
    /// Entry point: resolves the full type definition for a concrete type.
    /// </summary>
    public void Build(ConcreteTypeDefinition typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);
    }

    /// <summary>
    /// Resolves declaration-level information: base types, modifiers, generics.
    /// </summary>
    private void BuildDeclaration(ConcreteTypeDefinition typeDef)
    {
        baseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

        var modifiers = typeDef.DeclarationNode switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Modifiers,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Modifiers,
            _ => throw new InvalidOperationException(
                $"Internal compiler error: ConcreteTypeBuilder received unexpected declaration node '{typeDef.DeclarationNode.GetType().Name}'.")
        };

        typeDef.IsReadonly = modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
        typeDef.IsIntrinsic = modifiers.HasAny<ModifierNode, IntrinsicModifierModifierNode>();
        typeDef.IsOwned = modifiers.HasAny<ModifierNode, OwnedModifierModifierNode>();
        typeDef.Discipline = ResolveDiscipline(modifiers);

        if(typeDef is StructDefinition structDef)
        {
            structDef.IsRef = modifiers.HasAny<ModifierNode, RefModifierModifierNode>();
        }
    }

    /// <summary>
    /// Resolves all member signatures for the concrete type.
    /// </summary>
    private void BuildMembers(ConcreteTypeDefinition typeDef)
    {
        var members = typeDef.DeclarationNode switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Members,
            _ => throw new InvalidOperationException(
                $"Internal compiler error: ConcreteTypeBuilder received unexpected declaration node '{typeDef.DeclarationNode.GetType().Name}'.")
        };

        foreach(var member in members)
        {
            ResolveMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single member declaration to the appropriate handler.
    /// </summary>
    private void ResolveMember(MemberDeclarationNode member, ConcreteTypeDefinition typeDef)
    {
        switch(member)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                ResolveField(fieldMember.Field, typeDef);
                break;

            case PropertyMemberMemberDeclarationNode:
            case IndexerMemberMemberDeclarationNode:
            case ConstructorMemberMemberDeclarationNode:
            case MethodMemberMemberDeclarationNode:
            case OperatorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
                // These will be implemented in subsequent tasks (10–17).
                break;

            case NestedTypeMemberMemberDeclarationNode:
            case DefaultBlockMemberMemberDeclarationNode:
            case AssociatedTypeMemberMemberDeclarationNode:
            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode:
            case MemberTypeAliasMemberMemberDeclarationNode:
                // Nested types are already registered; others are out of scope.
                break;

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownMember, member, null, member.GetType().Name);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fields
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a field declaration: name, type, ownership, readonly, static.
    /// </summary>
    private void ResolveField(FieldDeclarationNode fieldDecl, ConcreteTypeDefinition typeDef)
    {
        var name = fieldDecl.Name.Lexeme;
        var type = typeResolver.Resolve(fieldDecl.Type, typeDef.Scope);
        var ownership = ResolveOwnership(fieldDecl.OwnershipSpecifier);
        var isReadonly = fieldDecl.Modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
        var isStatic = fieldDecl.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();

        var field = new FieldDefinition(name, type, ownership, isReadonly, isStatic, fieldDecl);
        typeDef.FieldsList.Add(field);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Maps an optional <see cref="OwnershipSpecifierNode"/> to an <see cref="OwnershipKind"/>.
    /// </summary>
    internal static OwnershipKind ResolveOwnership(OwnershipSpecifierNode? specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => OwnershipKind.Owned,
            BorrowedSpecifierOwnershipSpecifierNode => OwnershipKind.Borrowed,
            _ => OwnershipKind.Unrestricted
        };
    }

    /// <summary>
    /// Resolves the substructural discipline from modifiers.
    /// If multiple conflicting modifiers are present, returns None
    /// (the conflicting-modifier diagnostic is reported by a later analyzer).
    /// </summary>
    private static SubstructuralDiscipline ResolveDiscipline(ImmutableArray<ModifierNode> modifiers)
    {
        if(modifiers.IsDefault)
        {
            return SubstructuralDiscipline.None;
        }

        var hasAffine = false;
        var hasLinear = false;
        var hasRelevant = false;

        foreach(var mod in modifiers)
        {
            switch(mod)
            {
                case AffineModifierModifierNode:
                    hasAffine = true;
                    break;
                case LinearModifierModifierNode:
                    hasLinear = true;
                    break;
                case RelevantModifierModifierNode:
                    hasRelevant = true;
                    break;
            }
        }

        // Conflicting modifiers resolve to None; analyzer reports later.
        var count = (hasAffine ? 1 : 0) + (hasLinear ? 1 : 0) + (hasRelevant ? 1 : 0);
        if(count > 1)
        {
            return SubstructuralDiscipline.None;
        }

        if(hasAffine)
        {
            return SubstructuralDiscipline.Affine;
        }

        if(hasLinear)
        {
            return SubstructuralDiscipline.Linear;
        }

        if(hasRelevant)
        {
            return SubstructuralDiscipline.Relevant;
        }

        return SubstructuralDiscipline.None;
    }
}
