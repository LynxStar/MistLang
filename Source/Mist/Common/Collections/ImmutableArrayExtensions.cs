using System.Collections.Immutable;

namespace Mist.Common.Collections;

/// <summary>
/// Extension methods for <see cref="ImmutableArray{T}"/> that handle
/// default (uninitialized) arrays safely.
/// </summary>
internal static class ImmutableArrayExtensions
{
    /// <summary>
    /// Returns true if the array contains an element of type <typeparamref name="T"/>.
    /// Safe to call on default (uninitialized) arrays.
    /// </summary>
    public static bool HasAny<TElement, T>(this ImmutableArray<TElement> array)
        where T : TElement
    {
        if(array.IsDefault)
        {
            return false;
        }

        foreach(var item in array)
        {
            if(item is T)
            {
                return true;
            }
        }

        return false;
    }
}