using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Parsing;

/// <summary>
/// Wraps the Storm-generated parser behind the <see cref="IParser"/> interface,
/// selecting the appropriate entry rule for Mist compilation units.
/// </summary>
public sealed class MistParser : IParser
{
    public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
    {
        // Bridge to Storm-generated parser.
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        var stormTokenStream = new Storm.Runtime.TokenStream(
            new Storm.Runtime.SourceText(tokens.Source.Path, tokens.Source.Contents),
            tokens.AllTokens);

        Storm.Runtime.SyntaxNode? root = null;
        try
        {
            root = Mist.Generated.MistParser.ParseCompilationUnit(stormTokenStream, stormDiagnostics);
        }
        catch(NotImplementedException)
        {
            // The Storm-generated parser is skeletal — parse methods are not yet implemented.
            // This is expected during early development. Return null root gracefully.
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PARSE_STUB"),
                DiagnosticSeverity.Warning,
                PipelineStage.Parsing,
                "Parser is not yet implemented (Storm-generated parser is skeletal). Source file was lexed but not parsed.",
                Location: default,
                CompilationUnitPath: tokens.Source.Path,
                AnalyzerName: null));
        }

        // Map Storm diagnostics to Mist diagnostics.
        foreach (var entry in stormDiagnostics.Entries)
        {
            var location = (entry.Line > 0 && entry.Column > 0)
                ? new LinePositionSpan(
                    new LinePosition(entry.Line, entry.Column),
                    new LinePosition(entry.Line, entry.Column))
                : default;

            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_PARSE_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Parsing,
                entry.Message,
                Location: location,
                CompilationUnitPath: entry.FilePath ?? tokens.Source.Path,
                AnalyzerName: null));
        }

        // Assign ParsedOrigin to every node in the tree so diagnostics
        // can resolve source locations without span-containment heuristics.
        if (root is not null)
        {
            AssignOrigin(root, new ParsedOrigin(tokens.Source));
        }

        return new ParseResult(
            tokens.Source,
            tokens,
            Root: root,
            Diagnostics: ImmutableArray<Diagnostic>.Empty);
    }

    private static void AssignOrigin(Storm.Runtime.SyntaxNode node, ParsedOrigin origin)
    {
        node.Origin = origin;
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                AssignOrigin(child, origin);
            }
        }
    }
}
