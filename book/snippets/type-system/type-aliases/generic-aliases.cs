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
        return $"Box({MistInvariant.Text(this.value)})";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.GenericBox<int> boxInt = new Snippet.GenericBox<int>(7);
        Snippet.GenericBox<string> boxStr = new Snippet.GenericBox<string>("world");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
