using Mist.Common.Diagnostics;
using Mist.Parsing;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using SourceText = Mist.Common.Source.SourceText;
using TokenKind = Mist.Generated.TokenKind;
using TokenStream = Mist.Lexing.Contracts.TokenStream;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class ParserTokenScanTests
{
    [Fact]
    public void EachInterfaceIdentifierIsRefusedAtItsOwnToken()
    {
        var (source, offsets, findings) = Scan(
            (TokenKind.IDENTIFIER, "interface"), (TokenKind.IDENTIFIER, "Shape"),
            (TokenKind.LEFT_BRACE, "{"), (TokenKind.RIGHT_BRACE, "}"),
            (TokenKind.IDENTIFIER, "interface"), (TokenKind.IDENTIFIER, "Area"),
            (TokenKind.LEFT_BRACE, "{"), (TokenKind.RIGHT_BRACE, "}"));

        AssertRangesAt(source, findings, ParserDiagnostics.RemovedInterfaceKeyword, [offsets[0], offsets[4]], "interface".Length);
        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.RemovedTraitKeyword);
    }

    [Fact]
    public void EachTraitIdentifierIsRefusedAtItsOwnToken()
    {
        var (source, offsets, findings) = Scan(
            (TokenKind.IDENTIFIER, "trait"), (TokenKind.IDENTIFIER, "Shape"),
            (TokenKind.LEFT_BRACE, "{"), (TokenKind.RIGHT_BRACE, "}"),
            (TokenKind.IDENTIFIER, "trait"), (TokenKind.IDENTIFIER, "Area"),
            (TokenKind.LEFT_BRACE, "{"), (TokenKind.RIGHT_BRACE, "}"));

        AssertRangesAt(source, findings, ParserDiagnostics.RemovedTraitKeyword, [offsets[0], offsets[4]], "trait".Length);
        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.RemovedInterfaceKeyword);
    }

    [Fact]
    public void IdentifiersThatOnlyResembleTheRemovedKeywordsAreNotRefused()
    {
        var (_, _, findings) = Scan(
            (TokenKind.IDENTIFIER, "Interface"), (TokenKind.IDENTIFIER, "interfaces"),
            (TokenKind.IDENTIFIER, "Trait"), (TokenKind.IDENTIFIER, "traits"),
            (TokenKind.IDENTIFIER, "subtrait"), (TokenKind.SEMICOLON, ";"));

        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.RemovedInterfaceKeyword
            || finding.Descriptor == ParserDiagnostics.RemovedTraitKeyword);
    }

    [Theory]
    [InlineData(TokenKind.INT, "int")]
    [InlineData(TokenKind.UINT, "uint")]
    [InlineData(TokenKind.LONG, "long")]
    [InlineData(TokenKind.ULONG, "ulong")]
    [InlineData(TokenKind.SHORT, "short")]
    [InlineData(TokenKind.USHORT, "ushort")]
    [InlineData(TokenKind.BYTE, "byte")]
    [InlineData(TokenKind.SBYTE, "sbyte")]
    [InlineData(TokenKind.FLOAT, "float")]
    [InlineData(TokenKind.DOUBLE, "double")]
    [InlineData(TokenKind.DECIMAL, "decimal")]
    [InlineData(TokenKind.BOOL, "bool")]
    [InlineData(TokenKind.CHAR, "char")]
    [InlineData(TokenKind.STRING, "string")]
    [InlineData(TokenKind.REGEX, "regex")]
    [InlineData(TokenKind.UNKNOWN, "unknown")]
    public void EveryPrimitiveOrUnknownElementTypeWithoutASizeIsAStackallocSizeError(TokenKind elementKind, string elementText)
    {
        var (source, offsets, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (elementKind, elementText), (TokenKind.SEMICOLON, ";"));

        AssertRangesAt(source, findings, ParserDiagnostics.StackallocSize, [offsets[0]], "stackalloc".Length);
    }

    [Fact]
    public void AQualifiedGenericElementTypeWithoutASizeIsAStackallocSizeError()
    {
        var (source, offsets, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.IDENTIFIER, "Ns"), (TokenKind.DOT, "."),
            (TokenKind.IDENTIFIER, "Pair"), (TokenKind.LESS_THAN, "<"), (TokenKind.INT, "int"),
            (TokenKind.GREATER_THAN, ">"), (TokenKind.SEMICOLON, ";"));

        AssertRangesAt(source, findings, ParserDiagnostics.StackallocSize, [offsets[0]], "stackalloc".Length);
    }

    [Fact]
    public void EveryMalformedStackallocInTheFileIsReportedAndAWellFormedOneIsNot()
    {
        var (source, offsets, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.INT, "int"), (TokenKind.SEMICOLON, ";"),
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.BYTE, "byte"),
            (TokenKind.LEFT_BRACKET, "["), (TokenKind.INTEGER_LITERAL, "4"), (TokenKind.RIGHT_BRACKET, "]"),
            (TokenKind.SEMICOLON, ";"),
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.CHAR, "char"),
            (TokenKind.LEFT_BRACKET, "["), (TokenKind.RIGHT_BRACKET, "]"), (TokenKind.SEMICOLON, ";"));

        AssertRangesAt(source, findings, ParserDiagnostics.StackallocSize, [offsets[0], offsets[9]], "stackalloc".Length);
    }

    [Fact]
    public void ANestedGenericElementTypeFollowedByItsSizeIsNotAStackallocSizeError()
    {
        var (_, _, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.IDENTIFIER, "List"), (TokenKind.LESS_THAN, "<"),
            (TokenKind.IDENTIFIER, "List"), (TokenKind.LESS_THAN, "<"), (TokenKind.INT, "int"),
            (TokenKind.GREATER_THAN, ">"), (TokenKind.GREATER_THAN, ">"),
            (TokenKind.LEFT_BRACKET, "["), (TokenKind.INTEGER_LITERAL, "2"), (TokenKind.RIGHT_BRACKET, "]"),
            (TokenKind.SEMICOLON, ";"));

        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.StackallocSize);
    }

    [Fact]
    public void AnUnclosedTypeArgumentListIsNotAStackallocSizeError()
    {
        var (_, _, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.IDENTIFIER, "List"), (TokenKind.LESS_THAN, "<"),
            (TokenKind.INT, "int"), (TokenKind.LEFT_BRACKET, "["), (TokenKind.SEMICOLON, ";"));

        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.StackallocSize);
    }

    [Fact]
    public void ABracketWhereTheElementTypeBelongsIsNotAStackallocSizeError()
    {
        var (_, _, findings) = Scan(
            (TokenKind.STACKALLOC, "stackalloc"), (TokenKind.LEFT_BRACKET, "["),
            (TokenKind.INTEGER_LITERAL, "3"), (TokenKind.RIGHT_BRACKET, "]"), (TokenKind.SEMICOLON, ";"));

        Assert.DoesNotContain(findings, finding => finding.Descriptor == ParserDiagnostics.StackallocSize);
    }

    private static (SourceText Source, int[] Offsets, IReadOnlyList<Diagnostic> Findings) Scan(
        params (TokenKind Kind, string Text)[] tokens)
    {
        var source = new SourceText("scan.mist", string.Join(" ", tokens.Select(token => token.Text)));
        var offsets = new int[tokens.Length];
        var built = new List<Token>();
        var offset = 0;
        foreach(var (kind, text) in tokens.Append((TokenKind.EndOfFile, "")))
        {
            if(built.Count < offsets.Length)
            {
                offsets[built.Count] = offset;
            }

            built.Add(new Token(
                (int)kind, new SourceSpan(offset, text.Length),
                new LinePosition(1, offset + 1),
                new LinePosition(1, offset + text.Length + 1),
                TokenChannel.Default, text));
            offset = Math.Min(offset + text.Length + 1, source.Contents.Length);
        }

        var stream = new TokenStream(source, [.. built]);
        var diagnostics = new DiagnosticBag();

        _ = new MistParser().Parse(stream, diagnostics);

        return (source, offsets, diagnostics.Admitted);
    }

    private static void AssertRangesAt(
        SourceText source,
        IReadOnlyList<Diagnostic> findings,
        MistDiagnostic descriptor,
        int[] offsets,
        int length)
    {
        var ranges = findings
            .Where(finding => finding.Descriptor == descriptor)
            .Select(finding => Assert.IsType<SourceRange>(finding.Location))
            .ToList();

        Assert.All(ranges, range => Assert.Same(source, range.File.Text));
        Assert.Equal(offsets, ranges.Select(range => range.Offset));
        Assert.All(ranges, range => Assert.Equal(length, range.Length));
    }
}