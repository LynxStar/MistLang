using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

internal enum OwnedCopyKind
{
    Share,
    Clone,
    Struct,
    Tuple,
    Union,
    Generic,
}

internal sealed record OwnedCopy(IMistType Type, OwnedCopyKind Kind, IReadOnlyList<OwnedCopySlot> Slots, IReadOnlyList<OwnedCopy> Elements, CloneRefusal? Refusal = null)
{
    public bool Required => Kind is OwnedCopyKind.Clone or OwnedCopyKind.Generic || Slots.Any(slot => slot.Copy.Required) || Elements.Any(element => element.Required);
}

internal sealed record OwnedCopySlot(InstanceStorage Storage, OwnedCopy Copy);

/// <summary>
/// An owned destination constructs a struct from its fields: plain objects share, but owned objects
/// are copied even beneath plain structs and tuples. This is independent of whole-graph Clone derivation.
/// </summary>
internal static class OwnedStructCopies
{
    public static OwnedCopy Of(IMistType type, CloneDerivation clones)
    {
        return Read(type, clones, owned: false, []);
    }

    public static bool HoldsOwnedObjects(IMistType type)
    {
        type = Unwrapped(type);
        return type is StructType or ITupleType or UnionType or ConstructedType { Definition: StructType }
            && Of(type, new CloneDerivation([])).Required;
    }

    private static OwnedCopy Read(IMistType type, CloneDerivation clones, bool owned, HashSet<string> visiting)
    {
        type = Unwrapped(type);
        var verdict = clones.Judge(type);
        if(verdict.Kind == CloneKind.Unchangeable)
        {
            return Share(type);
        }

        if(type is INamedTupleType named)
        {
            return Read(named.Underlying, clones, owned, visiting);
        }

        if(type is ITupleType tuple)
        {
            return Aggregate(type, OwnedCopyKind.Tuple, tuple.ElementTypes.Select(element => Read(element, clones, owned, visiting)).ToArray());
        }

        if(type is UnionType union)
        {
            return Aggregate(type, OwnedCopyKind.Union, union.Constituents.Select(element => Read(element, clones, owned, visiting)).ToArray());
        }

        if(owned)
        {
            return new OwnedCopy(type, OwnedCopyKind.Clone, [], [], verdict.Refusal);
        }

        if((type is ConstructedType constructed ? constructed.Definition : type) is not StructType structure
            || !CloneDerivation.IsInMist(structure) || !visiting.Add(type.FullyQualifiedName))
        {
            return Share(type);
        }

        var slots = new List<OwnedCopySlot>();
        var substitution = TypeSubstitution.ForReceiver(type as ConstructedType);
        var bases = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);

        for(ConcreteType? current = structure; current is not null && bases.Add(current);)
        {
            foreach(var storage in InstanceStorage.Of(current))
            {
                var owns = storage.Field is { } field ? OwnedProvenance.IsOwned(field) : OwnedProvenance.IsOwned(storage.Property!) || current.IsOwned;
                var copy = Read(substitution.Apply(storage.Type, interner: null), clones, owns, visiting);
                if(copy.Required)
                {
                    var refusal = copy.Refusal is { } nested
                        ? new CloneRefusal($"{(storage.Property is null ? "field" : "property")} '{storage.Name}' of '{TypeDisplay.Mist(current)}' "
                            + (owns ? $"is owned but holds '{TypeDisplay.Mist(copy.Type)}', which cannot be cloned ({nested.Why})" : $"cannot be copied ({nested.Why})"), nested.Remedied)
                        : null;
                    slots.Add(new OwnedCopySlot(storage, copy with { Refusal = refusal }));
                }
            }

            var baseType = current.BaseType is { } written ? substitution.Apply(written, interner: null) : null;
            substitution = TypeSubstitution.ForReceiver(baseType as ConstructedType);
            current = (baseType is ConstructedType parent ? parent.Definition : baseType) as ConcreteType;
        }

        _ = visiting.Remove(type.FullyQualifiedName);
        return new OwnedCopy(type, OwnedCopyKind.Struct, slots, [], slots.Select(slot => slot.Copy.Refusal).FirstOrDefault(reason => reason is not null));
    }

    private static OwnedCopy Aggregate(IMistType type, OwnedCopyKind kind, IReadOnlyList<OwnedCopy> elements)
    {
        return new OwnedCopy(type, kind, [], elements, elements.Select(element => element.Refusal).FirstOrDefault(reason => reason is not null));
    }

    private static OwnedCopy Share(IMistType type)
    {
        return new OwnedCopy(type, OwnedCopyKind.Share, [], []);
    }

    private static IMistType Unwrapped(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Unwrapped(aliased),
            RefinedType refined => Unwrapped(refined.BaseType),
            _ => type,
        };
    }
}