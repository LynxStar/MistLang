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
        typeSlotBuilder = new TypeSlotBuilder(context, ConstraintResolver);
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
    /// Resolves declaration-level information: base abstracts, modifiers, constraints.
    /// </summary>
    private void BuildDeclaration(AbstractType typeDef)
    {
        BaseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;
        typeDef.Modifiers = ModifierBuilder.Build(body.TypeBody, Context.Diagnostics);

        ConstraintResolver.Resolve(body.TypeBody.Constraints, typeDef.TypeParametersList, typeDef.Scope);
    }

    /// <summary>
    /// Resolves all member signatures for the abstract type.
    /// </summary>
    private void BuildMembers(AbstractType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode;

        // First pass: register associated type slots as types in the abstract's
        // scope so members can reference them by simple name regardless of the
        // order in which they are declared.
        foreach(var member in body.TypeBody.Members)
        {
            if(member is AssociatedTypeMemberMemberDeclarationNode assocType)
            {
                typeSlotBuilder.Resolve(assocType.AssociatedType, typeDef);
            }
        }

        // Second pass: resolve the remaining member signatures.
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

            case AssociatedTypeMemberMemberDeclarationNode:
                // Type slots are registered in the pre-pass in BuildMembers.
                break;

            case NestedBodyTypeMemberMemberDeclarationNode:
            case NestedEnumMemberMemberDeclarationNode:
            case NestedNewtypeMemberMemberDeclarationNode:
            case NestedTypeAliasMemberMemberDeclarationNode:
                // Nested types are already registered.
                break;

            case FieldMemberMemberDeclarationNode:
            case ConstructorMemberMemberDeclarationNode:
            case CastOperatorMemberMemberDeclarationNode:
            case DefaultBlockMemberMemberDeclarationNode:
                Context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MemberNotAllowedOnAbstract, member, null);
                break;

            default:
                throw new InvalidOperationException($"Unrecognized member declaration node: {member.GetType().Name}");
        }
    }
}