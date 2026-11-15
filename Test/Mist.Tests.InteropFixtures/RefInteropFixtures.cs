namespace Mist.Tests.InteropFixtures;

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

    public void RefInt(ref int value)
    {
        value *= 2;
    }

    public void RefString(ref string value)
    {
        value = value.ToUpper();
    }

    public int GetField()
    {
        return field;
    }
}

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

    public int AddOffset(in int baseValue, int offset)
    {
        return baseValue + offset;
    }

    public int Constant()
    {
        return 99;
    }
}

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

    public int GetIntValue()
    {
        return intField;
    }
}

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

    public int GetIntValue()
    {
        return intField;
    }
}

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

    // IDE0060 suppressed: parameter exists to test ref parameter projection in the type projector.
#pragma warning disable IDE0060
    public bool TryParse<T>(string input, out T result) where T : struct
    {
        result = default;
        return false;
    }
#pragma warning restore IDE0060

    public int GetLength(string input)
    {
        return input.Length;
    }
}

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

    public int Add(int a, int b)
    {
        return a + b;
    }
}