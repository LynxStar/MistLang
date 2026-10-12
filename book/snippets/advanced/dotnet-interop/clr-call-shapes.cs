using Standard;
using System.Text;
using System.Text.Json;

namespace Snippet;

public class Program
{
    public static string Describe(System.Object value)
    {
        return $"object {MistInvariant.Text(value)}";
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize<int>(3));
        System.Console.WriteLine(System.Text.Json.JsonSerializer.Serialize<int>(4, new System.Text.Json.JsonSerializerOptions()));
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.AppendJoin("-", "a", "b", "c");
        builder.AppendFormat(" {0}+{1}", 1, 2);
        System.Console.WriteLine(MistInvariant.Text(builder));
        System.Object boxed = 7;
        System.Console.WriteLine(Describe(boxed));
        System.Console.WriteLine(Describe((1, "one")));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
