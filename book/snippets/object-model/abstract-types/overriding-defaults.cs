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

public class Robot : IGreeting
{
    private string model;

    public Robot(string model)
    {
        this.model = model;
    }

    public string Name()
    {
        return this.model;
    }

    public string Greet()
    {
        return $"Beep boop, I am {this.model}";
    }
}
