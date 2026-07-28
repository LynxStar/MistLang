namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// One namespace level, for a block namespace or for a file's own namespace.
/// <para>
/// Declares nothing itself: types written inside are registered into the program model under this
/// namespace and read back out here, so same-namespace scoping covers the whole namespace including
/// the parts other files contributed.
/// </para>
/// </summary>
internal sealed class NamespaceScope(Scope parent, string namespaceName, ProgramModel programModel)
    : Scope(parent)
{
    /// <summary>
    /// Explicit when written as <c>namespace X</c>, inferred from the file's path otherwise. Nothing
    /// here can tell the difference, and nothing should.
    /// </summary>
    public string NamespaceName => namespaceName;

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(programModel.FindType(namespaceName, request.Name) is { } type)
        {
            return new TypeResult(type);
        }

        // A namespace nested in this one, which a further qualification can step off. Checked after
        // types, though a valid program cannot declare both, so the order is only ever a tiebreak.
        var nested = string.IsNullOrEmpty(namespaceName)
            ? request.Name
            : $"{namespaceName}.{request.Name}";

        if(programModel.IsKnownNamespace(nested))
        {
            return new NamespaceResult(nested);
        }

        return new NotFoundResult();
    }
}