using System;

namespace Snippet;

public class Animal
{
    private string name;
    private int age;

    public Animal(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string GetName()
    {
        return name;
    }

    public int GetAge()
    {
        return age;
    }

    public string Describe()
    {
        return $"{name} (age {age})";
    }
}

public class Dog : Snippet.Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string GetBreed()
    {
        return breed;
    }

    public string DescribeDog()
    {
        return $"{GetName()} is a {breed}, age {GetAge()}";
    }
}

public class GuideDog : Snippet.Dog
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
}
