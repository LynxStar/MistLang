using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed partial class RazorParser
{
    private static readonly HashSet<string> ComponentDirectives = new(StringComparer.Ordinal)
    {
        "page", "layout", "inherits", "implements", "inject", "using", "namespace", "attribute", "rendermode",
        "typeparam", "preservewhitespace",
    };

    private static readonly HashSet<string> ViewDirectives = new(StringComparer.Ordinal)
    {
        "model", "section", "addTagHelper", "removeTagHelper", "tagHelperPrefix", "helper",
    };

    private bool TryParseDirective()
    {
        var start = position;
        if(!IsIdentifierStart(Peek(1)))
        {
            return false;
        }

        var nameEnd = ScanIdentifier(start + 1);
        var name = text[(start + 1)..nameEnd];
        var keyword = new SourceSpan(start, nameEnd - start);
        if(name == "code")
        {
            ParseCodeDirective(keyword);
            return true;
        }

        if(name == "functions" || ViewDirectives.Contains(name))
        {
            if(name == "functions")
            {
                Report(RazorDiagnostics.FunctionsDirectiveNotSupported, keyword);
            }
            else
            {
                Report(RazorDiagnostics.DirectiveNotValidInComponent, keyword, new NameValue(name));
            }

            SkipRefusedDirective(nameEnd);
            return true;
        }

        var known = ComponentDirectives.Contains(name);
        if(!known && (ControlKeywords.Contains(name) || !HasDirectiveShape(name, nameEnd)))
        {
            return false;
        }

        var lineEnd = EndOfLine(nameEnd);
        position = lineEnd;
        if(!known)
        {
            Report(RazorDiagnostics.UnknownDirective, keyword, new NameValue(name));
            return true;
        }

        var argument = OptionalRegion(nameEnd, lineEnd);
        directives.Add(new RazorDirective(name, keyword, argument, new SourceSpan(start, (argument?.Span.End ?? nameEnd) - start)));
        return true;
    }

    private bool HasDirectiveShape(string name, int nameEnd)
    {
        var argument = SkipHorizontalSpace(nameEnd);
        return char.IsLower(name[0])
            && argument > nameEnd
            && argument < text.Length
            && text[argument] is not ('\r' or '\n' or '<' or '@');
    }

    private void ParseCodeDirective(SourceSpan keyword)
    {
        var open = SkipWhitespace(keyword.End);
        if(open >= text.Length || text[open] != '{')
        {
            Report(RazorDiagnostics.UnterminatedCodeRegion, keyword, new EnumValue("code block"));
            position = EndOfLine(keyword.End);
            return;
        }

        var close = MistBalancer.FindClose(text, open);
        if(close < 0)
        {
            Report(RazorDiagnostics.UnterminatedCodeRegion, keyword, new EnumValue("code block"));
            position = text.Length;
            codeBlocks.Add(new CodeBlock(Region(open + 1, position), SpanFrom(keyword.Start)));
            return;
        }

        position = close + 1;
        codeBlocks.Add(new CodeBlock(Region(open + 1, close), SpanFrom(keyword.Start)));
    }

    private void SkipRefusedDirective(int argumentStart)
    {
        var lineEnd = EndOfLine(argumentStart);
        var open = text.IndexOf('{', argumentStart, lineEnd - argumentStart);
        if(open < 0 && SkipWhitespace(lineEnd) is var next && next < text.Length && text[next] == '{')
        {
            open = next;
        }

        if(open < 0)
        {
            position = lineEnd;
            return;
        }

        var close = MistBalancer.FindClose(text, open);
        position = close < 0 ? text.Length : close + 1;
    }
}