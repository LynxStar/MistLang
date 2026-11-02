using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed partial class RazorParser
{
    /// <summary>
    /// Code mode, from just inside a block's opening brace to its balancing close. Markup is
    /// recognized only where a statement could begin, the first non-blank of a line or the first
    /// after a brace or semicolon, so a comparison inside an expression stays Mist text.
    /// </summary>
    private (ImmutableArray<RazorNode> Body, bool Closed) ParseCodeBody()
    {
        var nodes = ImmutableArray.CreateBuilder<RazorNode>();
        var runStart = position;
        var depth = 0;
        var statementStart = true;
        while(!AtEnd)
        {
            var skipped = MistBalancer.SkipLiteral(text, position);
            if(skipped >= 0)
            {
                position = skipped;
                statementStart = false;
                continue;
            }

            var c = text[position];
            if(char.IsWhiteSpace(c))
            {
                statementStart |= c == '\n';
                position++;
                continue;
            }

            if(c == '}' && depth == 0)
            {
                AddCode(nodes, runStart, position);
                return (nodes.ToImmutable(), true);
            }

            if(At("@*") || (statementStart && AtCodeMarkup()))
            {
                AddCode(nodes, runStart, position);
                if(ParseCodeMarkup() is { } markup)
                {
                    nodes.Add(markup);
                }

                runStart = position;
                statementStart = true;
                continue;
            }

            if(c == '{')
            {
                depth++;
            }
            else if(c == '}')
            {
                depth--;
            }

            statementStart = c is '{' or '}' or ';';
            position++;
        }

        AddCode(nodes, runStart, position);
        return (nodes.ToImmutable(), false);
    }

    private bool AtCodeMarkup()
    {
        return (Peek() == '<' && (IsTagNameStart(Peek(1)) || Peek(1) == '/')) || At("<!--") || At("@:");
    }

    private RazorNode? ParseCodeMarkup()
    {
        if(At("@*"))
        {
            return ParseRazorComment();
        }

        if(At("<!--"))
        {
            return ParseHtmlComment();
        }

        if(At("@:"))
        {
            var start = position;
            position += 2;
            var content = ParseContent(ContentMode.TextLine);
            return new TextLine(content, SpanFrom(start));
        }

        if(At("</"))
        {
            ParseStrayEndTag();
            return null;
        }

        return ParseElement(inCode: true);
    }

    private void AddCode(ImmutableArray<RazorNode>.Builder nodes, int start, int end)
    {
        if(OptionalRegion(start, end) is { } region)
        {
            nodes.Add(region);
        }
    }
}