namespace Mist.Backends.CSharpTranspiler.Runtime;

internal static class ArrayOperations
{
    public static object? IfPresent<T>(T[]? array, Func<T[], object?> operation)
    {
        return array is null ? null : operation(array);
    }

    public static void IfPresentAction<T>(T[]? array, Action<T[]> operation)
    {
        if(array is not null)
        {
            operation(array);
        }
    }

    public static int? IndexOf<T>(T[] array, T value)
    {
        var index = Array.IndexOf(array, value);
        return index >= 0 ? index : null;
    }

    public static bool Contains<T>(T[] array, T value)
    {
        return IndexOf(array, value).HasValue;
    }

    public static void Sort<T>(T[] array, IComparer<T> comparer)
    {
        Array.Sort(array, comparer);
    }

    // Boxing preserves a matched value-type default independently of the null used for absence.
    public static object? Find<T>(T[] array, Func<T, bool> predicate)
    {
        foreach(var value in array)
        {
            if(predicate(value))
            {
                return value;
            }
        }

        return null;
    }

    public static bool Any<T>(T[] array)
    {
        return array.Length > 0;
    }

    public static bool Any<T>(T[] array, Func<T, bool> predicate)
    {
        foreach(var value in array)
        {
            if(predicate(value))
            {
                return true;
            }
        }

        return false;
    }
}