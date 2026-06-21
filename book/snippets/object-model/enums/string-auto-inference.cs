using System;

namespace Enums;

public record class Permission(string Value)
{
    public static readonly Permission Read = new Permission("Read");
    public static readonly Permission Write = new Permission("Write");
    public static readonly Permission Execute = new Permission("Execute");

    public static implicit operator string(Permission e) => e.Value;

    public override string ToString()
    {
        if (ReferenceEquals(this, Read)) return "Read";
        else if (ReferenceEquals(this, Write)) return "Write";
        else if (ReferenceEquals(this, Execute)) return "Execute";
        return Value.ToString();
    }
}
public record class Environment(string Value)
{
    public static readonly Environment Development = new Environment("dev");
    public static readonly Environment Staging = new Environment("Staging");
    public static readonly Environment Production = new Environment("prod");

    public static implicit operator string(Environment e) => e.Value;

    public override string ToString()
    {
        if (ReferenceEquals(this, Development)) return "Development";
        else if (ReferenceEquals(this, Staging)) return "Staging";
        else if (ReferenceEquals(this, Production)) return "Production";
        return Value.ToString();
    }
}
public class Program
{
    public static void Main()
    {
        string perm = Permission.Read;
        System.Console.WriteLine(perm);
        string env = Environment.Staging;
        System.Console.WriteLine(env);
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
