using System;

namespace RefinementTypes;

public class Program
{
    public static void Main(string[] args)
    {
        int known = 20;
        int userInput = 25;
        if (userInput >= 18)
        {
            int validAge = userInput;
            Console.WriteLine($"Valid adult age: {validAge}");
        }
        int childInput = 10;
        if (childInput >= 18)
        {
            int childAge = childInput;
            Console.WriteLine("This won't print");
        }
        else
        {
            Console.WriteLine("Not an adult age");
        }
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
