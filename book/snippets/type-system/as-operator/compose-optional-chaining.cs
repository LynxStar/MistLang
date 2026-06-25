using System;

namespace Snippet;

public class Animal
{
    public string Name;

    public Animal(string name)
    {
        Name = name;
    }
}
public class Dog : Animal
{
    public string Breed;

    public Dog(string name, string breed) : base(name)
    {
        Breed = breed;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        Animal animal = new Dog("Rex", "Labrador");
        string? breed = ((animal as Dog))?.Breed;
        if (breed is string b)
        {
            System.Console.WriteLine($"Breed: {b}");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
