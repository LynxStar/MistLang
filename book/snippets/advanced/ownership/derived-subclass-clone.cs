using Standard;

namespace Snippet;

public class Animal : Standard.IClonable<Snippet.Animal>
{
    public int Legs;

    public Animal(int legs)
    {
        this.Legs = default(int);
        this.Legs = legs;
    }

    public virtual Snippet.Animal Clone()
    {
        var copy = (Snippet.Animal)MemberwiseClone();
        return copy;
    }
}

public class Dog : Snippet.Animal, Standard.IClonable<Snippet.Dog>
{
    public string Name;

    public Dog(int legs, string name) : base(legs)
    {
        this.Name = "";
        this.Name = name;
    }

    public override Snippet.Dog Clone()
    {
        var copy = (Snippet.Dog)base.Clone();
        return copy;
    }
}

public class SubclassCloneDemo
{
    public static void Run()
    {
        Snippet.Animal pet = new Snippet.Dog(4, "Rex");
        Snippet.Animal kept = pet.Clone();
        {
            if (kept is Snippet.Dog dog)
            {
                System.Console.WriteLine($"kept a Dog named {dog.Name}");
            }
        }
    }

    public SubclassCloneDemo()
    {
    }

    public static SubclassCloneDemo __CreateDefault()
    {
        return new SubclassCloneDemo();
    }
}
