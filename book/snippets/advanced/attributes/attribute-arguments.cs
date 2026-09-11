using System;
using System.ComponentModel.DataAnnotations;

namespace Snippet;

internal class Validator
{
    [System.ComponentModel.DataAnnotations.RangeAttribute(1, 100)]
    public int Score;
    [System.ComponentModel.DataAnnotations.DisplayAttribute(Name = "User Score")]
    public int DisplayScore;

    [System.ObsoleteAttribute("use ValidateV2", true)]
    public void Validate()
    {
        Console.WriteLine("Validating");
    }

    public Validator()
    {
        this.Score = default(int);
        this.DisplayScore = default(int);
    }

    public static Validator __CreateDefault()
    {
        return new Validator();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Validator v = new Snippet.Validator();
        Console.WriteLine($"Score field exists: true");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
