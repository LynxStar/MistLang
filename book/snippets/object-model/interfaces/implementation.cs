using System;

namespace Snippet;

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

    protected Person()
    {
        this.name = "";
        this.age = 0;
    }

    public static Person __CreateDefault()
    {
        var inst = new Person();
        return inst;
    }
}
