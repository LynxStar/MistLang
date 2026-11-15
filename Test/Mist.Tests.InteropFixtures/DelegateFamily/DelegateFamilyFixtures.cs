namespace Mist.Tests.InteropFixtures.DelegateFamily;

// Shares only its simple name with System.Func`1, so a projection keyed by name alone would take it for
// the delegate family.
public delegate TResult Func<out TResult>();

// The System delegate family in each signature position a projected member has.
public static class DelegateFamilySignatures
{
    public static int Produce(System.Func<int> producer)
    {
        return producer();
    }

    public static bool Test(Func<int, long, bool> predicate)
    {
        return predicate(1, 2);
    }

    public static void Run(Action action)
    {
        action();
    }

    public static void Feed(Action<int, long> consumer)
    {
        consumer(1, 2);
    }

    public static Func<int, bool> Positive()
    {
        return value => value > 0;
    }
}