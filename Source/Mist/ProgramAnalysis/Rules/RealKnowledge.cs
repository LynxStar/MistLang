using System.Collections.Immutable;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal static class RealKnowledge
{
    public static RealSet? Of(ImmutableDictionary<object, Knowledge> state, Expression value)
    {
        if(RealRequirement.KindOf(value.Type) is not { } kind)
        {
            return null;
        }

        var declared = Trusts(value) ? RealRequirement.OfType(value.Type) : RealSet.Everything(kind);
        var known = RealRequirement.OfValue(value, local => Held(state, local));
        return known is null ? declared : declared is null ? known : known.And(declared);
    }

    public static RealSet? Assigned(ImmutableDictionary<object, Knowledge> state, Expression value, IMistType target)
    {
        if(RealRequirement.KindOf(target) is not { } kind)
        {
            return null;
        }

        var literal = RealRequirement.LiteralOf(value, local => state.TryGetValue(local, out var held) ? held.Literal : null);
        return literal is not null ? RealSet.Exactly(kind, literal) : Of(state, value)?.ConvertedTo(kind);
    }

    public static ImmutableDictionary<object, Knowledge> Compared(
        ImmutableDictionary<object, Knowledge> state, LocalVariable subject, Expression bound, ComparisonKind kind, bool holds)
    {
        if(RealRequirement.KindOf(subject.Type) is not { } domain
            || RealRequirement.LiteralOf(bound, local => state.TryGetValue(local, out var held) ? held.Literal : null) is not { } literal
            || RealSet.Compare(domain, kind, literal) is not { } admitted)
        {
            return state;
        }

        return Narrow(state, subject, holds ? admitted : admitted.Not());
    }

    public static ImmutableDictionary<object, Knowledge> Narrow(
        ImmutableDictionary<object, Knowledge> state, LocalVariable subject, RealSet admitted)
    {
        var known = state.TryGetValue(subject, out var held) ? held : Knowledge.Unknown;
        var before = Held(state, subject) ?? RealSet.Everything(admitted.Kind);
        return state.SetItem(subject, known with { Real = before.And(admitted) });
    }

    public static bool Satisfied(RealSet? known, IMistType target)
    {
        return known is not null && RealRequirement.OfType(target) is { } required
            && known.ConvertedTo(required.Kind) is { } converted && converted.IsSubsetOf(required);
    }

    private static RealSet? Held(ImmutableDictionary<object, Knowledge> state, LocalVariable local)
    {
        var declared = RealRequirement.OfType(local.Type);
        var known = state.TryGetValue(local, out var held) ? held.Real : null;
        return known is null ? declared : declared is null ? known : known.And(declared);
    }

    private static bool Trusts(Expression value)
    {
        return value switch
        {
            UnaryExpression { Operator.Modifiers.IsIntrinsic: false } => true,
            UnaryExpression or BinaryExpression => false,
            CloneExpression clone => Trusts(clone.Source),
            _ => true,
        };
    }
}