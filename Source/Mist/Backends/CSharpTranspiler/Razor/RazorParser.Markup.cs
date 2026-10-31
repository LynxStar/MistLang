using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed partial class RazorParser
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase) { "script", "style" };

    private enum StartTagEnd
    {
        Open,
        SelfClosed,
        Unterminated,
    }

    private static bool IsTagNameStart(char c)
    {
        return char.IsAsciiLetter(c);
    }

    private static bool IsTagNamePart(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':';
    }

    private ImmutableArray<RazorNode> ParseContent(ContentMode mode, string? rawTextElement = null)
    {
        var nodes = ImmutableArray.CreateBuilder<RazorNode>();
        var run = new MarkupRun();
        while(!AtEnd)
        {
            var c = text[position];
            if(mode == ContentMode.TextLine && (c == '\n' || (c == '\r' && Peek(1) == '\n')))
            {
                break;
            }

            if(c == '@')
            {
                ParseContentTransition(mode, nodes, run);
                continue;
            }

            if(c == '<' && mode is ContentMode.Document or ContentMode.Element && ParseContentTag(mode, nodes, run))
            {
                if(mode == ContentMode.Element && At("</"))
                {
                    break;
                }

                continue;
            }

            if(mode == ContentMode.RawText && AtEndTag(position, rawTextElement!))
            {
                break;
            }

            run.Append(position, c.ToString());
            position++;
        }

        run.Flush(nodes, position);
        return nodes.ToImmutable();
    }

    private void ParseContentTransition(ContentMode mode, ImmutableArray<RazorNode>.Builder nodes, MarkupRun run)
    {
        if(Peek(1) == '@' || IsEmailAt(position))
        {
            run.Append(position, "@");
            position += Peek(1) == '@' ? 2 : 1;
            return;
        }

        run.Flush(nodes, position);
        if(mode == ContentMode.Document && AtLineStart(position) && TryParseDirective())
        {
            return;
        }

        if(ParseTransition(text.Length) is { } node)
        {
            nodes.Add(node);
            return;
        }

        run.Append(position, "@");
        position++;
    }

    private bool ParseContentTag(ContentMode mode, ImmutableArray<RazorNode>.Builder nodes, MarkupRun run)
    {
        if(At("<!--"))
        {
            run.Flush(nodes, position);
            nodes.Add(ParseHtmlComment());
            return true;
        }

        if(At("</"))
        {
            run.Flush(nodes, position);
            if(mode == ContentMode.Document)
            {
                ParseStrayEndTag();
            }

            return true;
        }

        if(!IsTagNameStart(Peek(1)))
        {
            return false;
        }

        run.Flush(nodes, position);
        nodes.Add(ParseElement(inCode: false));
        return true;
    }

    private bool IsEmailAt(int index)
    {
        return index > 0 && char.IsLetterOrDigit(text[index - 1]) && index + 1 < text.Length && char.IsLetterOrDigit(text[index + 1]);
    }

    private bool AtEndTag(int index, string name)
    {
        var end = index + 2 + name.Length;
        return At(index, "</")
            && string.Compare(text, index + 2, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0
            && (end >= text.Length || !IsTagNamePart(text[end]));
    }

    private HtmlComment ParseHtmlComment()
    {
        var start = position;
        var close = text.IndexOf("-->", start + 4, StringComparison.Ordinal);
        if(close < 0)
        {
            Report(RazorDiagnostics.UnclosedElement, start, 4, new NameValue("!--"));
            position = text.Length;
        }
        else
        {
            position = close + 3;
        }

        return new HtmlComment(text[start..position], SpanFrom(start));
    }

    private RazorNode ParseElement(bool inCode)
    {
        var start = position;
        position++;
        var nameStart = position;
        while(!AtEnd && IsTagNamePart(text[position]))
        {
            position++;
        }

        var name = text[nameStart..position];
        var nameSpan = SpanFrom(nameStart);
        var attributes = ImmutableArray.CreateBuilder<MarkupAttribute>();
        var end = ParseAttributes(attributes);
        var startTag = SpanFrom(start);
        if(end == StartTagEnd.Unterminated)
        {
            Report(RazorDiagnostics.UnclosedElement, start, nameSpan.End - start, new NameValue(name));
            return new MarkupElement(name, nameSpan, attributes.ToImmutable(), [], false, startTag, null, SpanFrom(start));
        }

        if(end == StartTagEnd.SelfClosed || VoidElements.Contains(name))
        {
            return new MarkupElement(name, nameSpan, attributes.ToImmutable(), [], end == StartTagEnd.SelfClosed, startTag, null, SpanFrom(start));
        }

        openElements.Add(name);
        var children = RawTextElements.Contains(name) ? ParseContent(ContentMode.RawText, name) : ParseContent(ContentMode.Element);
        openElements.RemoveAt(openElements.Count - 1);
        var endTag = CloseElement(name, new SourceSpan(start, nameSpan.End - start));
        return inCode && name == "text" && attributes.Count == 0
            ? new TextTag(children, SpanFrom(start))
            : new MarkupElement(name, nameSpan, attributes.ToImmutable(), children, false, startTag, endTag, SpanFrom(start));
    }

    private SourceSpan? CloseElement(string name, SourceSpan openTag)
    {
        if(AtEnd)
        {
            Report(RazorDiagnostics.UnclosedElement, openTag, new NameValue(name));
            return null;
        }

        var start = position;
        var (endName, end) = ScanEndTag(start);
        var span = new SourceSpan(start, end - start);
        if(endName == name)
        {
            position = end;
            return span;
        }

        if(openElements.Contains(endName))
        {
            Report(RazorDiagnostics.UnclosedElement, openTag, new NameValue(name));
            return null;
        }

        Report(RazorDiagnostics.MismatchedEndTag, span, new NameValue(endName), new OptionalValue(SlotKind.Name, new NameValue(name)));
        position = end;
        return span;
    }

    private void ParseStrayEndTag()
    {
        var start = position;
        var (name, end) = ScanEndTag(start);
        Report(RazorDiagnostics.MismatchedEndTag, start, end - start, new NameValue(name), new OptionalValue(SlotKind.Name, null));
        position = end;
    }

    private (string Name, int End) ScanEndTag(int start)
    {
        var nameEnd = start + 2;
        while(nameEnd < text.Length && IsTagNamePart(text[nameEnd]))
        {
            nameEnd++;
        }

        var end = SkipWhitespace(nameEnd);
        return (text[(start + 2)..nameEnd], end < text.Length && text[end] == '>' ? end + 1 : nameEnd);
    }

    private StartTagEnd ParseAttributes(ImmutableArray<MarkupAttribute>.Builder attributes)
    {
        while(true)
        {
            position = SkipWhitespace(position);
            if(AtEnd || text[position] == '<')
            {
                return StartTagEnd.Unterminated;
            }

            if(At("/>"))
            {
                position += 2;
                return StartTagEnd.SelfClosed;
            }

            if(text[position] == '>')
            {
                position++;
                return StartTagEnd.Open;
            }

            if(ParseAttribute() is not { } attribute)
            {
                return StartTagEnd.Unterminated;
            }

            attributes.Add(attribute);
        }
    }

    private MarkupAttribute? ParseAttribute()
    {
        var start = position;
        var nameEnd = position + 1;
        while(nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] is not ('=' or '>' or '<' or '"' or '\'') && !At(nameEnd, "/>"))
        {
            nameEnd++;
        }

        var nameSpan = new SourceSpan(start, nameEnd - start);
        position = nameEnd;
        var equals = SkipWhitespace(position);
        if(equals >= text.Length || text[equals] != '=')
        {
            return Attribute(start, nameSpan, null, nameEnd);
        }

        var valueStart = SkipWhitespace(equals + 1);
        if(valueStart >= text.Length)
        {
            return null;
        }

        if(text[valueStart] is '"' or '\'')
        {
            var close = text.IndexOf(text[valueStart], valueStart + 1);
            return close < 0 ? null : Attribute(start, nameSpan, (valueStart + 1, close), close + 1);
        }

        var valueEnd = valueStart;
        while(valueEnd < text.Length && !char.IsWhiteSpace(text[valueEnd]) && text[valueEnd] is not ('>' or '<') && !At(valueEnd, "/>"))
        {
            valueEnd++;
        }

        return Attribute(start, nameSpan, (valueStart, valueEnd), valueEnd);
    }

    private MarkupAttribute Attribute(int start, SourceSpan nameSpan, (int Start, int End)? value, int end)
    {
        var name = text.Substring(nameSpan.Start, nameSpan.Length);
        MarkupAttribute attribute;
        if(name[0] != '@')
        {
            var parsed = value is { } range ? ParseAttributeValue(range.Start, range.End) : null;
            attribute = new HtmlAttribute(name, nameSpan, parsed, new SourceSpan(start, end - start));
        }
        else
        {
            var colon = name.IndexOf(':');
            var directive = colon < 0 ? name[1..] : name[1..colon];
            var parameter = colon < 0 ? null : name[(colon + 1)..];
            var code = value is { } bounds ? DirectiveAttributeValue(bounds.Start, bounds.End) : null;
            attribute = new DirectiveAttribute(directive, parameter, nameSpan, code, new SourceSpan(start, end - start));
        }

        position = end;
        return attribute;
    }

    private CodeRegion DirectiveAttributeValue(int start, int end)
    {
        var region = Region(start, end);
        var inner = region.Span.Start;
        if(region.Text.StartsWith("@(", StringComparison.Ordinal) && MistBalancer.FindClose(text, inner + 1) == region.Span.End - 1)
        {
            return Region(inner + 2, region.Span.End - 1);
        }

        return region.Text.Length > 1 && region.Text[0] == '@' && IsIdentifierStart(region.Text[1])
            ? Region(inner + 1, region.Span.End)
            : region;
    }

    private AttributeValue ParseAttributeValue(int start, int end)
    {
        var parts = ImmutableArray.CreateBuilder<RazorNode>();
        var run = new MarkupRun();
        position = start;
        while(position < end)
        {
            var c = text[position];
            if(c != '@' || IsEmailAt(position))
            {
                run.Append(position, c.ToString());
                position++;
                continue;
            }

            if(Peek(1) == '@')
            {
                run.Append(position, "@");
                position += 2;
                continue;
            }

            run.Flush(parts, position);
            var expression = Peek(1) == '(' || IsIdentifierStart(Peek(1)) ? ParseExpression(end) : null;
            if(expression is not null)
            {
                parts.Add(expression);
                continue;
            }

            Report(RazorDiagnostics.InvalidTransition, position, 1);
            run.Append(position, "@");
            position++;
        }

        run.Flush(parts, position);
        return new AttributeValue(parts.ToImmutable(), new SourceSpan(start, end - start));
    }
}