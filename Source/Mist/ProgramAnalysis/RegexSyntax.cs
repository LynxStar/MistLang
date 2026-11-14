using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Validate before wrapping: malformed syntax can close the full-match wrapper and parse
/// with a weaker boundary. Runtime tests and <see cref="LiteralPredicates"/> both rely on it.
/// </summary>
internal static class RegexSyntax
{
    public static string? Error(string body)
    {
        return LiteralValue.Decoded(body) is { } pattern ? ErrorIn(pattern) : null;
    }

    public static string? ErrorIn(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return null;
        }
        catch(ArgumentException malformed)
        {
            return malformed.Message;
        }
    }

    public static IEnumerable<RegexPattern> Malformed(Pattern? predicate)
    {
        return predicate is null
            ? []
            : BodyWalker.DescendantsAndSelf(predicate).OfType<RegexPattern>().Where(regex => Error(regex.Pattern) is not null);
    }
}