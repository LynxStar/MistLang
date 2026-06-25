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
public class Cat : Animal
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
        Animal animal = new Cat("Luna", 7);
        var dog = (animal as Dog) ?? new Dog("Default", "Unknown");
        System.Console.WriteLine($"{dog.Name}, {dog.Breed}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
