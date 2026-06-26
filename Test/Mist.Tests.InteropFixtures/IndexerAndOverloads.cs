namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Type with indexers for testing indexer projection.
/// </summary>
public class HasIndexer
{
    private readonly int[] _data = new int[10];

    public int this[int index]
    {
        get => _data[index];
        set => _data[index] = value;
    }

    public int this[int row, int col] => row * 10 + col;

    public int Length => _data.Length;
}

/// <summary>
/// Type with optional parameters for testing optional parameter projection.
/// </summary>
public class HasOptionalParams
{
    public int Compute(int a, int b = 10, int c = 20) => a + b + c;
    public string Format(string value, string prefix = "[", string suffix = "]")
        => $"{prefix}{value}{suffix}";
}

/// <summary>
/// Type with multiple overloads for testing overload preservation.
/// </summary>
public class HasOverloads
{
    public int Process(int value) => value;
    public int Process(int a, int b) => a + b;
    public int Process(int a, int b, int c) => a + b + c;
    public string Process(string value) => value.ToUpper();

    /// <summary>
    /// This overload has an out param — projected as ref with ClrCallingConvention.Out.
    /// </summary>
    public bool Process(string input, out int result) => int.TryParse(input, out result);
}