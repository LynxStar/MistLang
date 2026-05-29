namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A ref struct — should be excluded entirely by the VisibilityFilter.
/// </summary>
public ref struct RefStructType
{
    public int Value;

    public RefStructType(int value) => Value = value;

    public int GetValue() => Value;
}
