namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A name resolution scope: what it declares itself, then its parent.
/// <para>
/// Concrete rather than abstract because a scope that declares nothing is a useful thing to have --
/// it is what a synthetic type gets.
/// </para>
/// </summary>
internal class Scope
{
    public Scope(Scope? parent)
    {
        Parent = parent;
    }

    public Scope? Parent { get; }

    /// <summary>
    /// Resolves a name from here outward.
    /// </summary>
    internal virtual LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);
        if(self is not NotFoundResult)
        {
            return self;
        }

        return Parent?.Lookup(request) ?? new NotFoundResult();
    }

    /// <summary>
    /// What this scope itself declares.
    /// </summary>
    protected virtual LookupResult LookupSelf(LookupRequest request)
    {
        return new NotFoundResult();
    }
}