using System;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String a = 42;
        Union_Int32_String b = "hello";
        a = b;
        b = a;
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
