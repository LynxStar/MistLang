using Standard;

namespace Snippet;

public class Reading
{
    public int Value;
    public string Unit;

    public Reading(int value, string unit)
    {
        this.Value = default(int);
        this.Unit = "";
        this.Value = value;
        this.Unit = unit;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{this.Value} {this.Unit}");
    }
}

public class Recorded
{
    public readonly int Value;
    public readonly string Unit;

    public Recorded(int value, string unit)
    {
        this.Value = default(int);
        this.Unit = "";
        this.Value = value;
        this.Unit = unit;
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"{this.Value} {this.Unit}");
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Reading?(Snippet.Recorded? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Reading)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Reading));
        __Snippet_Reading_Value(copy) = __Snippet_Recorded_Value(value);
        __Snippet_Reading_Unit(copy) = __Snippet_Recorded_Unit(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Value")]
    private static extern ref int __Snippet_Recorded_Value(Snippet.Recorded target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Unit")]
    private static extern ref string __Snippet_Recorded_Unit(Snippet.Recorded target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Value")]
    private static extern ref int __Snippet_Reading_Value(Snippet.Reading target);
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "Unit")]
    private static extern ref string __Snippet_Reading_Unit(Snippet.Reading target);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Recorded?(Snippet.Reading? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Recorded)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Recorded));
        __Snippet_Recorded_Value(copy) = __Snippet_Reading_Value(value);
        __Snippet_Recorded_Unit(copy) = __Snippet_Reading_Unit(value);
        return copy;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.Reading live = new Snippet.Reading(20, "C");
        live.Value = 21;
        Snippet.Recorded recorded = new Snippet.Recorded(19, "C");
        System.Console.WriteLine($"live={live.Describe()} recorded={recorded.Describe()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
