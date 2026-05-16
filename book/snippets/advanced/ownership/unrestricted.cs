using System;

namespace OwnershipSystem;

public class Counter
{
    public int Value;
    public Counter(int value) { this.Value = value; }
    public void Increment() { this.Value = Value + 1; }
    public string Describe() { return $"Counter({Value})"; }
}

public class UnrestrictedDemo
{
    public static void Run()
    {
        var c1 = new Counter(10);
        var c2 = c1;
        c2.Increment();
        Console.WriteLine($"c1={c1.Describe()}, c2={c2.Describe()}");
    }
}
