using System;

namespace Snippet;

public record class Color(byte Value)
{
    public static readonly Color Red = new Color((byte)0);
    public static readonly Color Green = new Color((byte)1);
    public static readonly Color Blue = new Color((byte)2);

    public static implicit operator byte(Color e) => e.Value;

    public override string ToString()
    {
        if (ReferenceEquals(this, Red)) return "Red";
        else if (ReferenceEquals(this, Green)) return "Green";
        else if (ReferenceEquals(this, Blue)) return "Blue";
        return Value.ToString();
    }
}
