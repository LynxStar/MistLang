using System.Collections.Immutable;
using System.Text;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Source;

namespace Mist.Tests.Backend.Razor;

internal abstract record Hand;

internal sealed record HandText(string Text) : Hand;

internal sealed record HandImplicit(string Code) : Hand;

internal sealed record HandExplicit(string Code) : Hand;

internal sealed record HandCode(string Text) : Hand;

internal sealed record HandElement(string Name, ImmutableArray<HandAttribute> Attributes, ImmutableArray<Hand> Children, bool SelfClosing) : Hand;

internal sealed record HandControl(ImmutableArray<HandArm> Arms) : Hand;

internal sealed record HandArm(string Keyword, string? Header, ImmutableArray<Hand> Body);

internal sealed record HandBlock(ImmutableArray<Hand> Body) : Hand;

internal sealed record HandTextTag(ImmutableArray<Hand> Children) : Hand;

internal sealed record HandTextLine(ImmutableArray<Hand> Content) : Hand;

internal sealed record HandHtmlComment(string Text) : Hand;

internal sealed record HandRazorComment(string Text) : Hand;

internal abstract record HandAttribute;

internal sealed record HandHtmlAttribute(string Name, ImmutableArray<Hand>? Value, char Quote) : HandAttribute;

internal sealed record HandDirectiveAttribute(string Name, string? Parameter, string? Code) : HandAttribute;

/// <summary>
/// Builds a <see cref="RazorSyntaxTree"/> by hand, writing the component text each node stands for
/// beside it so that every span covers exactly the characters a verbatim segment will copy.
/// </summary>
internal sealed class HandTree
{
    private readonly StringBuilder text = new();
    private readonly ImmutableArray<RazorDirective>.Builder directives = ImmutableArray.CreateBuilder<RazorDirective>();
    private readonly ImmutableArray<CodeBlock>.Builder codeBlocks = ImmutableArray.CreateBuilder<CodeBlock>();
    private readonly ImmutableArray<RazorNode>.Builder content = ImmutableArray.CreateBuilder<RazorNode>();

    public HandTree Directive(string name, string argument)
    {
        var start = text.Length;
        Append("@" + name);
        var keyword = new SourceSpan(start, name.Length + 1);
        Append(" ");
        var region = Region(argument);
        directives.Add(new RazorDirective(name, keyword, region, new SourceSpan(start, region.Span.End - start)));
        Append("\n");
        return this;
    }

    public HandTree Content(params Hand[] nodes)
    {
        foreach(var node in nodes)
        {
            content.Add(Build(node));
        }

        return this;
    }

    public HandTree Code(string body, string leading = "\n", string trailing = "\n")
    {
        Append(leading);
        var start = text.Length;
        Append("@code {\n");
        var region = Region(body);
        Append("\n}");
        codeBlocks.Add(new CodeBlock(region, new SourceSpan(start, text.Length - start)));
        Append(trailing);
        return this;
    }

    public RazorSyntaxTree Tree(string path = TranslatorHarness.ComponentPath)
    {
        return new RazorSyntaxTree(new SourceText(path, text.ToString()), directives.ToImmutable(), codeBlocks.ToImmutable(), content.ToImmutable(), HasErrors: false);
    }

    private void Append(string value)
    {
        _ = text.Append(value);
    }

    private CodeRegion Region(string code)
    {
        var start = text.Length;
        Append(code);
        return new CodeRegion(code, new SourceSpan(start, code.Length));
    }

    private SourceSpan From(int start)
    {
        return new SourceSpan(start, text.Length - start);
    }

