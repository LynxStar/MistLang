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
        var dog = new Dog("Buddy", "Poodle");
        Animal widened = ((Animal)dog);
        System.Console.WriteLine(widened.Name);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
