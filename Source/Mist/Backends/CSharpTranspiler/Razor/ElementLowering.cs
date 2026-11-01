using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed class ElementLowering(RenderProtocolWriter render)
{
    private static readonly HashSet<string> BindModifiers = new(StringComparer.Ordinal) { "event", "format", "culture" };

    public void Write(MarkupElement element)
    {
        render.Call(element.StartTag, "OpenElement", $"{render.NextSequence()}, {MistLiteral.String(element.Name)}");
        var bind = element.Attributes.OfType<DirectiveAttribute>().FirstOrDefault(attribute => attribute is { Name: "bind", Parameter: null });
        DirectiveAttribute? key = null;
        DirectiveAttribute? capture = null;
        var binds = new List<(DirectiveAttribute Directive, CodeRegion Field)>();
        foreach(var attribute in element.Attributes)
        {
            switch(attribute)
            {
                case HtmlAttribute html:
                    WriteAttribute(html);
                    break;
                case DirectiveAttribute { Name: "key", Parameter: null, Value: not null } directive:
                    key = directive;
                    break;
                case DirectiveAttribute { Name: "ref", Parameter: null, Value: not null } directive:
                    capture = directive;
                    break;
                case DirectiveAttribute { Name: "attributes", Parameter: null, Value: { } value } directive:
                    render.Call(directive.Span, "AddMultipleAttributes", $"{render.NextSequence()}, ", render.Code(value));
                    break;
                case DirectiveAttribute { Name: "bind", Parameter: null, Value: { } value } directive:
                    binds.Add((directive, value));
                    break;
                case DirectiveAttribute { Name: "bind", Parameter: { } modifier } when bind is not null && BindModifiers.Contains(modifier):
                    break;
                case DirectiveAttribute { Name: ['o', 'n', _, ..] } directive when WriteEvent(directive):
                    break;
                case DirectiveAttribute directive:
                    Unknown(directive, "element");
                    break;
            }
        }

        foreach(var (directive, bound) in binds)
        {
            WriteBind(directive, bound, element.Attributes);
        }

        if(key is { Value: { } keyValue })
        {
            render.Call(key.Span, "SetKey", render.Code(keyValue));
        }

        if(capture is { Value: { } field })
        {
            render.Call(capture.Span, "AddElementReferenceCapture",
                $"{render.NextSequence()}, (ElementReference __r) => {{ ", render.Code(field), " = __r; }");
        }

        render.WriteContent(MarkupWhitespace.Content(element.Children));
        render.Call(element.EndTag ?? element.StartTag, "CloseElement");
    }

    public void Unknown(DirectiveAttribute directive, string target)
    {
        var name = directive.Parameter is null ? directive.Name : $"{directive.Name}:{directive.Parameter}";
        render.Report(RazorDiagnostics.UnknownDirectiveAttribute, directive.NameSpan, new NameValue(name), new EnumValue(target));
    }

    public ImmutableArray<MistPiece> Value(AttributeValue value)
    {
        if(value.Parts is [ImplicitExpression { Code: var implicitCode }])
        {
            return [render.Code(implicitCode)];
        }

        if(value.Parts is [ExplicitExpression { Code: var explicitCode }])
        {
            return [render.Code(explicitCode)];
        }

        var pieces = ImmutableArray.CreateBuilder<MistPiece>();
        foreach(var part in value.Parts)
        {
            if(pieces.Count > 0)
            {
                pieces.Add(" + ");
            }

            MistPiece[] piece = part switch
            {
                ImplicitExpression { Code: var code } => ["(", render.Code(code), ")"],
                ExplicitExpression { Code: var code } => ["(", render.Code(code), ")"],
                MarkupText text => [MistLiteral.String(text.Text)],
                _ => [],
            };
            pieces.AddRange(piece);
        }

        return pieces.ToImmutable();
    }

    private void WriteAttribute(HtmlAttribute attribute)
    {
        var name = MistLiteral.String(attribute.Name);
        if(attribute.Value is not { Parts.IsEmpty: false } value || value.Parts.All(part => part is MarkupText { Text.Length: 0 }))
        {
            render.Call(attribute.Span, "AddAttribute", $"{render.NextSequence()}, {name}");
            return;
        }

        render.Call(attribute.Span, "AddAttribute", [$"{render.NextSequence()}, {name}, ", .. Value(value)]);
    }

    private bool WriteEvent(DirectiveAttribute directive)
    {
        var eventName = MistLiteral.String(directive.Name);
        switch(directive.Parameter)
        {
            case null when directive.Value is { } handler:
                var arguments = render.TypeName(render.Scope.Catalog.EventArgumentType(directive.Name));
                render.Call(directive.Span, "AddAttribute",
                    $"{render.NextSequence()}, {eventName}, EventCallback.Factory.Create<{arguments}>(this, ", render.Code(handler), ")");
                return true;
            case "preventDefault" or "stopPropagation":
                var method = directive.Parameter == "preventDefault" ? "AddEventPreventDefaultAttribute" : "AddEventStopPropagationAttribute";
                MistPiece flag = directive.Value is { } value ? render.Code(value) : "true";
                render.Call(directive.Span, method, $"{render.NextSequence()}, {eventName}, ", flag);
                return true;
            default:
                return false;
        }
    }

    private void WriteBind(DirectiveAttribute directive, CodeRegion field, ImmutableArray<MarkupAttribute> attributes)
    {
        var modifiers = attributes.OfType<DirectiveAttribute>()
            .Where(attribute => attribute is { Name: "bind", Parameter: not null, Value: not null })
            .GroupBy(attribute => attribute.Parameter!)
            .ToDictionary(group => group.Key, group => group.Last().Value!, StringComparer.Ordinal);
        var extra = new List<MistPiece>();
        if(modifiers.TryGetValue("format", out var format))
        {
            extra.AddRange([", ", MistLiteral.String(format.Text)]);
        }

        if(modifiers.TryGetValue("culture", out var culture))
        {
            extra.AddRange([", ", render.Code(culture)]);
        }

        var eventName = modifiers.TryGetValue("event", out var changeEvent) ? changeEvent.Text : "onchange";
        render.Call(directive.Span, "AddAttribute",
            [$"{render.NextSequence()}, \"value\", BindConverter.FormatValue(", render.Code(field), .. extra, ")"]);
        render.Call(directive.Span, "AddAttribute",
            [$"{render.NextSequence()}, {MistLiteral.String(eventName)}, EventCallback.Factory.CreateBinder(this, ",
                .. Setter(field, render.Scope.MemberTypes.GetValueOrDefault(field.Text)), ", ", render.Code(field), .. extra, ")"]);
        render.Call(directive.Span, "SetUpdatesAttributeName", MistLiteral.String("value"));
    }

    public ImmutableArray<MistPiece> Setter(CodeRegion field, string? type)
    {
        return [type is null ? "(__v) => { " : $"({type} __v) => {{ ", render.Code(field), " = __v; }"];
    }
}