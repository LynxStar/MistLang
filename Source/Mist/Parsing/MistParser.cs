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
        // Pre-process the token stream to reclassify keyword tokens as IDENTIFIER
        // when they appear as segments in dotted qualified identifiers following
        // `using` or `namespace`. This allows CLR namespaces containing keyword-
        // like segments (e.g., "System.Runtime.in.Something") to parse correctly.
        var processedTokens = ReclassifyKeywordsInQualifiedNames(tokens.AllTokens);

        // Bridge to Storm-generated parser.
        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        var stormTokenStream = new Storm.Runtime.TokenStream(
            new Storm.Runtime.SourceText(tokens.Source.Path, tokens.Source.Contents),
            processedTokens);

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

        // Requirement 1.10: detect property slots with bodies inside abstract
        // type declarations. The grammar's InterfacePropertyDeclaration only
        // matches `Type Name { get; }` / `Type Name { get; set; }`. If someone
        // writes `Type Name { ... }` where the braced content is NOT get/set
        // accessors, the PEG parser backtracks and produces a generic error.
        // We detect this shape and emit a targeted diagnostic.
        DetectAbstractPropertyWithBodyErrors(tokens, diagnostics);

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
    /// Scans the token stream for property-like members with braced bodies inside
    /// abstract type declarations. The grammar's <c>InterfacePropertyDeclaration</c>
    /// only matches <c>Type Name { get; }</c> / <c>Type Name { get; set; }</c>.
    /// If someone writes <c>Type Name { ... }</c> where the braced content is not
    /// accessor declarations (get/set followed by semicolons), this emits a targeted
    /// diagnostic: "Property slots cannot have default implementations. Only methods
    /// can have defaults."
    /// </summary>
    /// <remarks>
    /// Detection strategy: find <c>ABSTRACT IDENTIFIER ... LEFT_BRACE</c> (the abstract
    /// type body), then inside that body look for <c>TypeRef IDENTIFIER LEFT_BRACE</c>
    /// where the token immediately after the inner <c>LEFT_BRACE</c> is NOT <c>GET</c>
    /// or <c>SET</c>. This distinguishes a property-with-body attempt from a valid
    /// property slot declaration.
    /// </remarks>
    private static void DetectAbstractPropertyWithBodyErrors(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var abstractKind = (int)Mist.Generated.TokenKind.ABSTRACT;
        var identifierKind = (int)Mist.Generated.TokenKind.IDENTIFIER;
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Mist.Generated.TokenKind.RIGHT_BRACE;
        var leftParenKind = (int)Mist.Generated.TokenKind.LEFT_PARENTHESIS;
        var getKind = (int)Mist.Generated.TokenKind.GET;
        var setKind = (int)Mist.Generated.TokenKind.SET;
        var semicolonKind = (int)Mist.Generated.TokenKind.SEMICOLON;
        var initKind = (int)Mist.Generated.TokenKind.INIT;

        for (var i = 0; i < defaultTokens.Length; i++)
        {
            if (defaultTokens[i].Kind != abstractKind)
            {
                continue;
            }

            // Skip to the opening brace of the abstract type body.
            // Pattern: ABSTRACT IDENTIFIER [type params] [: bases] [where clauses] LEFT_BRACE
            var bodyStart = FindAbstractBodyStart(defaultTokens, i + 1);
            if (bodyStart is null)
            {
                continue;
            }

            var pos = bodyStart.Value;
            var braceDepth = 1; // We are inside the abstract body.

            while (pos < defaultTokens.Length && braceDepth > 0)
            {
                var tok = defaultTokens[pos];

                if (tok.Kind == rightBraceKind)
                {
                    braceDepth--;
                    pos++;
                    continue;
                }

                if (tok.Kind == leftBraceKind)
                {
                    braceDepth++;
                    pos++;
                    continue;
                }

                // Only look for property-with-body patterns at the top level of
                // the abstract body (braceDepth == 1).
                if (braceDepth != 1)
                {
                    pos++;
                    continue;
                }

                // Look for: TypeRef IDENTIFIER LEFT_BRACE !(GET|SET|INIT)
                // A TypeRef starts with a primitive keyword, IDENTIFIER, or UNKNOWN.
                if (!IsTypeRefStart(tok.Kind))
                {
                    pos++;
                    continue;
                }

                // Skip the type reference tokens.
                var afterType = SkipTypeRef(defaultTokens, pos);
                if (afterType is null || afterType.Value >= defaultTokens.Length)
                {
                    pos++;
                    continue;
                }

                var namePos = afterType.Value;

                // Next must be IDENTIFIER (the property/member name).
                if (namePos >= defaultTokens.Length || defaultTokens[namePos].Kind != identifierKind)
                {
                    pos++;
                    continue;
                }

                var afterName = namePos + 1;

                // If next is LEFT_PARENTHESIS, this is a method — skip it entirely.
                if (afterName < defaultTokens.Length && defaultTokens[afterName].Kind == leftParenKind)
                {
                    pos++;
                    continue;
                }

                // Next must be LEFT_BRACE (the property body/accessor block).
                if (afterName >= defaultTokens.Length || defaultTokens[afterName].Kind != leftBraceKind)
                {
                    pos++;
                    continue;
                }

                // Check what's inside the brace: if it starts with GET, SET, or INIT
                // followed by SEMICOLON, this is a valid property slot (accessor declaration).
                var insideBrace = afterName + 1;
                if (insideBrace < defaultTokens.Length)
                {
                    var firstInside = defaultTokens[insideBrace].Kind;
                    if (firstInside == getKind || firstInside == setKind || firstInside == initKind)
                    {
                        // Check if the accessor is followed by semicolon (valid slot)
                        // or by a block brace (accessor with body — invalid for abstract).
                        var afterAccessorKeyword = insideBrace + 1;
                        if (afterAccessorKeyword < defaultTokens.Length
                            && defaultTokens[afterAccessorKeyword].Kind == semicolonKind)
                        {
                            // Valid property slot: `Type Name { get; }` — skip.
                            pos++;
                            continue;
                        }

                        // Accessor keyword followed by something other than semicolon
                        // (e.g., `get { return ...; }`) — this is a property with a body.
                    }
                    else
                    {
                        // Content after `{` is not an accessor keyword at all
                        // (e.g., `string Name { return "hello"; }`) — property with body.
                    }
                }

                // Emit the diagnostic pointing at the property name token.
                var nameToken = defaultTokens[namePos];
                var location = (nameToken.Start.Line > 0 && nameToken.Start.Column > 0)
                    ? new LinePositionSpan(
                        new LinePosition(nameToken.Start.Line, nameToken.Start.Column),
                        new LinePosition(nameToken.End.Line, nameToken.End.Column))
                    : default;

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_PARSE_PROPERTY_DEFAULT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Parsing,
                    "Property slots cannot have default implementations. Only methods can have defaults.",
                    Location: location,
                    CompilationUnitPath: tokens.Source.Path,
                    AnalyzerName: null));

                // Skip past the braced body to continue scanning.
                pos = SkipBracedBlock(defaultTokens, afterName);
            }

            // Advance past the abstract declaration for the outer loop.
            i = pos > 0 ? pos - 1 : i;
        }
    }

    /// <summary>
    /// Starting after the ABSTRACT token, skips the name, type parameters, base clause,
    /// and where clauses to find the opening LEFT_BRACE of the abstract type body.
    /// Returns the index of the first token AFTER the LEFT_BRACE, or null if not found.
    /// </summary>
    private static int? FindAbstractBodyStart(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int start)
    {
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var eofKind = (int)Mist.Generated.TokenKind.EndOfFile;

        // The abstract body starts with the first LEFT_BRACE at brace depth 0.
        // We track angle brackets and parentheses to skip type parameter lists.
        var angleDepth = 0;
        var parenDepth = 0;

        for (var pos = start; pos < tokens.Length; pos++)
        {
            var kind = tokens[pos].Kind;

            if (kind == eofKind)
            {
                return null;
            }

            if (kind == (int)Mist.Generated.TokenKind.LESS_THAN)
            {
                angleDepth++;
                continue;
            }

            if (kind == (int)Mist.Generated.TokenKind.GREATER_THAN && angleDepth > 0)
            {
                angleDepth--;
                continue;
            }

            if (kind == (int)Mist.Generated.TokenKind.LEFT_PARENTHESIS)
            {
                parenDepth++;
                continue;
            }

            if (kind == (int)Mist.Generated.TokenKind.RIGHT_PARENTHESIS && parenDepth > 0)
            {
                parenDepth--;
                continue;
            }

            if (kind == leftBraceKind && angleDepth == 0 && parenDepth == 0)
            {
                return pos + 1; // Return index after the opening brace.
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true if the given token kind can begin a type reference (a primitive
    /// type keyword, IDENTIFIER, or UNKNOWN).
    /// </summary>
    private static bool IsTypeRefStart(int kind) =>
        kind == (int)Mist.Generated.TokenKind.IDENTIFIER
        || IsPrimitiveTypeKind(kind)
        || kind == (int)Mist.Generated.TokenKind.UNKNOWN
        || kind == (int)Mist.Generated.TokenKind.VOID;

    /// <summary>
    /// Skips a type reference starting at the given position and returns the index
    /// of the first token after the type reference, or null if unrecognizable.
    /// Handles: primitives, IDENTIFIER, qualified identifiers with dots, and generic
    /// type argument lists.
    /// </summary>
    private static int? SkipTypeRef(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int start)
    {
        if (start >= tokens.Length)
        {
            return null;
        }

        var kind = tokens[start].Kind;

        if (IsPrimitiveTypeKind(kind)
            || kind == (int)Mist.Generated.TokenKind.UNKNOWN
            || kind == (int)Mist.Generated.TokenKind.VOID)
        {
            var pos = start + 1;
            // Check for nullable `?` suffix.
            if (pos < tokens.Length && tokens[pos].Kind == (int)Mist.Generated.TokenKind.QUESTION)
            {
                pos++;
            }

            return pos;
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
                var afterAngles = SkipBalancedAngles(tokens, pos);
                if (afterAngles is not null)
                {
                    pos = afterAngles.Value;
                }
            }

            // Check for nullable `?` suffix.
            if (pos < tokens.Length && tokens[pos].Kind == (int)Mist.Generated.TokenKind.QUESTION)
            {
                pos++;
            }

            return pos;
        }

        return null;
    }

    /// <summary>
    /// Skips a balanced braced block starting at the LEFT_BRACE at position
    /// <paramref name="openBrace"/> and returns the index after the matching
    /// RIGHT_BRACE.
    /// </summary>
    private static int SkipBracedBlock(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int openBrace)
    {
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Mist.Generated.TokenKind.RIGHT_BRACE;
        var eofKind = (int)Mist.Generated.TokenKind.EndOfFile;
        var depth = 0;

        for (var pos = openBrace; pos < tokens.Length; pos++)
        {
            var kind = tokens[pos].Kind;
            if (kind == leftBraceKind)
            {
                depth++;
            }
            else if (kind == rightBraceKind)
            {
                depth--;
                if (depth == 0)
                {
                    return pos + 1;
                }
            }
            else if (kind == eofKind)
            {
                return pos;
            }
        }

        return tokens.Length;
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

    /// <summary>
    /// Scans the token stream and reclassifies keyword tokens as IDENTIFIER when
    /// they appear as namespace segments in using declarations or namespace declarations.
    /// <para>
    /// CLR namespaces may contain segments that coincide with Mist keywords (e.g.,
    /// <c>using Some.Namespace.type.Foo;</c>). The DFA lexer produces keyword token
    /// kinds for these, causing the PEG parser's <c>QualifiedIdentifier</c> rule
    /// (which expects IDENTIFIER tokens) to fail. This pre-processing step fixes up
    /// those tokens so parsing succeeds.
    /// </para>
    /// </summary>
    private static ImmutableArray<Storm.Runtime.Token> ReclassifyKeywordsInQualifiedNames(
        ImmutableArray<Storm.Runtime.Token> tokens)
    {
        var identifierKind = (int)Mist.Generated.TokenKind.IDENTIFIER;
        var usingKind = (int)Mist.Generated.TokenKind.USING;
        var namespaceKind = (int)Mist.Generated.TokenKind.NAMESPACE;
        var dotKind = (int)Mist.Generated.TokenKind.DOT;

        // Quick scan: check if any reclassification is needed.
        // A qualified identifier following USING or NAMESPACE is a sequence of
        // identifier-like tokens separated by DOTs. A keyword token appearing
        // after a DOT (or directly after USING/NAMESPACE) in such a position
        // needs reclassification.
        var needsFixup = false;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var kind = tokens[i].Kind;
            if (kind != usingKind && kind != namespaceKind)
                continue;

            // Check segments: (DOT segment)* pattern after the first segment.
            var j = i + 1;
            while (j < tokens.Length)
            {
                var segKind = tokens[j].Kind;
                if (segKind == identifierKind)
                {
                    j++;
                    // Check for DOT after identifier.
                    if (j < tokens.Length && tokens[j].Kind == dotKind)
                    {
                        j++; // skip DOT, continue to next segment
                        continue;
                    }
                    break; // end of qualified identifier
                }
                else if (segKind == dotKind)
                {
                    // DOT without preceding identifier — shouldn't happen normally
                    break;
                }
                else if (IsIdentifierLikeKeyword(segKind))
                {
                    // Found a keyword where we expect an identifier — fixup needed.
                    needsFixup = true;
                    break;
                }
                else
                {
                    break; // end of qualified identifier
                }
            }
            if (needsFixup) break;
        }

        if (!needsFixup)
            return tokens;

        // Build a new token array with reclassified tokens.
        var builder = ImmutableArray.CreateBuilder<Storm.Runtime.Token>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            builder.Add(tokens[i]);
        }

        for (var i = 0; i < builder.Count - 1; i++)
        {
            var kind = builder[i].Kind;
            if (kind != usingKind && kind != namespaceKind)
                continue;

            // Reclassify keyword tokens in the qualified identifier that follows.
            // The qualified identifier is: segment (DOT segment)* where segment is
            // IDENTIFIER or a keyword that looks like an identifier.
            var j = i + 1;
            while (j < builder.Count)
            {
                var tok = builder[j];
                if (tok.Kind == identifierKind)
                {
                    j++; // valid segment, continue
                }
                else if (IsIdentifierLikeKeyword(tok.Kind))
                {
                    // Reclassify this keyword token as IDENTIFIER.
                    builder[j] = new Storm.Runtime.Token(
                        identifierKind,
                        tok.Span,
                        tok.Start,
                        tok.End,
                        tok.Channel,
                        tok.Lexeme);
                    j++;
                }
                else if (tok.Kind == dotKind)
                {
                    j++; // DOT separator, continue to next segment
                }
                else
                {
                    break; // end of qualified identifier (semicolon, brace, etc.)
                }
            }
        }

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Returns true if the given token kind is a keyword that could appear as a
    /// namespace segment (i.e., it looks like an identifier but was lexed as a keyword).
    /// </summary>
    private static bool IsIdentifierLikeKeyword(int kind)
    {
        // All keyword token kinds that have identifier-like lexemes.
        // Excludes punctuation, operators, and structural tokens.
        return kind != (int)Mist.Generated.TokenKind.IDENTIFIER
            && kind != (int)Mist.Generated.TokenKind.DOT
            && kind != (int)Mist.Generated.TokenKind.SEMICOLON
            && kind != (int)Mist.Generated.TokenKind.LEFT_BRACE
            && kind != (int)Mist.Generated.TokenKind.RIGHT_BRACE
            && kind != (int)Mist.Generated.TokenKind.LEFT_PARENTHESIS
            && kind != (int)Mist.Generated.TokenKind.RIGHT_PARENTHESIS
            && kind != (int)Mist.Generated.TokenKind.LEFT_BRACKET
            && kind != (int)Mist.Generated.TokenKind.RIGHT_BRACKET
            && kind != (int)Mist.Generated.TokenKind.LESS_THAN
            && kind != (int)Mist.Generated.TokenKind.GREATER_THAN
            && kind != (int)Mist.Generated.TokenKind.EndOfFile
            && kind != (int)Mist.Generated.TokenKind.COMMA
            && kind != (int)Mist.Generated.TokenKind.COLON
            && kind != (int)Mist.Generated.TokenKind.EQUAL
            && kind != (int)Mist.Generated.TokenKind.STAR
            && kind != (int)Mist.Generated.TokenKind.PLUS
            && kind != (int)Mist.Generated.TokenKind.MINUS
            && kind != (int)Mist.Generated.TokenKind.SLASH
            && kind != (int)Mist.Generated.TokenKind.MODULO
            && kind != (int)Mist.Generated.TokenKind.EXCLAMATION
            && kind != (int)Mist.Generated.TokenKind.QUESTION
            && kind != (int)Mist.Generated.TokenKind.PIPE;
    }
}
