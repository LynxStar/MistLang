using Standard;

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

    public static T Unwrap<T>(Snippet.Container<T> container)
    {
        return container.GetValue();
    }

    public static void Run()
    {
        int x = Identity<int>(42);
        int y = Identity<int>(42);
        Snippet.Container<string> box = new Snippet.Container<string>("hello");
        string s = Unwrap<string>(box);
    }

    public InferenceDemo()
    {
    }

    public static InferenceDemo __CreateDefault()
    {
        return new InferenceDemo();
    }
}
