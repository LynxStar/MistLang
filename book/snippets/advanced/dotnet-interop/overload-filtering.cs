using Standard;
using System.Text;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Hello");
        System.Console.WriteLine($"length={bytes.Length}");
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        System.Console.WriteLine($"text={text}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
