using System.Collections.Immutable;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor.Syntax;

internal abstract record RazorNode(SourceSpan Span);

/// <summary>
/// Mist text copied from the file: its <see cref="Text"/> is exactly the characters its span covers.
/// </summary>
internal sealed record CodeRegion(string Text, SourceSpan Span) : RazorNode(Span);

internal sealed record MarkupText(string Text, SourceSpan Span) : RazorNode(Span);

internal sealed record HtmlComment(string Text, SourceSpan Span) : RazorNode(Span);

internal sealed record RazorComment(SourceSpan Span) : RazorNode(Span);

internal sealed record MarkupElement(
    string Name,
    SourceSpan NameSpan,
    ImmutableArray<MarkupAttribute> Attributes,
    ImmutableArray<RazorNode> Children,
    bool SelfClosing,
    SourceSpan StartTag,
    SourceSpan? EndTag,
    SourceSpan Span) : RazorNode(Span);

internal sealed record TextTag(ImmutableArray<RazorNode> Children, SourceSpan Span) : RazorNode(Span);

internal sealed record TextLine(ImmutableArray<RazorNode> Content, SourceSpan Span) : RazorNode(Span);

internal sealed record ImplicitExpression(CodeRegion Code, SourceSpan Span) : RazorNode(Span);

internal sealed record ExplicitExpression(CodeRegion Code, SourceSpan Span) : RazorNode(Span);

internal sealed record StatementBlock(ImmutableArray<RazorNode> Body, SourceSpan Span) : RazorNode(Span);

internal sealed record ControlBlock(ImmutableArray<ControlArm> Arms, SourceSpan Span) : RazorNode(Span);

internal sealed record ControlArm(string Keyword, CodeRegion? Header, ImmutableArray<RazorNode> Body, SourceSpan Span);

internal abstract record MarkupAttribute(string Name, SourceSpan NameSpan, SourceSpan Span);

internal sealed record HtmlAttribute(string Name, SourceSpan NameSpan, AttributeValue? Value, SourceSpan Span)
    : MarkupAttribute(Name, NameSpan, Span);

internal sealed record DirectiveAttribute(string Name, string? Parameter, SourceSpan NameSpan, CodeRegion? Value, SourceSpan Span)
    : MarkupAttribute(Name, NameSpan, Span);

internal sealed record AttributeValue(ImmutableArray<RazorNode> Parts, SourceSpan Span);