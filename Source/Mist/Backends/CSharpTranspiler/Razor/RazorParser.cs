using System.Collections.Immutable;
using System.Text;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

/// <summary>
/// A hand-written scanner rather than a Storm grammar: Storm accepts one root grammar per
/// compilation, and Razor's mode switches end regions on context no token action can see.
/// <c>specs/razor-components/design.md</c>, Detailed Design 3, records the attempt and the dialect.
/// </summary>
internal sealed partial class RazorParser
{
    private readonly string text;
    private readonly SourceIdentity identity;
    private readonly DiagnosticBag diagnostics;
    private readonly ImmutableArray<RazorDirective>.Builder directives = ImmutableArray.CreateBuilder<RazorDirective>();
    private readonly ImmutableArray<CodeBlock>.Builder codeBlocks = ImmutableArray.CreateBuilder<CodeBlock>();
    private readonly List<string> openElements = [];
    private int position;
    private bool hasErrors;

    private RazorParser(SourceText source, DiagnosticBag diagnostics)
    {
        text = source.Contents;
        identity = new SourceIdentity(source);
        this.diagnostics = diagnostics;
    }

    private enum ContentMode
    {
        Document,
        Element,
        RawText,
        TextLine,
    }

    private bool AtEnd => position >= text.Length;

    public static RazorSyntaxTree Parse(SourceText source, DiagnosticBag diagnostics)
    {
        var parser = new RazorParser(source, diagnostics);
        var content = parser.ParseContent(ContentMode.Document);
        return new RazorSyntaxTree(source, parser.directives.ToImmutable(), parser.codeBlocks.ToImmutable(), content, parser.hasErrors);
    }

    private static bool IsIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private static bool IsIdentifierPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static bool IsHorizontalSpace(char c)
    {
        return c is ' ' or '\t';
    }

    private char Peek(int offset = 0)
    {
        var index = position + offset;
        return index < text.Length ? text[index] : '\0';
    }

    private bool At(string expected)
    {
        return At(position, expected);
    }

    private bool At(int index, string expected)
    {
        return string.CompareOrdinal(text, index, expected, 0, expected.Length) == 0;
    }

    private bool WordAt(int index, string word)
    {
        var end = index + word.Length;
        return At(index, word) && (end >= text.Length || !IsIdentifierPart(text[end]));
    }

    private int ScanIdentifier(int start)
    {
        var end = start;
        while(end < text.Length && IsIdentifierPart(text[end]))
        {
            end++;
        }

        return end;
    }

    private int SkipWhitespace(int index)
    {
        while(index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private int SkipHorizontalSpace(int index)
    {
        while(index < text.Length && IsHorizontalSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private bool AtLineStart(int index)
    {
        while(index > 0 && IsHorizontalSpace(text[index - 1]))
        {
            index--;
        }

        return index == 0 || text[index - 1] == '\n';
    }

    private int EndOfLine(int index)
    {
        var newline = text.IndexOf('\n', index);
        if(newline < 0)
        {
            return text.Length;
        }

        return newline > index && text[newline - 1] == '\r' ? newline - 1 : newline;
    }

    private SourceSpan SpanFrom(int start)
    {
        return new SourceSpan(start, position - start);
    }

    private CodeRegion Region(int start, int end)
    {
        while(start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while(end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new CodeRegion(text[start..end], new SourceSpan(start, end - start));
    }

    private CodeRegion? OptionalRegion(int start, int end)
    {
        var region = Region(start, end);
        return region.Text.Length == 0 ? null : region;
    }

    private void Report(MistDiagnostic descriptor, int start, int length, params ReadOnlySpan<SlotValue> arguments)
    {
        hasErrors = true;
        diagnostics.ReportTyped(descriptor, new SourceRange(identity, start, length), arguments);
    }

    private void Report(MistDiagnostic descriptor, SourceSpan span, params ReadOnlySpan<SlotValue> arguments)
    {
        Report(descriptor, span.Start, span.Length, arguments);
    }

    private sealed class MarkupRun
    {
        private readonly StringBuilder content = new();
        private int start = -1;

        public void Append(int at, string value)
        {
            if(start < 0)
            {
                start = at;
            }

            _ = content.Append(value);
        }

        public void Flush(ImmutableArray<RazorNode>.Builder nodes, int end)
        {
            if(start < 0)
            {
                return;
            }

            nodes.Add(new MarkupText(content.ToString(), new SourceSpan(start, end - start)));
            _ = content.Clear();
            start = -1;
        }
    }
}