using System;

namespace TypeAliases;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        GenericBox<int> boxInt = new GenericBox<int>(7);
        GenericBox<string> boxStr = new GenericBox<string>("world");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
