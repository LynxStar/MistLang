using Standard;

namespace Snippet;

public class Animal
{
    public string Name;

    public Animal(string name)
    {
        this.Name = "";
        Name = name;
    }
}

public class Dog : Snippet.Animal
{
    public string Breed;

    public Dog(string name, string breed) : base(name)
    {
        this.Breed = "";
        Breed = breed;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Animal animal = new Snippet.Dog("Rex", "Labrador");
        string breed = (animal as Snippet.Dog)?.Breed;
        {
            if (breed is string b)
            {
                System.Console.WriteLine($"Breed: {b}");
            }
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
