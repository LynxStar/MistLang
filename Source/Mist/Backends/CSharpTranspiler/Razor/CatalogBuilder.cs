using System.Collections.Immutable;
using Mist.Backends.Contracts;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class CatalogBuilder
{
    private readonly Dictionary<string, MistClass> classes = new(StringComparer.Ordinal);
    private readonly HashSet<string> components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImmutableArray<ParameterFacts>> parameters = new(StringComparer.Ordinal);
    private readonly ClrComponents clr;
    private readonly CatalogBaseLookup bases;

    private CatalogBuilder(IEnumerable<MistClass> declared, ClrComponents clr)
    {
        foreach(var declaration in declared)
        {
            _ = classes.TryAdd(declaration.FullName, declaration);
        }

        this.clr = clr;
        bases = new CatalogBaseLookup(classes, clr.Lookup);
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
                declaration.FullName, builder.Parameters(declaration, []), declaration.TypeParameters.Length, IsMist: true));
        return new ComponentCatalog([.. mistComponents], clr.Find, clr.Events());
    }

    private bool BaseIsComponent(string baseName, ImmutableArray<string> scope)
    {
        var resolved = bases.Resolve(baseName, scope);
        return resolved?.Mist is { } mist
            ? components.Contains(mist.FullName)
            : resolved is not null && clr.IsComponentBase(resolved.FullName);
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
        var resolved = bases.Resolve(baseName, scope);
        if(resolved?.Mist is { } mist)
        {
            return CatalogTypeText.Substitute(Parameters(mist, visiting), mist.TypeParameters, resolved.Arguments);
        }

        return resolved is not null && clr.FindBase(resolved.FullName, resolved.Arguments) is { } facts ? facts.Parameters : [];
    }
}