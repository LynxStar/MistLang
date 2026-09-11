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
        Union_Int32_String result = Introduction.Scorer.Grade(95);
        {
            if (result.AsStringOrNull is string letter)
            {
                Console.WriteLine($"Grade: {letter}");
            }
        }
        {
            if (result.AsInt32OrNull is int numeric)
            {
                Console.WriteLine($"Score: {numeric}");
            }
        }
    }

    public Scorer()
    {
    }

    public static Scorer __CreateDefault()
    {
        return new Scorer();
    }
}
