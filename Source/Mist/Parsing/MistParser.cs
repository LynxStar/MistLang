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

        // The Storm-generated parser is a backtracking PEG parser: when the
        // `stackalloc T[n]` alternative fails partway (e.g. the `[size]` is
        // missing or empty), it truncates its own diagnostics and backtracks,
        // leaving only a generic MIST_PARSE_ERROR. Requirements 3.4 and 3.5
        // call for a specific recovery diagnostic identifying the missing or
        // empty stackalloc size. We detect those shapes here with a focused
        // token scan and emit MIST_PARSE_STACKALLOC_SIZE; parsing still
        // continues (no node is produced for the malformed expression).
        DetectStackallocSizeErrors(tokens, diagnostics);

        // Assign ParsedOrigin to every node in the tree so diagnostics
        // can resolve source locations without span-containment heuristics.
        if (root is not null)
        {
            AssignOrigin(root, new ParsedOrigin(tokens.Source));

            // Completeness check: verify the parser consumed all default-channel tokens.
            // The CompilationUnit rule does not consume the EOF sentinel, so we exclude it
            // from the comparison — only flag genuinely unconsumed content tokens.
            var lastPos = Mist.Generated.MistParser.LastPosition;
            var defaultTokens = tokens.DefaultChannelTokens;
            var contentTokenCount = defaultTokens.Length > 0
                && defaultTokens[^1].Kind == (int)Mist.Generated.TokenKind.EndOfFile
                ? defaultTokens.Length - 1
                : defaultTokens.Length;

            if (lastPos < contentTokenCount)
            {
                var unconsumedToken = defaultTokens[lastPos];
                var location = new LinePositionSpan(
                    new LinePosition(unconsumedToken.Start.Line, unconsumedToken.Start.Column),
                    new LinePosition(unconsumedToken.Start.Line, unconsumedToken.Start.Column));

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_PARSE_INCOMPLETE"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Parsing,
                    $"Parser did not consume all input. Unparseable content begins at line {unconsumedToken.Start.Line}.",
                    Location: location,
                    CompilationUnitPath: tokens.Source.Path,
                    AnalyzerName: null));
            }
        }

        return new ParseResult(
            tokens.Source,
            tokens,
            Root: root,
            Diagnostics: ImmutableArray<Diagnostic>.Empty);
    }

    /// <summary>
    /// Scans the token stream for malformed <c>stackalloc</c> expressions that lack a
    /// bracketed size (<c>stackalloc T</c>, Requirement 3.4) or use empty brackets
    /// (<c>stackalloc T[]</c>, Requirement 3.5) and emits a
    /// <c>MIST_PARSE_STACKALLOC_SIZE</c> recovery diagnostic for each.
    /// </summary>
    /// <remarks>
    /// The grammar's <c>StackallocExpression</c> alternative requires
    /// <c>STACKALLOC ElementTypeRef LEFT_BRACKET Expression RIGHT_BRACKET</c>. An
    /// <c>ElementTypeRef</c> never contains a <c>[</c> (array ranks are not part of it),
    /// so the first bracket after the element type is always the size bracket. We skip
    /// the element-type tokens (a primitive keyword, <c>unknown</c>, or a qualified
    /// identifier with an optional generic argument list) and then inspect what follows:
    /// no <c>[</c> means a missing size; <c>[]</c> means an empty size.
    /// </remarks>
    private static void DetectStackallocSizeErrors(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        for (var i = 0; i < defaultTokens.Length; i++)
        {
            if (defaultTokens[i].Kind != (int)Mist.Generated.TokenKind.STACKALLOC)
            {
                continue;
            }

            var afterType = SkipElementType(defaultTokens, i + 1);
            if (afterType is null)
            {
                // The token after `stackalloc` does not begin a recognizable element
                // type (e.g. `stackalloc [3]`). That is a different malformed shape;
                // leave it to the generic parser error rather than mis-attributing it.
                continue;
            }

            var pos = afterType.Value;
            var hasOpenBracket = pos < defaultTokens.Length
                && defaultTokens[pos].Kind == (int)Mist.Generated.TokenKind.LEFT_BRACKET;

            if (!hasOpenBracket)
            {
                // Requirement 3.4: `stackalloc T` with no bracketed size.
                AddStackallocSizeDiagnostic(defaultTokens[i], tokens.Source.Path, diagnostics);
                continue;
            }

            var isEmptyBrackets = pos + 1 < defaultTokens.Length
                && defaultTokens[pos + 1].Kind == (int)Mist.Generated.TokenKind.RIGHT_BRACKET;

            if (isEmptyBrackets)
            {
                // Requirement 3.5: `stackalloc T[]` with empty brackets.
                AddStackallocSizeDiagnostic(defaultTokens[i], tokens.Source.Path, diagnostics);
            }
        }
    }

    private static void AddStackallocSizeDiagnostic(
        Storm.Runtime.Token stackallocToken,
        string sourcePath,
        DiagnosticBag diagnostics)
    {
        var location = (stackallocToken.Start.Line > 0 && stackallocToken.Start.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(stackallocToken.Start.Line, stackallocToken.Start.Column),
                new LinePosition(stackallocToken.End.Line, stackallocToken.End.Column))
            : default;

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_PARSE_STACKALLOC_SIZE"),
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            "A 'stackalloc' expression requires an element type and a bracketed size, "
                + "for example 'stackalloc T[n]'.",
            Location: location,
            CompilationUnitPath: sourcePath,
            AnalyzerName: null));
    }

    /// <summary>
    /// Given the index of the first token after <c>stackalloc</c>, skips the tokens that
    /// make up an <c>ElementTypeRef</c> and returns the index of the token immediately
    /// following it, or <c>null</c> if no element type is recognized.
    /// </summary>
    private static int? SkipElementType(
        System.Collections.Immutable.ImmutableArray<Storm.Runtime.Token> tokens,
        int start)
    {
        if (start >= tokens.Length)
        {
            return null;
        }

        var kind = tokens[start].Kind;

        if (IsPrimitiveTypeKind(kind) || kind == (int)Mist.Generated.TokenKind.UNKNOWN)
        {
            return start + 1;
        }

        if (kind == (int)Mist.Generated.TokenKind.IDENTIFIER)
        {
            var pos = start + 1;

            // QualifiedIdentifier: IDENTIFIER (DOT IDENTIFIER)*
            while (pos + 1 < tokens.Length
                && tokens[pos].Kind == (int)Mist.Generated.TokenKind.DOT
                && tokens[pos + 1].Kind == (int)Mist.Generated.TokenKind.IDENTIFIER)
            {
                pos += 2;
            }

            // Optional TypeArgumentList: LESS_THAN ... GREATER_THAN (balanced).
            if (pos < tokens.Length && tokens[pos].Kind == (int)Mist.Generated.TokenKind.LESS_THAN)
            {
                return SkipBalancedAngles(tokens, pos);
            }

            return pos;
        }

        return null;
    }

    /// <summary>
    /// Skips a balanced generic argument list starting at a <c>&lt;</c> token and returns the
    /// index after the matching <c>&gt;</c>, or <c>null</c> if the angles are unbalanced or a
    /// structural terminator is reached first.
    /// </summary>
    private static int? SkipBalancedAngles(
        System.Collections.Immutable.ImmutableArray<Storm.Runtime.Token> tokens,
        int openAngle)
    {
        var depth = 0;
        for (var pos = openAngle; pos < tokens.Length; pos++)
        {
            var kind = tokens[pos].Kind;
            if (kind == (int)Mist.Generated.TokenKind.LESS_THAN)
            {
                depth++;
            }
            else if (kind == (int)Mist.Generated.TokenKind.GREATER_THAN)
            {
                depth--;
                if (depth == 0)
                {
                    return pos + 1;
                }
            }
            else if (kind == (int)Mist.Generated.TokenKind.SEMICOLON
                || kind == (int)Mist.Generated.TokenKind.LEFT_BRACE
                || kind == (int)Mist.Generated.TokenKind.RIGHT_BRACE
                || kind == (int)Mist.Generated.TokenKind.EndOfFile)
            {
                // Malformed type argument list; bail rather than guessing.
                return null;
            }
        }

        return null;
    }

    private static bool IsPrimitiveTypeKind(int kind) =>
        kind == (int)Mist.Generated.TokenKind.INT
        || kind == (int)Mist.Generated.TokenKind.UINT
        || kind == (int)Mist.Generated.TokenKind.LONG
        || kind == (int)Mist.Generated.TokenKind.ULONG
        || kind == (int)Mist.Generated.TokenKind.SHORT
        || kind == (int)Mist.Generated.TokenKind.USHORT
        || kind == (int)Mist.Generated.TokenKind.BYTE
        || kind == (int)Mist.Generated.TokenKind.SBYTE
        || kind == (int)Mist.Generated.TokenKind.FLOAT
        || kind == (int)Mist.Generated.TokenKind.DOUBLE
        || kind == (int)Mist.Generated.TokenKind.DECIMAL
        || kind == (int)Mist.Generated.TokenKind.BOOL
        || kind == (int)Mist.Generated.TokenKind.CHAR
        || kind == (int)Mist.Generated.TokenKind.STRING
        || kind == (int)Mist.Generated.TokenKind.REGEX;

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
