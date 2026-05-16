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
}

public class Program
{
    public static void ReadSensor(Sensor s)
    {
        // readonly ref is erased — passed by value at runtime
        Console.WriteLine($"reading={s.Describe()}");
    }

    public static void Main()
    {
        Sensor s = new Sensor(42);
        ReadSensor(s);
        s.Reading = 100;
        Console.WriteLine($"after={s.Describe()}");
    }
}
