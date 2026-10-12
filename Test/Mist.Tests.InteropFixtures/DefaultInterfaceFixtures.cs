namespace Mist.Tests.InteropFixtures;

/// <summary>
/// An interface with one required member and one instance default, and two interfaces restating that
/// default: one supplies its own body, the other withdraws it.
/// </summary>
public interface IGreeter
{
    string Name();

    string Greet()
    {
        return "hello " + Name();
    }

    static string Bracket(IGreeter greeter)
    {
        return "[" + greeter.Greet() + "]";
    }
}

public interface IShoutingGreeter : IGreeter
{
    string IGreeter.Greet()
    {
        return "HELLO " + Name();
    }
}

public interface IStrictGreeter : IGreeter
{
    abstract string IGreeter.Greet();
}

public static class Greeters
{
    public static string Through(IGreeter greeter)
    {
        return greeter.Greet();
    }
}