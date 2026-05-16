namespace RefinementTypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Compile-time: literal 20 satisfies >= 18
        int known = 20;

        // Runtime: dynamic value checked via pattern matching
        int userInput = 25;
        if (userInput >= 18)
        {
            int validAge = userInput;
            Console.WriteLine($"Valid adult age: {validAge}");
        }

        int childInput = 10;
        if (childInput >= 18)
        {
            Console.WriteLine("This won't print");
        }
        else
        {
            Console.WriteLine("Not an adult age");
        }
    }
}
