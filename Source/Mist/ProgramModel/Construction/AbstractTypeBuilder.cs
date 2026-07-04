using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for abstract types. Resolves base abstracts and member
/// signatures (method declarations, property declarations, associated type slots).
/// </summary>
internal sealed class AbstractTypeBuilder : MemberTypeBuilder
{
    private readonly TypeSlotBuilder typeSlotBuilder;

    public AbstractTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
        : base(context, typeResolver)
    {
        typeSlotBuilder = new TypeSlotBuilder(context);
    }

    /// <summary>
    /// Entry point: resolves the full type definition for an abstract type.
    /// </summary>
    public void Build(AbstractType typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);
    }

    /// <summary>
    /// Resolves declaration-level information: base abstracts, modifiers.
    /// </summary>
    private void BuildDeclaration(AbstractType typeDef)
    {
        BaseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;
        var modifiers = body.TypeBody.Modifiers;

        typeDef.IsReadonly = modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
    }

    /// <summary>
    /// Resolves all member signatures for the abstract type.
    /// </summary>
    private void BuildMembers(AbstractType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;

        foreach(var member in body.TypeBody.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single abstract member declaration.
    /// </summary>
    private void ResolveMember(MemberDeclarationNode member, AbstractType typeDef)
    {
        switch(member)
        {
            case MethodMemberMemberDeclarationNode methodMember:
                MethodBuilder.Resolve(methodMember.Method, typeDef);
                break;

            case PropertyMemberMemberDeclarationNode propertyMember:
                PropertyBuilder.Resolve(propertyMember.Property, typeDef);
                break;

            case IndexerMemberMemberDeclarationNode indexerMember:
                IndexerBuilder.Resolve(indexerMember.Indexer, typeDef);
                break;

            case OperatorMemberMemberDeclarationNode opMember:
                OperatorBuilder.Resolve(opMember.Operator, typeDef);
                break;

            case AssociatedTypeMemberMemberDeclarationNode assocType:
                TypeSlotBuilder.Resolve(assocType.AssociatedType, typeDef);
                break;

            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode unconstrainedAssocType:
                TypeSlotBuilder.ResolveUnconstrained(unconstrainedAssocType.UnconstrainedAssociatedType, typeDef);
                break;

            case ModifiedAssociatedTypeMemberMemberDeclarationNode modifiedAssocType:
                typeSlotBuilder.ResolveModified(modifiedAssocType.ModifiedAssociatedType, typeDef);
                break;

            case NestedTypeMemberMemberDeclarationNode:
                // Nested types are already registered.
                break;

            case FieldMemberMemberDeclarationNode:
            case ConstructorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
            case DefaultBlockMemberMemberDeclarationNode:
            case MemberTypeAliasMemberMemberDeclarationNode:
                Context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MemberNotAllowedOnAbstract, member, null);
                break;

            default:
                throw new InvalidOperationException($"Unrecognized member declaration node: {member.GetType().Name}");
        }
    }
}