namespace Mist.Tests.InteropFixtures.ConstraintProjection;

public class Missing;

public interface IMarker<T>;

public interface IDeep<T>;

public struct MissingValue;

public class Structural<T> where T : IMarker<(int, int?, int, int, int, int, int, int)>;

public class StructuralMissing<T> where T : IMarker<(int, MissingValue?)>;

public class Direct<T> where T : Missing
{
    public class Child
    {
        public class Grandchild;
    }
}

public class Nested<T> where T : IMarker<IDeep<Missing>>;

public class Dependent<T> where T : IMarker<Direct<Missing>>;

public class Derived : Direct<Missing>;

public interface IInherited : IMarker<Direct<Missing>>;

public class FBound<T> where T : IMarker<FBound<T>>;

public class Siblings<T, TOther> where T : TOther;

public class CycleA<T> where T : CycleB;

public class CycleB : CycleA<CycleB>;

public class AliasConstrained<T> where T : IComparable<T>
{
    public class Child;
}

public class Outer<T> where T : class
{
    public class Inner<TOther> where TOther : IMarker<T>;

    public class Invalid<TOther> where TOther : Missing;
}

public delegate int HiddenDelegate<T>() where T : Missing;

public class Methods
{
    public static int Valid()
    {
        return 0;
    }

    public static int Direct<T>() where T : Missing
    {
        return 0;
    }

    public static int Nested<T>() where T : IMarker<IDeep<Missing>>
    {
        return 0;
    }

    public static int Siblings<T, TOther>() where T : TOther
    {
        return 0;
    }

    public static int FBound<T>() where T : IMarker<T>
    {
        return 0;
    }

    public static int Special<T>() where T : class, new()
    {
        return 0;
    }
}

public static class Extensions
{
    public static int Valid(this int source)
    {
        return source;
    }

    public static int Direct<T>(this T source) where T : Missing
    {
        _ = source;

        return 0;
    }

    public static int Nested<T>(this int source) where T : IMarker<IDeep<Missing>>
    {
        return source;
    }

    public static int Siblings<T, TOther>(this T source, TOther other) where T : TOther
    {
        _ = source;
        _ = other;

        return 0;
    }

    public static int Hidden(this Direct<Missing> source)
    {
        _ = source;

        return 0;
    }
}

public class Consumer
{
    public Direct<Missing>? Field;

    public Direct<Missing>? Property { get; set; }

    public Consumer()
    {
    }

    public Consumer(Direct<Missing> value)
    {
        Field = value;
    }

    public static Direct<Missing>? Result()
    {
        return null;
    }

    public static int Argument(IMarker<Direct<Missing>> value)
    {
        _ = value;

        return 0;
    }

    public static int Array(Direct<Missing>[] value)
    {
        _ = value;

        return 0;
    }

    public static int ByRef(ref Direct<Missing> value)
    {
        _ = value;

        return 0;
    }

    public static int Delegate(HiddenDelegate<Missing> value)
    {
        _ = value;

        return 0;
    }

    public static int Valid()
    {
        return 0;
    }
}