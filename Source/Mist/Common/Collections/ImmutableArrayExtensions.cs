using System.Collections.Immutable;

namespace Mist.Common.Collections;

internal static class ImmutableArrayExtensions
{
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