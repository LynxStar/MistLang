using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.UserId first = ((Snippet.UserId)41);
        Snippet.UserId next = first + ((Snippet.UserId)1);
        int raw = next;
        object boxed = next;
        System.Console.WriteLine($"next={next} value={next.Value} even={next.IsEven()} raw={raw}");
        System.Console.WriteLine($"is UserId={boxed is Snippet.UserId} is int={boxed is int}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
