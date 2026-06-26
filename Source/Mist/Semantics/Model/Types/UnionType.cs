using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A structural union type representing a value that may be one of several constituent types.
/// Two union types are equal if they have the same set of constituents (regardless of source order).
/// Constituents are stored in canonical order (sorted by DisplayName) to ensure deterministic
/// equality, hashing, and helper type naming.
/// </summary>
public sealed class UnionType : IType, IEquatable<UnionType>
{
    /// <summary>
    /// The distinct constituent types, in canonical order (sorted by DisplayName).
    /// </summary>
    public IReadOnlyList<IType> Constituents { get; }

    public string DisplayName { get; }

    public TypeKind Kind => TypeKind.Struct;

    public UnionType(IReadOnlyList<IType> constituents)
    {
        // Sort constituents by DisplayName for canonical ordering.
        var sorted = constituents.OrderBy(t => t.DisplayName, StringComparer.Ordinal).ToList();
        Constituents = sorted;
        DisplayName = string.Join(" | ", sorted.Select(t => t.DisplayName));
    }

    public bool Equals(UnionType? other)
    {
        if(other is null)
        {
            return false;
        }

        if(ReferenceEquals(this, other))
        {
            return true;
        }

        if(Constituents.Count != other.Constituents.Count)
        {
            return false;
        }

        for(var i = 0; i < Constituents.Count; i++)
        {
            if(Constituents[i].DisplayName != other.Constituents[i].DisplayName)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as UnionType);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach(var constituent in Constituents)
        {
            hash.Add(constituent.DisplayName, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Nullable projection metadata for unions created by the Assembly Type Provider.
    /// Non-null when this union represents a <c>T | void</c> projection from a CLR
    /// <c>Nullable&lt;T&gt;</c> or reference type. Used by the CSharp emitter to
    /// produce correct CLR-level signatures.
    /// </summary>
    public NullableProjectionInfo? NullableProjection { get; internal set; }
}