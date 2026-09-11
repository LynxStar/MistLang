using System;

namespace Snippet;

internal class MethodConstraints
{
    public static T CreateItem<T>()
        where T : new()
    {
        return new T();
    }

    public MethodConstraints()
    {
    }

    public static MethodConstraints __CreateDefault()
    {
        return new MethodConstraints();
    }
}
