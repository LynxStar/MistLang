using Standard;

namespace Snippet;

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.GenericBox<int> box = new Snippet.GenericBox<int>(42);
        System.Console.WriteLine(box.GetItem().ToString(System.Globalization.CultureInfo.InvariantCulture));
        string result = Snippet.Utilities.Identity<string>("hello");
        System.Console.WriteLine(result);
        Snippet.IGenericContainer<int> container = new Snippet.GenericBox<int>(99);
        System.Console.WriteLine(container.GetItem().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
