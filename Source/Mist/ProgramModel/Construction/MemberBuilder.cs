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
    /// Resolves the base type list for any member-bearing type declaration.
    /// Extracts the <see cref="BaseListNode"/> uniformly from all three type kinds.
    /// </summary>
    private void ResolveBaseTypes(TypeDeclarationNode typeDecl, MemberTypeDefinition typeDef)
    {
        var baseList = typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.BaseList,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.BaseList,
            AbstractTypeTypeDeclarationNode at => at.AbstractDeclaration.BaseList,
            _ => null
        };

        if(baseList is null)
        {
            return;
        }

        // Collect all entries: first + rest.
        var allEntries = new List<BaseTypeRefNode>(baseList.BaseTypeRefs.Length + 1) { baseList.First };
        foreach(var entry in baseList.BaseTypeRefs)
        {
            allEntries.Add(entry);
        }

        var startIndex = 0;

        // For concrete types, check if the first entry is a concrete base type.
        if(typeDef is ConcreteTypeDefinition concreteTypeDef)
        {
            var firstHandled = TryResolveConcreteBase(allEntries[0], concreteTypeDef);
            if(firstHandled)
            {
                startIndex = 1;
            }
        }

        // Remaining entries must all be abstracts.
        for(var i = startIndex; i < allEntries.Count; i++)
        {
            ResolveAbstractBase(allEntries[i], typeDef);
        }
    }

    /// <summary>
    /// Attempts to resolve the first base list entry as a concrete base type
    /// (class inheriting from class, struct inheriting from struct).
    /// Returns true if the entry was consumed as a concrete base, false if it
    /// should be treated as an abstract.
    /// </summary>
    private bool TryResolveConcreteBase(BaseTypeRefNode baseTypeRef, ConcreteTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;
        if(string.IsNullOrEmpty(name))
        {
            return false;
        }

        var resolved = typeResolver.LookupTypeByName(name, typeDef.Scope);
        if(resolved is null)
        {
            // Can't resolve — will be reported by ResolveAbstractBase.
            return false;
        }

        // Check if it's a concrete type (class or struct).
        if(resolved.Kind is not (TypeDefinitionKind.Class or TypeDefinitionKind.Struct))
        {
            // It's an abstract — let ResolveAbstractBase handle it.
            return false;
        }

        // Verify kind match: class must inherit from class, struct from struct.
        if(resolved.Kind != typeDef.Kind)
        {
            context.ReportError(
                "MIST_PM_BASE_TYPE_KIND_MISMATCH",
                $"A {typeDef.Kind.ToString().ToLowerInvariant()} cannot inherit from a {resolved.Kind.ToString().ToLowerInvariant()} ('{name}').",
                baseTypeRef);
            return true;
        }

        typeDef.BaseType = (TypeDefinition)resolved;
        return true;
    }

    /// <summary>
    /// Resolves a single base type entry that is expected to be an abstract.
    /// Emits a diagnostic if it resolves to a concrete type.
    /// </summary>
    private void ResolveAbstractBase(BaseTypeRefNode baseTypeRef, MemberTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;
        if(string.IsNullOrEmpty(name))
        {
            return;
        }

        var resolved = typeResolver.LookupTypeByName(name, typeDef.Scope);
        if(resolved is null)
        {
            context.ReportError(
                "MIST_PM_UNRESOLVED_BASE_TYPE",
                $"The base type '{name}' could not be found.",
                baseTypeRef);
            return;
        }

        if(resolved.Kind is TypeDefinitionKind.Class or TypeDefinitionKind.Struct)
        {
            context.ReportError(
                "MIST_PM_CONCRETE_IN_ABSTRACT_LIST",
                $"A concrete type ('{name}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.",
                baseTypeRef);
            return;
        }

        if(resolved.Kind is not TypeDefinitionKind.Abstract)
        {
            context.ReportError(
                "MIST_PM_INVALID_BASE_TYPE",
                $"'{name}' is a {resolved.Kind.ToString().ToLowerInvariant()} and cannot be used as a base type.",
                baseTypeRef);
            return;
        }

        typeDef.BaseAbstractTypes.Add((TypeDefinition)resolved);
    }
}