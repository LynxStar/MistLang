using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

// Observed paths bound expansion, while unknown and hidden storage retain independent remainder slots.
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

    internal static bool IsEmpty(IMistType type)
    {
        return IsEmpty(type, new StorageRecursion());
    }

    private static bool IsEmpty(IMistType type, StorageRecursion visiting)
    {
        if(!IsValueStorage(type) || !visiting.Enter(Definition(type), type))
        {
            return false;
        }

        var layout = ValueStorage.Describe(type);
        var empty = layout.IsComplete
            && layout.Fields.All(field => IsEmpty(field.Type, visiting))
            && (layout.BaseType is not { } baseType || IsEmpty(baseType, visiting));
        visiting.Exit();
        return empty;
    }

    private static bool IsValueStorage(IMistType type)
    {
        return type is StructType or ConstructedType { Definition: StructType } or ITupleType;
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
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

        return receiver?.Fields?.GetValueOrDefault(path.Key!);
    }

    private sealed class Slot(IMistType type)
    {
        private readonly List<Slot> hidden = [];
        private readonly List<object> opaque = [];

        public Dictionary<object, Slot>? Fields { get; private set; }

        public void Expand()
        {
            if(Fields is not null || !IsValueStorage(type))
            {
                return;
            }

            Fields = [];
            Add(type, new StorageRecursion());
        }

        private void Add(IMistType candidate, StorageRecursion visiting)
        {
            if(!IsValueStorage(candidate) || !visiting.Enter(Definition(candidate), candidate))
            {
                opaque.Add(new object());
                return;
            }

            var layout = ValueStorage.Describe(candidate);
            for(var index = 0; index < layout.Fields.Count; index++)
            {
                var field = layout.Fields[index];
                var child = new Slot(field.Type);
                if(candidate is ITupleType)
                {
                    Fields!.Add(index, child);
                }
                else if(field.Field is not { } member || !Fields!.TryAdd(member, child))
                {
                    hidden.Add(child);
                }
            }

            if(!layout.IsComplete)
            {
                opaque.Add(new object());
            }

            if(layout.BaseType is { } baseType)
            {
                Add(baseType, visiting);
            }

            visiting.Exit();
        }

        public IEnumerable<object> Leaves()
        {
            if(Fields is null)
            {
                if(!IsEmpty(type))
                {
                    yield return this;
                }

                yield break;
            }

            foreach(var child in Fields.Values.Concat(hidden))
            {
                foreach(var leaf in child.Leaves())
                {
                    yield return leaf;
                }
            }

            foreach(var remainder in opaque)
            {
                yield return remainder;
            }
        }
    }
}