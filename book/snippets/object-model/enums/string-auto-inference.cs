using Standard;

namespace Enums;

public sealed record class Permission(string Value)
{
    public static readonly Permission Read = new Permission("Read");
    public static readonly Permission Write = new Permission("Write");
    public static readonly Permission Execute = new Permission("Execute");

    public static implicit operator string(Permission e) => e.Value;

    public static explicit operator Permission?(string value)
    {
        if(Equals(value, Permission.Read.Value)) return Permission.Read;
        if(Equals(value, Permission.Write.Value)) return Permission.Write;
        if(Equals(value, Permission.Execute.Value)) return Permission.Execute;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Permission.Read)) return "Read";
        if(ReferenceEquals(this, Permission.Write)) return "Write";
        if(ReferenceEquals(this, Permission.Execute)) return "Execute";
        return Value.ToString();
    }
}

public sealed record class Environment(string Value)
{
    public static readonly Environment Development = new Environment("dev");
    public static readonly Environment Staging = new Environment("Staging");
    public static readonly Environment Production = new Environment("prod");

    public static implicit operator string(Environment e) => e.Value;

    public static explicit operator Environment?(string value)
    {
        if(Equals(value, Environment.Development.Value)) return Environment.Development;
        if(Equals(value, Environment.Staging.Value)) return Environment.Staging;
        if(Equals(value, Environment.Production.Value)) return Environment.Production;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Environment.Development)) return "Development";
        if(ReferenceEquals(this, Environment.Staging)) return "Staging";
        if(ReferenceEquals(this, Environment.Production)) return "Production";
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

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
