using Standard;

namespace Snippet;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Point(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Point __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Point(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly Snippet.Point Moved(int dx)
    {
        return new Snippet.Point(X + dx, Y);
    }
}

#pragma warning disable CS0660, CS0661
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public readonly struct UserId : Standard.IClonable<Snippet.UserId>
{
    public readonly int Value;

    public UserId(int value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString();

    private UserId(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static UserId __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new UserId(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public Snippet.UserId Clone()
        => new Snippet.UserId(Value);

    public bool IsEven()
    {
        return this % 2 == 0;
    }

    public static implicit operator long(Snippet.UserId value)
        => (long)(value.Value);

    public static implicit operator float(Snippet.UserId value)
        => (float)(value.Value);

    public static implicit operator double(Snippet.UserId value)
        => (double)(value.Value);

    public static implicit operator decimal(Snippet.UserId value)
        => (decimal)(value.Value);

    public static implicit operator int(Snippet.UserId value)
        => value.Value;

    public static explicit operator Snippet.UserId(int value)
        => new Snippet.UserId(value);

    public static Snippet.UserId operator +(Snippet.UserId a, Snippet.UserId b)
        => new Snippet.UserId((a.Value + b.Value));

    public static Snippet.UserId operator -(Snippet.UserId a, Snippet.UserId b)
        => new Snippet.UserId((a.Value - b.Value));

    public static Snippet.UserId operator *(Snippet.UserId a, Snippet.UserId b)
        => new Snippet.UserId((a.Value * b.Value));

    public static Snippet.UserId operator /(Snippet.UserId a, Snippet.UserId b)
        => new Snippet.UserId((a.Value / b.Value));

    public static Snippet.UserId operator %(Snippet.UserId a, Snippet.UserId b)
        => new Snippet.UserId((a.Value % b.Value));

    public static bool operator <(Snippet.UserId a, Snippet.UserId b)
        => (a.Value < b.Value);

    public static bool operator >(Snippet.UserId a, Snippet.UserId b)
        => (a.Value > b.Value);

    public static bool operator <=(Snippet.UserId a, Snippet.UserId b)
        => (a.Value <= b.Value);

    public static bool operator >=(Snippet.UserId a, Snippet.UserId b)
        => (a.Value >= b.Value);

    public static bool operator ==(Snippet.UserId a, Snippet.UserId b)
        => (a.Value == b.Value);

    public static bool operator !=(Snippet.UserId a, Snippet.UserId b)
        => (a.Value != b.Value);

    public static Snippet.UserId operator -(Snippet.UserId a)
        => new Snippet.UserId((-a.Value));
}
#pragma warning restore CS0660, CS0661

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Position
{
    public int X;
    public int Y;

    public Position(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    private Position(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        this.X = p0;
        this.Y = p1;
    }

    internal static Position __FromParts(int p0, int p1, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Position(p0, p1, default(Mist.Emission.CSharp.__StructInit));
    }

    public readonly Snippet.Position Moved(int dx)
    {
        return new Snippet.Position(X + dx, Y);
    }

    public static implicit operator Snippet.Point(Snippet.Position value)
    {
        var copy = default(Snippet.Point);
        __Snippet_Point_X(ref copy) = __Snippet_Position_X(ref value);
        __Snippet_Point_Y(ref copy) = __Snippet_Position_Y(ref value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "X")]
    private static extern ref int __Snippet_Position_X(ref Snippet.Position target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Y")]
    private static extern ref int __Snippet_Position_Y(ref Snippet.Position target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "X")]
    private static extern ref int __Snippet_Point_X(ref Snippet.Point target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Y")]
    private static extern ref int __Snippet_Point_Y(ref Snippet.Point target);

    public static explicit operator Snippet.Position(Snippet.Point value)
    {
        var copy = default(Snippet.Position);
        __Snippet_Position_X(ref copy) = __Snippet_Point_X(ref value);
        __Snippet_Position_Y(ref copy) = __Snippet_Point_Y(ref value);
        return copy;
    }
}
