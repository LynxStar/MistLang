using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

// Expand only observed field paths before solving, so the leaf universe is finite even for recursive
// layouts, and whole-value writes and individual field writes intersect in exactly the same slots.
internal sealed class LocalAssignmentSlots
{
    private readonly Dictionary<LocalVariable, Slot> roots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalStoragePath, IReadOnlyList<object>> reads = [];

    public LocalAssignmentSlots(IEnumerable<LocalVariable> locals, object body)
    {
        foreach(var local in locals)
        {
            var slot = new Slot(local.Type);
            slot.Expand();
            roots.Add(local, slot);
        }

        if(roots.Count == 0)
        {
            All = [];
            return;
        }

        foreach(var expression in BodyWalker.DescendantsAndSelf(body).OfType<Expression>())
        {
            if(LocalStoragePath.Of(expression) is { } path)
            {
                _ = Find(path, expand: true);
            }
        }

        All = roots.Values.SelectMany(slot => slot.Leaves()).ToArray();
    }

    public IReadOnlyList<object> All { get; }

    public bool Tracks(LocalStoragePath path)
    {
        return roots.ContainsKey(path.Local);
    }

    public IReadOnlyList<object> Required(LocalStoragePath? path)
    {
        if(path is null)
        {
            return [];
        }

        if(!reads.TryGetValue(path, out var required))
        {
            required = Find(path, expand: false) is { } slot ? slot.Leaves().ToArray() : Required(path.Receiver);
            reads.Add(path, required);
        }

        return required;
    }

    public IEnumerable<object> Written(LocalStoragePath path)
    {
        return Find(path, expand: false)?.Leaves() ?? [];
    }

    private Slot? Find(LocalStoragePath path, bool expand)
    {
        if(path.Receiver is null)
        {
            return roots.GetValueOrDefault(path.Local);
        }

        var receiver = Find(path.Receiver, expand);

        if(expand)
        {
            receiver?.Expand();
        }

        return receiver?.Fields?.GetValueOrDefault(path.Field!);
    }

    private sealed class Slot(IMistType type)
    {
        private readonly List<object> opaque = [];

        public Dictionary<Field, Slot>? Fields { get; private set; }

        public void Expand()
        {
            if(Fields is not null || Definition(type) is not { IsIntrinsic: false, DeclarationNode: not null })
            {
                return;
            }

            Fields = new Dictionary<Field, Slot>(ReferenceEqualityComparer.Instance);
            var visited = new HashSet<StructType>(ReferenceEqualityComparer.Instance);

            for(var current = type; Definition(current) is { } definition;)
            {
                if(definition.IsIntrinsic || definition.DeclarationNode is null || !visited.Add(definition))
                {
                    opaque.Add(new object());
                    break;
                }

                var substitution = TypeSubstitution.ForReceiver(current as ConstructedType);

                foreach(var field in definition.FieldsList.Where(field => !field.Modifiers.IsStatic))
                {
                    Fields.Add(field, new Slot(substitution.Apply(field.Type, interner: null)));
                }

                if(definition.PropertiesList.Any(property =>
                    !property.Modifiers.IsStatic && (property.Getter is { IsAuto: true } || property.Setter is { IsAuto: true })))
                {
                    opaque.Add(new object());
                }

                if(definition.BaseType is not { } baseType)
                {
                    break;
                }

                current = substitution.Apply(baseType, interner: null);
            }
        }

        public IEnumerable<object> Leaves()
        {
            if(Fields is null)
            {
                if(!Empty(type, []))
                {
                    yield return this;
                }

                yield break;
            }

            foreach(var child in Fields.Values)
            {
                foreach(var leaf in child.Leaves())
                {
                    yield return leaf;
                }
            }

            foreach(var hidden in opaque)
            {
                yield return hidden;
            }
        }

        private static bool Empty(IMistType candidate, HashSet<StructType> visiting)
        {
            if(Definition(candidate) is not { IsIntrinsic: false, DeclarationNode: not null } definition || !visiting.Add(definition))
            {
                return false;
            }

            var substitution = TypeSubstitution.ForReceiver(candidate as ConstructedType);
            var empty = !definition.PropertiesList.Any(property => !property.Modifiers.IsStatic
                    && (property.Getter is { IsAuto: true } || property.Setter is { IsAuto: true }))
                && definition.FieldsList.Where(field => !field.Modifiers.IsStatic)
                    .All(field => Empty(substitution.Apply(field.Type, interner: null), visiting))
                && (definition.BaseType is not { } baseType || Empty(substitution.Apply(baseType, interner: null), visiting));
            _ = visiting.Remove(definition);
            return empty;
        }

        private static StructType? Definition(IMistType candidate)
        {
            return (candidate is ConstructedType constructed ? constructed.Definition : candidate) as StructType;
        }
    }
}