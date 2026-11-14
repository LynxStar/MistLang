using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Parsing;

public sealed class MistParser : IParser
{
    public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
    {
        diagnostics.Stage = PipelineStage.Parsing;
        var processedTokens = ReclassifyKeywordsInQualifiedNames(tokens.AllTokens);

        var stormDiagnostics = new Storm.Runtime.DiagnosticBag();
        var stormTokenStream = new Storm.Runtime.TokenStream(
            new Storm.Runtime.SourceText(tokens.Source.Path, tokens.Source.Contents),
            processedTokens);

        Storm.Runtime.SyntaxNode? root = null;
        try
        {
            root = Generated.MistParser.ParseCompilationUnit(stormTokenStream, stormDiagnostics);
        }
        catch(NotImplementedException)
        {
            diagnostics.ReportTyped(ParserDiagnostics.ParseStub, new FileOnly(tokens.Source.Path));
        }

        foreach(var entry in stormDiagnostics.Entries)
        {
            diagnostics.ReportTyped(ParserDiagnostics.ParseError, StormEntryLocation.Of(tokens.Source, entry.FilePath, entry.Line, entry.Column), new TextValue(entry.Message));
        }

        // The PEG parser backtracks out of a failed `stackalloc T[n]` alternative and keeps only a
        // generic MIST0122, so a token scan reports the missing or empty size.
        DetectStackallocSizeErrors(tokens, diagnostics);

        DetectRemovedInterfaceKeyword(tokens, diagnostics);

        DetectRemovedTraitKeyword(tokens, diagnostics);

        DetectPropertyDefaultInAbstract(tokens, diagnostics);

        if(root is not null)
        {
            AssignOrigin(root, new ParsedOrigin(tokens.Source));
            ValidateAccessModifierCombinations(root, diagnostics);
        }

        if(root is not null)
        {
            // The CompilationUnit rule leaves the EOF sentinel unconsumed, so it is not counted.
            var lastPos = Generated.MistParser.LastPosition;
            var defaultTokens = tokens.DefaultChannelTokens;
            var contentTokenCount = defaultTokens.Length > 0
                && defaultTokens[^1].Kind == (int)Generated.TokenKind.EndOfFile
                ? defaultTokens.Length - 1
                : defaultTokens.Length;

            if(lastPos < contentTokenCount)
            {
                var unconsumedToken = defaultTokens[lastPos];

                diagnostics.ReportTyped(ParserDiagnostics.ParseIncomplete, TokenLocation(tokens.Source, unconsumedToken, point: true), new QuantityValue(unconsumedToken.Start.Line));
            }
        }

        return new ParseResult(
            tokens.Source,
            tokens,
            Root: root,
            Diagnostics: ImmutableArray<Diagnostic>.Empty);
    }

    private static void ValidateAccessModifierCombinations(
        Storm.Runtime.SyntaxNode root,
        DiagnosticBag diagnostics)
    {
        if(root is Generated.IHasModifiersNode hasModifiers)
        {
            CheckModifiers(hasModifiers, diagnostics);
        }

        foreach(var child in root.Children())
        {
            if(child is not null)
            {
                ValidateAccessModifierCombinations(child, diagnostics);
            }
        }
    }

    private static void CheckModifiers(
        Generated.IHasModifiersNode node,
        DiagnosticBag diagnostics)
    {
        if(AccessModifierSyntax.CountAccessModifiers(node.Modifiers) > 1)
        {
            diagnostics.ReportAtNodeTyped(ParserDiagnostics.MultipleAccessModifiers, (Storm.Runtime.SyntaxNode)node, null);
        }
    }

    /// <summary>
    /// An <c>ElementTypeRef</c> never contains a <c>[</c>, so the first bracket after the element type
    /// is the size bracket: none is a missing size (Requirement 3.4) and <c>[]</c> an empty one
    /// (Requirement 3.5).
    /// </summary>
    private static void DetectStackallocSizeErrors(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind != (int)Generated.TokenKind.STACKALLOC)
            {
                continue;
            }

