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
        foreach(var entry in stormDiagnostics.Entries)
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

        // The `interface` keyword was removed from the grammar (replaced by
        // `abstract`). The lexer now produces IDENTIFIER for "interface". Scan
        // for those identifiers and emit a helpful migration diagnostic.
        DetectRemovedInterfaceKeyword(tokens, diagnostics);

        // The `trait` keyword was removed from the grammar. Use 'abstract' with
        // nominal typing or a 'with' block for retroactive conformance instead.
        DetectRemovedTraitKeyword(tokens, diagnostics);

        // Abstract types may declare default METHOD implementations (methods with
        // bodies), but properties may NOT have default implementations. A property
        // with a body inside an abstract type body fails to parse through the
        // grammar; detect that shape with a focused token scan and emit a clear
        // diagnostic (Requirement 13.3).
        DetectPropertyDefaultInAbstract(tokens, diagnostics);

        // Assign ParsedOrigin to every node in the tree so diagnostics
        // can resolve source locations without span-containment heuristics.
        if(root is not null)
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

            if(lastPos < contentTokenCount)
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
        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind != (int)Mist.Generated.TokenKind.STACKALLOC)
            {
                continue;
            }

            var afterType = SkipElementType(defaultTokens, i + 1);
            if(afterType is null)
            {
                // The token after `stackalloc` does not begin a recognizable element
                // type (e.g. `stackalloc [3]`). That is a different malformed shape;
                // leave it to the generic parser error rather than mis-attributing it.
                continue;
            }

            var pos = afterType.Value;
            var hasOpenBracket = pos < defaultTokens.Length
                && defaultTokens[pos].Kind == (int)Mist.Generated.TokenKind.LEFT_BRACKET;

            if(!hasOpenBracket)
            {
                // Requirement 3.4: `stackalloc T` with no bracketed size.
                AddStackallocSizeDiagnostic(defaultTokens[i], tokens.Source.Path, diagnostics);
                continue;
            }

            var isEmptyBrackets = pos + 1 < defaultTokens.Length
                && defaultTokens[pos + 1].Kind == (int)Mist.Generated.TokenKind.RIGHT_BRACKET;

            if(isEmptyBrackets)
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
    /// Scans the token stream for IDENTIFIER tokens with lexeme <c>"interface"</c> and
    /// emits a diagnostic directing the user to use <c>abstract</c> instead.
    /// </summary>
    /// <remarks>
    /// Since the <c>INTERFACE</c> token was removed from <c>keywords.storm</c>, the
    /// lexer produces a plain <c>IDENTIFIER</c> for the text "interface". This method
    /// detects that usage and provides a clear migration message.
    /// </remarks>
    private static void DetectRemovedInterfaceKeyword(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var identifierKind = (int)Mist.Generated.TokenKind.IDENTIFIER;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind == identifierKind
                && defaultTokens[i].Lexeme == "interface")
            {
                var token = defaultTokens[i];
                var location = (token.Start.Line > 0 && token.Start.Column > 0)
                    ? new LinePositionSpan(
                        new LinePosition(token.Start.Line, token.Start.Column),
                        new LinePosition(token.End.Line, token.End.Column))
                    : default;

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_REMOVED_INTERFACE_KEYWORD"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Parsing,
                    "The 'interface' keyword has been removed. Use 'abstract' instead.",
                    Location: location,
                    CompilationUnitPath: tokens.Source.Path,
                    AnalyzerName: null));
            }
        }
    }

    /// <summary>
    /// Scans the token stream for IDENTIFIER tokens with lexeme <c>"trait"</c> and
    /// emits a diagnostic directing the user to use <c>abstract</c> with nominal typing
    /// or a <c>with</c> block for retroactive conformance.
    /// </summary>
    /// <remarks>
    /// Since the <c>TRAIT</c> token was removed from <c>keywords.storm</c>, the
    /// lexer produces a plain <c>IDENTIFIER</c> for the text "trait". This method
    /// detects that usage and provides a clear migration message.
    /// </remarks>
    private static void DetectRemovedTraitKeyword(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var identifierKind = (int)Mist.Generated.TokenKind.IDENTIFIER;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind == identifierKind
                && defaultTokens[i].Lexeme == "trait")
            {
                var token = defaultTokens[i];
                var location = (token.Start.Line > 0 && token.Start.Column > 0)
                    ? new LinePositionSpan(
                        new LinePosition(token.Start.Line, token.Start.Column),
                        new LinePosition(token.End.Line, token.End.Column))
                    : default;

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_REMOVED_TRAIT_KEYWORD"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Parsing,
                    "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance.",
                    Location: location,
                    CompilationUnitPath: tokens.Source.Path,
                    AnalyzerName: null));
            }
        }
    }

    /// <summary>
    /// Scans each abstract type body for property declarations that carry a body
    /// (block accessors like <c>get { ... }</c>, expression accessors like
    /// <c>get =&gt; ...;</c>, or an expression-bodied property like
    /// <c>int Foo =&gt; 1;</c>) and emits a <c>MIST_ABSTRACT_PROPERTY_DEFAULT</c>
    /// diagnostic. Only methods may have default implementations in an abstract
    /// type; property slots must always be filled by the implementer
    /// (Requirement 13.3).
    /// </summary>
    /// <remarks>
    /// A property with a body cannot be parsed by the <c>AbstractMemberDeclaration</c>
    /// grammar rule (its accessors are semicolon-terminated slots, and the
    /// default-method alternative requires a parameter list), so the failure would
    /// otherwise surface only as a generic parse error. This focused scan recognizes
    /// the shape and reports the precise cause.
    ///
    /// The scan operates strictly at the direct member level of each abstract body
    /// (brace depth 1). Members of nested types (classes, structs, enums, nested
    /// abstract types) live at deeper brace depths and are not flagged here — a
    /// nested abstract type is scanned independently when its own <c>abstract</c>
    /// keyword is encountered by the outer loop.
    /// </remarks>
    private static void DetectPropertyDefaultInAbstract(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var abstractKind = (int)Mist.Generated.TokenKind.ABSTRACT;
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var semicolonKind = (int)Mist.Generated.TokenKind.SEMICOLON;
        var eofKind = (int)Mist.Generated.TokenKind.EndOfFile;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind != abstractKind)
            {
                continue;
            }

            // Locate the abstract body's opening brace. Between `abstract` and the
            // body, only `<...>` type params, `: BaseList`, and `where` clauses may
            // appear — none of which use braces — so the first `{` is the body.
            var open = i + 1;
            while(open < defaultTokens.Length
                && defaultTokens[open].Kind != leftBraceKind
                && defaultTokens[open].Kind != semicolonKind
                && defaultTokens[open].Kind != eofKind)
            {
                open++;
            }

            if(open >= defaultTokens.Length || defaultTokens[open].Kind != leftBraceKind)
            {
                continue;
            }

            ScanAbstractBodyForPropertyDefaults(defaultTokens, open, tokens.Source.Path, diagnostics);
        }
    }

    /// <summary>
    /// Scans a single abstract type body (starting at its opening brace) for
    /// property-with-body members at the direct member level, emitting a diagnostic
    /// for each. See <see cref="DetectPropertyDefaultInAbstract"/> for the rationale.
    /// </summary>
    private static void ScanAbstractBodyForPropertyDefaults(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int bodyOpenIndex,
        string sourcePath,
        DiagnosticBag diagnostics)
    {
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Mist.Generated.TokenKind.RIGHT_BRACE;
        var leftParenKind = (int)Mist.Generated.TokenKind.LEFT_PARENTHESIS;
        var semicolonKind = (int)Mist.Generated.TokenKind.SEMICOLON;
        var suchThatKind = (int)Mist.Generated.TokenKind.SUCH_THAT;

        var depth = 0;

        // Whether the current direct member (since the last member boundary) is a
        // method or a nested type declaration — both of which legitimately introduce
        // a brace at member level that is NOT a property accessor block.
        var memberIsMethodOrType = false;

        for(var i = bodyOpenIndex; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;

            if(kind == leftBraceKind)
            {
                if(depth == 1 && !memberIsMethodOrType
                    && AccessorBlockHasBody(tokens, i))
                {
                    // `Type Name { get { ... } }` or `Type Name { get => ...; }`.
                    AddPropertyDefaultDiagnostic(tokens[i], sourcePath, diagnostics);
                }

                depth++;
                continue;
            }

            if(kind == rightBraceKind)
            {
                depth--;
                if(depth == 0)
                {
                    return; // End of this abstract body.
                }

                if(depth == 1)
                {
                    // Returned to member level after a method body / nested type.
                    memberIsMethodOrType = false;
                }

                continue;
            }

            if(depth != 1)
            {
                continue;
            }

            if(kind == leftParenKind || IsTypeDeclarationKeyword(kind))
            {
                // A method (has a parameter list) or a nested type declaration.
                memberIsMethodOrType = true;
            }
            else if(kind == suchThatKind && !memberIsMethodOrType)
            {
                // `Type Name => expr;` expression-bodied property at member level.
                AddPropertyDefaultDiagnostic(tokens[i], sourcePath, diagnostics);
            }
            else if(kind == semicolonKind)
            {
                // Member boundary; the next member starts fresh.
                memberIsMethodOrType = false;
            }
        }
    }

    /// <summary>
    /// Given the index of a property's opening brace, returns <c>true</c> if any
    /// accessor inside carries a body — i.e. an inner <c>{</c> (block accessor) or
    /// <c>=&gt;</c> (expression accessor) appears at the property's top level. A
    /// slot-only block such as <c>{ get; set; }</c> contains neither and returns
    /// <c>false</c>.
    /// </summary>
    private static bool AccessorBlockHasBody(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int braceIndex)
    {
        var leftBraceKind = (int)Mist.Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Mist.Generated.TokenKind.RIGHT_BRACE;
        var suchThatKind = (int)Mist.Generated.TokenKind.SUCH_THAT;

        var depth = 0;
        for(var i = braceIndex; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;

            if(kind == leftBraceKind)
            {
                if(depth == 1)
                {
                    // An accessor opened its own block body: `get { ... }`.
                    return true;
                }

                depth++;
            }
            else if(kind == rightBraceKind)
            {
                depth--;
                if(depth == 0)
                {
                    return false; // Reached the matching close with no bodied accessor.
                }
            }
            else if(kind == suchThatKind && depth == 1)
            {
                // An accessor used an expression body: `get => ...;`.
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeDeclarationKeyword(int kind)
    {
        return kind == (int)Mist.Generated.TokenKind.CLASS
        || kind == (int)Mist.Generated.TokenKind.STRUCT
        || kind == (int)Mist.Generated.TokenKind.ABSTRACT
        || kind == (int)Mist.Generated.TokenKind.ENUM;
    }

    private static void AddPropertyDefaultDiagnostic(
        Storm.Runtime.Token token,
        string sourcePath,
        DiagnosticBag diagnostics)
    {
        var location = (token.Start.Line > 0 && token.Start.Column > 0)
            ? new LinePositionSpan(
                new LinePosition(token.Start.Line, token.Start.Column),
                new LinePosition(token.End.Line, token.End.Column))
            : default;

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_ABSTRACT_PROPERTY_DEFAULT"),
            DiagnosticSeverity.Error,
            PipelineStage.Parsing,
            "Property slots cannot have default implementations. Only methods can have defaults.",
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
        ImmutableArray<Storm.Runtime.Token> tokens,
        int start)
    {
        if(start >= tokens.Length)
        {
            return null;
        }

        var kind = tokens[start].Kind;

        if(IsPrimitiveTypeKind(kind) || kind == (int)Mist.Generated.TokenKind.UNKNOWN)
        {
            return start + 1;
        }

        if(kind == (int)Mist.Generated.TokenKind.IDENTIFIER)
        {
            var pos = start + 1;

            // QualifiedIdentifier: IDENTIFIER (DOT IDENTIFIER)*
            while(pos + 1 < tokens.Length
                && tokens[pos].Kind == (int)Mist.Generated.TokenKind.DOT
                && tokens[pos + 1].Kind == (int)Mist.Generated.TokenKind.IDENTIFIER)
            {
                pos += 2;
            }

            // Optional TypeArgumentList: LESS_THAN ... GREATER_THAN (balanced).
            if(pos < tokens.Length && tokens[pos].Kind == (int)Mist.Generated.TokenKind.LESS_THAN)
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
        ImmutableArray<Storm.Runtime.Token> tokens,
        int openAngle)
    {
        var depth = 0;
        for(var pos = openAngle; pos < tokens.Length; pos++)
        {
            var kind = tokens[pos].Kind;
            if(kind == (int)Mist.Generated.TokenKind.LESS_THAN)
            {
                depth++;
            }
            else if(kind == (int)Mist.Generated.TokenKind.GREATER_THAN)
            {
                depth--;
                if(depth == 0)
                {
                    return pos + 1;
                }
            }
            else if(kind == (int)Mist.Generated.TokenKind.SEMICOLON
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

    private static bool IsPrimitiveTypeKind(int kind)
    {
        return kind == (int)Mist.Generated.TokenKind.INT
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
    }

    private static void AssignOrigin(Storm.Runtime.SyntaxNode node, ParsedOrigin origin)
    {
        node.Origin = origin;
        foreach(var child in node.Children())
        {
            if(child is not null)
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
        for(var i = 0; i < tokens.Length - 1; i++)
        {
            var kind = tokens[i].Kind;
            if(kind != usingKind && kind != namespaceKind)
            {
                continue;
            }

            // Check segments: (DOT segment)* pattern after the first segment.
            var j = i + 1;
            while(j < tokens.Length)
            {
                var segKind = tokens[j].Kind;
                if(segKind == identifierKind)
                {
                    j++;
                    // Check for DOT after identifier.
                    if(j < tokens.Length && tokens[j].Kind == dotKind)
                    {
                        j++; // skip DOT, continue to next segment
                        continue;
                    }

                    break; // end of qualified identifier
                }
                else if(segKind == dotKind)
                {
                    // DOT without preceding identifier — shouldn't happen normally
                    break;
                }
                else if(IsIdentifierLikeKeyword(segKind))
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

            if(needsFixup)
            {
                break;
            }
        }

        if(!needsFixup)
        {
            return tokens;
        }

        // Build a new token array with reclassified tokens.
        var builder = ImmutableArray.CreateBuilder<Storm.Runtime.Token>(tokens.Length);
        for(var i = 0; i < tokens.Length; i++)
        {
            builder.Add(tokens[i]);
        }

        for(var i = 0; i < builder.Count - 1; i++)
        {
            var kind = builder[i].Kind;
            if(kind != usingKind && kind != namespaceKind)
            {
                continue;
            }

            // Reclassify keyword tokens in the qualified identifier that follows.
            // The qualified identifier is: segment (DOT segment)* where segment is
            // IDENTIFIER or a keyword that looks like an identifier.
            var j = i + 1;
            while(j < builder.Count)
            {
                var tok = builder[j];
                if(tok.Kind == identifierKind)
                {
                    j++; // valid segment, continue
                }
                else if(IsIdentifierLikeKeyword(tok.Kind))
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
                else if(tok.Kind == dotKind)
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