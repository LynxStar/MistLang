using Standard;
using System;

namespace ScopingDemo;

public class Outer
{
    public class Inner
    {
        public static string Hello()
        {
            return "Outer.Inner";
        }

        public static Inner __CreateDefault()
        {
            var inst = new Inner();
            return inst;
        }
    }

    public record class Status(string Value)
    {
        public static readonly Status Active = new Status("active");
        public static readonly Status Inactive = new Status("inactive");

        public static implicit operator string(Status e) => e.Value;
    }

    public static void Demo()
    {
        System.Console.WriteLine(Inner.Hello());
        System.Console.WriteLine($"Status.Active = {Status.Active}");
    }

    public static Outer __CreateDefault()
    {
        var inst = new Outer();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine(Outer.Inner.Hello());
        Outer.Demo();
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
