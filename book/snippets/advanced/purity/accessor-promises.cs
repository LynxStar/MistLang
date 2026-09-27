using Standard;

namespace PurityAccessors;

public class Dial
{
    public int Level { get; set; }

    public Dial()
    {
    }

    public static Dial __CreateDefault()
    {
        return new Dial();
    }
}

public class Program
{
    public static int Turned(int by)
    {
        PurityAccessors.Dial dial = new PurityAccessors.Dial();
        dial.Level = 4;
        dial.Level = dial.Level + by;
        return dial.Level;
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Turned(3)={Turned(3)}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
