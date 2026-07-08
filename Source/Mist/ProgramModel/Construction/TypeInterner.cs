using System.Runtime.CompilerServices;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Interns synthetic type instances (unions, constructed types) so that structurally
/// identical type references resolve to the same object. Keyed by constituent identity.
/// </summary>
internal sealed class TypeInterner
{
    private readonly Dictionary<ConstructedTypeKey, ConstructedType> constructedTypes = [];
    private readonly Dictionary<UnionTypeKey, UnionType> unionTypes = [];
    private readonly Dictionary<TupleTypeKey, TupleType> tupleTypes = [];
    private readonly Dictionary<object, LiftedType> liftedTypes = [];

    /// <summary>
    /// Returns the canonical <see cref="LiftedType"/> for the given compile-time constant.
    /// A single instance is shared across all constructed types that use the same value.
    /// </summary>
    public LiftedType GetOrCreateLiftedType(object value, string displayValue)
    {
        if(liftedTypes.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var lifted = new LiftedType(value, displayValue);
        liftedTypes[value] = lifted;
        return lifted;
    }

    /// <summary>
    /// Collapses a name-carrying tuple decorator to its interned structural type so
    /// that identity keys never depend on cosmetic element names. All identity-bearing
    /// inputs to the interner funnel through here, guaranteeing that structurally
    /// identical types (differing only in tuple element names) intern to one instance.
    /// </summary>
    public static IMistType Unwrap(IMistType type)
    {
        return type is NamedTupleType named ? named.Underlying : type;
    }

    private static IReadOnlyList<IMistType> Unwrap(IReadOnlyList<IMistType> types)
    {
        var result = new IMistType[types.Count];

        for(var i = 0; i < types.Count; i++)
        {
            result[i] = Unwrap(types[i]);
        }

        return result;
    }

    /// <summary>
    /// Returns the canonical <see cref="ConstructedType"/> for the given definition and type arguments.
    /// </summary>
    public ConstructedType GetOrCreateConstructedType(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        var structuralDefinition = Unwrap(definition);
        var structuralArguments = Unwrap(typeArguments);
        var key = new ConstructedTypeKey(structuralDefinition, structuralArguments);

        if(constructedTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var constructed = new ConstructedType(structuralDefinition, structuralArguments);
        constructedTypes[key] = constructed;
        return constructed;
    }

    /// <summary>
    /// Returns the canonical <see cref="UnionType"/> for the given constituents.
    /// Constituents are compared as a sorted set so that order does not affect identity.
    /// </summary>
    public UnionType GetOrCreateUnionType(IReadOnlyList<IMistType> constituents)
    {
        var structuralConstituents = Unwrap(constituents);
        var key = new UnionTypeKey(structuralConstituents);

        if(unionTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var union = new UnionType(structuralConstituents);
        unionTypes[key] = union;
        return union;
    }

    /// <summary>
    /// Returns the canonical structural <see cref="TupleType"/> for the given element types.
    /// Element names are not part of tuple identity and are carried separately by
    /// <see cref="NamedTupleType"/> at declaration sites.
    /// </summary>
    public TupleType GetOrCreateTupleType(IReadOnlyList<IMistType> elementTypes)
    {
        var structuralElements = Unwrap(elementTypes);
        var key = new TupleTypeKey(structuralElements);

        if(tupleTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var tuple = new TupleType(structuralElements);
        tupleTypes[key] = tuple;
        return tuple;
    }

    /// <summary>
    /// Key for constructed types: definition identity + ordered type argument identities.
    /// </summary>
    private readonly struct ConstructedTypeKey : IEquatable<ConstructedTypeKey>
    {
        private readonly IMistType definition;
        private readonly IMistType[] typeArguments;
        private readonly int hashCode;

        public ConstructedTypeKey(IMistType definition, IReadOnlyList<IMistType> typeArguments)
        {
            this.definition = definition;
            this.typeArguments = typeArguments.ToArray();

            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(definition));

            foreach(var arg in this.typeArguments)
            {
                hash.Add(RuntimeHelpers.GetHashCode(arg));
            }

            hashCode = hash.ToHashCode();
        }

        public bool Equals(ConstructedTypeKey other)
        {
            if(!ReferenceEquals(definition, other.definition))
            {
                return false;
            }

            if(typeArguments.Length != other.typeArguments.Length)
            {
                return false;
            }

            for(var i = 0; i < typeArguments.Length; i++)
            {
                if(!ReferenceEquals(typeArguments[i], other.typeArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is ConstructedTypeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }

    /// <summary>
    /// Key for union types: sorted set of constituent identities (order-independent).
    /// </summary>
    private readonly struct UnionTypeKey : IEquatable<UnionTypeKey>
    {
        private readonly IMistType[] sortedConstituents;
        private readonly int hashCode;

        public UnionTypeKey(IReadOnlyList<IMistType> constituents)
        {
            // Sort by identity hash code for canonical ordering.
            sortedConstituents = constituents.ToArray();
            Array.Sort(sortedConstituents, (a, b) =>
                RuntimeHelpers.GetHashCode(a).CompareTo(RuntimeHelpers.GetHashCode(b)));

            var hash = new HashCode();

            foreach(var c in sortedConstituents)
            {
                hash.Add(RuntimeHelpers.GetHashCode(c));
            }

            hashCode = hash.ToHashCode();
        }

        public bool Equals(UnionTypeKey other)
        {
            if(sortedConstituents.Length != other.sortedConstituents.Length)
            {
                return false;
            }

            for(var i = 0; i < sortedConstituents.Length; i++)
            {
                if(!ReferenceEquals(sortedConstituents[i], other.sortedConstituents[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is UnionTypeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }

    /// <summary>
    /// Key for tuple types: ordered element type identities (names are ignored for identity).
    /// </summary>
    private readonly struct TupleTypeKey : IEquatable<TupleTypeKey>
    {
        private readonly IMistType[] elementTypes;
        private readonly int hashCode;

        public TupleTypeKey(IReadOnlyList<IMistType> elementTypes)
        {
            this.elementTypes = elementTypes.ToArray();

            var hash = new HashCode();

            foreach(var t in this.elementTypes)
            {
                hash.Add(RuntimeHelpers.GetHashCode(t));
            }

            hashCode = hash.ToHashCode();
        }

        public bool Equals(TupleTypeKey other)
        {
            if(elementTypes.Length != other.elementTypes.Length)
            {
                return false;
            }

            for(var i = 0; i < elementTypes.Length; i++)
            {
                if(!ReferenceEquals(elementTypes[i], other.elementTypes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is TupleTypeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }
}