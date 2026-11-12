using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed record UnprovenSlot(IMistType Source, IMistType Target, string Left, string Right);

/// <summary>
/// Whether a value's type and the type it meets are one type where they differ only in the values of
/// dependent slots, which <see cref="ConversionClassifier.AgreesUpToProof"/> admits and leaves to be
/// proven here. Only two slots that hold one value are proven: the same capture, two captures the
/// flow bound to one value because each read the same unchanged holder, or the <c>unknown</c> slot of
/// the value against a capture that was made by reading that very value's slot, which inference does
/// for an argument. A slot the target leaves <c>unknown</c> owes nothing.
/// </summary>
internal static class SlotIdentity
{
    public static UnprovenSlot? Judge(FactState state, Expression value, IMistType target)
    {
        return Judge(state, Written(value).Type, target, value);
    }

    public static UnprovenSlot? Judge(FactState state, IMistType source, IMistType target, Expression? through)
    {
        UnprovenSlot? first = null;

        foreach(var constituent in Constituents(target))
        {
            if(Plain(constituent) is not ConstructedType closing || Reaching(source, closing, []) is not { } reaching)
            {
                continue;
            }

            var unproven = Pairs(reaching, closing)
                .Where(pair => !Proven(state, pair.Argument, pair.Slot, through))
                .Select(pair => new UnprovenSlot(source, constituent, Shown(pair.Argument, pair.Parameter, through), Shown(pair.Slot, pair.Parameter, null)))
                .FirstOrDefault();

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
        return value is not null && target is not null
            && Constituents(target).Any(constituent => Plain(constituent) is ConstructedType closing
                && Reaching(Written(value).Type, closing, []) is { } reaching && Pairs(reaching, closing).Any());
    }

    private static bool Proven(FactState state, IMistType argument, IMistType slot, Expression? through)
    {
        if(argument is UnknownType or UnknownValueType)
        {
            return through is not null
                && slot is CapturedValue { Capture.Initializer: MemberAccessExpression { Member: TypeResult, Receiver: var read } }
                && ReferenceEquals(Written(read), Written(through));
        }

        return argument is CapturedValue left && slot is CapturedValue right
            && (ReferenceEquals(left.Capture, right.Capture)
                || (state.Bindings.Get(left.Capture) is { } mine && ReferenceEquals(mine, state.Bindings.Get(right.Capture))));
    }

    private static IEnumerable<(IMistType Argument, IMistType Slot, TypeParameter Parameter)> Pairs(ConstructedType source, ConstructedType target)
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
                    yield return (argument, slot, ((IHasTypeParameters)target.Definition).TypeParametersList.First(parameter => parameter.Ordinal == i));
                }
            }
            else if(Plain(argument) is ConstructedType inner && Plain(slot) is ConstructedType outer && SameDefinition(inner, outer))
            {
                foreach(var nested in Pairs(inner, outer))
                {
                    yield return nested;
                }
            }
        }
    }

    private static ConstructedType? Reaching(IMistType source, ConstructedType closing, HashSet<string> visited)
    {
        var from = Plain(source);

        if(from is ConstructedType constructed && SameDefinition(constructed, closing))
        {
            return constructed;
        }

        return ConversionRelation.Bases(from)
            .Where(wider => visited.Add(wider.FullyQualifiedName))
            .Select(wider => Reaching(wider, closing, visited))
            .FirstOrDefault(found => found is not null);
    }

    private static bool SameDefinition(ConstructedType left, ConstructedType right)
    {
        return (ReferenceEquals(left.Definition, right.Definition) || ConversionRelation.IsIdentity(left.Definition, right.Definition))
            && left.TypeArguments.Count == right.TypeArguments.Count;
    }

    private static IReadOnlyList<IMistType> Constituents(IMistType target)
    {
        return Plain(target) is UnionType union ? union.Constituents : [target];
    }

    private static IMistType Plain(IMistType type)
    {
        return Requirement.Unrefined(type);
    }

    private static Expression Written(Expression value)
    {
        return value switch
        {
            ImplicitConversionExpression conversion => Written(conversion.Operand),
            CloneExpression clone => Written(clone.Source),
            _ => value,
        };
    }

    private static string Shown(IMistType slot, TypeParameter parameter, Expression? through)
    {
        return slot switch
        {
            CapturedValue captured => captured.WrittenName,
            UnknownType or UnknownValueType when through is not null => $"{RefinementFindings.Display(Written(through))}.{parameter.Name}",
            _ => TypeDisplay.Mist(slot),
        };
    }
}