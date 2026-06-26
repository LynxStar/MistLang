namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A simple public class for basic class projection testing.
/// </summary>
public class SimpleClass
{
    public int Value { get; set; }
    public string Name { get; set; } = "default";

    public SimpleClass() { }
    public SimpleClass(int value) => Value = value;

    public int GetValue()
    {
        return Value;
    }

    public void SetValue(int value)
    {
        Value = value;
    }
}

/// <summary>
/// A simple public struct for basic struct projection testing.
/// </summary>
public struct SimpleStruct
{
    public int X;
    public int Y;

    public SimpleStruct(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int Sum()
    {
        return X + Y;
    }
}

/// <summary>
/// A simple public interface for basic interface projection testing.
/// </summary>
public interface ISimpleInterface
{
    int Compute(int input);
    string Description { get; }
}

/// <summary>
/// A simple public enum for basic enum projection testing.
/// </summary>
public enum SimpleEnum
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 3
}