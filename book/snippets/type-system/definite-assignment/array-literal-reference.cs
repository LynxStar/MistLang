using System;

namespace Snippet;

public class Color
{
    public string Name;

    public Color(string name)
    {
        this.Name = name;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Color[] colors = new Snippet.Color[] { new Snippet.Color("red"), new Snippet.Color("green"), new Snippet.Color("blue") };
        Console.WriteLine(colors[0].Name);
        Console.WriteLine(colors[2].Name);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
