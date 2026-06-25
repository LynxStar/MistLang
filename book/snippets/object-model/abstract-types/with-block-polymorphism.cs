using System;

namespace Snippet;

interface IDescribable
{
    string Describe();
}
public class Sensor : IDescribable
{
    public string GetReading()
    {
        return "Temperature: 23C";
    }

    string IDescribable.Describe() => this.GetReading();

    public static Sensor __CreateDefault()
    {
        var inst = new Sensor();
        return inst;
    }
}
public class Actuator : IDescribable
{
    public string GetStatus()
    {
        return "Motor: active";
    }

    string IDescribable.Describe() => this.GetStatus();

    public static Actuator __CreateDefault()
    {
        var inst = new Actuator();
        return inst;
    }
}
public class Program
{
    private static void PrintInfo(IDescribable item)
    {
        Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        var sensor = new Sensor();
        var actuator = new Actuator();
        PrintInfo(sensor);
        PrintInfo(actuator);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
