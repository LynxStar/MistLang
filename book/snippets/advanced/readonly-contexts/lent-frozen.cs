using System;

namespace ReadonlyContexts;

public class Sensor
{
    public int Id;

    public int Reading;

    public Sensor(int id, int reading)
    {
        this.Id = id;
        this.Reading = reading;
    }

    public int GetReading()
    {
        return Reading;
    }

    public void SetReading(int r)
    {
        this.Reading = r;
    }

    public string Describe()
    {
        return $"Sensor({Id}, {Reading})";
    }

    protected Sensor()
    {
        this.Id = 0;
        this.Reading = 0;
    }

    public static Sensor __CreateDefault()
    {
        var inst = new Sensor();
        return inst;
    }
}
public class LentFrozenDemo
{
    public static void WithFrozenRef(Sensor s)
    {
        Console.WriteLine($"frozen view={s.GetReading()}");
    }

    public static void Main(string[] args)
    {
        Sensor s = new Sensor(1, 100);
        WithFrozenRef(s);
        s.SetReading(200);
        Console.WriteLine($"after thaw={s.Describe()}");
    }

    public static LentFrozenDemo __CreateDefault()
    {
        var inst = new LentFrozenDemo();
        return inst;
    }
}
