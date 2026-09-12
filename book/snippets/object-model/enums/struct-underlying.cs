using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct Vec2
{
    public readonly int x;
    public readonly int y;

    public Vec2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    private Vec2(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.x = p0;
        this.y = p1;
    }

    internal static Vec2 __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Vec2(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public string Describe()
    {
        return $"({this.x}, {this.y})";
    }
}

public sealed record class CardinalDir(Snippet.Vec2 Value)
{
    public static readonly CardinalDir Up = new CardinalDir(new Snippet.Vec2(0, 1));
    public static readonly CardinalDir Down = new CardinalDir(new Snippet.Vec2(0, -1));
    public static readonly CardinalDir Left = new CardinalDir(new Snippet.Vec2(-1, 0));
    public static readonly CardinalDir Right = new CardinalDir(new Snippet.Vec2(1, 0));

    public static implicit operator Snippet.Vec2(CardinalDir e) => e.Value;

    public static explicit operator CardinalDir?(Snippet.Vec2 value)
    {
        if(Equals(value, CardinalDir.Up.Value)) return CardinalDir.Up;
        if(Equals(value, CardinalDir.Down.Value)) return CardinalDir.Down;
        if(Equals(value, CardinalDir.Left.Value)) return CardinalDir.Left;
        if(Equals(value, CardinalDir.Right.Value)) return CardinalDir.Right;
        return null;
    }

    public override string ToString()
    {
        if(ReferenceEquals(this, CardinalDir.Up)) return "Up";
        if(ReferenceEquals(this, CardinalDir.Down)) return "Down";
        if(ReferenceEquals(this, CardinalDir.Left)) return "Left";
        if(ReferenceEquals(this, CardinalDir.Right)) return "Right";
        return Value.ToString();
    }
}
