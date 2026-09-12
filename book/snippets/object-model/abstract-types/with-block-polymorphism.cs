using Standard;

namespace Snippet;

public interface IDescribable
{
    string Describe();
}

public class Sensor : Snippet.IDescribable
{
    public string GetReading()
    {
        return "Temperature: 23C";
    }

    public Sensor()
    {
    }

    public static Sensor __CreateDefault()
    {
        return new Sensor();
    }

    string Snippet.IDescribable.Describe() => this.GetReading();
}

public class Actuator : Snippet.IDescribable
{
    public string GetStatus()
    {
        return "Motor: active";
    }

    public Actuator()
    {
    }

    public static Actuator __CreateDefault()
    {
        return new Actuator();
    }

    string Snippet.IDescribable.Describe() => this.GetStatus();
}

public class Program
{
    private static void PrintInfo(Snippet.IDescribable item)
    {
        System.Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        Snippet.Sensor sensor = new Snippet.Sensor();
        Snippet.Actuator actuator = new Snippet.Actuator();
        PrintInfo(sensor);
        PrintInfo(actuator);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
