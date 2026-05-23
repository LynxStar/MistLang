using System;

namespace TypeAliases;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String x = 42;
        Union_Int32_String y = "hello";
        Union_Boolean_Int32_String r1 = 99;
        Union_Boolean_Int32_String r2 = "success";
        Union_Boolean_Int32_String r3 = true;
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
