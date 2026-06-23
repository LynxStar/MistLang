using System;

interface IShape
{
    string Name { get; }
    int Area();
    string Describe();
}
namespace Snippet;

public class Rectangle : IShape
{
    private int width;

    private int height;

    public Rectangle(int width, int height)
    {
        this.width = width;
        this.height = height;
    }

    public string Name { get; } = "Rectangle";

    public int Area()
    {
        return this.width * this.height;
    }

    public string Describe()
    {
        return $"{this.Name}: {this.width}x{this.height}, area={this.Area()}";
    }
}
