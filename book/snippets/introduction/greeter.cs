using System;

namespace Introduction;

public class Scorer
{
    public static Union_Int32_String Grade(int score)
    {
        if (score >= 90)
        {
            return "A";
        }
        return score;
    }

    public static void Main(string[] args)
    {
        var result = Scorer.Grade(95);
        if (result.IsString)
        {
            string letter = result.AsString;
            Console.WriteLine($"Grade: {letter}");
        }
        if (result.IsInt32)
        {
            int numeric = result.AsInt32;
            Console.WriteLine($"Score: {numeric}");
        }
    }

    public static Scorer __CreateDefault()
    {
        var inst = new Scorer();
        return inst;
    }
}
