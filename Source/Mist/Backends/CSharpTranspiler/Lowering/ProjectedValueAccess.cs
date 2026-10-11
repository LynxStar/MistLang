using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Names the declaring construction because C# never inherits an interface's statics: an implementing type
/// has no member for an abstract's projected value.
/// </summary>
internal static class ProjectedValueAccess
{
    public static IMistType? Declaring(MemberAccessExpression access)
    {
        if(access.Member is not PropertyResult found)
        {
            return null;
        }

        var receiver = access.Receiver is TypeReferenceExpression reference ? reference.ReferencedType : access.Receiver.Type;
        var answering = receiver is TypeAlias { AliasedType: { } aliased } ? aliased : receiver;

        return Declaring(found.Property, answering as ConstructedType, found.BaseConstructions);
    }

    public static IMistType? Supplied(MemberAccessExpression access)
    {
        return access.Member is PropertyResult { Property: { ProjectedParameter: { } projected, DeclaringType: null } } found
            ? TypeSubstitution.ForPath(receiver: null, found.BaseConstructions, interner: null).Apply(projected, interner: null)
            : null;
    }

    public static IMistType? Declaring(PropertyReferenceExpression reference)
    {
        return Declaring(reference.Property, receiver: null, reference.BaseConstructions);
    }

    private static IMistType? Declaring(Property property, ConstructedType? receiver, IReadOnlyList<ConstructedType> path)
    {
        if(property is not { ProjectedParameter: not null, DeclaringType: { } declaring })
        {
            return null;
        }

        return TypeSubstitution
            .ForPath(receiver, path, interner: null)
            .Apply(new ConstructedType(declaring, declaring.TypeParametersList), interner: null);
    }
}