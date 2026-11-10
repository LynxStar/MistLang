using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal sealed class AbstractTypeBuilder : MemberTypeBuilder
{
    private readonly TypeSlotBuilder typeSlotBuilder;

    public AbstractTypeBuilder(BuilderContext context, ScopeResolver scopeResolver)
        : base(context, scopeResolver)
    {
        typeSlotBuilder = new TypeSlotBuilder(context, ConstraintResolver);
    }

    public void Build(AbstractType typeDef)
    {
        BuildDeclaration(typeDef);
        BuildMembers(typeDef);
    }

    private void BuildDeclaration(AbstractType typeDef)
    {
        BaseTypeResolver.Resolve(typeDef.DeclarationNode!, typeDef);

        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode!;
        var modifiers = ModifierBuilder.Build(body.TypeBody, Context.Diagnostics, typeDef.DeclaringType);
        Context.Attributes.Build(body.TypeBody.Attributes, typeDef.DeclaredAttributesList, typeDef.Scope);

        typeDef.Modifiers = modifiers;

        ConstraintResolver.Resolve(body.TypeBody.Constraints, typeDef.TypeParametersList, typeDef.Scope);
    }

    private void BuildMembers(AbstractType typeDef)
    {
        var body = (BodyTypeTypeDeclarationNode)typeDef.DeclarationNode!;

        // Slots first, so members can reference them by simple name whatever order they are
        // declared in.
        foreach(var member in body.TypeBody.Members)
        {
            if(member is AssociatedTypeMemberMemberDeclarationNode assocType)
            {
                typeSlotBuilder.Resolve(assocType.AssociatedType, typeDef);
            }
        }

        foreach(var member in body.TypeBody.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

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
            case OperatorMemberMemberDeclarationNode:
            case DefaultBlockMemberMemberDeclarationNode:
                Context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.MemberNotAllowedOnAbstract, member, null);
                break;

            default:
                throw new InvalidOperationException($"Unrecognized member declaration node: {member.GetType().Name}");
        }
    }
}