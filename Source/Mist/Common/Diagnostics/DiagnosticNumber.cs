namespace Mist.Common.Diagnostics;

/// Producers name registry fields rather than strings so mistyped codes cannot reach users.
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