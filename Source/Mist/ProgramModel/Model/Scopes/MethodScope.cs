namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A method, constructor, operator or cast operator.
/// </summary>
internal sealed class MethodScope(Scope? parent) : MemberScope(parent)
{
    /// <summary>
    /// Whether the member is declared static.
    /// <para>
    /// Settable because the scope must exist before the declaration is resolved -- the signature
    /// needs somewhere to put its type parameters -- while the modifiers only arrive from that same
    /// resolution. Nothing reads it in between: signature resolution looks up type references,
    /// which exclude members outright.
    /// </para>
    /// </summary>
    public bool IsStatic { get; internal set; }

    /// <summary>
    /// A static member is inside its type but has no instance of it, so it crosses the same
    /// boundary a nested type does.
    /// </summary>
    internal override LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);
        if(self is not NotFoundResult)
        {
            return self;
        }

        var outward = IsStatic ? request.CrossTypeBoundary() : request;

        return Parent?.Lookup(outward) ?? new NotFoundResult();
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        // Stops here with an answer of its own rather than travelling out and coming back as "no
        // enclosing type", which is a different mistake with a different diagnostic.
        if(IsStatic && request.Name == LookupRequest.ReceiverName)
        {
            return new StaticReceiverResult();
        }

        return base.LookupSelf(request);
    }
}