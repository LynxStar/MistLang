using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder: walks the stashed AST declaration nodes on each type
/// and resolves member signatures (fields, properties, methods, etc.)
/// into the program model.
/// </summary>
internal sealed class MemberBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;

    public MemberBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
    }

    /// <summary>
    /// Resolves all member signatures for the given type definition.
    /// </summary>
    public void BuildMembers(MemberTypeDefinition memberTypeDef)
    {
        var typeDecl = memberTypeDef.DeclarationNode;

        // Resolve base types first.
        ResolveBaseTypes(typeDecl, memberTypeDef);

        var members = GetMembers(typeDecl);
        if(!members.IsDefault)
        {
            foreach(var member in members)
            {
                ResolveMember(member, memberTypeDef);
            }
        }
    }

    /// <summary>
    /// Dispatches a single member declaration to the appropriate handler.
    /// </summary>
    private void ResolveMember(MemberDeclarationNode member, MemberTypeDefinition typeDef)
    {
        _ = typeDef;

        switch(member)
        {
            case FieldMemberMemberDeclarationNode:
            case PropertyMemberMemberDeclarationNode:
            case IndexerMemberMemberDeclarationNode:
            case ConstructorMemberMemberDeclarationNode:
            case MethodMemberMemberDeclarationNode:
            case OperatorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
                // These will be implemented in subsequent tasks (8–17).
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

    /// <summary>
    /// Gets the members array from a type declaration node.
    /// Returns default for type kinds that don't have members in this sense.
    /// </summary>
    private static ImmutableArray<MemberDeclarationNode> GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Members,
            _ => default
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Base Types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves base types for classes (BaseList) and structs (Abstracts).
    /// All entries in the base list are resolved and added to
    /// <see cref="MemberTypeDefinition.BaseTypes"/>.
    /// </summary>
    private void ResolveBaseTypes(TypeDeclarationNode typeDecl, MemberTypeDefinition typeDef)
    {
        switch(typeDecl)
        {
            case ClassTypeTypeDeclarationNode ct:
                ResolveClassBaseTypes(ct.ClassDeclaration, typeDef);
                break;
            case StructTypeTypeDeclarationNode st:
                ResolveStructBaseTypes(st.StructDeclaration, typeDef);
                break;
        }
    }

    /// <summary>
    /// Resolves the base list on a class declaration. Includes the base class
    /// (if any) and all abstract implementations.
    /// </summary>
    private void ResolveClassBaseTypes(ClassDeclarationNode classDecl, MemberTypeDefinition typeDef)
    {
        if(classDecl.BaseList is null)
        {
            return;
        }

        ResolveBaseTypeRef(classDecl.BaseList.First, typeDef);

        foreach(var entry in classDecl.BaseList.BaseTypeRefs)
        {
            ResolveBaseTypeRef(entry, typeDef);
        }
    }

    /// <summary>
    /// Resolves the abstracts list on a struct declaration. The first entry
    /// may be a base struct or an abstract; all are treated uniformly here.
    /// </summary>
    private void ResolveStructBaseTypes(StructDeclarationNode structDecl, MemberTypeDefinition typeDef)
    {
        if(structDecl.Abstracts is null)
        {
            return;
        }

        ResolveBaseTypeRef(structDecl.Abstracts.First, typeDef);

        foreach(var entry in structDecl.Abstracts.BaseTypeRefs)
        {
            ResolveBaseTypeRef(entry, typeDef);
        }
    }

    /// <summary>
    /// Resolves a single <see cref="BaseTypeRefNode"/> and adds the result
    /// to the type's base types list.
    /// </summary>
    private void ResolveBaseTypeRef(BaseTypeRefNode baseTypeRef, MemberTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;
        if(string.IsNullOrEmpty(name))
        {
            return;
        }

        var resolved = typeResolver.LookupByName(name, typeDef.Scope);
        if(resolved is not null)
        {
            typeDef.BaseTypes.Add(resolved);
        }
        else
        {
            context.ReportError(
                "MIST_PM_UNRESOLVED_BASE_TYPE",
                $"The base type '{name}' could not be found.",
                baseTypeRef);
            typeDef.BaseTypes.Add(Model.TypeReferences.ErrorTypeReference.Instance);
        }
    }
}