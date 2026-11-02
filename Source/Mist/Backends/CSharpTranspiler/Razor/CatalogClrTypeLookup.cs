using System.Reflection;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal static class CatalogClrTypeLookup
{
    public static Type? Find(string fullName, Func<string, bool, Type?> find, int? arity = null)
    {
        var metadataName = arity is > 0 ? $"{fullName}`{arity}" : fullName;
        if(find(metadataName, arity is null) is { } direct)
        {
            return direct;
        }

        for(var dot = fullName.LastIndexOf('.'); dot > 0; dot = fullName.LastIndexOf('.', dot - 1))
        {
            if(find(fullName[..dot], true) is { } containing
                && FindNested(containing, fullName[(dot + 1)..], arity) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static Type? FindNested(Type containing, string path, int? arity)
    {
        var names = path.Split('.');
        for(var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            var candidates = containing.GetNestedTypes(BindingFlags.Public)
                .Where(type => index < names.Length - 1 || arity is null || type.GetGenericArguments().Length == arity);
            var nested = candidates.FirstOrDefault(type => type.Name == name)
                ?? candidates.FirstOrDefault(type => type.Name.StartsWith(name + "`", StringComparison.Ordinal));
            if(nested is null)
            {
                return null;
            }

            containing = nested;
        }

        return containing;
    }
}