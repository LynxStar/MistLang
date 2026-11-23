using System.Globalization;
using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed record UnprovenSlot(IMistType Source, IMistType Target, string Left, string Right, Proof? Proof = null, string? Refinement = null);

/// <summary>
/// How a site meets the slots of the type it expects. By default a slot is met by refinement. A site
/// whose capture stands for one value, a call's own or a creation's own, meets it by equality (<see cref="Exact"/>),
/// and a site the other side can write through, a <c>ref</c> or <c>out</c> argument or a <c>ref</c> local,
/// by refinement equality (<see cref="Alike"/>), and by equality as well where the slot is exact. A member
/// reached through a value the binder gave no read of its own slot still stands for that value's slot
/// (Requirement 7.6), so a slot the <see cref="Receiver"/>'s type closes it with is met by equality with the
/// read through the receiver as evaluated. A slot nested in a type argument meets by refinement equality, and
/// by equality as well where it is exact, unless its capture was written inside a type the site states
/// (<see cref="Inner"/>), as <c>First&lt;Ring&lt;n&gt;&gt;</c> writes <c>n</c>, which stands for the refinement.
/// </summary>
internal sealed record SlotMeeting(IReadOnlyCollection<SlotCapture> Exact, bool Alike = false, Expression? Receiver = null)
{
    private static readonly HashSet<SlotCapture> NoValues = new(ReferenceEqualityComparer.Instance);

    private static readonly Dictionary<SlotCapture, TypeParameter> NoneOwned = new(ReferenceEqualityComparer.Instance);

    public static readonly SlotMeeting Fit = new([]);

    public static readonly SlotMeeting Equal = new([], Alike: true);

    public IReadOnlyCollection<SlotCapture> Inner { get; init; } = [];

    public IReadOnlySet<SlotCapture> Values { get; init; } = NoValues;

    public IMistType? Declared { get; init; }

    public IReadOnlyDictionary<SlotCapture, TypeParameter> Owned { get; init; } = NoneOwned;

    public IReadOnlyDictionary<SlotCapture, TypeParameter> Reached { get; init; } = NoneOwned;

    public bool OneValue(IMistType argument)
    {
        return SlotReads.StandsForOneValue(argument) || (argument is CapturedValue captured && Values.Contains(captured.Capture));
    }

    public static IReadOnlyCollection<SlotCapture> Inside(IReadOnlyCollection<SlotCapture> exact, IEnumerable<IMistType>? stated)
    {
        var written = stated?.Where(argument => argument is not CapturedValue).ToList() ?? [];

        return written.Count == 0 ? [] : [.. exact.Where(capture => written.Exists(argument => CaptureScope.Mentions(argument, capture)))];
    }

    public bool IsExact(IMistType slot)
    {
        return (slot is CapturedValue captured && Exact.Any(capture => ReferenceEquals(capture, captured.Capture))) || ReceiverSlot(slot) is not null;
    }

    public bool HoldsNested(IMistType slot)
    {
        return IsExact(slot) && !(slot is CapturedValue captured && Inner.Any(capture => ReferenceEquals(capture, captured.Capture)));
    }

    public TypeParameter? ReceiverSlot(IMistType slot)
    {
        if(slot is CapturedValue marked && Owned.TryGetValue(marked.Capture, out var owned))
        {
            return owned;
        }

        if(Receiver is null or TypeReferenceExpression or ThisExpression || slot is not (CapturedValue or RefinementArgument)
            || SlotReads.Plain(Receiver.Type) is not ConstructedType { Definition: ClassType or StructType } closing || closing.Definition is not IHasTypeParameters declared)
        {
            return null;
        }

        return declared.TypeParametersList.Find(parameter => parameter is { IsDependent: true, IsPack: false }
            && parameter.Ordinal < closing.TypeArguments.Count && ReferenceEquals(closing.TypeArguments[parameter.Ordinal], slot));
    }
}

/// <summary>
/// Whether a value's type fits the type it meets where they differ only in dependent slots, which
/// <see cref="ConversionClassifier.AgreesUpToProof"/> admits and leaves to be proven here
/// (runtime-dependent-values Requirements 2 and 8.4). The source's slot is read through
/// <see cref="SlotReads"/>, through the value's own holder where there is a value, and the chain is asked
/// whether it lies inside the target slot's refinement: a capture's kept refinement or a refinement
/// argument's predicate. A literal, a dependent parameter and a capture <see cref="SlotMeeting.Exact"/>
/// stand for one value and are met by equality. A pair nested in a type argument, and every pair of an
/// <see cref="SlotMeeting.Alike"/> site, is met by refinement equality, since a type argument never
/// widens. Without a value the source slot is only what its type admits; without a reading there are no
/// facts and only the same slot argument fits. A slot the target leaves <c>unknown</c> owes nothing.
/// </summary>
internal static class SlotIdentity
{
    public static UnprovenSlot? Judge(FactState state, Expression value, IMistType target, FactReading? reading = null, Budget? budget = null, SlotMeeting? meeting = null)
    {
        return Judge(state, SlotReads.Written(value).Type, target, value, reading, budget, meeting);
    }

