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

public class Cat : Snippet.Animal
{
    public int Lives;

    public Cat(string name, int lives) : base(name)
    {
        Lives = lives;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Animal animal = new Snippet.Cat("Luna", 7);
        Snippet.Dog dog = (animal as Snippet.Dog) ?? new Snippet.Dog("Default", "Unknown");
        Console.WriteLine($"{dog.Name}, {dog.Breed}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
