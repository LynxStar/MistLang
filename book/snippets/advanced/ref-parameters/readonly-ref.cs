using Standard;

namespace Snippet;

public class Sensor
{
    public int Reading;

    public Sensor(int reading)
    {
        this.Reading = default(int);
        this.Reading = reading;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"Sensor({Reading})");
    }
}

public class Program
{
    public static void ReadSensor(ref readonly Snippet.Sensor s)
    {
        System.Console.WriteLine($"reading={s.Describe()}");
    }

    public static void Main()
    {
        Snippet.Sensor s = new Snippet.Sensor(42);
        ReadSensor(in s);
        s.Reading = 100;
        System.Console.WriteLine($"after={s.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
