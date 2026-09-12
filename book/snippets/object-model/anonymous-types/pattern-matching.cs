using Standard;

namespace Snippet;

internal class Program
{
    public static void Main(string[] args)
    {
        var point = new { X = 5, Y = 10 };
        {
            if (point is { X: var x, Y: var y })
            {
                System.Console.WriteLine($"Matched: X={x} Y={y}");
            }
        }
        var status = new { Code = 200, Message = "OK" };
        if (status is { Code: 200 })
        {
            System.Console.WriteLine($"Success: {status.Message}");
        }
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
