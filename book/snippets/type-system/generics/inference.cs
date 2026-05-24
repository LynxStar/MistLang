using System;

namespace Snippet;

public class Container<T>
{
    private T value;

    public Container(T value)
    {
        this.value = value;
    }

    public T GetValue()
    {
        return value;
    }
}
public class InferenceDemo
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static T Unwrap<T>(Container<T> container)
    {
        return container.GetValue();
    }

    public static void Run()
    {
        var x = Identity(42);
        var y = Identity > (42);
        var box = new Container<string>("hello");
        var s = Unwrap(box);
    }

    public static InferenceDemo __CreateDefault()
    {
        var inst = new InferenceDemo();
        return inst;
    }
}
