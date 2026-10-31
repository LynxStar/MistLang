using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed partial class RazorParser
{
    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
    {
        "if", "while", "for", "foreach", "readonly", "with",
    };

    private RazorNode? ParseTransition(int limit)
    {
        var next = Peek(1);
        if(next == '*')
        {
            return ParseRazorComment();
        }

        if(next == '{')
        {
            return ParseStatementBlock();
        }

        if(IsIdentifierStart(next))
        {
            var word = text[(position + 1)..ScanIdentifier(position + 1)];
            if(ControlKeywords.Contains(word))
            {
                return ParseControlBlock(word);
            }
        }

        if(next == '(' || IsIdentifierStart(next))
        {
            return ParseExpression(limit);
        }

        Report(RazorDiagnostics.InvalidTransition, position, 1);
        return null;
    }

    private RazorNode ParseExpression(int limit)
    {
        return Peek(1) == '(' ? ParseExplicitExpression(limit) : ParseImplicitExpression(limit);
    }

    private RazorComment ParseRazorComment()
    {
        var start = position;
        var close = text.IndexOf("*@", start + 2, StringComparison.Ordinal);
        if(close < 0)
        {
            Report(RazorDiagnostics.UnterminatedCodeRegion, start, 2, new EnumValue("comment"));
            position = text.Length;
        }
        else
        {
            position = close + 2;
        }

        return new RazorComment(SpanFrom(start));
    }

    private ExplicitExpression ParseExplicitExpression(int limit)
    {
        var start = position;
        var close = MistBalancer.FindClose(text, start + 1);
        if(close < 0 || close >= limit)
        {
            Report(RazorDiagnostics.UnterminatedCodeRegion, start, 2, new EnumValue("explicit expression"));
            position = Math.Min(EndOfLine(start), limit);
            return new ExplicitExpression(Region(start + 2, position), SpanFrom(start));
        }

        position = close + 1;
        return new ExplicitExpression(Region(start + 2, close), SpanFrom(start));
    }

    private ImplicitExpression ParseImplicitExpression(int limit)
    {
        var start = position;
        var end = ScanIdentifier(start + 1);
        while(end < limit)
        {
            if(text[end] == '.' && end + 1 < limit && IsIdentifierStart(text[end + 1]))
            {
                end = ScanIdentifier(end + 1);
                continue;
            }

            if(text[end] is not ('(' or '['))
            {
                break;
            }

            var close = MistBalancer.FindClose(text, end);
            if(close < 0 || close >= limit)
            {
                Report(RazorDiagnostics.UnterminatedCodeRegion, start, end + 1 - start, new EnumValue("implicit expression"));
                end = Math.Min(EndOfLine(end), limit);
                break;
            }

            end = close + 1;
        }

        position = end;
        return new ImplicitExpression(new CodeRegion(text[(start + 1)..end], new SourceSpan(start + 1, end - start - 1)), SpanFrom(start));
    }

    private StatementBlock ParseStatementBlock()
    {
        var start = position;
        position += 2;
        var (body, closed) = ParseCodeBody();
        if(closed)
        {
            position++;
        }
        else
        {
            Report(RazorDiagnostics.UnterminatedCodeRegion, start, 2, new EnumValue("statement block"));
        }

        return new StatementBlock(body, SpanFrom(start));
    }

    private ControlBlock ParseControlBlock(string keyword)
    {
        var start = position;
        var arms = ImmutableArray.CreateBuilder<ControlArm>();
        var arm = (Keyword: keyword, Start: start, Transition: new SourceSpan(start, keyword.Length + 1), Header: start + keyword.Length + 1);
        while(true)
        {
            var open = FindBlockOpen(arm.Header);
            if(open < 0)
            {
                Report(RazorDiagnostics.UnterminatedCodeRegion, arm.Transition, new EnumValue("control block"));
                position = EndOfLine(arm.Header);
                arms.Add(new ControlArm(arm.Keyword, OptionalRegion(arm.Header, position), [], SpanFrom(arm.Start)));
                break;
            }

            position = open + 1;
            var (body, closed) = ParseCodeBody();
            if(!closed)
            {
                Report(RazorDiagnostics.UnterminatedCodeRegion, arm.Transition, new EnumValue("control block"));
                arms.Add(new ControlArm(arm.Keyword, OptionalRegion(arm.Header, open), body, SpanFrom(arm.Start)));
                break;
            }

            position++;
            arms.Add(new ControlArm(arm.Keyword, OptionalRegion(arm.Header, open), body, SpanFrom(arm.Start)));
            if(keyword != "if" || NextElse() is not { } next)
            {
                break;
            }

            arm = next;
        }

        return new ControlBlock(arms.ToImmutable(), SpanFrom(start));
    }

    private (string Keyword, int Start, SourceSpan Transition, int Header)? NextElse()
    {
        var start = SkipWhitespace(position);
        if(!WordAt(start, "else"))
        {
            return null;
        }

        var after = SkipWhitespace(start + 4);
        return WordAt(after, "if")
            ? ("else if", start, new SourceSpan(start, after + 2 - start), after + 2)
            : ("else", start, new SourceSpan(start, 4), start + 4);
    }

    private int FindBlockOpen(int index)
    {
        while(index < text.Length)
        {
            var skipped = MistBalancer.SkipLiteral(text, index);
            if(skipped >= 0)
            {
                index = skipped;
                continue;
            }

            switch(text[index])
            {
                case '{':
                    return index;
                case '(' or '[':
                    var close = MistBalancer.FindClose(text, index);
                    if(close < 0)
                    {
                        return -1;
                    }

                    index = close + 1;
                    continue;
                case ';' or '<' or '}' or ')' or ']':
                    return -1;
                default:
                    index++;
                    continue;
            }
        }

        return -1;
    }
}