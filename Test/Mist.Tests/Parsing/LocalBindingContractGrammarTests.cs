using System.Collections.Immutable;
using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

[Trait("Tier", "2")]
public sealed class LocalBindingContractGrammarTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ATypedDeclarationPreservesItsNameTokenAndReadonlyModifier(bool discard, bool isReadonly)
    {
        var parts = new List<(TokenKind Kind, string Text)>();
        if(isReadonly)
        {
            parts.Add((TokenKind.READONLY, "readonly"));
        }

        var nameKind = discard ? TokenKind.DISCARD : TokenKind.IDENTIFIER;
        var name = discard ? "_" : "value";
        parts.AddRange([(TokenKind.INT, "int"), (nameKind, name), (TokenKind.EQUAL, "="),
            (TokenKind.INTEGER_LITERAL, "3"), (TokenKind.SEMICOLON, ";")]);
        var tokens = ImmutableArray.CreateBuilder<Token>();
        var offset = 0;
        foreach(var (kind, text) in parts)
        {
            tokens.Add(new Token((int)kind, new SourceSpan(offset, text.Length),
                new LinePosition(1, offset + 1), new LinePosition(1, offset + text.Length + 1),
                TokenChannel.Default, text));
            offset += text.Length + 1;
        }

        var source = new SourceText("typed-discard.mist", string.Join(" ", parts.Select(part => part.Text)));
        var diagnostics = new DiagnosticBag();
        var parsed = MistParser.ParseVariableDeclarationStatement(new TokenStream(source, tokens.ToImmutable()), diagnostics);
        var declaration = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(parsed);

        Assert.Empty(diagnostics.Entries);
        Assert.Equal(parts.Count, MistParser.LastPosition);
        Assert.Equal((int)nameKind, declaration.Name.Kind);
        Assert.Equal(name, declaration.Name.Lexeme);
        Assert.Equal(isReadonly, declaration.ReadonlyModifier.HasValue);
        Assert.NotNull(declaration.Initializer);
    }
}