namespace Mist.Tests.InteropFixtures.HeritageTupleNames;

public class RankedInterfaces : IPairs<(int Flat, int)[]>, IPairs<(int Grid, int)[,]>, IPairs<(int Cube, int)[,,]>
{
}

public class NestedElementInterfaces : IPairs<(NestedElementInterfaces.Local Here, int)>, IPairs<(Environment.SpecialFolder There, int)>
{
    public sealed class Local
    {
    }
}

public class MethodParameterKeys<TOuter>
{
    public static void Pick<TFirst, TSecond>()
        where TSecond : IPairs<(TOuter Outer, int)>, IPairs<(TFirst Inner, int)>
    {
    }
}

public interface IMarker
{
}

public class PlainSiblingInterfaces : IMarker, IDisposable, IPairs<(int Kept, int)>
{
    public void Dispose()
    {
    }
}