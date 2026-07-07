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

    /// <summary>
    /// Returns the canonical <see cref="ConstructedType"/> for the given definition and type arguments.
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
}