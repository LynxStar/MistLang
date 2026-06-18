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
        var colors = new[] { new Color("red"), new Color("green"), new Color("blue") };
        System.Console.WriteLine(colors[0].Name);
        System.Console.WriteLine(colors[2].Name);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
