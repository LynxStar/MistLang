namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Concrete rather than abstract because a scope that declares nothing is a useful thing to have --
/// it is what a synthetic type gets.
/// </summary>
internal class Scope
{
    public Scope(Scope? parent)
    {
        Parent = parent;
    }

    public Scope? Parent { get; }

    internal virtual LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);
        if(self is not NotFoundResult)
        {
            return self;
        }

        return Parent?.Lookup(request) ?? new NotFoundResult();
    }

    protected virtual LookupResult LookupSelf(LookupRequest request)
    {
        return new NotFoundResult();
    }
}