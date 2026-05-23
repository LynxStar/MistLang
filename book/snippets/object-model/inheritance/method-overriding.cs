using System;

namespace Snippet;

public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string DescribeDog()
    {
        return $"{GetName()} is a {breed}, age {GetAge()}";
    }

    protected Dog()
    {
        this.breed = "";
    }

    public static Dog __CreateDefault()
    {
        var inst = new Dog();
        return inst;
    }
}
public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string DescribeGuideDog()
    {
        return $"{GetName()} is a {GetBreed()} guide dog, age {GetAge()}, handler: {handler}";
    }

    protected GuideDog()
    {
        this.handler = "";
    }

    public static GuideDog __CreateDefault()
    {
        var inst = new GuideDog();
        return inst;
    }
}
