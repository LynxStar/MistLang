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
        Snippet.Animal animal = new Snippet.Dog("Fido", "Beagle");
        Snippet.Dog? maybeDog = (animal as Snippet.Dog);
        {
            if (maybeDog is Snippet.Dog d)
            {
                System.Console.WriteLine($"Got a dog: {d.Breed}");
            }
            else
            {
                System.Console.WriteLine("Not a dog");
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
