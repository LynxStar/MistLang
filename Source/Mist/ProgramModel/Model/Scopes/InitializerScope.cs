namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A field or property initializer. Declares nothing of its own, since an initializer has no locals
/// and no type parameters; it exists only to apply <see cref="LookupRequest.WithoutReceiver"/>, which
/// documents when that applies and why.
/// <para>
/// Unconditionally, unlike <see cref="MethodScope"/> -- there is no staticness to consult, because an
/// initializer is receiver-less either way. That is what keeps the emitter from writing a C# field
/// initializer that reads an instance member, which C# rejects outright: the name does not resolve.
/// </para>
/// </summary>
internal sealed class InitializerScope(Scope? parent) : Scope(parent)
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
        // Stops here with an answer of its own rather than travelling out and coming back as "no
        // enclosing type", which is a different mistake with a different diagnostic.
        return request.Name == LookupRequest.ReceiverName
            ? new StaticReceiverResult()
            : new NotFoundResult()
            ;
    }
}