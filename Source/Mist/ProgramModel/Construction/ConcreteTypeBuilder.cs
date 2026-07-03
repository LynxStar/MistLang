using System.Collections.Immutable;
using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for concrete types (classes and structs). Resolves the type's
/// declaration-level information (base types, generics, modifiers) and its member
/// signatures (fields, properties, methods, etc.) from the stashed AST.
/// </summary>
internal sealed class ConcreteTypeBuilder : MemberTypeBuilder
{
    public ConcreteTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
        : base(context, typeResolver)
    {
    }

    /// <summary>
    /// Entry point: resolves the full type definition for a concrete type.
    /// </summary>
    public void Build(ConcreteType typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);
    }

    /// <summary>
    /// Resolves declaration-level information: base types, modifiers, generics.
    /// </summary>
    private void BuildDeclaration(ConcreteType typeDef)
    {
        BaseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;
        var modifiers = body.TypeBody.Modifiers;

        typeDef.IsReadonly = modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
        typeDef.IsIntrinsic = modifiers.HasAny<ModifierNode, IntrinsicModifierModifierNode>();
        typeDef.IsOwned = modifiers.HasAny<ModifierNode, OwnedModifierModifierNode>();
        typeDef.Discipline = ResolveDiscipline(modifiers);

        if(typeDef is StructType structDef)
        {
            structDef.IsRef = modifiers.HasAny<ModifierNode, RefModifierModifierNode>();
        }
    }

    /// <summary>
    /// Resolves all member signatures for the concrete type.
    /// </summary>
    private void BuildMembers(ConcreteType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;

        foreach(var member in body.TypeBody.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single member declaration to the appropriate handler.
    /// </summary>
    private void ResolveMember(MemberDeclarationNode member, ConcreteType typeDef)
    {
        switch(member)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                ResolveField(fieldMember.Field, typeDef);
                break;

            case PropertyMemberMemberDeclarationNode propertyMember:
                PropertyBuilder.Resolve(propertyMember.Property, typeDef, isAbstract: false);
                break;

            case IndexerMemberMemberDeclarationNode indexerMember:
                IndexerBuilder.Resolve(indexerMember.Indexer, typeDef, isAbstract: false);
                break;

            case ConstructorMemberMemberDeclarationNode:
            case MethodMemberMemberDeclarationNode:
            case OperatorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
                // These will be implemented in subsequent tasks.
                break;

            case NestedTypeMemberMemberDeclarationNode:
            case DefaultBlockMemberMemberDeclarationNode:
            case AssociatedTypeMemberMemberDeclarationNode:
            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode:
            case MemberTypeAliasMemberMemberDeclarationNode:
                // Nested types are already registered; others are out of scope.
                break;

            default:
                Context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownMember, member, null, member.GetType().Name);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fields
    // ═══════════════════════════════════════════════════════════════════════

    private void ResolveField(FieldDeclarationNode fieldDecl, ConcreteType typeDef)
    {
        var name = fieldDecl.Name.Lexeme;
        var type = TypeResolver.Resolve(fieldDecl.Type, typeDef.Scope);
        var ownership = ResolveOwnership(fieldDecl.OwnershipSpecifier);
        var isReadonly = fieldDecl.Modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
        var isStatic = fieldDecl.Modifiers.HasAny<ModifierNode, StaticModifierModifierNode>();
        var initializer = fieldDecl.Initializer is { } initExpr ? new Expression(initExpr) : null;

        var field = new Field(name, type, ownership, isReadonly, isStatic, fieldDecl) { Initializer = initializer };
        typeDef.FieldsList.Add(field);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

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