using System;

namespace ScopingDemo;

public class Outer
{
    public class Inner
    {
        public static string Hello() { return "Outer.Inner"; }
    }

    public readonly struct Status
    {
        public static readonly Status Active = new Status("active");
        public static readonly Status Inactive = new Status("inactive");

        public string Value { get; }
        private Status(string value) { Value = value; }

        public static implicit operator string(Status s) => s.Value;
        public override string ToString() => Value;
    }

    public static void Demo()
    {
        Console.WriteLine(Inner.Hello());
        Console.WriteLine($"Status.Active = {Status.Active}");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine(Outer.Inner.Hello());
        Outer.Demo();
    }
}
