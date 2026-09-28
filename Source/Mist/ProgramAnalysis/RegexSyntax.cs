using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Whether a written regex is one the .NET engine accepts on its own, which is the question every full
/// match rests on: the runtime test and <see cref="LiteralPredicates"/> both wrap the pattern as text, so
/// one that fails alone can still parse once wrapped -- <c>a)|(?:b</c> closes the wrapper's group and
/// leaves a partial match -- and only asking before wrapping keeps the whole-input boundary intact.
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