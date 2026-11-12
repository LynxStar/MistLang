using Mist.Generated;

namespace Mist.ProgramModel.Construction.Resolution;

internal static class QualifiedNameHelper
{
    public static string Build(QualifiedIdentifierNode qualifiedId)
    {
        if(qualifiedId.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return qualifiedId.IDENTIFIER.Lexeme;
        }

        var sb = new System.Text.StringBuilder(qualifiedId.IDENTIFIER.Lexeme);

        foreach(var token in qualifiedId.IDENTIFIERs)
        {
            if(!string.IsNullOrEmpty(token.Lexeme))
            {
                _ = sb.Append('.');
                _ = sb.Append(token.Lexeme);
            }
        }

        return sb.ToString();
    }
}