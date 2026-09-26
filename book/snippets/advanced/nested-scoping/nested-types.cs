using Standard;

namespace Snippet;

public class Outer
{
    public static void Demo()
    {
        System.Console.WriteLine(Snippet.Outer.Inner.Hello());
        System.Console.WriteLine($"Status.Active = {Snippet.Outer.Status.Active}");
    }

    public Outer()
    {
    }

    public static Outer __CreateDefault()
    {
        return new Outer();
    }

    public class Inner
    {
        public static string Hello()
        {
            return "Outer.Inner";
        }

        public Inner()
        {
        }

        public static Inner __CreateDefault()
        {
            return new Inner();
        }
    }

    public sealed record class Status(string Value)
    {
        public static readonly Status Active = new Status("active");
        public static readonly Status Inactive = new Status("inactive");

        public static implicit operator string(Status e) => e.Value;

        public static explicit operator Status?(string value)
        {
            if(Equals(value, Status.Active.Value)) return Status.Active;
            if(Equals(value, Status.Inactive.Value)) return Status.Inactive;
            return null;
        }

        public override string ToString()
        {
            if(ReferenceEquals(this, Status.Active)) return "Active";
            if(ReferenceEquals(this, Status.Inactive)) return "Inactive";
            return Value.ToString();
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        System.Console.WriteLine(Snippet.Outer.Inner.Hello());
        Snippet.Outer.Demo();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
