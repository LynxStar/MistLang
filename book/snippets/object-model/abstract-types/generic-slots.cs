using Standard;

namespace Snippet;

public interface IShow
{
    string Show();
}

public class Card : Snippet.IShow
{
    public string Show()
    {
        return "card";
    }

    public Card()
    {
    }

    public static Card __CreateDefault()
    {
        return new Card();
    }
}

public interface IPicker
{
    string Pick<T>(T value)
        where T : Snippet.IShow;

    string Name<T>(T value)
        where T : Snippet.IShow;
}

public class Picker : Snippet.IPicker
{
    public string Pick<U>(U value)
        where U : Snippet.IShow
    {
        return value.Show();
    }

    public string Name<V>(V value)
    {
        return "any value";
    }

    public Picker()
    {
    }

    public static Picker __CreateDefault()
    {
        return new Picker();
    }

    string Snippet.IPicker.Name<T>(T value) => this.Name<T>(value);
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.IPicker picker = new Snippet.Picker();
        System.Console.WriteLine(picker.Pick<Snippet.Card>(new Snippet.Card()));
        System.Console.WriteLine(picker.Name<Snippet.Card>(new Snippet.Card()));
        System.Console.WriteLine(new Snippet.Picker().Name<int>(5));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
