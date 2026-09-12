using Standard;
using System.IO;

namespace Snippets;

public class Program
{
    public static void Main()
    {
        {
            var ms = new System.IO.MemoryStream();
            MistContext.Push<System.IO.MemoryStream>(ms);
            try
            {
                ms.WriteByte(42);
                System.Console.WriteLine($"length={ms.Length}");
            }
            finally
            {
                MistContext.Pop<System.IO.MemoryStream>();
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
