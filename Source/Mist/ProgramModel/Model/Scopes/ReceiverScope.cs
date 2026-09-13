using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A body run with a value of another type as its receiver (newtypes spec Requirement 10.1): <c>this</c>
/// and every instance member answer from that type, while a static member or a type name answers from the
/// declaration around the body, as it does in the member the body was written for.
/// </summary>
internal sealed class ReceiverScope(Scope parent, IMistType receiver) : Scope(parent)
{
    internal override LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);
        if(self is not NotFoundResult)
        {
            return self;
        }

        return Parent?.Lookup(request.WithoutReceiver()) ?? new NotFoundResult();
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(!request.Permits(isStatic: false))
        {
            return new NotFoundResult();
        }

        if(request.Name == LookupRequest.ReceiverName)
        {
            return new ReceiverResult(receiver);
        }

        var definition = (MistType)((receiver as ConstructedType)?.Definition ?? receiver);
        var found = TypeMemberLookup.Find(definition, request with { Visibility = MemberVisibility.InstanceOnly });

        return receiver is ConstructedType construction ? ConstructionScope.Through(found, construction) : found;
    }
}