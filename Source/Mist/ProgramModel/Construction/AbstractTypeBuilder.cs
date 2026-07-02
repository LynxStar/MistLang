using Mist.Common.Collections;
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

        if(typeDef.DeclarationNode is AbstractTypeTypeDeclarationNode at)
        {
            typeDef.IsReadonly = at.AbstractDeclaration.Modifiers.HasAny<ModifierNode, ReadonlyModifierModifierNode>();
        }
    }

    /// <summary>
    /// Resolves all member signatures for the abstract type.
    /// </summary>
    private void BuildMembers(AbstractType typeDef)
    {
        if(typeDef.DeclarationNode is not AbstractTypeTypeDeclarationNode at)
        {
            throw new InvalidOperationException(
                $"Internal compiler error: AbstractTypeBuilder received unexpected declaration node '{typeDef.DeclarationNode.GetType().Name}'.");
        }

        foreach(var member in at.AbstractDeclaration.Members)
        {
            ResolveMember(member, typeDef);
        }
    }

    /// <summary>
    /// Dispatches a single abstract member declaration.
    /// </summary>
    private void ResolveMember(AbstractMemberDeclarationNode member, AbstractType typeDef)
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
