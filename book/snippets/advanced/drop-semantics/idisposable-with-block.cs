using System;
using System.IO;

namespace Snippets;

public class Program
{
    public static void Main()
    {
        {
            var ms = new MemoryStream();
            try
            {
                ms.WriteByte(42);
                System.Console.WriteLine($"length={ms.Length}");
            }
            finally
            {
                ms.Dispose();
            }
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
