using System.Collections.Immutable;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor.Syntax;

/// <summary>
/// A parsed component file: its directives and code blocks in source order, and the markup
/// content of the file's root with both left out.
/// </summary>
internal sealed record RazorSyntaxTree(
    SourceText Text,
    ImmutableArray<RazorDirective> Directives,
    ImmutableArray<CodeBlock> CodeBlocks,
    ImmutableArray<RazorNode> Content,
    bool HasErrors);

internal sealed record RazorDirective(string Name, SourceSpan KeywordSpan, CodeRegion? Argument, SourceSpan Span);

internal sealed record CodeBlock(CodeRegion Body, SourceSpan Span);