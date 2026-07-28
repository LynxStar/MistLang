using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A type declaration. Answers for the type's members and the receiver they name as <c>this</c>.
/// <para>
/// A boundary: leaving it leaves the type behind, so instance members drop out of the request on the
/// way out. That is what lets a nested type name the enclosing type's statics but not its state.
/// </para>
/// </summary>
internal sealed class TypeScope(Scope? parent, MistType type) : Scope(parent)
{
    /// <summary>
    /// Never null: a type scope is built by its own type, from inside that type's constructor, so
    /// neither can exist without the other.
    /// </summary>
    public MistType Type { get; } = type;

    internal override LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);
        if(self is not NotFoundResult)
        {
            return self;
        }

        return Parent?.Lookup(request.CrossTypeBoundary()) ?? new NotFoundResult();
    }

    /// <summary>
    /// The type and its bases both count as self: an inherited member is reached through the same
    /// receiver as a declared one, so only leaving for the parent narrows the request.
    /// </summary>
    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(request.Name == LookupRequest.ReceiverName)
        {
            // Answered by the innermost enclosing type, and only where instance members are still
            // admitted -- so a nested type's `this` is its own, never its declaring type's.
            return Type is MemberType memberType && request.Permits(isStatic: false)
                ? new ReceiverResult(memberType)
                : new NotFoundResult();
        }

        return TypeMemberLookup.Find(Type, request);
    }
}