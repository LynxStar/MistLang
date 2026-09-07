using System.Collections.Immutable;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The comptime <c>Type[]</c> a pack slot is bound to: an ordered list of types, standing as one
/// VALUE the way <c>5</c> stands in <c>FixedBuffer&lt;5&gt;</c>.
/// <para>
/// Equatable because that is what makes it interable, and interning is what gives a constructed
/// variadic type its identity. Order and length both count -- <c>Foo&lt;[int, string]&gt;</c>,
/// <c>Foo&lt;[string, int]&gt;</c> and <c>Foo&lt;[int, string, bool]&gt;</c> are three types --
/// which falls out of comparing the elements pairwise rather than being stated anywhere.
/// </para>
/// </summary>
internal sealed class TypePackValue : IEquatable<TypePackValue>
{
    private readonly int hashCode;

    public TypePackValue(IReadOnlyList<IMistType> elements)
    {
        Elements = [.. elements];

        var hash = new HashCode();
        hash.Add(Elements.Length);

        foreach(var element in Elements)
        {
            hash.Add(element.FullyQualifiedName, StringComparer.Ordinal);
        }

        hashCode = hash.ToHashCode();
    }

    public ImmutableArray<IMistType> Elements { get; }

    public bool Equals(TypePackValue? other)
    {
        if(other is null || Elements.Length != other.Elements.Length)
        {
            return false;
        }

        for(var i = 0; i < Elements.Length; i++)
        {
            if(!ReferenceEquals(Elements[i], other.Elements[i])
                && Elements[i].FullyQualifiedName != other.Elements[i].FullyQualifiedName)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is TypePackValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return hashCode;
    }

    /// <summary>
    /// The bracketed form the language writes, which is also how the lifted type carrying this is
    /// named -- so a diagnostic mentioning <c>Func&lt;[int, string], void&gt;</c> reads back.
    /// </summary>
    public override string ToString()
    {
        return $"[{string.Join(", ", Elements.Select(element => element.Name))}]";
    }
}