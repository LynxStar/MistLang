using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record CatalogBase(string FullName, ImmutableArray<string> Arguments, MistClass? Mist, Type? Clr)
{
    public ImmutableArray<string> TypeParameters => Mist?.TypeParameters
        ?? [.. Clr!.GetGenericArguments().Select(argument => argument.Name)];
}

internal sealed class CatalogBaseLookup(IReadOnlyDictionary<string, MistClass> classes, Func<string, Type?> findClrType)
{
    public CatalogBase? Resolve(string text, ImmutableArray<string> scope)
    {
        return Resolve(CatalogTypeText.Parse(text), scope, []);
    }

    private CatalogBase? Resolve(CatalogTypeText type, ImmutableArray<string> scope, HashSet<string> visiting)
    {
        var key = type.Name + ":" + string.Join('|', scope);
        if(!visiting.Add(key))
        {
            return null;
        }

        try
        {
            foreach(var prefix in scope.Prepend("").Distinct(StringComparer.Ordinal))
            {
                var name = string.IsNullOrEmpty(prefix) ? type.Name : prefix + "." + type.Name;
                if(Direct(name, type) is { } direct)
                {
                    return direct;
                }

                if(!string.IsNullOrEmpty(prefix)
                    && Direct(prefix, CatalogTypeText.Parse(prefix)) is { } enclosing
                    && NestedInBases(enclosing, type, visiting) is { } inherited)
                {
                    return inherited;
                }
            }

            return null;
        }
        finally
        {
            _ = visiting.Remove(key);
        }
    }

    private CatalogBase? Direct(string name, CatalogTypeText type)
    {
        if(classes.TryGetValue(name, out var mist))
        {
            return new CatalogBase(name, type.ArgumentsFor(mist.TypeParameterScopes), mist, null);
        }

        if(findClrType(name) is not { } clr)
        {
            return null;
        }

        var scopes = new Stack<ImmutableArray<string>>();
        for(var segment = clr; segment is not null; segment = segment.DeclaringType)
        {
            var enclosingCount = segment.DeclaringType?.GetGenericArguments().Length ?? 0;
            scopes.Push([.. segment.GetGenericArguments().Skip(enclosingCount).Select(argument => argument.Name)]);
        }

        return new CatalogBase(ClrTypeText.Name(clr), type.ArgumentsFor([.. scopes]), null, clr);
    }

    private CatalogBase? NestedInBases(CatalogBase enclosing, CatalogTypeText nested, HashSet<string> visiting)
    {
        var key = "bases:" + enclosing.FullName;
        if(!visiting.Add(key))
        {
            return null;
        }

        try
        {
            foreach(var baseType in Bases(enclosing, visiting))
            {
                if(Direct(baseType.FullName + "." + nested.Name, nested) is { } found)
                {
                    return found with { Arguments = [.. baseType.Arguments, .. found.Arguments.Skip(baseType.TypeParameters.Length)] };
                }

                if(NestedInBases(baseType, nested, visiting) is { } inherited)
                {
                    return inherited;
                }
            }

            return null;
        }
        finally
        {
            _ = visiting.Remove(key);
        }
    }

    private IEnumerable<CatalogBase> Bases(CatalogBase enclosing, HashSet<string> visiting)
    {
        var bases = enclosing.Mist?.BaseNames
            ?? (enclosing.Clr?.BaseType is { } baseType ? [ClrTypeText.Of(baseType)] : ImmutableArray<string>.Empty);
        foreach(var text in bases)
        {
            if(Resolve(CatalogTypeText.Parse(text), enclosing.Mist?.Scope ?? [], visiting) is { } resolved)
            {
                yield return resolved with
                {
                    Arguments = [.. resolved.Arguments.Select(argument => CatalogTypeText.Substitute(argument, enclosing.TypeParameters, enclosing.Arguments))],
                };
            }
        }
    }
}