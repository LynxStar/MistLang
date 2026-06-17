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
    public static void ReadSensor(ref readonly Sensor s)
    {
        Console.WriteLine($"reading={s.Describe()}");
    }

    public static void Main()
    {
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        Sensor s = new Sensor(42);
        ReadSensor(s);
        s.Reading = 100;
        Console.WriteLine($"after={s.Describe()}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
