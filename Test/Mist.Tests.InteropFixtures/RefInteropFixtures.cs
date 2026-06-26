namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Fixture with public methods that have <c>ref</c> parameters of various types.
/// These methods should pass through the VisibilityFilter now that ref params are supported.
/// </summary>
public class RefParamFixture
{
    private int field;

    public void IncrementRef(ref int value)
    {
        value++;
    }

    public void SwapStrings(ref string a, ref string b)
    {
        (a, b) = (b, a);
    }

    public void SetToDefault<T>(ref T value) where T : new()
    {
        value = new T();
    }

    public void Exchange(ref int location, int newValue)
    {
        field = location;
        location = newValue;
    }

    /// <summary>A method with ref int — CLR intrinsic element type.</summary>
    public void RefInt(ref int value)
    {
        value *= 2;
    }

    /// <summary>A method with ref string — CLR intrinsic element type.</summary>
    public void RefString(ref string value)
    {
        value = value.ToUpper();
    }

    /// <summary>A normal method without ref params that should remain visible.</summary>
    public int GetField()
    {
        return field;
    }
}

/// <summary>
/// Fixture with public methods that have <c>in</c> parameters.
/// These methods should pass through the VisibilityFilter and be projected as Mist ref parameters.
/// </summary>
public class InParamFixture
{
    public int DoubleValue(in int value)
    {
        return value * 2;
    }

    public int SumTwo(in int a, in int b)
    {
        return a + b;
    }

    public string FormatValue(in string value)
    {
        return $"[{value}]";
    }

    public T Identity<T>(in T value)
    {
        return value;
    }

    /// <summary>A method mixing in and regular params.</summary>
    public int AddOffset(in int baseValue, int offset)
    {
        return baseValue + offset;
    }

    /// <summary>A normal method without in params that should remain visible.</summary>
    public int Constant()
    {
        return 99;
    }
}

/// <summary>
/// Fixture with public methods that have mutable <c>ref</c> return types.
/// These methods should pass through the VisibilityFilter.
/// </summary>
public class MutableRefReturnFixture
{
    private int intField = 10;
    private string stringField = "hello";

    public ref int GetIntRef()
    {
        return ref intField;
    }

    public ref string GetStringRef()
    {
        return ref stringField;
    }

    /// <summary>A normal method without ref return that should remain visible.</summary>
    public int GetIntValue()
    {
        return intField;
    }
}

/// <summary>
/// Fixture with public methods that have <c>ref readonly</c> return types.
/// These methods should remain filtered by the VisibilityFilter.
/// </summary>
public class RefReadonlyReturnFixture
{
    private int intField = 20;
    private string stringField = "world";

    public ref readonly int GetReadonlyIntRef()
    {
        return ref intField;
    }

    public ref readonly string GetReadonlyStringRef()
    {
        return ref stringField;
    }

    /// <summary>A normal method without ref return that should remain visible.</summary>
    public int GetIntValue()
    {
        return intField;
    }
}

/// <summary>
/// Fixture with public methods that have <c>out</c> parameters.
/// These methods should remain filtered by the VisibilityFilter (negative testing).
/// </summary>
public class OutParamFixture
{
    public bool TryGetValue(string key, out int result)
    {
        result = key.Length;
        return true;
    }

    public void Initialize(out int value)
    {
        value = 0;
    }

    public bool TryParse<T>(string input, out T result) where T : struct
    {
        result = default;
        return false;
    }

    /// <summary>A normal method without out params that should remain visible.</summary>
    public int GetLength(string input)
    {
        return input.Length;
    }
}

/// <summary>
/// Fixture with public methods combining <c>ref</c> and <c>out</c> parameters.
/// The entire method should be filtered when any <c>out</c> parameter is present.
/// </summary>
public class MixedRefOutFixture
{
    public void RefAndOut(ref int source, out int copy)
    {
        copy = source;
    }

    public void MultipleRefAndOut(ref int a, ref int b, out int sum)
    {
        sum = a + b;
    }

    public void InAndOut(in int source, out int doubled)
    {
        doubled = source * 2;
    }

    /// <summary>A normal method without ref/out params that should remain visible.</summary>
    public int Add(int a, int b)
    {
        return a + b;
    }
}