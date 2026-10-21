using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

// Repeated definitions must descend into a previous construction's arguments, a finite tree.
// Identical or expanding constructions cannot establish complete recursive value storage.
internal sealed class StorageRecursion
{
    private readonly List<Visit> visiting = [];

    public bool Enter(IMistType definition, IMistType instance)
    {
        for(var i = visiting.Count - 1; i >= 0; i--)
        {
            if(!ReferenceEquals(visiting[i].Definition, definition))
            {
                continue;
            }

            if(visiting[i].Instance is not ConstructedType previous
                || !Children(previous).Any(child => Contains(child, instance, [])))
            {
                return false;
            }

            break;
        }

        visiting.Add(new Visit(definition, instance));
        return true;
    }

    public void Exit()
    {
        visiting.RemoveAt(visiting.Count - 1);
    }

    private static bool Contains(IMistType container, IMistType instance, HashSet<IMistType> visited)
    {
        return Same(container, instance)
            || (visited.Add(container) && Children(container).Any(child => Contains(child, instance, visited)));
    }

    private static IEnumerable<IMistType> Children(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => constructed.Enclosing is { } enclosing
                ? constructed.TypeArguments.Append(enclosing)
                : constructed.TypeArguments,
            ITupleType tuple => tuple.ElementTypes,
            TypeAlias { AliasedType: { } aliased } => [aliased],
            LiftedType { Value: TypePackValue pack } => pack.Elements,
            _ => [],
        };
    }

    private static bool Same(IMistType left, IMistType right)
    {
        if(left is NamedTupleType leftTuple)
        {
            left = leftTuple.Underlying;
        }

        if(right is NamedTupleType rightTuple)
        {
            right = rightTuple.Underlying;
        }

        if(ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (ConstructedType first, ConstructedType second) => ReferenceEquals(first.Definition, second.Definition)
                && SameElements(first.TypeArguments, second.TypeArguments)
                && SameEnclosing(first.Enclosing, second.Enclosing),
            (ITupleType first, ITupleType second) => SameElements(first.ElementTypes, second.ElementTypes),
            (LiftedType { Value: TypePackValue first } firstLifted, LiftedType { Value: TypePackValue second } secondLifted) =>
                Same(firstLifted.Type, secondLifted.Type) && SameElements(first.Elements, second.Elements),
            (LiftedType first, LiftedType second) => Same(first.Type, second.Type) && Equals(first.Value, second.Value),
            _ => false,
        };
    }

    private static bool SameEnclosing(ConstructedType? left, ConstructedType? right)
    {
        return left is null || right is null ? ReferenceEquals(left, right) : Same(left, right);
    }

    private static bool SameElements(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second));
    }

    private sealed record Visit(IMistType Definition, IMistType Instance);
}