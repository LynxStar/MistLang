using Mist.Generated;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared utility for building dot-separated qualified names from AST nodes.
/// </summary>
internal static class QualifiedNameHelper
{
    /// <summary>
    /// Builds a qualified name from a <see cref="QualifiedIdentifierNode"/> by joining
    /// its identifier tokens with dots.
    /// </summary>
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