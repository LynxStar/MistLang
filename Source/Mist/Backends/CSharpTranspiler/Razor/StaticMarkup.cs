using System.Collections.Immutable;
using System.Net;
using System.Text;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record ContentRun(ImmutableArray<RazorNode> Nodes, bool IsStatic);

/// <summary>
/// Which markup lowers to one <c>AddMarkupContent</c> string, as the Razor compiler folds it: an
/// element with no code, component or directive attribute anywhere inside is static, and a static
/// node joins its static left sibling only when it has no static content of its own, because Razor
/// folds siblings in a postorder walk that meets an element's children before the sibling.
/// </summary>
internal sealed class StaticMarkup(string source, Func<MarkupElement, bool> isComponent)
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private readonly Dictionary<RazorNode, bool> decided = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<RazorNode, bool> decidedInSelect = new(ReferenceEqualityComparer.Instance);

    public static bool TryPlainText(string text, out string decoded)
    {
        decoded = text;
        if(text.Any(c => c is > (char)127 or '\r' or '\n' or '\t'))
        {
            return false;
        }

        var result = new StringBuilder();
        var index = 0;
        while(index < text.Length)
        {
            if(text[index] != '&')
            {
                _ = result.Append(text[index++]);
                continue;
            }

            var end = index + 1;
            while(end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '#'))
            {
                end++;
            }

            if(end >= text.Length || text[end] != ';' || end == index + 1)
            {
                return false;
            }

            var entity = text[index..(end + 1)];
            var value = WebUtility.HtmlDecode(entity);
            if(value == entity)
            {
                return false;
            }

            _ = result.Append(value);
            index = end + 1;
        }

        decoded = result.ToString();
        return true;
    }

    public bool IsStatic(RazorNode node, bool insideSelect = false)
    {
        if(node is MarkupText or HtmlComment or RazorComment)
        {
            return true;
        }

        if(node is not MarkupElement element)
        {
            return false;
        }

        var decisions = insideSelect ? decidedInSelect : decided;
        if(!decisions.TryGetValue(element, out var result))
        {
            result = !(insideSelect && element.Name.Equals("option", StringComparison.OrdinalIgnoreCase))
                && !isComponent(element)
                && element.Attributes.All(attribute => attribute is HtmlAttribute html
                    && (html.Value is null || html.Value.Parts.All(part => part is MarkupText)))
                && MarkupWhitespace.Content(element.Children).All(child => IsStatic(child, insideSelect || element.Name.Equals("select", StringComparison.OrdinalIgnoreCase)));
            decisions[element] = result;
        }

        return result;
    }

    public ImmutableArray<ContentRun> Runs(ImmutableArray<RazorNode> nodes, bool insideSelect = false)
    {
        var runs = new List<ContentRun>();
        var index = nodes.Length - 1;
        while(index >= 0)
        {
            if(!IsStatic(nodes[index], insideSelect))
            {
                runs.Add(new ContentRun([nodes[index]], IsStatic: false));
                index--;
                continue;
            }

            var start = index;
            while(start > 0 && IsStatic(nodes[start - 1], insideSelect) && !HasStaticContent(nodes[start]))
            {
                start--;
            }

            runs.Add(new ContentRun([.. nodes[start..(index + 1)]], IsStatic: true));
            index = start - 1;
        }

        runs.Reverse();
        return [.. runs];
    }

    public string Render(ImmutableArray<RazorNode> nodes)
    {
        var html = new StringBuilder();
        foreach(var node in nodes)
        {
            Render(node, html);
        }

        return html.ToString();
    }

    private static bool HasStaticContent(RazorNode node)
    {
        return node is MarkupElement element && MarkupWhitespace.Content(element.Children).Any(child => child is not (HtmlComment or RazorComment));
    }

    private void Render(RazorNode node, StringBuilder html)
    {
        switch(node)
        {
            case MarkupText text:
                _ = html.Append(text.Text);
                return;
            case MarkupElement element:
                _ = html.Append('<').Append(element.Name);
                foreach(var attribute in element.Attributes.OfType<HtmlAttribute>())
                {
                    RenderAttribute(attribute, html);
                }

                _ = html.Append('>');
                var children = MarkupWhitespace.Content(element.Children);
                if(children.IsEmpty && VoidElements.Contains(element.Name))
                {
                    return;
                }

                foreach(var child in children)
                {
                    Render(child, html);
                }

                _ = html.Append("</").Append(element.Name).Append('>');
                return;
        }
    }

    private void RenderAttribute(HtmlAttribute attribute, StringBuilder html)
    {
        _ = html.Append(' ').Append(attribute.Name);
        var value = string.Concat(attribute.Value?.Parts.OfType<MarkupText>().Select(part => part.Text) ?? []);
        if(value.Length == 0)
        {
            return;
        }

        var start = attribute.Value!.Span.Start;
        var quote = start > 0 && source[start - 1] == '\'' ? '\'' : '"';
        _ = html.Append('=').Append(quote).Append(value).Append(quote);
    }
}