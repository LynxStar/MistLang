using System.Collections.Immutable;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Facts;

/// <summary>
/// Match facts: a string value fully matches a regex, named by the regex literal's body. A value's own type states the matches
/// its refinement names; a narrowing <c>is</c> states the rest. A match is proven when a value equal to the text holds a literal
/// <see cref="LiteralPredicates.Decide(IMistType, LiteralValue?)"/> accepts, or holds every match the target asks for. Where paths
/// that knew different things about a value join, each path's knowledge is kept as a <see cref="TextCase"/> and the value holds
/// at least one of them, so the match is proven when every case proves it.
/// </summary>
internal static class Matching
{
    public static ImmutableArray<string>? Required(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Required(aliased),
            RefinedType refined => Required(refined.BaseType) is { } beneath && Stated(refined.Predicate) is { } own ? beneath.AddRange(own) : null,
            _ => Requirement.Refined(type) is null ? [] : null,
        };
    }

    public static ImmutableArray<string>? Stated(Pattern? pattern)
    {
        switch(pattern)
        {
            case RegexPattern regex:
                return [regex.Pattern];

            case ConjunctionPattern conjunction:
                {
                    var all = ImmutableArray.CreateBuilder<string>();
                    foreach(var operand in conjunction.Operands)
                    {
                        if(Stated(operand) is not { } part)
                        {
                            return null;
                        }

                        all.AddRange(part);
                    }

                    return all.ToImmutable();
                }

            case TypeTestPattern typed:
                return Required(typed.NarrowedType) is { } named && (typed.Refinement is null ? [] : Stated(typed.Refinement)) is { } refined
                    ? named.AddRange(refined) : null;

            case CatchAllPattern:
                return [];

            default:
                return null;
        }
    }

    public static bool Proven(
        Bag bag, Value text, IMistType target, Func<Value, LiteralValue?> literal, Func<Value, IEnumerable<string>> matched, TimeSpan matchLimit,
        Func<Value, ImmutableArray<TextCase>>? cases = null)
    {
        if(bag.Unreachable)
        {
            return true;
        }

        var required = Required(target);
        foreach(var alternative in bag.Alternatives)
        {
            var equal = Equal(bag, alternative, text);
            bool holds(LiteralValue? known, IEnumerable<string> extra)
            {
                return (known is not null && LiteralPredicates.Decide(target, known, matchLimit) == true) || (required is { } patterns
                    && patterns.All(pattern => extra.Contains(pattern) || equal.Any(value => matched(value).Contains(pattern) || (Required(value.Type) ?? []).Contains(pattern))));
            }

            if(!equal.Any(value => holds(literal(value), [])) && (cases is null
                || !equal.Any(value => cases(value) is { IsEmpty: false } either && either.All(known => holds(known.Literal, known.Matches)))))
            {
                return false;
            }
        }

        return true;
    }

    private static List<Value> Equal(Bag bag, Alternative alternative, Value text)
    {
        var copies = bag.Relevant([text], alternative.Facts).Where(fact => fact is { Relation: Relation.Equal, Values: null } && fact.Term.Constant.IsZero
            && fact.Term.Multipliers is [var first, var second] && first == -second).ToList();
        var equal = new List<Value> { text };
        for(var i = 0; i < equal.Count; i++)
        {
            foreach(var copy in copies.Where(copy => copy.Term.Variables.Contains(equal[i])))
            {
                equal.AddRange(copy.Term.Variables.Where(other => !equal.Contains(other)));
            }
        }

        return equal;
    }
}

internal sealed record TextCase(LiteralValue? Literal, ImmutableHashSet<string> Matches)
{
    public bool Equals(TextCase? other)
    {
        return other is not null && Equals(Literal, other.Literal) && Matches.SetEquals(other.Matches);
    }

    public override int GetHashCode()
    {
        return Matches.Aggregate(Literal?.GetHashCode() ?? 0, (hash, pattern) => hash ^ pattern.GetHashCode(StringComparison.Ordinal));
    }
}