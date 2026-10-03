using Mist.ProgramModel.Construction;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CharacterLiteralValueTests
{
    [Theory]
    [InlineData("'A'", 'A')]
    [InlineData("'é'", 'é')]
    [InlineData("'\\\\'", '\\')]
    [InlineData("'\\\"'", '"')]
    [InlineData("'\\n'", '\n')]
    [InlineData("'\\r'", '\r')]
    [InlineData("'\\t'", '\t')]
    [InlineData("'\\0'", '\0')]
    [InlineData("'\\''", '\'')]
    public void CharacterLexeme_DecodesItsValue(string lexeme, char expected)
    {
        Assert.Equal(expected, CharacterLiterals.Decode(lexeme));
    }

    [Theory]
    [InlineData("")]
    [InlineData("'ab'")]
    [InlineData("'\\x'")]
    [InlineData("'\\'")]
    public void MalformedLexeme_IsRejected(string lexeme)
    {
        _ = Assert.Throws<FormatException>(() => CharacterLiterals.Decode(lexeme));
    }
}