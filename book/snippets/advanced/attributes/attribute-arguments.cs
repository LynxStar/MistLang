using System;
using System.ComponentModel.DataAnnotations;

namespace Snippet;

public class Validator
{
    [Range(1, 100)]
    public int Score;

    [Obsolete("use ValidateV2", true)]
    public void Validate()
    {
        Console.WriteLine("Validating");
    }

    [Display(Name = "User Score")]
    public int DisplayScore;

    public Validator()
    {
        this.Score = 0;
        this.DisplayScore = 0;
    }

    public static Validator __CreateDefault()
    {
        var inst = new Validator();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var v = new Validator();
        Console.WriteLine($"Score field exists: true");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
