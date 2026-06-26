namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A struct with operator overloads for testing operator projection.
/// Defines op_Addition, op_Equality, and op_Implicit.
/// </summary>
public struct Operatable
{
    public int Value { get; }

    public Operatable(int value) => Value = value;

    public static Operatable operator +(Operatable left, Operatable right)
        => new(left.Value + right.Value);

    public static bool operator ==(Operatable left, Operatable right)
        => left.Value == right.Value;

    public static bool operator !=(Operatable left, Operatable right)
        => left.Value != right.Value;

    public static implicit operator int(Operatable op) => op.Value;

    public override bool Equals(object? obj)
    {
        return obj is Operatable other && this == other;
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
}