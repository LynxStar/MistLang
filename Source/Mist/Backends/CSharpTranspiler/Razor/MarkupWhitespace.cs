using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

/// <summary>
/// The whitespace the Razor compiler drops by default, read from its output for the fixture and
/// recorded in <c>design.md</c>, Detailed Design 6: whitespace-only text at either end of a
/// content list and on either side of code, the line break that ends a code block's line, and the
/// indentation around a <c>text</c> tag, whose content stays apart from the text beside it.
/// </summary>
internal static class MarkupWhitespace
{
    public static ImmutableArray<RazorNode> Content(ImmutableArray<RazorNode> nodes, ImmutableArray<CodeBlock> codeBlocks = default)
    {
        return [.. Trim(Merge(SplittingComments(nodes), codeBlocks)).Where(node => node is not TextBoundary)];
    }

    // A Razor comment between two texts keeps them separate siblings, which Razor trims apart and folds as markup; anywhere else it is nothing.
    private static IEnumerable<RazorNode> SplittingComments(ImmutableArray<RazorNode> nodes)
    {
        RazorComment? split = null;
        RazorNode? previous = null;
        foreach(var node in nodes)
        {
            if(node is RazorComment comment)
            {
                split ??= previous is MarkupText ? comment : null;
                continue;
            }

            if(split is not null && node is MarkupText)
            {
                yield return split;
            }

            split = null;
            previous = node;
            yield return node;
        }
    }

    public static ImmutableArray<RazorNode> CodeBody(ImmutableArray<RazorNode> nodes, string source)
    {
        var flat = new List<RazorNode>();
        RazorNode? previous = null;
        var previousEnd = 0;
        foreach(var node in nodes)
        {
            if(TakesGap(previous) && TakesGap(node) && node.Span.Start > previousEnd)
            {
                flat.Add(new MarkupText(source[previousEnd..node.Span.Start], new SourceSpan(previousEnd, node.Span.Start - previousEnd)));
            }

            previousEnd = node.Span.End;
            switch(node)
            {
                case RazorComment:
                    break;
                case TextTag tag:
                    flat.Add(TextBoundary.Instance);
                    flat.AddRange(tag.Children);
                    flat.Add(TextBoundary.Instance);
                    break;
                case TextLine line:
                    flat.AddRange(line.Content);
                    var newline = NewlineAt(source, node.Span.End);
                    if(newline > 0)
                    {
                        flat.Add(new MarkupText(source.Substring(node.Span.End, newline), new SourceSpan(node.Span.End, newline)));
                        previousEnd += newline;
                    }

                    break;
                default:
                    flat.Add(node);
                    break;
            }

            previous = node;
        }

        return Content([.. flat]);
    }

    public static bool IsWhitespace(RazorNode node)
    {
        return node is MarkupText text && string.IsNullOrWhiteSpace(text.Text);
    }

    private static bool IsCode(RazorNode node)
    {
        return node is CodeRegion or StatementBlock or ControlBlock;
    }

    private static bool TakesGap(RazorNode? node)
    {
        return node is MarkupElement or HtmlComment or TextLine;
    }

    private static int NewlineAt(string source, int index)
    {
        if(index < source.Length && source[index] == '\n')
        {
            return 1;
        }

        return index + 1 < source.Length && source[index] == '\r' && source[index + 1] == '\n' ? 2 : 0;
    }

    private static ImmutableArray<RazorNode> Merge(IEnumerable<RazorNode> nodes, ImmutableArray<CodeBlock> codeBlocks)
    {
        var merged = ImmutableArray.CreateBuilder<RazorNode>();
        foreach(var node in nodes)
        {
            // Member blocks live outside content; their gaps keep EOF whitespace separate until trimming.
            if(node is MarkupText text && merged.Count > 0 && merged[^1] is MarkupText last
                && (codeBlocks.IsDefaultOrEmpty || !codeBlocks.Any(block => last.Span.End <= block.Span.Start && block.Span.End <= text.Span.Start)))
            {
                merged[^1] = new MarkupText(last.Text + text.Text, new SourceSpan(last.Span.Start, text.Span.End - last.Span.Start));
                continue;
            }

            if(node is not MarkupText { Text.Length: 0 })
            {
                merged.Add(node);
            }
        }

        return merged.ToImmutable();
    }

    private static ImmutableArray<RazorNode> Trim(ImmutableArray<RazorNode> nodes)
    {
        var kept = nodes.ToBuilder();
        var removed = new bool[nodes.Length];
        _ = Sweep(nodes, removed, 0, 1);
        _ = Sweep(nodes, removed, nodes.Length - 1, -1);
        for(var index = 0; index < nodes.Length; index++)
        {
            if(IsCode(nodes[index]))
            {
                _ = Sweep(nodes, removed, index - 1, -1);
                var next = Sweep(nodes, removed, index + 1, 1);
                if(next < nodes.Length && nodes[next] is MarkupText text && !nodes[(index + 1)..next].Any(node => node is HtmlComment)
                    && LineBreakEnd(text.Text) is var end and > 0)
                {
                    kept[next] = new MarkupText(text.Text[end..], new SourceSpan(text.Span.Start + end, text.Span.Length - end));
                }
            }
        }

        return [.. kept.Where((_, index) => !removed[index])];
    }

    private static int Sweep(ImmutableArray<RazorNode> nodes, bool[] removed, int start, int step)
    {
        var index = start;
        for(; index >= 0 && index < nodes.Length && (IsWhitespace(nodes[index]) || nodes[index] is TextBoundary or HtmlComment or RazorComment); index += step)
        {
            removed[index] |= nodes[index] is MarkupText;
        }

        return index;
    }

    private static int LineBreakEnd(string text)
    {
        var index = 0;
        while(index < text.Length && text[index] is ' ' or '\t')
        {
            index++;
        }

        var newline = NewlineAt(text, index);
        return newline == 0 ? 0 : index + newline;
    }

    private sealed record TextBoundary() : RazorNode(default(SourceSpan))
    {
        public static readonly TextBoundary Instance = new();
    }
}