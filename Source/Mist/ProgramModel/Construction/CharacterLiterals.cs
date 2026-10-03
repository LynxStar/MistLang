namespace Mist.ProgramModel.Construction;

internal static class CharacterLiterals
{
    internal static char Decode(string lexeme)
    {
        if(lexeme.Length == 3 && lexeme[0] == '\'' && lexeme[2] == '\'' && lexeme[1] is not ('\'' or '\\'))
        {
            return lexeme[1];
        }

        if(lexeme.Length == 4 && lexeme[0] == '\'' && lexeme[1] == '\\' && lexeme[3] == '\'')
        {
            return lexeme[2] switch
            {
                '\\' => '\\',
                '"' => '"',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '0' => '\0',
                '\'' => '\'',
                _ => throw new FormatException($"Invalid character literal: {lexeme}"),
            };
        }

        throw new FormatException($"Invalid character literal: {lexeme}");
    }
}