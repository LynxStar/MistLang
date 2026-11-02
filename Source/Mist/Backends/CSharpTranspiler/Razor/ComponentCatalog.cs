using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class ComponentCatalog
{
    public const string FallbackEventArgs = "System.EventArgs";

    private readonly Dictionary<string, ComponentFacts> mistComponents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> eventArguments = new(StringComparer.Ordinal);
    private readonly Func<string, ComponentFacts?> clrComponents;

    public ComponentCatalog(
        IEnumerable<ComponentFacts> mistComponents, Func<string, ComponentFacts?> clrComponents, IEnumerable<EventFacts> events)
    {
        foreach(var component in mistComponents)
        {
            _ = this.mistComponents.TryAdd(component.FullName, component);
        }

        foreach(var facts in events)
        {
            _ = eventArguments.TryAdd(facts.EventName, facts.ArgumentTypeFullName);
        }

        this.clrComponents = clrComponents;
    }

    public ComponentResolution Resolve(string tagName, ComponentScope scope)
    {
        var candidates = scope.Namespaces
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name + "." + tagName)
            .Prepend(tagName)
            .Distinct(StringComparer.Ordinal)
            .Select(Find)
            .OfType<ComponentFacts>()
            .ToImmutableArray();

        return candidates.Length switch
        {
            1 => new ComponentResolution(ResolutionKind.Component, candidates),
            > 1 => new ComponentResolution(ResolutionKind.Ambiguous, candidates),
            _ => new ComponentResolution(tagName.Length > 0 && char.IsUpper(tagName[0]) ? ResolutionKind.Unknown : ResolutionKind.Element, []),
        };
    }

    public ComponentFacts? Find(string fullName)
    {
        return mistComponents.GetValueOrDefault(fullName) ?? clrComponents(fullName);
    }

    public string EventArgumentType(string eventName)
    {
        return eventArguments.GetValueOrDefault(eventName, FallbackEventArgs);
    }
}