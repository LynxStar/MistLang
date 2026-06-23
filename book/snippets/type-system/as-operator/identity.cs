using System;

namespace Snippet;

public class Dog
{
    public string Name;

    public Dog(string name)
    {
        Name = name;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var dog = new Dog("Rex");
        Dog same = dog;
        System.Console.WriteLine(same.Name);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
