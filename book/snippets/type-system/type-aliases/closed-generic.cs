using Standard;

namespace Snippet;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.GenericBox<int> intBox = new Snippet.GenericBox<int>(42);
        Snippet.GenericBox<string> strBox = new Snippet.GenericBox<string>("Mist");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
