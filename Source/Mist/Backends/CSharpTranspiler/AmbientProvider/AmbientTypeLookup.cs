namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class AmbientTypeLookup
{
    public static Type? Find(
        string fullName, bool ignoreArity, Func<string, Type?> findExact, Func<string, IEnumerable<string>> namesInNamespace)
    {
        var exact = findExact(fullName);
        if(exact is not null || !ignoreArity)
        {
            return exact;
        }

        var dot = fullName.LastIndexOf('.');
        var namespaceName = dot < 0 ? "" : fullName[..dot];
        var name = fullName[(dot + 1)..];
        foreach(var candidate in namesInNamespace(namespaceName).Where(candidate => candidate.StartsWith(name + "`", StringComparison.Ordinal)))
        {
            var qualified = string.IsNullOrEmpty(namespaceName) ? candidate : namespaceName + "." + candidate;
            if(findExact(qualified) is { } generic)
            {
                return generic;
            }
        }

        return null;
    }
}