    public static UnprovenSlot? Judge(
        FactState state, IMistType source, IMistType target, Expression? through, FactReading? reading = null, Budget? budget = null, SlotMeeting? meeting = null)
    {
        UnprovenSlot? first = null;
        var written = through is null ? null : SlotReads.Written(through);
        (source, target, meeting) = Own(source, target, through, meeting ?? SlotMeeting.Fit);

        foreach(var constituent in Constituents(target))
        {
            if(SlotReads.Plain(constituent) is not ConstructedType closing || SlotReads.Closing(source, closing.Definition) is not { } reaching
                || reaching.TypeArguments.Count != closing.TypeArguments.Count)
            {
                continue;
            }

            UnprovenSlot? unproven = null;

            foreach(var pair in Pairs(reaching, closing, meeting))
            {
                var via = pair.Nested || (meeting.Alike && !meeting.IsExact(pair.Slot)) ? null : written;
                var (proof, refinement) = Prove(ref state, pair, via, reading, budget, meeting);

                if(proof is not Proven)
                {
                    unproven = new UnprovenSlot(
                        Spelled(state, source), Spelled(state, constituent),
                        Shown(state, pair.Argument, pair.Parameter, via),
                        meeting.ReceiverSlot(pair.Slot) is { } own ? $"{RefinementFindings.Display(meeting.Receiver!)}.{own.Name}" : Shown(state, pair.Slot, pair.Parameter, null),
                        proof, refinement);
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

    public static bool Compares(IMistType source, IMistType tested)
    {
        return SlotReads.Plain(tested) is ConstructedType closing && SlotReads.Closing(source, closing.Definition) is { } reaching
            && reaching.TypeArguments.Count == closing.TypeArguments.Count
            && Pairs(reaching, closing).Any(pair => !Same(pair.Argument, pair.Slot));
    }

    public static bool OwesThrough(Expression? receiver, IMistType? target, IMistType? declared = null)
    {
        if(target is null)
        {
            return false;
        }

        var (_, marked, meeting) = Own(target, target, null, new SlotMeeting([], Receiver: receiver) { Declared = declared });

        return Constituents(marked).Any(constituent => Reaches(meeting, constituent));
    }

    public static Expression? Closer(BinaryExpression operation)
    {
        return operation is { Operator: { Modifiers.IsIntrinsic: false } declared, ParameterTypes: { } types }
            ? new[] { operation.Left, operation.Right }.FirstOrDefault(operand => types.Where((type, i) => OwesThrough(operand, type, Declared(declared, i))).Any())
            : null;
    }

    public static IMistType? Declared(Method? member, int ordinal)
    {
        return member is not null && ordinal < member.ParametersList.Count ? member.ParametersList[ordinal].Type : null;
    }

    // A value reached through a member is given its receiver's own slots only where the site meets one, so every other site reads it as before.
    private static (IMistType Source, IMistType Target, SlotMeeting Meeting) Own(IMistType source, IMistType target, Expression? through, SlotMeeting meeting)
    {
        if(meeting is { Declared: null } or { Receiver: null })
        {
            return (source, target, meeting);
        }

        var owned = new Dictionary<SlotCapture, TypeParameter>(ReferenceEqualityComparer.Instance);
        target = OwnSlots.Mark(meeting.Receiver, meeting.Declared, target, owned);

        if(owned.Count == 0)
        {
            return (source, target, meeting);
        }

        var reached = new Dictionary<SlotCapture, TypeParameter>(ReferenceEqualityComparer.Instance);

        if(through is not null && OwnSlots.Reached(through) is var (receiver, declared))
        {
            source = OwnSlots.Mark(receiver, declared, source, reached);
        }

        return (source, target, meeting with { Owned = owned, Reached = reached });
    }

    private static bool Reaches(SlotMeeting meeting, IMistType type)
    {
        return SlotReads.Plain(type) is ConstructedType closing && closing.TypeArguments.Any(argument => meeting.ReceiverSlot(argument) is not null || Reaches(meeting, argument));
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

    private static (Proof? Proof, string? Refinement) Prove(ref FactState state, Pair pair, Expression? through, FactReading? reading, Budget? budget, SlotMeeting meeting)
    {
        if(ReadsItself(pair, through) || Refused(pair.Argument, pair.Parameter) || Refused(pair.Slot, pair.Parameter)
            || (Same(pair.Argument, pair.Slot) && meeting.ReceiverSlot(pair.Slot) is null))
        {
            return (new Proven(), null);
        }

        if(reading is null)
        {
            return (null, null);
        }

        var held = pair.Nested ? meeting.HoldsNested(pair.Slot) : meeting.IsExact(pair.Slot);

        if((pair.Nested || meeting.Alike) && Alike(ref state, pair, reading, budget, meeting) is var alike && (!held || alike is not Proven))
        {
            return (alike, null);
        }

        var read = pair.Read ?? pair.Parameter;
        var left = pair.Argument is CapturedValue reached && meeting.Reached.ContainsKey(reached.Capture)
            ? reading.Read(ref state, reached.Capture.Initializer)
            : through is null && !meeting.OneValue(pair.Argument) ? null : SlotReads.Value(ref state, reading, pair.Argument, through, read);

        if(SlotReads.StandsForOneValue(pair.Slot) || meeting.IsExact(pair.Slot) || SlotReads.Refinement(state, pair.Slot) is not { } admitted)
        {
            var right = meeting.ReceiverSlot(pair.Slot) is { } own
                ? reading.Read(ref state, SlotReads.Read(meeting.Receiver!, own))
                : SlotReads.Value(ref state, reading, pair.Slot, null, pair.Parameter);

            return (right is null ? null : left is not null ? Equal(state, left, right, budget) : Single(ref state, Admits(state, pair.Argument, pair.Parameter), right, budget), null);
        }

        var shown = Describe(Domain(pair.Parameter, pair.Slot), admitted);

        if(left is null)
        {
            return (Admits(state, pair.Argument, pair.Parameter).IsSubsetOf(admitted) ? new Proven() : null, shown);
        }

        if(left.Variables.IsEmpty)
        {
            return (left.Constant >= long.MinValue && left.Constant <= long.MaxValue && admitted.Contains((long)left.Constant) ? new Proven() : new Refuted(Alternative.Empty, false), shown);
        }

        return (Chain.Prove(state.Bag, [.. ContractReader.Intervals(admitted.Not(), left)], left.Variables.ToHashSet(), budget), shown);
    }

    private static string Describe(IMistType domain, Admitted admitted)
    {
        var whole = SlotReads.Typed(domain);
        var (floor, ceiling) = (whole.Low ?? long.MinValue, whole.High ?? long.MaxValue);
        var parts = admitted.Intervals.Select(interval => interval.Low == interval.High
            ? string.Create(CultureInfo.InvariantCulture, $"== {interval.Low}")
            : string.Join(" and ", new[]
            {
                interval.Low > floor ? string.Create(CultureInfo.InvariantCulture, $">= {interval.Low}") : null,
                interval.High < ceiling ? string.Create(CultureInfo.InvariantCulture, $"<= {interval.High}") : null,
            }.OfType<string>())).ToList();

        return parts.All(part => part.Length > 0) && parts.Count > 0 ? $"{TypeDisplay.Mist(domain)} {string.Join(" or ", parts)}" : TypeDisplay.Mist(domain);
    }

    private static Proof Equal(FactState state, Term left, Term right, Budget? budget)
    {
        var difference = left.Subtract(right);

        if(difference.Variables.IsEmpty)
        {
            return difference.Constant.IsZero ? new Proven() : new Refuted(Alternative.Empty, false);
        }

        return Chain.Prove(state.Bag, new Fact(difference, Relation.Equal).Denial(), difference.Variables.ToHashSet(), budget);
    }

    private static Proof? Single(ref FactState state, Admitted refinement, Term value, Budget? budget)
    {
        return refinement.Intervals is [var only] && only.Low == only.High ? Equal(state, new Term(only.Low), value, budget) : null;
    }

    private static Proof? Alike(ref FactState state, Pair pair, FactReading reading, Budget? budget, SlotMeeting meeting)
    {
        var argument = meeting.OneValue(pair.Argument) && pair.Argument is not LiftedType ? SlotReads.Value(ref state, reading, pair.Argument, null, pair.Read ?? pair.Parameter) : null;
        var slot = meeting.OneValue(pair.Slot) && pair.Slot is not LiftedType ? SlotReads.Value(ref state, reading, pair.Slot, null, pair.Parameter) : null;

        return (argument, slot) switch
        {
            ({ } left, { } right) => Equal(state, left, right, budget),
            ({ } left, null) => Single(ref state, Admits(state, pair.Slot, pair.Parameter), left, budget),
            (null, { } right) => Single(ref state, Admits(state, pair.Argument, pair.Parameter), right, budget),
            _ => Admits(state, pair.Argument, pair.Parameter) is var mine && Admits(state, pair.Slot, pair.Parameter) is var theirs
                && mine.IsSubsetOf(theirs) && theirs.IsSubsetOf(mine) ? new Proven() : null,
        };
    }

    private static Admitted Admits(FactState state, IMistType argument, TypeParameter parameter)
    {
        return SlotReads.Refinement(state, argument) ?? SlotReads.Typed(Domain(parameter, argument));
    }

    private static IMistType Domain(TypeParameter parameter, IMistType argument)
    {
        return argument switch
        {
            CapturedValue captured => captured.Domain,
            UnknownValueType unknown => unknown.Domain,
            _ => parameter.Domain ?? argument,
        };
    }

    // A slot inferred from an argument's unknown one is a capture of that argument's own read, the same value
    // however often the argument would run if read again.
    private static bool ReadsItself(Pair pair, Expression? through)
    {
        return pair is { Argument: UnknownType or UnknownValueType, Slot: CapturedValue captured }
            && through is not null
            && SlotReads.Written(captured.Capture.Initializer) is MemberAccessExpression { Member: TypeResult { Type: TypeParameter read }, Receiver: { } receiver }
            && ReferenceEquals(read, pair.Read ?? pair.Parameter)
            && ReferenceEquals(SlotReads.Written(receiver), through);
    }

    private static bool Same(IMistType argument, IMistType slot)
    {
        return (argument, slot) switch
        {
            (CapturedValue left, CapturedValue right) => ReferenceEquals(left.Capture, right.Capture),
            (LiftedType left, LiftedType right) => SlotReads.Constant(left) is { } mine && mine == SlotReads.Constant(right),
            (RefinementArgument left, RefinementArgument right) => ConversionRelation.IsIdentity(left.Refinement, right.Refinement)
                && (left.Contract is null) == (right.Contract is null) && (left.Contract is null || ConversionRelation.IsIdentity(left.Contract, right.Contract!)),
            _ => ReferenceEquals(argument, slot),
        };
    }

    // A length closed with a parameter of another type is refused once, at its method's signature.
    private static bool Refused(IMistType closing, TypeParameter parameter)
    {
        return closing is ParameterValue held && !RefinementAnalyzer.Admits(held, parameter);
    }

    private sealed record Pair(IMistType Argument, IMistType Slot, TypeParameter Parameter, TypeParameter? Read, bool Nested = false);

    private static IEnumerable<Pair> Pairs(ConstructedType source, ConstructedType target, SlotMeeting? meeting = null)
    {
        for(var i = 0; i < source.TypeArguments.Count && i < target.TypeArguments.Count; i++)
        {
            var argument = source.TypeArguments[i];
            var slot = target.TypeArguments[i];
            var own = meeting?.ReceiverSlot(slot) is not null;

            if((ReferenceEquals(argument, slot) && !own && (meeting is null || !Reaches(meeting, slot))) || argument is ErrorType || slot is ErrorType)
            {
                continue;
            }

            if(own || ConversionClassifier.OwesProof(argument, slot, target.Definition, i))
            {
                if(slot is not (UnknownType or UnknownValueType) or RefinementArgument)
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
                foreach(var nested in Pairs(reaching, outer, meeting))
                {
                    yield return nested with { Nested = true };
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
            ParameterValue held => held.Parameter.Name,
            UnknownType or UnknownValueType when through is not null => $"{RefinementFindings.Display(through)}.{parameter.Name}",
            _ => TypeDisplay.Mist(slot),
        };
    }

    private static IMistType Spelled(FactState state, IMistType type)
    {
        return type switch
        {
            CapturedValue captured when SlotReads.Known(state, captured) is { } known => new LiftedType(captured.Domain, Literal(captured.Domain, known)),
            ConstructedType constructed when constructed.TypeArguments.Select(argument => Spelled(state, argument)).ToList() is var arguments
                && arguments.Where((argument, i) => !ReferenceEquals(argument, constructed.TypeArguments[i])).Any()
                => new ConstructedType(constructed.Definition, arguments, constructed.Enclosing),
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