    private RazorNode Build(Hand node)
    {
        var start = text.Length;
        switch(node)
        {
            case HandText value:
                Append(value.Text);
                return new MarkupText(value.Text, From(start));
            case HandImplicit value:
                Append("@");
                return new ImplicitExpression(Region(value.Code), From(start));
            case HandExplicit value:
                Append("@(");
                var code = Region(value.Code);
                Append(")");
                return new ExplicitExpression(code, From(start));
            case HandCode value:
                return Region(value.Text);
            case HandHtmlComment value:
                Append("<!--" + value.Text + "-->");
                return new HtmlComment("<!--" + value.Text + "-->", From(start));
            case HandRazorComment value:
                Append("@*" + value.Text + "*@");
                return new RazorComment(From(start));
            case HandTextTag tag:
                Append("<text>");
                var children = tag.Children.Select(Build).ToImmutableArray();
                Append("</text>");
                return new TextTag(children, From(start));
            case HandTextLine line:
                Append("@:");
                return new TextLine([.. line.Content.Select(Build)], From(start));
            case HandBlock block:
                Append("@{");
                var body = CodeBody(block.Body);
                Append("}");
                return new StatementBlock(body, From(start));
            case HandControl control:
                return Control(control, start);
            case HandElement element:
                return Element(element, start);
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private ImmutableArray<RazorNode> CodeBody(ImmutableArray<Hand> body)
    {
        var nodes = ImmutableArray.CreateBuilder<RazorNode>();
        foreach(var node in body)
        {
            Append("\n    ");
            nodes.Add(Build(node));
        }

        Append("\n");
        return nodes.ToImmutable();
    }

    private ControlBlock Control(HandControl control, int start)
    {
        var arms = ImmutableArray.CreateBuilder<ControlArm>();
        foreach(var arm in control.Arms)
        {
            var armStart = text.Length;
            Append(arms.Count == 0 ? "@" + arm.Keyword : arm.Keyword);
            CodeRegion? header = null;
            if(arm.Header is { } value)
            {
                Append(" ");
                header = Region(value);
            }

            Append("\n{");
            var body = CodeBody(arm.Body);
            Append("}");
            arms.Add(new ControlArm(arm.Keyword, header, body, From(armStart)));
            if(arms.Count < control.Arms.Length)
            {
                Append("\n");
            }
        }

        return new ControlBlock(arms.ToImmutable(), From(start));
    }

    private MarkupElement Element(HandElement element, int start)
    {
        Append("<");
        var nameStart = text.Length;
        Append(element.Name);
        var nameSpan = From(nameStart);
        var attributes = element.Attributes.Select(Attribute).ToImmutableArray();
        Append(element.SelfClosing ? " />" : ">");
        var startTag = From(start);
        if(element.SelfClosing)
        {
            return new MarkupElement(element.Name, nameSpan, attributes, [], true, startTag, null, From(start));
        }

        var children = element.Children.Select(Build).ToImmutableArray();
        var endStart = text.Length;
        Append("</" + element.Name + ">");
        return new MarkupElement(element.Name, nameSpan, attributes, children, false, startTag, From(endStart), From(start));
    }

    private MarkupAttribute Attribute(HandAttribute attribute)
    {
        Append(" ");
        var start = text.Length;
        switch(attribute)
        {
            case HandHtmlAttribute html:
                Append(html.Name);
                var nameSpan = From(start);
                AttributeValue? value = null;
                if(html.Value is { } parts)
                {
                    Append("=" + html.Quote);
                    var valueStart = text.Length;
                    var built = parts.Select(Build).ToImmutableArray();
                    value = new AttributeValue(built, From(valueStart));
                    Append(html.Quote.ToString());
                }

                return new HtmlAttribute(html.Name, nameSpan, value, From(start));
            case HandDirectiveAttribute directive:
                Append("@" + directive.Name + (directive.Parameter is null ? "" : ":" + directive.Parameter));
                var directiveName = From(start);
                CodeRegion? code = null;
                if(directive.Code is { } source)
                {
                    Append("=\"");
                    code = Region(source);
                    Append("\"");
                }

                return new DirectiveAttribute(directive.Name, directive.Parameter, directiveName, code, From(start));
            default:
                throw new ArgumentOutOfRangeException(nameof(attribute));
        }
    }
}

internal static class H
{
    public static Hand Text(string text)
    {
        return new HandText(text);
    }

    public static Hand Expr(string code)
    {
        return new HandImplicit(code);
    }

    public static Hand Explicit(string code)
    {
        return new HandExplicit(code);
    }

    public static Hand Code(string text)
    {
        return new HandCode(text);
    }

    public static Hand El(string name, params Hand[] children)
    {
        return new HandElement(name, [], [.. children], SelfClosing: false);
    }

    public static Hand El(string name, HandAttribute[] attributes, params Hand[] children)
    {
        return new HandElement(name, [.. attributes], [.. children], SelfClosing: false);
    }

    public static Hand Void(string name, params HandAttribute[] attributes)
    {
        return new HandElement(name, [.. attributes], [], SelfClosing: true);
    }

    public static HandAttribute Attr(string name, string value, char quote = '"')
    {
        return new HandHtmlAttribute(name, [new HandText(value)], quote);
    }

    public static HandAttribute Attr(string name, params Hand[] parts)
    {
        return new HandHtmlAttribute(name, [.. parts], '"');
    }

    public static HandAttribute Bare(string name)
    {
        return new HandHtmlAttribute(name, null, '"');
    }

    public static HandAttribute Dir(string name, string? code, string? parameter = null)
    {
        return new HandDirectiveAttribute(name, parameter, code);
    }

    public static Hand If(string header, params Hand[] body)
    {
        return new HandControl([new HandArm("if", header, [.. body])]);
    }

    public static Hand Control(params HandArm[] arms)
    {
        return new HandControl([.. arms]);
    }

    public static HandArm Arm(string keyword, string? header, params Hand[] body)
    {
        return new HandArm(keyword, header, [.. body]);
    }

    public static Hand Block(params Hand[] body)
    {
        return new HandBlock([.. body]);
    }

    public static Hand TextTag(params Hand[] children)
    {
        return new HandTextTag([.. children]);
    }

    public static Hand Line(params Hand[] content)
    {
        return new HandTextLine([.. content]);
    }

    public static Hand Comment(string text)
    {
        return new HandHtmlComment(text);
    }

    public static Hand RazorComment(string text)
    {
        return new HandRazorComment(text);
    }
}