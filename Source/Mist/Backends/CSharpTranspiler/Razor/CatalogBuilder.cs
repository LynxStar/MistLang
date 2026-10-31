using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class CatalogBuilder
{
    private readonly Dictionary<string, MistClass> classes = new(StringComparer.Ordinal);
    private readonly HashSet<string> components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImmutableArray<ParameterFacts>> parameters = new(StringComparer.Ordinal);
    private readonly ClrComponents clr;

    private CatalogBuilder(IEnumerable<MistClass> declared, ClrComponents clr)
    {
        foreach(var declaration in declared)
        {
            _ = classes.TryAdd(declaration.FullName, declaration);
        }

        this.clr = clr;
        components.UnionWith(classes.Values.Where(declaration => declaration.IsComponentClass).Select(declaration => declaration.FullName));
        var changed = true;
        while(changed)
        {
            var derived = classes.Values
                .Where(declaration => !components.Contains(declaration.FullName)
                    && declaration.BaseNames.Any(name => BaseIsComponent(name, declaration.Scope)))
                .Select(declaration => declaration.FullName)
                .ToArray();
            components.UnionWith(derived);
            changed = derived.Length > 0;
        }
    }

    public static ComponentCatalog Build(TranslationContext context, ImmutableArray<ComponentSource> components)
    {
        var clr = new ClrComponents(context.AmbientTypes);
        var declared = components
            .Select(component => MistClassDeclarations.FromComponent(component, context.Lexer, context.Parser, context.Diagnostics.Operation))
            .Concat(context.ParsedUnits.SelectMany(unit => MistClassDeclarations.FromUnit(unit, context.Project.GlobalUsings)));
        var builder = new CatalogBuilder(declared, clr);
        var mistComponents = builder.classes.Values
            .Where(declaration => builder.components.Contains(declaration.FullName))
            .Select(declaration => new ComponentFacts(
                declaration.FullName, builder.Parameters(declaration, []), declaration.TypeParameterCount, IsMist: true));
        return new ComponentCatalog([.. mistComponents], clr.Find, clr.Events());
    }

    private bool BaseIsComponent(string baseName, ImmutableArray<string> scope)
    {
        var candidates = Candidates(baseName, scope).ToArray();
        return candidates.FirstOrDefault(classes.ContainsKey) is { } mist
            ? components.Contains(mist)
            : candidates.Any(clr.IsComponentBase);
    }

    private ImmutableArray<ParameterFacts> Parameters(MistClass declaration, HashSet<string> visiting)
    {
        if(parameters.TryGetValue(declaration.FullName, out var known))
        {
            return known;
        }

        var inherited = visiting.Add(declaration.FullName) && !declaration.BaseNames.IsEmpty
            ? Inherited(declaration.BaseNames[0], declaration.Scope, visiting)
            : [];
        var names = declaration.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var result = declaration.Parameters.AddRange(inherited.Where(parameter => !names.Contains(parameter.Name)));
        parameters[declaration.FullName] = result;
        return result;
    }

    private ImmutableArray<ParameterFacts> Inherited(string baseName, ImmutableArray<string> scope, HashSet<string> visiting)
    {
        foreach(var candidate in Candidates(baseName, scope))
        {
            if(classes.TryGetValue(candidate, out var mist))
            {
                return Parameters(mist, visiting);
            }

            if(clr.Find(candidate) is { } facts)
            {
                return facts.Parameters;
            }
        }

        return [];
    }

    private static IEnumerable<string> Candidates(string name, ImmutableArray<string> scope)
    {
        return scope.Select(namespaceName => namespaceName + "." + name).Prepend(name).Distinct(StringComparer.Ordinal);
    }
}