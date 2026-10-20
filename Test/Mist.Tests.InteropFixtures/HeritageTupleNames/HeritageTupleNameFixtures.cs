namespace Mist.Tests.InteropFixtures.HeritageTupleNames;

/// <summary>
/// Named tuples in the positions C# records outside a member signature: a base type (an attribute on the
/// type definition), an implemented interface (InterfaceImpl rows) and a type-parameter constraint
/// (GenericParamConstraint rows), each beside an unnamed control. Used by <c>AmbientHeritageTupleNameTests</c>.
/// </summary>
public interface IPairs<T>
{
}

public interface IOther<T>
{
}

public class PairBase<T>
{
}

public class NamedBase : PairBase<(int Left, int Right)>
{
}

public class UnnamedBase : PairBase<(int, int)>
{
}

public class NamedInterfaces : IPairs<(int Key, string Value)>, IOther<(int Low, int High)>
{
}

public class UnnamedInterface : IPairs<(int, string)>
{
}

public class PartlyNamedInterface : IPairs<(int Named, int)>
{
}

public class GenericNamedInterface<TFirst, TSecond> : IPairs<(TFirst First, TSecond Second)>
{
}

public class Container<TElement>
{
    public sealed class Items : IPairs<(TElement Element, int Priority)>
    {
    }
}

public interface INamedChild : IPairs<(int Up, int Down)>
{
}

public class LongNamedInterface : IPairs<(int A, int B, int C, int D, int E, int F, int G, int H)>
{
}

public class NamedConstraint<T> where T : IPairs<(int Key, string Value)>
{
}

public class UnnamedConstraint<T> where T : IPairs<(int, string)>
{
}

public static class NamedMethodConstraint
{
    public static T Take<T>(T value) where T : IPairs<(int Ticks, bool Done)>
    {
        return value;
    }
}