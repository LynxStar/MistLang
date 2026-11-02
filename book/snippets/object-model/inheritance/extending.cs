using Standard;

namespace Snippet;

public class Animal
{
    private string name;
    private int age;

    public Animal(string name, int age)
    {
        this.name = "";
        this.age = default(int);
        this.name = name;
        this.age = age;
    }

    public string GetName()
    {
        return this.name;
    }

    public int GetAge()
    {
        return this.age;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{this.name} (age {this.age})");
    }
}

public class Dog : Snippet.Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = "";
        this.breed = breed;
    }

    public string GetBreed()
    {
        return this.breed;
    }
}