            var afterType = SkipElementType(defaultTokens, i + 1);
            if(afterType is null)
            {
                // Not a size error, as in `stackalloc [3]`; the generic parse error reports it.
                continue;
            }

            var pos = afterType.Value;
            var hasOpenBracket = pos < defaultTokens.Length
                && defaultTokens[pos].Kind == (int)Generated.TokenKind.LEFT_BRACKET;

            if(!hasOpenBracket)
            {
                AddStackallocSizeDiagnostic(defaultTokens[i], tokens.Source, diagnostics);
                continue;
            }

            var isEmptyBrackets = pos + 1 < defaultTokens.Length
                && defaultTokens[pos + 1].Kind == (int)Generated.TokenKind.RIGHT_BRACKET;

            if(isEmptyBrackets)
            {
                AddStackallocSizeDiagnostic(defaultTokens[i], tokens.Source, diagnostics);
            }
        }
    }

    private static void AddStackallocSizeDiagnostic(
        Storm.Runtime.Token stackallocToken,
        SourceText source,
        DiagnosticBag diagnostics)
    {
        diagnostics.ReportTyped(ParserDiagnostics.StackallocSize, TokenLocation(source, stackallocToken, point: false));
    }

    private static Location TokenLocation(SourceText source, Storm.Runtime.Token token, bool point)
    {
        var identity = new SourceIdentity(source);
        return point ? new SourcePoint(identity, token.Span.Start) : new SourceRange(identity, token.Span.Start, token.Span.Length);
    }

    private static void DetectRemovedInterfaceKeyword(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var identifierKind = (int)Generated.TokenKind.IDENTIFIER;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind == identifierKind
                && defaultTokens[i].Lexeme == "interface")
            {
                diagnostics.ReportTyped(ParserDiagnostics.RemovedInterfaceKeyword, TokenLocation(tokens.Source, defaultTokens[i], point: false));
            }
        }
    }

    private static void DetectRemovedTraitKeyword(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var identifierKind = (int)Generated.TokenKind.IDENTIFIER;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind == identifierKind
                && defaultTokens[i].Lexeme == "trait")
            {
                diagnostics.ReportTyped(ParserDiagnostics.RemovedTraitKeyword, TokenLocation(tokens.Source, defaultTokens[i], point: false));
            }
        }
    }

    /// <summary>
    /// Checks direct members only (Requirement 13.3); a nested abstract type is scanned from its own
    /// <c>abstract</c> keyword.
    /// </summary>
    private static void DetectPropertyDefaultInAbstract(TokenStream tokens, DiagnosticBag diagnostics)
    {
        var defaultTokens = tokens.DefaultChannelTokens;
        var abstractKind = (int)Generated.TokenKind.ABSTRACT;
        var leftBraceKind = (int)Generated.TokenKind.LEFT_BRACE;
        var semicolonKind = (int)Generated.TokenKind.SEMICOLON;
        var eofKind = (int)Generated.TokenKind.EndOfFile;

        for(var i = 0; i < defaultTokens.Length; i++)
        {
            if(defaultTokens[i].Kind != abstractKind)
            {
                continue;
            }

            // Type parameters, a base list and where clauses use no braces, so the first `{` is the body.
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

            ScanAbstractBodyForPropertyDefaults(defaultTokens, open, tokens.Source, diagnostics);
        }
    }

    private static void ScanAbstractBodyForPropertyDefaults(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int bodyOpenIndex,
        SourceText source,
        DiagnosticBag diagnostics)
    {
        var leftBraceKind = (int)Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Generated.TokenKind.RIGHT_BRACE;
        var leftParenKind = (int)Generated.TokenKind.LEFT_PARENTHESIS;
        var semicolonKind = (int)Generated.TokenKind.SEMICOLON;
        var suchThatKind = (int)Generated.TokenKind.SUCH_THAT;

        var depth = 0;

        // A method or nested type opens a member-level brace that is not an accessor block.
        var memberIsMethodOrType = false;

        for(var i = bodyOpenIndex; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;

            if(kind == leftBraceKind)
            {
                if(depth == 1 && !memberIsMethodOrType
                    && AccessorBlockHasBody(tokens, i))
                {
                    AddPropertyDefaultDiagnostic(tokens[i], source, diagnostics);
                }

                depth++;
                continue;
            }

            if(kind == rightBraceKind)
            {
                depth--;
                if(depth == 0)
                {
                    return;
                }

                if(depth == 1)
                {
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
                AddPropertyDefaultDiagnostic(tokens[i], source, diagnostics);
            }
            else if(kind == semicolonKind)
            {
                memberIsMethodOrType = false;
            }
        }
    }

    private static bool AccessorBlockHasBody(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int braceIndex)
    {
        var leftBraceKind = (int)Generated.TokenKind.LEFT_BRACE;
        var rightBraceKind = (int)Generated.TokenKind.RIGHT_BRACE;
        var suchThatKind = (int)Generated.TokenKind.SUCH_THAT;

        var depth = 0;
        for(var i = braceIndex; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;

            if(kind == leftBraceKind)
            {
                if(depth == 1)
                {
                    return true;
                }

                depth++;
            }
            else if(kind == rightBraceKind)
            {
                depth--;
                if(depth == 0)
                {
                    return false;
                }
            }
            else if(kind == suchThatKind && depth == 1)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypeDeclarationKeyword(int kind)
    {
        return kind is ((int)Generated.TokenKind.CLASS)
        or ((int)Generated.TokenKind.STRUCT)
        or ((int)Generated.TokenKind.ABSTRACT)
        or ((int)Generated.TokenKind.ENUM);
    }

    private static void AddPropertyDefaultDiagnostic(
        Storm.Runtime.Token token,
        SourceText source,
        DiagnosticBag diagnostics)
    {
        diagnostics.ReportTyped(ParserDiagnostics.AbstractPropertyDefault, TokenLocation(source, token, point: false));
    }

    private static int? SkipElementType(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int start)
    {
        if(start >= tokens.Length)
        {
            return null;
        }

        var kind = tokens[start].Kind;

        if(IsPrimitiveTypeKind(kind) || kind == (int)Generated.TokenKind.UNKNOWN)
        {
            return start + 1;
        }

        if(kind == (int)Generated.TokenKind.IDENTIFIER)
        {
            var pos = start + 1;

            while(pos + 1 < tokens.Length
                && tokens[pos].Kind == (int)Generated.TokenKind.DOT
                && tokens[pos + 1].Kind == (int)Generated.TokenKind.IDENTIFIER)
            {
                pos += 2;
            }

            if(pos < tokens.Length && tokens[pos].Kind == (int)Generated.TokenKind.LESS_THAN)
            {
                return SkipBalancedAngles(tokens, pos);
            }

            return pos;
        }

        return null;
    }

    private static int? SkipBalancedAngles(
        ImmutableArray<Storm.Runtime.Token> tokens,
        int openAngle)
    {
        var depth = 0;
        for(var pos = openAngle; pos < tokens.Length; pos++)
        {
            var kind = tokens[pos].Kind;
            if(kind == (int)Generated.TokenKind.LESS_THAN)
            {
                depth++;
            }
            else if(kind == (int)Generated.TokenKind.GREATER_THAN)
            {
                depth--;
                if(depth == 0)
                {
                    return pos + 1;
                }
            }
            else if(kind is ((int)Generated.TokenKind.SEMICOLON)
                or ((int)Generated.TokenKind.LEFT_BRACE)
                or ((int)Generated.TokenKind.RIGHT_BRACE)
                or ((int)Generated.TokenKind.EndOfFile))
            {
                return null;
            }
        }

        return null;
    }

    private static bool IsPrimitiveTypeKind(int kind)
    {
        return kind is ((int)Generated.TokenKind.INT)
        or ((int)Generated.TokenKind.UINT)
        or ((int)Generated.TokenKind.LONG)
        or ((int)Generated.TokenKind.ULONG)
        or ((int)Generated.TokenKind.SHORT)
        or ((int)Generated.TokenKind.USHORT)
        or ((int)Generated.TokenKind.BYTE)
        or ((int)Generated.TokenKind.SBYTE)
        or ((int)Generated.TokenKind.FLOAT)
        or ((int)Generated.TokenKind.DOUBLE)
        or ((int)Generated.TokenKind.DECIMAL)
        or ((int)Generated.TokenKind.BOOL)
        or ((int)Generated.TokenKind.CHAR)
        or ((int)Generated.TokenKind.STRING)
        or ((int)Generated.TokenKind.REGEX);
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
    /// A CLR namespace segment can coincide with a Mist keyword, as in <c>using Some.Namespace.type.Foo;</c>.
    /// The lexer emits a keyword token there, which <c>QualifiedIdentifier</c> rejects, so such segments
    /// after <c>using</c> or <c>namespace</c> become IDENTIFIER.
    /// </summary>
    private static ImmutableArray<Storm.Runtime.Token> ReclassifyKeywordsInQualifiedNames(
        ImmutableArray<Storm.Runtime.Token> tokens)
    {
        var identifierKind = (int)Generated.TokenKind.IDENTIFIER;
        var usingKind = (int)Generated.TokenKind.USING;
        var namespaceKind = (int)Generated.TokenKind.NAMESPACE;
        var dotKind = (int)Generated.TokenKind.DOT;

        var needsFixup = false;
        for(var i = 0; i < tokens.Length - 1; i++)
        {
            var kind = tokens[i].Kind;
            if(kind != usingKind && kind != namespaceKind)
            {
                continue;
            }

            var j = i + 1;
            while(j < tokens.Length)
            {
                var segKind = tokens[j].Kind;
                if(segKind == identifierKind)
                {
                    j++;
                    if(j < tokens.Length && tokens[j].Kind == dotKind)
                    {
                        j++;
                        continue;
                    }

                    break;
                }
                else if(segKind == dotKind)
                {
                    break;
                }
                else if(IsIdentifierLikeKeyword(segKind))
                {
                    needsFixup = true;
                    break;
                }
                else
                {
                    break;
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

            var j = i + 1;
            while(j < builder.Count)
            {
                var tok = builder[j];
                if(tok.Kind == identifierKind)
                {
                    j++;
                }
                else if(IsIdentifierLikeKeyword(tok.Kind))
                {
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
                    j++;
                }
                else
                {
                    break;
                }
            }
        }

        return builder.MoveToImmutable();
    }

    private static bool IsIdentifierLikeKeyword(int kind)
    {
        return kind is not ((int)Generated.TokenKind.IDENTIFIER)
            and not ((int)Generated.TokenKind.DOT)
            and not ((int)Generated.TokenKind.SEMICOLON)
            and not ((int)Generated.TokenKind.LEFT_BRACE)
            and not ((int)Generated.TokenKind.RIGHT_BRACE)
            and not ((int)Generated.TokenKind.LEFT_PARENTHESIS)
            and not ((int)Generated.TokenKind.RIGHT_PARENTHESIS)
            and not ((int)Generated.TokenKind.LEFT_BRACKET)
            and not ((int)Generated.TokenKind.RIGHT_BRACKET)
            and not ((int)Generated.TokenKind.LESS_THAN)
            and not ((int)Generated.TokenKind.GREATER_THAN)
            and not ((int)Generated.TokenKind.EndOfFile)
            and not ((int)Generated.TokenKind.COMMA)
            and not ((int)Generated.TokenKind.COLON)
            and not ((int)Generated.TokenKind.EQUAL)
            and not ((int)Generated.TokenKind.STAR)
            and not ((int)Generated.TokenKind.PLUS)
            and not ((int)Generated.TokenKind.MINUS)
            and not ((int)Generated.TokenKind.SLASH)
            and not ((int)Generated.TokenKind.MODULO)
            and not ((int)Generated.TokenKind.EXCLAMATION)
            and not ((int)Generated.TokenKind.QUESTION)
            and not ((int)Generated.TokenKind.PIPE);
    }
}