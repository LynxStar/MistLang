namespace Mist.Common.Diagnostics;

/// <summary>
/// The public code of a descriptor. Minted only by registry declarations: a producer names a
/// descriptor field, never a string, so a misspelled code cannot reach a user.
/// </summary>
public readonly record struct DiagnosticNumber
{
    private DiagnosticNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    internal static DiagnosticNumber Mint(string value)
    {
        return new DiagnosticNumber(value);
    }

    public override string ToString()
    {
        return Value;
    }
}