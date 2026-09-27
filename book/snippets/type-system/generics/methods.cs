using Standard;

namespace Snippet;

public class Utilities
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void PrintItem<T>(T item)
    {
        System.Console.WriteLine($"Item: {MistInvariant.Text(item)}");
    }

    public Utilities()
    {
    }

    public static Utilities __CreateDefault()
    {
        return new Utilities();
    }
}
