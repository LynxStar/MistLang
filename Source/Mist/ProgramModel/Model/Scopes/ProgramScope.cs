namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// The root, and the global namespace level: types declared under no namespace at all, and the last
/// word on whether a name is a namespace.
/// </summary>
internal sealed class ProgramScope(ProgramModel programModel) : Scope(parent: null)
{
    protected override LookupResult LookupSelf(LookupRequest request)
    {
        // One probe serves two cases: a type in the global namespace has its simple name AS its
        // fully qualified name, so `Foo` and `A.B.Foo` are looked up the same way.
        if(programModel.FindQualifiedType(request.Name) is { } type)
        {
            return new TypeResult(type);
        }

        // Or a namespace being used to qualify something, as `Simpliest` in
        // `Simpliest.Program.RunDemo()`.
        if(programModel.IsKnownNamespace(request.Name))
        {
            return new NamespaceResult(request.Name);
        }

        return new NotFoundResult();
    }
}