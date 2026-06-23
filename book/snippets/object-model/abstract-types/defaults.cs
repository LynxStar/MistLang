using System;

interface IGreeting
{
    string Name();
    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}
