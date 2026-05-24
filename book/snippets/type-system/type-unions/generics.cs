using System;

namespace Snippet;

class Box<T>
{
    public T Value;

    public Box(T value)
    {
        this.Value = value;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var box = new Box<Union_Int32_String>(42);
        Union_Int32_TError<string> ok = 100;
        Union_Int32_TError<string> err = "not found";
        if (ok.IsInt32)
        {
            int n = ok.AsInt32;
            Console.WriteLine($"ok: {n}");
        }
        if (err is string s)
        {
            Console.WriteLine($"err: {s}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
