using System;
using System.Text;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        var bytes = Encoding.UTF8.GetBytes("Hello");
        System.Console.WriteLine($"length={bytes.Length}");
        var text = Encoding.UTF8.GetString(bytes);
        System.Console.WriteLine($"text={text}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
