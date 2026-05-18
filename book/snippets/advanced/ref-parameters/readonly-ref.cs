using System;

namespace RefParameters;

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

    protected Sensor()
    {
        this.Reading = 0;
    }

    public static Sensor __CreateDefault()
    {
        var inst = new Sensor();
        return inst;
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
