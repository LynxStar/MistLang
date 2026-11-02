namespace Mist.Backends.CSharpTranspiler.Razor;

/// <summary>
/// Decides a parameter's shape from its type's text alone, so a Mist alias of <c>string</c> is
/// <see cref="ParameterShape.Plain"/> and its attribute value an expression: the one place the
/// catalog departs from Razor, which reads the resolved type. <c>design.md</c>, Detailed Design 4.
/// </summary>
internal static class ParameterShapes
{
    private const string ComponentsNamespace = "Microsoft.AspNetCore.Components.";

    public static ParameterShape Of(string typeText)
    {
        var text = string.Concat(typeText.Where(c => !char.IsWhiteSpace(c)));
        if(text is "string" or "string?")
        {
            return ParameterShape.String;
        }

        var open = text.IndexOf('<');
        var generic = open >= 0;
        if(generic && !text.TrimEnd('?').EndsWith('>'))
        {
            return ParameterShape.Plain;
        }

        var name = generic ? text[..open] : text.TrimEnd('?');
        if(name.StartsWith(ComponentsNamespace, StringComparison.Ordinal))
        {
            name = name[ComponentsNamespace.Length..];
        }

        return (name, generic) switch
        {
            ("RenderFragment", false) => ParameterShape.RenderFragment,
            ("RenderFragment", true) => ParameterShape.TemplatedFragment,
            ("EventCallback", _) => ParameterShape.EventCallback,
            _ => ParameterShape.Plain,
        };
    }
}