using Standard;

namespace Snippet;

public class Money
{
    public Money(int amount)
    {
        this.Amount = amount;
    }

    public int Amount { get; }
}

internal class Cash
{
    public Cash(int amount)
    {
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

internal class Coins
{
    public Coins(int amount)
    {
        this.Amount = amount;
    }

    public int Amount { get; }

    public static implicit operator Snippet.Cash(Snippet.Coins value)
    {
        if(value is null)
        {
            return null!;
        }

        var copy = (Snippet.Cash)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Cash));
        __Snippet_Cash_Amount_BackingField(copy) = __Snippet_Coins_Amount_BackingField(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "<Amount>k__BackingField")]
    private static extern ref int __Snippet_Coins_Amount_BackingField(Snippet.Coins target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "<Amount>k__BackingField")]
    private static extern ref int __Snippet_Cash_Amount_BackingField(Snippet.Cash target);

    public static explicit operator Snippet.Coins(Snippet.Cash value)
    {
        if(value is null)
        {
            return null!;
        }

        var copy = (Snippet.Coins)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Coins));
        __Snippet_Coins_Amount_BackingField(copy) = __Snippet_Cash_Amount_BackingField(value);
        return copy;
    }
}

#pragma warning disable CS0660, CS0661
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal readonly struct Age : Standard.IClonable<Snippet.Age>
{
    public readonly int Value;

    public Age(int value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Age(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static Age __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Age(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public Snippet.Age Clone()
        => new Snippet.Age(Value);

    public bool IsEven()
    {
        return this % 2 == 0;
    }

    public static implicit operator long(Snippet.Age value)
        => (long)(value.Value);

    public static implicit operator float(Snippet.Age value)
        => (float)(value.Value);

    public static implicit operator double(Snippet.Age value)
        => (double)(value.Value);

    public static implicit operator decimal(Snippet.Age value)
        => (decimal)(value.Value);

    public static implicit operator int(Snippet.Age value)
        => value.Value;

    public static explicit operator Snippet.Age(int value)
        => new Snippet.Age(value);

    public static Snippet.Age operator +(Snippet.Age a, Snippet.Age b)
        => new Snippet.Age((a.Value + b.Value));

    public static Snippet.Age operator -(Snippet.Age a, Snippet.Age b)
        => new Snippet.Age((a.Value - b.Value));

    public static Snippet.Age operator *(Snippet.Age a, Snippet.Age b)
        => new Snippet.Age((a.Value * b.Value));

    public static Snippet.Age operator /(Snippet.Age a, Snippet.Age b)
        => new Snippet.Age((a.Value / b.Value));

    public static Snippet.Age operator %(Snippet.Age a, Snippet.Age b)
        => new Snippet.Age((a.Value % b.Value));

    public static bool operator <(Snippet.Age a, Snippet.Age b)
        => (a.Value < b.Value);

    public static bool operator >(Snippet.Age a, Snippet.Age b)
        => (a.Value > b.Value);

    public static bool operator <=(Snippet.Age a, Snippet.Age b)
        => (a.Value <= b.Value);

    public static bool operator >=(Snippet.Age a, Snippet.Age b)
        => (a.Value >= b.Value);

    public static bool operator ==(Snippet.Age a, Snippet.Age b)
        => (a.Value == b.Value);

    public static bool operator !=(Snippet.Age a, Snippet.Age b)
        => (a.Value != b.Value);

    public static Snippet.Age operator -(Snippet.Age a)
        => new Snippet.Age((-a.Value));
}
#pragma warning restore CS0660, CS0661

#pragma warning disable CS0660, CS0661
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal readonly struct Years : Standard.IClonable<Snippet.Years>
{
    public readonly int Value;

    public Years(int value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Years(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        this.Value = p0;
    }

    internal static Years __FromParts(int p0, Mist.Emission.CSharp.__StructInit marker)
    {
        return new Years(p0, default(Mist.Emission.CSharp.__StructInit));
    }

    public Snippet.Years Clone()
        => new Snippet.Years(Value);

    public bool IsEven()
    {
        return ((Snippet.Age)((Snippet.Years)(this))) % 2 == 0;
    }

    public static implicit operator long(Snippet.Years value)
        => (long)(value.Value);

    public static implicit operator float(Snippet.Years value)
        => (float)(value.Value);

    public static implicit operator double(Snippet.Years value)
        => (double)(value.Value);

    public static implicit operator decimal(Snippet.Years value)
        => (decimal)(value.Value);

    public static implicit operator Snippet.Age(Snippet.Years value)
        => (Snippet.Age)(value.Value);

    public static explicit operator Snippet.Years(Snippet.Age value)
        => new Snippet.Years((int)(value));

    public static Snippet.Years operator +(Snippet.Years a, Snippet.Years b)
        => new Snippet.Years((a.Value + b.Value));

    public static Snippet.Years operator -(Snippet.Years a, Snippet.Years b)
        => new Snippet.Years((a.Value - b.Value));

    public static Snippet.Years operator *(Snippet.Years a, Snippet.Years b)
        => new Snippet.Years((a.Value * b.Value));

    public static Snippet.Years operator /(Snippet.Years a, Snippet.Years b)
        => new Snippet.Years((a.Value / b.Value));

    public static Snippet.Years operator %(Snippet.Years a, Snippet.Years b)
        => new Snippet.Years((a.Value % b.Value));

    public static bool operator <(Snippet.Years a, Snippet.Years b)
        => (a.Value < b.Value);

    public static bool operator >(Snippet.Years a, Snippet.Years b)
        => (a.Value > b.Value);

    public static bool operator <=(Snippet.Years a, Snippet.Years b)
        => (a.Value <= b.Value);

    public static bool operator >=(Snippet.Years a, Snippet.Years b)
        => (a.Value >= b.Value);

    public static bool operator ==(Snippet.Years a, Snippet.Years b)
        => (a.Value == b.Value);

    public static bool operator !=(Snippet.Years a, Snippet.Years b)
        => (a.Value != b.Value);

    public static Snippet.Years operator -(Snippet.Years a)
        => new Snippet.Years((-a.Value));
}
#pragma warning restore CS0660, CS0661

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Coins coins = new Snippet.Coins(5);
        Snippet.Cash cash = coins;
        Snippet.Money money = ((Snippet.Cash)(coins));
        Snippet.Coins fromCash = ((Snippet.Coins)(cash));
        Snippet.Coins fromMoney = ((Snippet.Coins)(((Snippet.Cash)(money))));
        Snippet.Years years = ((Snippet.Years)(((Snippet.Age)(30))));
        Snippet.Age age = years;
        int raw = ((Snippet.Age)(years));
        System.Console.WriteLine(System.FormattableString.Invariant($"cash={cash.Amount} money={money.Amount} fromCash={fromCash.Amount} fromMoney={fromMoney.Amount}"));
        System.Console.WriteLine(System.FormattableString.Invariant($"years={years} age={age} raw={raw}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
