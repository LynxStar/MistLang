using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record ComponentScope(ImmutableArray<string> Namespaces);

internal enum ResolutionKind
{
    Element,
    Component,
    Unknown,
    Ambiguous,
}

internal sealed record ComponentResolution(ResolutionKind Kind, ImmutableArray<ComponentFacts> Candidates)
{
    public ComponentFacts? Component => Kind == ResolutionKind.Component ? Candidates[0] : null;
}