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

public class Dog : Snippet.Animal
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
        Snippet.Dog dog = new Snippet.Dog("Buddy", "Poodle");
        Snippet.Animal widened = ((Snippet.Animal)dog);
        Console.WriteLine(widened.Name);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
