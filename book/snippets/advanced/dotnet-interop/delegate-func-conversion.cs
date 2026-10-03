using Standard;
using System.Collections.Generic;

namespace Snippet;

public class Program
{
    public static bool IsPositive(int x)
    {
        return x > 0;
    }

    public static void Main(string[] args)
    {
        System.Collections.Generic.List<int> numbers = new System.Collections.Generic.List<int>();
        numbers.Add(-2);
        numbers.Add(-1);
        numbers.Add(0);
        numbers.Add(1);
        numbers.Add(2);
        numbers.Add(3);
        System.Collections.Generic.List<int> positives = numbers.FindAll((int x) => x > 0);
        System.Console.WriteLine(System.FormattableString.Invariant($"positives={positives.Count}"));
        System.Collections.Generic.List<int> alsoPositives = numbers.FindAll(IsPositive);
        System.Console.WriteLine(System.FormattableString.Invariant($"alsoPositives={alsoPositives.Count}"));
        System.Func<int, bool> isEven = (int x) => x % 2 == 0;
        System.Predicate<int> predicate = new System.Predicate<int>((isEven).Invoke);
        System.Collections.Generic.List<int> evens = numbers.FindAll(predicate);
        System.Console.WriteLine(System.FormattableString.Invariant($"evens={evens.Count}"));
        System.Func<int, bool> backToFunc = new System.Func<int, bool>((predicate).Invoke);
        System.Console.WriteLine($"backToFunc(4)={backToFunc(4)}");
        System.Console.WriteLine($"predicate(6)={predicate(6)}");
        System.Console.WriteLine($"predicate(7)={predicate(7)}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
