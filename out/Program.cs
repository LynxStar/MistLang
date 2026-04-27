using System;

namespace HelloMist;

public class Program
{
    private static int Factorial(int n)
    {
        if (n <= 1)
        {
            return 1;
        }
        return n * Factorial(n - 1);
    }

    private static int Sum(int[] numbers)
    {
        var total = 0;
        foreach (int num in numbers)
        {
            total = total + num;
        }
        return total;
    }

    public static void Main(string[] args)
    {
        var origin = new Point();
        var p = new Point(3);
        Console.WriteLine(p.Describe());
        Console.WriteLine(origin.Describe());
        var red = new Color(255);
        Console.WriteLine(red.ToHex());
        int a = 10;
        int b = 3;
        int sum = a + b;
        int diff = a - b;
        int prod = a * b;
        int quot = a / b;
        int rem = a % b;
        bool isGreater = a > b;
        bool isEqual = a == b;
        Console.WriteLine($"Sum: {sum}, Diff: {diff}, Prod: {prod}");
        Console.WriteLine($"Quot: {quot}, Rem: {rem}");
        Console.WriteLine($"a > b: {isGreater}, a == b: {isEqual}");
        bool t = true;
        bool f = false;
        bool andResult = t && f;
        bool orResult = t || f;
        bool notResult = !t;
        Console.WriteLine($"AND: {andResult}, OR: {orResult}, NOT: {notResult}");
        if (a > 5)
        {
            Console.WriteLine("a is greater than 5");
        }
        else
        {
            Console.WriteLine("a is 5 or less");
        }
        int count = 0;
        while (count < 3)
        {
            Console.WriteLine($"While count: {count}");
            count = count + 1;
        }
        for (int i = 0; i < 5; i = i + 1)
        {
            Console.WriteLine($"For i: {i}");
        }
        int[] numbers = new int[5];
        for (int i = 0; i < 5; i = i + 1)
        {
            numbers[i] = i * 10;
        }
        foreach (int n in numbers)
        {
            Console.WriteLine($"Array element: {n}");
        }
        var message = "Hello from Mist!";
        Console.WriteLine(message);
        int hexVal = 0xFF;
        Console.WriteLine($"Hex value: {hexVal}");
        double pi = 3.14;
        Console.WriteLine($"Pi: {pi}");
        char letter = 'A';
        Console.WriteLine($"Letter: {letter}");
        int fact5 = Factorial(5);
        Console.WriteLine($"5! = {fact5}");
        int total = Sum(numbers);
        Console.WriteLine($"Sum of array: {total}");
        for (int i = 0; i < 10; i = i + 1)
        {
            if (i == 3)
            {
                break;
            }
            Console.WriteLine($"Break loop: {i}");
        }
        Console.WriteLine("Done!");
    }
}
