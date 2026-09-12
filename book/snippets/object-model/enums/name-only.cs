using Standard;

namespace Snippet;

public sealed record class Color(byte Value)
{
    public static readonly Color Red = new Color(0);
    public static readonly Color Green = new Color(1);
    public static readonly Color Blue = new Color(2);

    public static implicit operator byte(Color e) => e.Value;

    public static explicit operator Color?(byte value)
    {
        if(Equals(value, Color.Red.Value)) return Color.Red;
        if(Equals(value, Color.Green.Value)) return Color.Green;
        if(Equals(value, Color.Blue.Value)) return Color.Blue;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, Color.Red)) return "Red";
        if(ReferenceEquals(this, Color.Green)) return "Green";
        if(ReferenceEquals(this, Color.Blue)) return "Blue";
        return Value.ToString();
    }
}
