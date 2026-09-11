using System;

namespace Snippet;

public class Sensor
{
    public int Reading;

    public Sensor(int reading)
    {
        this.Reading = reading;
    }

    public string Describe()
    {
        return $"Sensor({Reading})";
    }
}

public class Program
{
    public static void ReadSensor(ref readonly Snippet.Sensor s)
    {
        Console.WriteLine($"reading={s.Describe()}");
    }

    public static void Main()
    {
        Snippet.Sensor s = new Snippet.Sensor(42);
        ReadSensor(in s);
        s.Reading = 100;
        Console.WriteLine($"after={s.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
