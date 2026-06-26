namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Type with an out parameter — out params are projected as ref with ClrCallingConvention.Out.
/// </summary>
public class HasOutParam
{
    public bool TryParse(string input, out int result)
    {
        return int.TryParse(input, out result);
    }

    /// <summary>
    /// A method without out params that should still be visible.
    /// </summary>
    public int Parse(string input)
    {
        return int.Parse(input);
    }
}

/// <summary>
/// Type with a ref parameter — methods should be filtered by VisibilityFilter.
/// </summary>
public class HasRefParam
{
    public void Swap(ref int a, ref int b)
    {
        (a, b) = (b, a);
    }

    /// <summary>
    /// A method without ref params that should still be visible.
    /// </summary>
    public int Add(int a, int b)
    {
        return a + b;
    }
}

/// <summary>
/// Type with an in parameter — methods should NOT be filtered (in is ref-readonly, treated as pass-by-value).
/// </summary>
public class HasInParam
{
    public int ReadValue(in int value)
    {
        return value * 2;
    }

    public int Sum(in int a, in int b)
    {
        return a + b;
    }

    public string Format(in int value)
    {
        return value.ToString();
    }
}