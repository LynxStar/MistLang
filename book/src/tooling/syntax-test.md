# Syntax Highlighting Test

```mist
// Gray: comments
// Purple: keywords (namespace, class, if, return, var, new, etc.)
// Red: modifiers (public, private, static, readonly)
// Blue: reserved types (int, string, bool, void)
// Orange: numeric literals
// Green: strings and regex
// Aqua: type/method names

public static readonly class Config
{
    private int _value;
    public string Name;
}

interface IShape
{
    int Area();
    string Describe();
}

class Circle : IShape
{
    public int Radius;

    public Circle(int radius)
    {
        this.Radius = radius;
    }

    public int Area()
    {
        return Radius * Radius * 3;
    }

    public string Describe()
    {
        return $"Circle(r={Radius})";
    }
}

var x = 42;
var hex = 0xFF;
var msg = "hello world";
var re = `\d+`;
var circle = new Circle(10);
int area = circle.Area();
bool big = area > 100;

if (big)
{
    Console.WriteLine(msg);
}
```
