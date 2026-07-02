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
        switch(member)
        {
            case FieldMemberMemberDeclarationNode fieldMember when typeDef is ConcreteTypeDefinition concrete:
                ResolveField(fieldMember.Field, concrete);
                break;

            case PropertyMemberMemberDeclarationNode:
            case IndexerMemberMemberDeclarationNode:
            case ConstructorMemberMemberDeclarationNode:
            case MethodMemberMemberDeclarationNode:
            case OperatorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
                // These will be implemented in subsequent tasks (10–17).
                break;

            case FieldMemberMemberDeclarationNode:
                // Field on a non-concrete type (shouldn't happen via grammar).
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
        var isReadonly = HasModifier<ReadonlyModifierModifierNode>(fieldDecl.Modifiers);
        var isStatic = HasModifier<StaticModifierModifierNode>(fieldDecl.Modifiers);

        var field = new FieldDefinition(name, type, ownership, isReadonly, isStatic);
        typeDef.FieldsList.Add(field);
    }

    /// <summary>
    /// Maps an optional <see cref="OwnershipSpecifierNode"/> to an <see cref="OwnershipKind"/>.
    /// </summary>
    private static OwnershipKind ResolveOwnership(OwnershipSpecifierNode? specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => OwnershipKind.Owned,
            BorrowedSpecifierOwnershipSpecifierNode => OwnershipKind.Borrowed,
            _ => OwnershipKind.Unrestricted
        };
    }

    /// <summary>
    /// Checks whether a modifier of the specified type is present in a modifiers list.
    /// </summary>
    private static bool HasModifier<T>(ImmutableArray<ModifierNode> modifiers) where T : ModifierNode
    {
        if(modifiers.IsDefault)
        {
            return false;
        }

        foreach(var mod in modifiers)
        {
            if(mod is T)
            {
                return true;
            }
        }

        return false;
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

        var resolved = typeResolver.ResolveNamedType(name, typeDef.Scope, baseTypeRef);
        if(resolved is null)
        {
            // Diagnostic already emitted by ResolveNamedType.
            return true;
        }

        // Not a concrete type — let ResolveAbstractBase handle it.
        if(resolved is not ConcreteTypeDefinition resolvedConcrete)
        {
            return false;
        }

        // Verify kind match: class must inherit from class, struct from struct.
        var isSameKind = (typeDef, resolvedConcrete) switch
        {
            (ClassDefinition, ClassDefinition) => true,
            (StructDefinition, StructDefinition) => true,
            _ => false
        };

        if(!isSameKind)
        {
            var ownerKind = typeDef is ClassDefinition ? "class" : "struct";
            var baseKind = resolvedConcrete is ClassDefinition ? "class" : "struct";
            context.ReportError(
                "MIST_PM_BASE_TYPE_KIND_MISMATCH",
                $"A {ownerKind} cannot inherit from a {baseKind} ('{name}').",
                baseTypeRef);
            return true;
        }

        typeDef.BaseType = resolvedConcrete;
        return true;
    }

    /// <summary>
    /// Resolves a single base type entry that is expected to be an abstract.
    /// Emits a diagnostic if it resolves to a concrete type.
    /// </summary>
    private void ResolveAbstractBase(BaseTypeRefNode baseTypeRef, MemberTypeDefinition typeDef)
    {
        var name = baseTypeRef.Name.Lexeme;

        var resolved = typeResolver.ResolveNamedType(name, typeDef.Scope, baseTypeRef);
        if(resolved is null)
        {
            // Diagnostic already emitted by ResolveNamedType.
            return;
        }

        if(resolved is ConcreteTypeDefinition)
        {
            context.ReportError(
                "MIST_PM_CONCRETE_IN_ABSTRACT_LIST",
                $"A concrete type ('{name}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.",
                baseTypeRef);
            return;
        }

        if(resolved is not AbstractDefinition abstractDef)
        {
            context.ReportError(
                "MIST_PM_INVALID_BASE_TYPE",
                $"'{name}' is not an abstract and cannot be used as a base type.",
                baseTypeRef);
            return;
        }

        typeDef.BaseAbstractTypes.Add(abstractDef);
    }
}