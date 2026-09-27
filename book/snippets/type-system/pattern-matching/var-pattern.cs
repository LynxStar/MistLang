using Standard;

namespace Snippet;

public class Program
{
    private static int ComputeScore()
    {
        return 85;
    }

    public static void Main(string[] args)
    {
        {
            if (Snippet.Program.ComputeScore() is var score and > 0)
            {
                System.Console.WriteLine(System.FormattableString.Invariant($"Positive score: {score}"));
            }
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
