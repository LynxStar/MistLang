using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record ComponentFacts(
    string FullName,
    ImmutableArray<ParameterFacts> Parameters,
    int TypeParameterCount,
    bool IsMist);

internal sealed record ParameterFacts(string Name, string TypeText, ParameterShape Shape)
{
    public static ParameterFacts Declared(string name, string typeText)
    {
        return new ParameterFacts(name, typeText, ParameterShapes.Of(typeText));
    }
}

internal sealed record EventFacts(string EventName, string ArgumentTypeFullName);

internal enum ParameterShape
{
    Plain,
    String,
    RenderFragment,
    TemplatedFragment,
    EventCallback,
}