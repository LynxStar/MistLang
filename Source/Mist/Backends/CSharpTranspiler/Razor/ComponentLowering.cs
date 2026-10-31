using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class ComponentLowering(RenderProtocolWriter render)
{
    private const string ChildContent = "ChildContent";
    private const string Context = "Context";

    private readonly ElementLowering attributes = new(render);

    public void Write(MarkupElement tag, ComponentFacts facts)
    {
        if(facts.TypeParameterCount > 0)
        {
            render.Report(RazorDiagnostics.GenericComponentNotSupported, tag.NameSpan, new TypeValue(facts.FullName, facts.FullName));
        }

        var parameters = facts.Parameters.GroupBy(parameter => parameter.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var childContent = parameters.GetValueOrDefault(ChildContent);
        var templatedChild = childContent?.Shape == ParameterShape.TemplatedFragment;
        render.Call(tag.StartTag, $"OpenComponent<{tag.Name}>", $"{render.NextSequence()}");
        DirectiveAttribute? key = null;
        DirectiveAttribute? capture = null;
        DirectiveAttribute? mode = null;
        HtmlAttribute? context = null;
        foreach(var attribute in tag.Attributes)
        {
            switch(attribute)
            {
                case HtmlAttribute { Name: Context } html when templatedChild:
                    context = html;
                    break;
                case HtmlAttribute html:
                    WriteParameter(html, parameters.GetValueOrDefault(html.Name));
                    break;
                case DirectiveAttribute { Name: "key", Parameter: null, Value: not null } directive:
                    key = directive;
                    break;
                case DirectiveAttribute { Name: "ref", Parameter: null, Value: not null } directive:
                    capture = directive;
                    break;
                case DirectiveAttribute { Name: "rendermode", Parameter: null, Value: not null } directive:
                    mode = directive;
                    break;
                case DirectiveAttribute { Name: "attributes", Parameter: null, Value: { } value } directive:
                    render.Call(directive.Span, "AddMultipleAttributes", $"{render.NextSequence()}, ", render.Code(value));
                    break;
                case DirectiveAttribute { Name: ['b', 'i', 'n', 'd', '-', _, ..], Parameter: null, Value: { } bound } directive:
                    WriteBind(directive, bound, facts, parameters);
                    break;
                case DirectiveAttribute directive:
                    attributes.Unknown(directive, "component");
                    break;
            }
        }

        WriteChildren(tag, parameters, childContent, context);
        if(key is { Value: { } keyValue })
        {
            render.Call(key.Span, "SetKey", render.Code(keyValue));
        }

        if(capture is { Value: { } field })
        {
            render.Call(capture.Span, "AddComponentReferenceCapture",
                $"{render.NextSequence()}, (System.Object __r) => {{ if (__r is {tag.Name} __c) {{ ", render.Code(field), " = __c; } }");
        }

        if(mode is { Value: { } modeValue })
        {
            render.Call(mode.Span, "AddComponentRenderMode", render.Code(modeValue));
        }

        render.Call(tag.EndTag ?? tag.StartTag, "CloseComponent");
    }

    private static string? TypeArgument(string typeText)
    {
        var open = typeText.IndexOf('<');
        var close = typeText.LastIndexOf('>');
        return open < 0 || close < open ? null : typeText[(open + 1)..close].Trim();
    }

    private void WriteParameter(HtmlAttribute attribute, ParameterFacts? parameter)
    {
        var sequence = render.NextSequence();
        var name = MistLiteral.String(attribute.Name);
        if(attribute.Value is not { } value)
        {
            render.Call(attribute.Span, "AddComponentParameter", $"{sequence}, {name}, true");
            return;
        }

        var literal = value.Parts.All(part => part is MarkupText);
        if(literal && parameter?.Shape is null or ParameterShape.String)
        {
            var text = string.Concat(value.Parts.OfType<MarkupText>().Select(part => part.Text));
            render.Call(attribute.Span, "AddComponentParameter", $"{sequence}, {name}, {MistLiteral.String(text)}");
            return;
        }

        var expression = literal ? [render.Code(value.Span)] : attributes.Value(value);
        if(parameter?.Shape == ParameterShape.EventCallback)
        {
            var argument = TypeArgument(parameter.TypeText);
            expression = [argument is null ? "EventCallback.Factory.Create(this, " : $"EventCallback.Factory.Create<{argument}>(this, ", .. expression, ")"];
        }

        render.Call(attribute.Span, "AddComponentParameter", [$"{sequence}, {name}, ", .. expression]);
    }

    private void WriteBind(DirectiveAttribute directive, CodeRegion field, ComponentFacts facts, Dictionary<string, ParameterFacts> parameters)
    {
        var name = directive.Name["bind-".Length..];
        if(parameters.ContainsKey(name + "Expression"))
        {
            render.Report(RazorDiagnostics.ExpressionParameterNotSupported, directive.NameSpan,
                new NameValue(name), new TypeValue(facts.FullName, facts.FullName));
        }

        var type = parameters.GetValueOrDefault(name)?.TypeText;
        render.Call(directive.Span, "AddComponentParameter", $"{render.NextSequence()}, {MistLiteral.String(name)}, ", render.Code(field));
        render.Call(directive.Span, "AddComponentParameter",
            [$"{render.NextSequence()}, {MistLiteral.String(name + "Changed")}, EventCallback.Factory.Create{(type is null ? "" : $"<{type}>")}(this, ",
                .. attributes.Setter(field, type), ")"]);
    }

    private void WriteChildren(MarkupElement tag, Dictionary<string, ParameterFacts> parameters, ParameterFacts? childContent, HtmlAttribute? context)
    {
        var content = MarkupWhitespace.Content(tag.Children);
        var templates = content.OfType<MarkupElement>()
            .Where(child => parameters.GetValueOrDefault(child.Name)?.Shape is ParameterShape.RenderFragment or ParameterShape.TemplatedFragment)
            .ToImmutableArray();
        var rest = MarkupWhitespace.Content([.. content.Where(node => !templates.Contains(node))]);
        if(!rest.IsEmpty)
        {
            WriteTemplate(RenderProtocolWriter.SpanOf(rest), ChildContent, childContent, context, rest);
        }

        foreach(var template in templates)
        {
            var named = template.Attributes.OfType<HtmlAttribute>().FirstOrDefault(attribute => attribute.Name == Context);
            WriteTemplate(template.Span, template.Name, parameters[template.Name], named, MarkupWhitespace.Content(template.Children));
        }
    }

    private void WriteTemplate(SourceSpan construct, string name, ParameterFacts? parameter, HtmlAttribute? context, ImmutableArray<RazorNode> content)
    {
        var valueType = parameter?.Shape == ParameterShape.TemplatedFragment ? TypeArgument(parameter.TypeText) : null;
        MistPiece contextName = context?.Value is { Parts: [MarkupText] } value ? render.Code(value.Span) : "context";
        render.WriteFragment(construct, name, valueType, contextName, content);
    }
}