using System;

namespace Interfaces;

interface IDescribable
{
    string Describe();
}

public class Person : IDescribable
{
    private string name;

    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {name}, age {age}";
    }
}
