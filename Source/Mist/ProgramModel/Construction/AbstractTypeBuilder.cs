using Mist.Common.Collections;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Phase 2 builder for abstract types. Resolves base abstracts and member
/// signatures (method declarations, property declarations, associated type slots).
/// </summary>
internal sealed class AbstractTypeBuilder
{
    private readonly BuilderContext context;
    private readonly BaseTypeResolver baseTypeResolver;

    public AbstractTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        baseTypeResolver = new BaseTypeResolver(context, typeResolver);
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
    /// Resolves declaration-level information: base abstracts, modifiers, generics.
    /// </summary>
    private void BuildDeclaration(AbstractType typeDef)
    {
        baseTypeResolver.Resolve(typeDef.DeclarationNode, typeDef);

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
        _ = typeDef;

        switch(member)
        {
            case MethodMemberMemberDeclarationNode methodMember
                when methodMember.Method.Body is MethodNoBodyMethodBodyNode:
                // Abstract method (no body) — will be implemented in tasks 13, 16.
                break;

            case MethodMemberMemberDeclarationNode methodMember
                when methodMember.Method.Body is MethodBlockBodyMethodBodyNode:
                // Default method (has body) — will be implemented in tasks 13, 16.
                break;

            case PropertyMemberMemberDeclarationNode:
                // Abstract property — will be implemented in tasks 13, 16.
                break;

            case AssociatedTypeMemberMemberDeclarationNode:
            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode:
                // Associated type slots — out of scope for this plan.
                break;

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownAbstractMember, member, null, member.GetType().Name);
                break;
        }
    }
}