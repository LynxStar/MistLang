using System;

namespace SwitchDemo;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String mixed = 123;
        var result = mixed switch
        {
            { IsInt32: true, AsInt32: var n } => $"integer: {n}",
            { IsString: true, AsString: var s } => $"string: {s}",
        };
        Console.WriteLine(result);

        Union_Int32_String mixed2 = "hello";
        var result2 = mixed2 switch
        {
            { IsInt32: true, AsInt32: var n } => $"integer: {n}",
            { IsString: true, AsString: var s } => $"string: {s}",
        };
        Console.WriteLine(result2);
    }
}
