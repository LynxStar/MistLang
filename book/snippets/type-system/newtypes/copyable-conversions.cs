using Standard;

namespace Snippet;

public class Money
{
    public Money(int amount)
    {
        this.Amount = default(int);
        this.Amount = amount;
    }

    public int Amount { get; }
}

internal class Cash
{
    public Cash(int amount)
    {
        this.Amount = default(int);
        this.Amount = amount;
    }

    public int Amount { get; }

    public static implicit operator Snippet.Money(Snippet.Cash value)
    {
        if(value is null)
        {
            return null!;
        }

        var copy = (Snippet.Money)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Money));
        __Snippet_Money_Amount_BackingField(copy) = __Snippet_Cash_Amount_BackingField(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "<Amount>k__BackingField")]
    private static extern ref int __Snippet_Cash_Amount_BackingField(Snippet.Cash target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "<Amount>k__BackingField")]
    private static extern ref int __Snippet_Money_Amount_BackingField(Snippet.Money target);

    public static explicit operator Snippet.Cash(Snippet.Money value)
    {
        if(value is null)
        {
            return null!;
        }

        var copy = (Snippet.Cash)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Cash));
        __Snippet_Cash_Amount_BackingField(copy) = __Snippet_Money_Amount_BackingField(value);
        return copy;
    }
}

#pragma warning disable CS0660, CS0661
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal readonly struct Meters : Standard.IClonable<Snippet.Meters>
{
    public readonly double Value;

    public Meters(double value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Meters(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static Meters __FromParts(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Meters(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public Snippet.Meters Clone()
        => new Snippet.Meters(Value);

    public static implicit operator double(Snippet.Meters value)
        => value.Value;

    public static explicit operator Snippet.Meters(double value)
        => new Snippet.Meters(value);

    public static Snippet.Meters operator +(Snippet.Meters a, Snippet.Meters b)
        => new Snippet.Meters((a.Value + b.Value));

    public static Snippet.Meters operator -(Snippet.Meters a, Snippet.Meters b)
        => new Snippet.Meters((a.Value - b.Value));

    public static Snippet.Meters operator *(Snippet.Meters a, Snippet.Meters b)
        => new Snippet.Meters((a.Value * b.Value));

    public static Snippet.Meters operator /(Snippet.Meters a, Snippet.Meters b)
        => new Snippet.Meters((a.Value / b.Value));

    public static Snippet.Meters operator %(Snippet.Meters a, Snippet.Meters b)
        => new Snippet.Meters((a.Value % b.Value));

    public static bool operator <(Snippet.Meters a, Snippet.Meters b)
        => (a.Value < b.Value);

    public static bool operator >(Snippet.Meters a, Snippet.Meters b)
        => (a.Value > b.Value);

    public static bool operator <=(Snippet.Meters a, Snippet.Meters b)
        => (a.Value <= b.Value);

    public static bool operator >=(Snippet.Meters a, Snippet.Meters b)
        => (a.Value >= b.Value);

    public static bool operator ==(Snippet.Meters a, Snippet.Meters b)
        => (a.Value == b.Value);

    public static bool operator !=(Snippet.Meters a, Snippet.Meters b)
        => (a.Value != b.Value);

    public static Snippet.Meters operator -(Snippet.Meters a)
        => new Snippet.Meters((-a.Value));
}
#pragma warning restore CS0660, CS0661

#pragma warning disable CS0660, CS0661
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal readonly struct Feet : Standard.IClonable<Snippet.Feet>
{
    public readonly double Value;

    public Feet(double value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Feet(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static Feet __FromParts(double p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Feet(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public Snippet.Feet Clone()
        => new Snippet.Feet(Value);

    public static implicit operator double(Snippet.Feet value)
        => value.Value;

    public static explicit operator Snippet.Feet(double value)
        => new Snippet.Feet(value);

    public static Snippet.Feet operator +(Snippet.Feet a, Snippet.Feet b)
        => new Snippet.Feet((a.Value + b.Value));

    public static Snippet.Feet operator -(Snippet.Feet a, Snippet.Feet b)
        => new Snippet.Feet((a.Value - b.Value));

    public static Snippet.Feet operator *(Snippet.Feet a, Snippet.Feet b)
        => new Snippet.Feet((a.Value * b.Value));

    public static Snippet.Feet operator /(Snippet.Feet a, Snippet.Feet b)
        => new Snippet.Feet((a.Value / b.Value));

    public static Snippet.Feet operator %(Snippet.Feet a, Snippet.Feet b)
        => new Snippet.Feet((a.Value % b.Value));

    public static bool operator <(Snippet.Feet a, Snippet.Feet b)
        => (a.Value < b.Value);

    public static bool operator >(Snippet.Feet a, Snippet.Feet b)
        => (a.Value > b.Value);

    public static bool operator <=(Snippet.Feet a, Snippet.Feet b)
        => (a.Value <= b.Value);

    public static bool operator >=(Snippet.Feet a, Snippet.Feet b)
        => (a.Value >= b.Value);

    public static bool operator ==(Snippet.Feet a, Snippet.Feet b)
        => (a.Value == b.Value);

    public static bool operator !=(Snippet.Feet a, Snippet.Feet b)
        => (a.Value != b.Value);

    public static Snippet.Feet operator -(Snippet.Feet a)
        => new Snippet.Feet((-a.Value));
}
#pragma warning restore CS0660, CS0661

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Position position = new Snippet.Position(1, 2);
        Snippet.Point point = position;
        point.X = 99;
        Snippet.Position entered = ((Snippet.Position)(point));
        Snippet.Position moved = entered.Moved(5);
        Snippet.Money money = new Snippet.Cash(7);
        Snippet.Cash cash = ((Snippet.Cash)(money));
        Snippet.Feet feet = ((Snippet.Feet)(3.5));
        Snippet.Meters meters = ((Snippet.Meters)((double)(feet)));
        System.Console.WriteLine(System.FormattableString.Invariant($"position.X={position.X} point.X={point.X} moved.X={moved.X}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"money={money.Amount} cash={cash.Amount} meters={meters}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
