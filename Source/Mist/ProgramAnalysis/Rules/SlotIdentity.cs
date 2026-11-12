using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed record UnprovenSlot(IMistType Source, IMistType Target, string Left, string Right, Proof? Proof = null);

/// <summary>
/// Whether a value's type and the type it meets are one type where they differ only in the values of
/// dependent slots, which <see cref="ConversionClassifier.AgreesUpToProof"/> admits and leaves to be
/// proven here (runtime-dependent-values Requirement 2). Each slot pair that is not one object is read
/// as two terms through <see cref="SlotReads"/> and the chain is asked to rule out their difference, so
/// <c>if(n == m)</c> proves <c>int[m]</c> to be an <c>int[n]</c> and the failing case names the program's
/// own values. Without a reading in hand there are no facts, and only the same capture, literal or
/// parameter is one value. A slot the target leaves <c>unknown</c> owes nothing.
/// </summary>
internal static class SlotIdentity
{
    public static UnprovenSlot? Judge(FactState state, Expression value, IMistType target, FactReading? reading = null, Budget? budget = null)
    {
        return Judge(state, SlotReads.Written(value).Type, target, value, reading, budget);
    }

    public static UnprovenSlot? Judge(FactState state, IMistType source, IMistType target, Expression? through, FactReading? reading = null, Budget? budget = null)
    {
        UnprovenSlot? first = null;
        var written = through is null ? null : SlotReads.Written(through);

        foreach(var constituent in Constituents(target))
        {
            if(SlotReads.Plain(constituent) is not ConstructedType closing || SlotReads.Closing(source, closing.Definition) is not { } reaching
                || reaching.TypeArguments.Count != closing.TypeArguments.Count)
            {
                continue;
            }

            UnprovenSlot? unproven = null;

            foreach(var pair in Pairs(reaching, closing))
            {
                var proof = Prove(ref state, pair, written, reading, budget);

                if(proof is not Proven)
                {
                    unproven = new UnprovenSlot(
                        Spelled(state, source), Spelled(state, constituent),
                        Shown(state, pair.Argument, pair.Parameter, written), Shown(state, pair.Slot, pair.Parameter, null), proof);
                    break;
                }
            }

            if(unproven is null)
            {
                return null;
            }

            first ??= unproven;
        }

        return first;
    }

    public static bool Owes(Expression? value, IMistType? target)
    {
        return value is not null && Owes(SlotReads.Written(value).Type, target);
    }

    public static bool Owes(IMistType? source, IMistType? target)
    {
        return source is not null && target is not null
            && Constituents(target).Any(constituent => SlotReads.Plain(constituent) is ConstructedType closing
                && SlotReads.Closing(source, closing.Definition) is { } reaching && reaching.TypeArguments.Count == closing.TypeArguments.Count
                && Pairs(reaching, closing).Any());
    }

    private static Proof? Prove(ref FactState state, Pair pair, Expression? through, FactReading? reading, Budget? budget)
    {
        if(reading is null)
        {
            return Same(pair.Argument, pair.Slot) ? new Proven() : null;
        }

        var left = SlotReads.Value(ref state, reading, pair.Argument, through, pair.Read ?? pair.Parameter);
        var right = SlotReads.Value(ref state, reading, pair.Slot, null, pair.Parameter);

        if(left is null || right is null)
        {
            return null;
        }

        var difference = left.Subtract(right);

        if(difference.Variables.IsEmpty)
        {
            return difference.Constant.IsZero ? new Proven() : new Refuted(Alternative.Empty, false);
        }

        return Chain.Prove(state.Bag, new Fact(difference, Relation.Equal).Denial(), difference.Variables.ToHashSet(), budget);
    }

    private static bool Same(IMistType argument, IMistType slot)
    {
        return (argument, slot) switch
        {
            (CapturedValue left, CapturedValue right) => ReferenceEquals(left.Capture, right.Capture),
            (LiftedType left, LiftedType right) => SlotReads.Constant(left) is { } mine && mine == SlotReads.Constant(right),
            _ => ReferenceEquals(argument, slot),
        };
    }

    private sealed record Pair(IMistType Argument, IMistType Slot, TypeParameter Parameter, TypeParameter? Read);

    private static IEnumerable<Pair> Pairs(ConstructedType source, ConstructedType target)
    {
        for(var i = 0; i < source.TypeArguments.Count && i < target.TypeArguments.Count; i++)
        {
            var argument = source.TypeArguments[i];
            var slot = target.TypeArguments[i];

            if(ReferenceEquals(argument, slot) || argument is ErrorType || slot is ErrorType)
            {
                continue;
            }

            if(ConversionClassifier.OwesProof(argument, slot, target.Definition, i))
            {
                if(slot is not (UnknownType or UnknownValueType))
                {
                    var ordinal = i;
                    yield return new Pair(
                        argument, slot,
                        ((IHasTypeParameters)target.Definition).TypeParametersList.First(parameter => parameter.Ordinal == ordinal),
                        (source.Definition as IHasTypeParameters)?.TypeParametersList.Find(parameter => parameter.Ordinal == ordinal));
                }
            }
            else if(SlotReads.Plain(argument) is ConstructedType inner && SlotReads.Plain(slot) is ConstructedType outer
                && SlotReads.Closing(inner, outer.Definition) is { } reaching && reaching.TypeArguments.Count == outer.TypeArguments.Count)
            {
                foreach(var nested in Pairs(reaching, outer))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IReadOnlyList<IMistType> Constituents(IMistType target)
    {
        return SlotReads.Plain(target) is UnionType union ? union.Constituents : [target];
    }

    private static string Shown(FactState state, IMistType slot, TypeParameter parameter, Expression? through)
    {
        return slot switch
        {
            CapturedValue captured => SlotReads.Known(state, captured) is { } known ? Spell(captured.Domain, known) : captured.WrittenName,
            LiftedType lifted => lifted.Name,
            UnknownType or UnknownValueType when through is not null => $"{RefinementFindings.Display(through)}.{parameter.Name}",
            _ => TypeDisplay.Mist(slot),
        };
    }

    private static IMistType Spelled(FactState state, IMistType type)
    {
        return type switch
        {
            CapturedValue captured when SlotReads.Known(state, captured) is { } known => new LiftedType(captured.Domain, Literal(captured.Domain, known)),
            ConstructedType constructed when constructed.TypeArguments.Any(argument => argument is CapturedValue captured && SlotReads.Known(state, captured) is not null)
                => new ConstructedType(constructed.Definition, [.. constructed.TypeArguments.Select(argument => Spelled(state, argument))], constructed.Enclosing),
            _ => type,
        };
    }

    private static string Spell(IMistType domain, BigInteger known)
    {
        return DependentDomains.Spell(Literal(domain, known));
    }

    private static object Literal(IMistType domain, BigInteger known)
    {
        return SlotReads.Plain(domain).FullyQualifiedName switch
        {
            "Standard.Boolean" => !known.IsZero,
            "Standard.Char" => (char)known,
            "Standard.UInt64" => (ulong)known,
            _ => (long)known,
        };
    }
}