using System.Runtime.CompilerServices;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction;

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
    private readonly Dictionary<object, LiftedType> liftedTypes = [];
    private readonly Dictionary<TypeArrayKey, LiftedType> liftedTypeArrays = [];

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
    /// Returns the canonical <see cref="LiftedType"/> for a comptime <c>Type[]</c> value —
    /// the ordered array of element types bound to a type-parameter pack's slot in a
    /// constructed variadic type (for example the <c>[int, string]</c> in
    /// <c>Foo&lt;[int, string]&gt;</c>). This is the pack side of the dependent-type
    /// reduction: just as the scalar <c>5</c> in <c>Buffer&lt;5&gt;</c> is a lifted value,
    /// a pack binds a lifted <c>Type[]</c> value.
    /// <para>
    /// A single instance is shared across all constructions whose pack slot binds an
    /// equal <c>Type[]</c> value. Two <c>Type[]</c> values are equal exactly when they
    /// have the same length and, at each index, reference-equal element types — the same
    /// (order-sensitive) type-equality relation used for ordinary type arguments. Because
    /// <see cref="GetOrCreateConstructedType"/> keys on the reference identity of each type
    /// argument and the pack slot's argument is this interned value, the enclosing
    /// constructed type's identity is automatically order-sensitive
    /// (<c>Foo&lt;[int, string]&gt;</c> differs from <c>Foo&lt;[string, int]&gt;</c>) and
    /// arity-sensitive (<c>Foo&lt;[int, string]&gt;</c>, <c>Foo&lt;[int, string, bool]&gt;</c>,
    /// and the empty <c>Foo&lt;[]&gt;</c> are pairwise distinct).
    /// </para>
    /// </summary>
    public LiftedType GetOrCreateLiftedTypeArray(IReadOnlyList<IMistType> elementTypes)
    {
        var key = new TypeArrayKey(elementTypes);

        if(liftedTypeArrays.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var elements = elementTypes.ToArray();
        var displayValue = $"[{string.Join(", ", elements.Select(e => e.Name))}]";
        var lifted = new LiftedType(elements, displayValue);
        liftedTypeArrays[key] = lifted;
        return lifted;
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

    /// <summary>
    /// Key for a comptime <c>Type[]</c> value bound to a pack slot: the ordered element
    /// type identities. Equality is order-sensitive and arity-sensitive — the length must
    /// match and every element must be reference-equal at the same index — so it composes
    /// the interned-type reference equality used for ordinary type arguments elementwise.
    /// This is the same shape as <see cref="TupleTypeKey"/>; it is kept distinct so a lifted
    /// <c>Type[]</c> pack value and a structural tuple type never collide in the interner.
    /// </summary>
    private readonly struct TypeArrayKey : IEquatable<TypeArrayKey>
    {
        private readonly IMistType[] elementTypes;
        private readonly int hashCode;

        public TypeArrayKey(IReadOnlyList<IMistType> elementTypes)
        {
            this.elementTypes = elementTypes.ToArray();

            var hash = new HashCode();
            hash.Add(this.elementTypes.Length);

            foreach(var t in this.elementTypes)
            {
                hash.Add(RuntimeHelpers.GetHashCode(t));
            }

            hashCode = hash.ToHashCode();
        }

        public bool Equals(TypeArrayKey other)
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
            return obj is TypeArrayKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return hashCode;
        }
    }
}