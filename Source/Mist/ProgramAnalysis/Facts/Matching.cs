using System.Collections.Immutable;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Facts;

/// <summary>
/// Regex facts name full matches by the literal's body, from a value's type or a narrowing <c>is</c>.
/// Literals and matches stay with their own values; copies in each alternative expose them without
/// losing path-specific knowledge at joins.
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
        Bag bag, Value text, IMistType target, Func<Value, LiteralValue?> literal, Func<Value, IEnumerable<string>> matched, TimeSpan matchLimit)
    {
        if(bag.Unreachable)
        {
            return true;
        }

        var required = Required(target);
        foreach(var alternative in bag.Alternatives)
        {
            var equal = Equal(bag, alternative, text);
            if(!equal.Any(value => literal(value) is { } known && LiteralPredicates.Decide(target, known, matchLimit) == true) && (required is not { } patterns
                || !patterns.All(pattern => equal.Any(value => matched(value).Contains(pattern) || (Required(value.Type) ?? []).Contains(pattern)))))
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