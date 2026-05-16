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

    public int GetReading() { return Reading; }
    public void SetReading(int r) { this.Reading = r; }
    public string Describe() { return $"Sensor({Id}, {Reading})"; }
}

public class LentFrozenDemo
{
    // frozen erased — LentFrozen state is compile-time only
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
}
