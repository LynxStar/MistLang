using System;

interface IGreeting
{
    string Name();
    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}
namespace Snippet;

public class Person : IGreeting
{
    private string name;

    public Person(string name)
    {
        this.name = name;
    }

    public string Name()
    {
        return this.name;
    }

    public string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}
