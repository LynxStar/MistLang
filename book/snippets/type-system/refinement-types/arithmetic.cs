namespace RefinementTypes;

public class Program
{
    public static void Main(string[] args)
    {
        int a = 20;

        // Arithmetic produces int, not AdultAge
        int sum = a + 5;
        int diff = a - 2;

        // To get back to AdultAge, use pattern matching
        int computed = a + 10;
        if (computed >= 18)
        {
            int verified = computed;
            Console.WriteLine($"Still adult: {verified}");
        }
    }
}
