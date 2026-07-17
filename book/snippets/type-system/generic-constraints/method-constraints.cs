using System;

namespace Snippet;

internal class MethodConstraints
{
    public static T CreateItem<T>()
        where T : new()
    {
        return new T();
    }

    public static MethodConstraints __CreateDefault()
    {
        var inst = new MethodConstraints();
        return inst;
    }
}
