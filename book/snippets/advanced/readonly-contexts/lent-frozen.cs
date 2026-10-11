using Standard;

namespace Snippet;

public class Sensor
{
    public int Id;
    public int Reading;

    public Sensor(int id, int reading)
    {
        this.Id = default(int);
        this.Reading = default(int);
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
        return System.FormattableString.Invariant($"Sensor({Id}, {Reading})");
    }
}

public class LentFrozenDemo
{
    public static void WithFrozenRef(Snippet.Sensor s)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"frozen view={s.GetReading()}"));
    }

    public static void Main(string[] args)
    {
        Snippet.Sensor s = new Snippet.Sensor(1, 100);
        WithFrozenRef(s);
        s.SetReading(200);
        System.Console.WriteLine($"after thaw={s.Describe()}");
    }

    public LentFrozenDemo()
    {
    }

    public static LentFrozenDemo __CreateDefault()
    {
        return new LentFrozenDemo();
    }
}
