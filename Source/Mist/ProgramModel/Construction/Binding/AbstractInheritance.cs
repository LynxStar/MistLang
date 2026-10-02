using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal static class AbstractInheritance
{
    public static IEnumerable<(IMistType Type, AbstractType Definition, TypeSubstitution Substitution)> Reached(
        IEnumerable<IMistType> roots, TypeInterner? interner)
    {
        var queue = new Queue<(IMistType Named, ConstructedType? Root, IReadOnlyList<ConstructedType> Path, IReadOnlyList<AbstractType> Ancestors)>();
        var seen = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);

        foreach(var root in roots)
        {
            queue.Enqueue((root, root as ConstructedType, [], []));
        }

        while(queue.Count > 0)
        {
            var (named, root, path, ancestors) = queue.Dequeue();

            if((named is ConstructedType construction ? construction.Definition : named) is not AbstractType definition || ancestors.Contains(definition))
            {
                continue;
            }

            var substitution = TypeSubstitution.ForPath(root, path, interner);
            var closed = substitution.Apply(named, interner);

            if(!seen.Add(closed))
            {
                continue;
            }

            yield return (closed, definition, substitution);

            foreach(var inherited in definition.BaseAbstractTypes)
            {
                var inheritedPath = inherited is ConstructedType constructed ? [.. path, constructed] : path;
                queue.Enqueue((inherited, root, inheritedPath, [.. ancestors, definition]));
            }
        }
    }
}