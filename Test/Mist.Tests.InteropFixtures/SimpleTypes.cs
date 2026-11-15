namespace Mist.Tests.InteropFixtures;

public class SimpleClass
{
    public int Value { get; set; }
    public string Name { get; set; } = "default";

    public SimpleClass() { }
    public SimpleClass(int value)
    {
        Value = value;
    }

    public int GetValue()
    {
        return Value;
    }

    public void SetValue(int value)
    {
        Value = value;
    }
}

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

public interface ISimpleInterface
{
    int Compute(int input);
    string Description { get; }
}

public enum SimpleEnum
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 3
}