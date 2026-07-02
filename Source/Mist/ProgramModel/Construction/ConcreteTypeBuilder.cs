using System.Collections.Immutable;
using Mist.Common.Collections;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for concrete types (classes and structs). Resolves base types
/// and member signatures (fields, properties, methods, etc.) from the stashed AST.
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
    /// Resolves base types and all member signatures for a concrete type.
    /// </summary>
    public void BuildMembers(ConcreteTypeDefinition typeDef)
    {
        var typeDecl = typeDef.DeclarationNode;

        // Resolve base types first.
        baseTypeResolver.Resolve(typeDecl, typeDef);

        // Get the members array from the declaration.
        var members = typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Members,
            _ => default
        };

        if(members.IsDefault)
        {
            return;
        }

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
                context.ReportError(
                    "MIST_PM_UNKNOWN_MEMBER",
                    $"Internal compiler error: unrecognized member declaration node '{member.GetType().Name}'.",
                    member);
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
}
