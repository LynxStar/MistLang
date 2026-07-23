using System;

namespace Snippet;

public interface IShape
{
    int Area();
}

public struct Circle : IShape
{
    public readonly int Area()
    {
        return 3;
    }
}

public struct Square : IShape
{
    public readonly int Area()
    {
        return 4;
    }
}

public struct ShapePair<T1, T2> where T1 : IShape where T2 : IShape
{
    public System.ValueTuple<T1, T2> Shape;
    public T1 Shape1 { get => Shape.Item1; set => Shape.Item1 = value; }
    public T2 Shape2 { get => Shape.Item2; set => Shape.Item2 = value; }

    public ShapePair(System.ValueTuple<T1, T2> values)
    {
        Shape = values;
    }

    public int TotalArea()
    {
        return Shape1.Area() + Shape2.Area();
    }
}

public class Program
{
    public static void Main(string[] cmdArgs)
    {
        var pair = new ShapePair<Circle, Square>((new Circle(), new Square()));
        System.Console.WriteLine($"area={pair.TotalArea()}");
    }
}
