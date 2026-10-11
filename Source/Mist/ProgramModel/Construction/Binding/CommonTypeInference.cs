using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal static class CommonTypeInference
{
    public static IMistType? Infer(
        IReadOnlyList<Expression> values, Func<WellKnownType, IMistType> wellKnown, TypeInterner? interner = null)
    {
        var sound = values.Where(value => !PriorFailure.Of(value)).ToList();
        if(sound.Count == 0 || sound.Any(value => value.Type is ErrorType))
        {
            return ErrorType.Instance;
        }

        var first = sound[0].Type;
        if(sound.All(value => ReferenceEquals(first, value.Type)))
        {
            return first;
        }

        var candidates = new List<IMistType>();
        var sources = sound.Select(value => ConversionClassifier.Normalized(value.Type, interner)).ToList();
        var declaredTargets = sources.SelectMany(ConversionRelation.DeclaredOn)
            .Where(conversion => conversion.Operator.IsImplicit).Select(conversion => conversion.Result);
        var pending = new Queue<IMistType>(sources.Concat(declaredTargets));
        while(pending.TryDequeue(out var candidate))
        {
            candidate = ConversionClassifier.Normalized(candidate, interner);
            if(candidate is ErrorType || candidates.Any(existing => Same(existing, candidate)))
            {
                continue;
            }

            candidates.Add(candidate);
            foreach(var wider in Wider(candidate))
            {
                pending.Enqueue(wider);
            }

            foreach(var primitive in WellKnownTypes.PrimitiveTypes)
            {
                if(ConversionRelation.WidensNumerically(candidate.FullyQualifiedName, primitive.FullyQualifiedName))
                {
                    pending.Enqueue(wellKnown(primitive));
                }
            }
        }

        var applicable = candidates.Where(candidate => sound.All(value => Accepts(value.Type, candidate, interner))).ToList();
        var best = applicable.Where(candidate => !applicable.Any(other =>
            ConversionClassifier.IsFree(other, candidate, interner)
                && !ConversionClassifier.IsFree(candidate, other, interner))).ToList();

        return best.Count == 1 ? best[0] : null;
    }

    public static (IMistType First, IMistType Other) Disagreement(IReadOnlyList<Expression> values)
    {
        var sound = values.Where(value => !PriorFailure.Of(value)).ToList();
        var first = sound[0].Type;
        return (first, sound.First(value => !Same(first, value.Type)).Type);
    }

    private static bool Same(IMistType left, IMistType right)
    {
        return ReferenceEquals(left, right) || ConversionRelation.IsIdentity(left, right);
    }

    private static bool Accepts(IMistType source, IMistType target, TypeInterner? interner)
    {
        return Same(source, target) || ConversionClassifier.Classify(source, target, interner) is ConversionKind.Identity or ConversionKind.Implicit;
    }

    private static IEnumerable<IMistType> Wider(IMistType type)
    {
        return type switch
        {
            TypeParameter parameter => parameter.ConstraintsList.OfType<TypeConstraint>().Select(constraint => constraint.ConstraintType),
            RefinedType refined => [refined.BaseType],
            EnumType { UnderlyingType: { } underlying } => [underlying],
            _ => ConversionRelation.Bases(type),
        };
    }
}