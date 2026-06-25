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
        Animal animal = new Dog("Fido", "Beagle");
        Dog maybeDog = (animal as Dog);
        if (maybeDog is Dog d)
        {
            System.Console.WriteLine($"Got a dog: {d.Breed}");
        }
        else
        {
            System.Console.WriteLine("Not a dog");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
