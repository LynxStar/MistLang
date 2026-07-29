using System.Runtime.CompilerServices;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Interns synthetic type instances (unions, constructed types) so that structurally
/// identical type references resolve to the same object. Keyed by constituent identity.
/// Also provides factory methods for non-interned synthetic types (refined types).
/// </summary>
internal sealed class TypeInterner
{
    private readonly Dictionary<ConstructedTypeKey, ConstructedType> constructedTypes = [];
    private readonly Dictionary<UnionTypeKey, UnionType> unionTypes = [];
    private readonly Dictionary<TupleTypeKey, TupleType> tupleTypes = [];
    private readonly Dictionary<LiftedTypeKey, LiftedType> liftedTypes = [];
    private readonly Dictionary<IMistType, UnknownValueType> unknownValueTypes = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Returns the canonical <see cref="LiftedType"/> for an integral compile-time
    /// constant of the given Mist type.
    /// <para>
    /// The overloads exist so a caller cannot choose the CLR box. Identity is keyed on
    /// the boxed value, so a boxed <c>int</c> and a boxed <c>long</c> of equal numeric
    /// value would be different keys — previously that made <c>Array&lt;string, 7&gt;</c>
    /// and <c>string[7]</c> distinct types. Widening every integral value to
    /// <see cref="long"/> at the signature removes the choice, and
    /// <paramref name="type"/> is what distinguishes an <c>int</c> from a <c>byte</c>.
    /// </para>
    /// </summary>
    public LiftedType LiftLiteral(IMistType type, long value)
    {
        return GetOrCreateLifted(type, value);
    }

    /// <inheritdoc cref="LiftLiteral(IMistType, long)"/>
    public LiftedType LiftLiteral(IMistType type, bool value)
    {
        return GetOrCreateLifted(type, value);
    }

    /// <inheritdoc cref="LiftLiteral(IMistType, long)"/>
    public LiftedType LiftLiteral(IMistType type, char value)
    {
        return GetOrCreateLifted(type, value);
    }

    private LiftedType GetOrCreateLifted(IMistType type, object value)
    {
        var key = new LiftedTypeKey(type, value);

        if(liftedTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var lifted = new LiftedType(type, value);
        liftedTypes[key] = lifted;
        return lifted;
    }

    /// <summary>
    /// Returns the canonical <see cref="UnknownValueType"/> for a domain — a dependent argument
    /// whose value is not statically known.
    /// <para>
    /// Keyed on the domain alone, since there is no value to distinguish two of them. One
    /// unknown-of-int is therefore shared by every unsized array, which is what keeps
    /// <c>int[]</c> a single interned type however many times it is written.
    /// </para>
    /// </summary>
    public UnknownValueType GetOrCreateUnknownValue(IMistType domain)
    {
        if(unknownValueTypes.TryGetValue(domain, out var existing))
        {
            return existing;
        }

        var unknownValue = new UnknownValueType(domain);
        unknownValueTypes[domain] = unknownValue;
        return unknownValue;
    }

    /// <summary>
    /// Returns the canonical <see cref="ConstructedType"/> for the given definition and type arguments.
    /// The interner does not normalize inputs — callers pass types exactly as resolved.
    /// Structurally equivalent types that differ only by decoration (e.g., named vs unnamed
    /// tuples) will produce distinct interned instances. The type checker handles equivalence.
    /// </summary>
    public ConstructedType GetOrCreateConstructedType(IMistType definition, IReadOnlyList<IMistType> typeArguments)
    {
        var key = new ConstructedTypeKey(definition, typeArguments);

        if(constructedTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var constructed = new ConstructedType(definition, typeArguments);
        constructedTypes[key] = constructed;
        return constructed;
    }

    /// <summary>
    /// Returns the canonical <see cref="UnionType"/> for the given constituents.
    /// Constituents are compared as a sorted set so that order does not affect identity.
    /// </summary>
    public UnionType GetOrCreateUnionType(IReadOnlyList<IMistType> constituents)
    {
        var key = new UnionTypeKey(constituents);

        if(unionTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var union = new UnionType(constituents);
        unionTypes[key] = union;
        return union;
    }

    /// <summary>
    /// Returns the canonical <see cref="TupleType"/> for the given element types.
    /// </summary>
    public TupleType GetOrCreateTupleType(IReadOnlyList<IMistType> elementTypes)
    {
        var key = new TupleTypeKey(elementTypes);

        if(tupleTypes.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var tuple = new TupleType(elementTypes);
        tupleTypes[key] = tuple;
        return tuple;
    }

    /// <summary>
    /// Creates a new <see cref="RefinedType"/> wrapping the given base type with a
    /// predicate AST node. Refined types are not interned — each declaration site
    /// produces a distinct type instance even if the predicate is textually identical.
    /// </summary>
    public static RefinedType CreateRefinedType(IMistType baseType, SyntaxNode predicateNode)
    {
        return new RefinedType(baseType, predicateNode);
    }

    /// <summary>
    /// Key for lifted types: the Mist type's identity plus the value. Mirrors the other
    /// keys in using reference identity for types, and adds value equality for the
    /// constant, so equal numbers of the same Mist type collapse regardless of spelling.
    /// </summary>
    private readonly struct LiftedTypeKey : IEquatable<LiftedTypeKey>
    {
        private readonly IMistType type;
        private readonly object value;
        private readonly int hashCode;

        public LiftedTypeKey(IMistType type, object value)
        {
            this.type = type;
            this.value = value;
            hashCode = HashCode.Combine(RuntimeHelpers.GetHashCode(type), value);
        }

        public bool Equals(LiftedTypeKey other)
        {
            return ReferenceEquals(type, other.type) && value.Equals(other.value);
        }

        public override bool Equals(object? obj)
        {
            return obj is LiftedTypeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
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