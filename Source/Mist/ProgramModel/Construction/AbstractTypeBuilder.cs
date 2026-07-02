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
    private readonly TypeReferenceResolver typeResolver;
    private readonly BaseTypeResolver baseTypeResolver;

    public AbstractTypeBuilder(BuilderContext context, TypeReferenceResolver typeResolver)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        baseTypeResolver = new BaseTypeResolver(context, typeResolver);
    }

    /// <summary>
    /// Resolves base abstracts and all member signatures for an abstract type.
    /// </summary>
    public void BuildMembers(AbstractDefinition typeDef)
    {
        var typeDecl = typeDef.DeclarationNode;

        // Resolve base abstracts.
        baseTypeResolver.Resolve(typeDecl, typeDef);

        // Abstract member resolution will be implemented in tasks 13, 16.
        if(typeDecl is not AbstractTypeTypeDeclarationNode at)
        {
            return;
        }

        foreach(var member in at.AbstractDeclaration.Members)
        {
            ResolveAbstractMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single abstract member declaration.
    /// </summary>
    private void ResolveAbstractMember(AbstractMemberDeclarationNode member, AbstractDefinition typeDef)
    {
        _ = typeDef;

        switch(member)
        {
            case AbstractMethodMemberAbstractMemberDeclarationNode:
            case AbstractPropertyMemberAbstractMemberDeclarationNode:
            case AbstractDefaultMethodMemberAbstractMemberDeclarationNode:
                // Will be implemented in tasks 13, 16.
                break;

            case AbstractAssociatedTypeMemberAbstractMemberDeclarationNode:
            case AbstractUnconstrainedAssociatedTypeMemberAbstractMemberDeclarationNode:
                // Associated type slots — out of scope for this plan.
                break;

            default:
                context.ReportError(
                    "MIST_PM_UNKNOWN_ABSTRACT_MEMBER",
                    $"Internal compiler error: unrecognized abstract member declaration node '{member.GetType().Name}'.",
                    member);
                break;
        }
    }
}
