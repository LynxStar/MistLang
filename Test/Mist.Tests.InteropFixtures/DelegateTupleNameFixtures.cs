namespace Mist.Tests.InteropFixtures;

public delegate ((int Left, int Right) Pair, (int, int Label) Tail) NamedSignature(
    (int X, int Y) point, ((int Inner, int), int Last) nested);

public delegate T EchoTuple<T>(T first, T again, (T Value, (int Fixed, int) Edge) nested);

public delegate TResult ConvertTuple<T, TResult>(T input, T repeated);

public delegate List<T[]> DeepTuple<T>(List<T[]> values);

public delegate (int One, int Two, int Three, int Four, int Five, int Six, int Seven, int Eight)
    LongNamedSignature((int A, int B, int C, int D, int E, int F, int G, int H) values);

public delegate (int, int) UnnamedSignature((int, int) value);

public delegate string? OptionalSignature();

public delegate string RequiredSignature();

public static class DelegateTupleNameUses
{
    public static EchoTuple<(int X, int Y)> EchoOne()
    {
        throw new NotImplementedException();
    }

    public static EchoTuple<(int Up, int Down)> EchoTwo()
    {
        throw new NotImplementedException();
    }

    public static ConvertTuple<(int First, int), ((int Inner, int), int Last)> Convert()
    {
        throw new NotImplementedException();
    }

    public static DeepTuple<(int X, int Y)> Deep()
    {
        throw new NotImplementedException();
    }

    public static (EchoTuple<(int One, int Two, int Three, int Four, int Five, int Six, int Seven, int Eight)> Handler,
        (int Low, int High) Window) LongWithSibling()
    {
        throw new NotImplementedException();
    }

    public static (Predicate<(int X, int Y)> Test, (int Low, int High) Window) PredicateWithSibling()
    {
        throw new NotImplementedException();
    }

    public static (Func<(int X, int Y), (int First, int Last)> Selector,
        Action<(int Up, int Down)> Sink, (int Low, int High) Window) FuncAndActionWithSibling()
    {
        throw new NotImplementedException();
    }
}