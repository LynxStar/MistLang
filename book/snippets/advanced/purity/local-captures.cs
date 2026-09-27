using Standard;

namespace PurityCaptures;

public class Program
{
    public static int Spread()
    {
        System.Collections.Generic.List<int> readings = new System.Collections.Generic.List<int>();
        System.Action<int> record = (int value) => readings.Add(value);
        int Widest()
        {
            int lo = readings[0];
            int hi = readings[0];
            foreach (int r in readings)
            {
                if (r < lo)
                {
                    lo = r;
                }
                if (r > hi)
                {
                    hi = r;
                }
            }
            return hi - lo;
        }
        record(3);
        record(9);
        record(4);
        return Widest();
    }

    public static void Main(string[] args)
    {
        System.Console.WriteLine(System.FormattableString.Invariant($"Spread()={Spread()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
