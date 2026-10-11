using Standard;

namespace Snippet;

public interface IPriced
{
    int Price();
}

public class Money : Snippet.IPriced
{
    public Money(int amount)
    {
        this.Amount = default(int);
        this.Amount = amount;
    }

    public int Amount { get; }

    public int ReadAmount()
    {
        return Amount;
    }

    int Snippet.IPriced.Price() => this.ReadAmount();
}

internal class Cash
{
    public Cash(int amount)
    {
        this.Amount = default(int);
        this.Amount = amount;
    }

    public int Amount { get; }

    public int ReadAmount()
    {
        return Amount;
    }

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

public class Program
{
    public static int Total(Snippet.IPriced first, Snippet.IPriced second)
    {
        return first.Price() + second.Price();
    }

    public static void Main(string[] args)
    {
        Snippet.Cash cash = new Snippet.Cash(7);
        Snippet.IPriced priced = ((Snippet.Money)(cash));
        object held = priced;
        System.Console.WriteLine(System.FormattableString.Invariant($"price={priced.Price()} total={Total(((Snippet.Money)(cash)), new Snippet.Money(3))}"));
        System.Console.WriteLine($"held is Money: {held is Snippet.Money}, held is Cash: {held is Snippet.Cash}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
