using Standard;

namespace Snippet;

public class Box<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private T item;

    public Box(T item)
    {
        this.item = item;
    }

    public T Get()
    {
        return this.item;
    }

    public Snippet.Box<T> Again()
    {
        return new Snippet.Box<T>(this.item);
    }

    public Snippet.Box<string> Label()
    {
        return new Snippet.Box<string>($"<{MistInvariant.Text(this.item)}>");
    }
}

internal class IntBox
{
    private int item;

    public IntBox(int item)
    {
        this.item = default(int);
        this.item = item;
    }

    public int Get()
    {
        return this.item;
    }

    public Snippet.IntBox Again()
    {
        return new Snippet.IntBox(this.item);
    }

    public Snippet.Box<string> Label()
    {
        return new Snippet.Box<string>(System.FormattableString.Invariant($"<{this.item}>"));
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Box<int>?(Snippet.IntBox? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Box<int>)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Box<int>));
        __Snippet_Box_Access<int>.__item(copy) = __Snippet_IntBox_item(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "item")]
    private static extern ref int __Snippet_IntBox_item(Snippet.IntBox target);

    private static class __Snippet_Box_Access<T>
    {
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "item")]
        public static extern ref T __item(Snippet.Box<T> target);
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.IntBox?(Snippet.Box<int>? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.IntBox)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.IntBox));
        __Snippet_IntBox_item(copy) = __Snippet_Box_Access<int>.__item(value);
        return copy;
    }
}

internal class Boxed<T>
{
    [System.Diagnostics.CodeAnalysis.AllowNull]
    private T item;

    public Boxed(T item)
    {
        this.item = item;
    }

    public T Get()
    {
        return this.item;
    }

    public Snippet.Boxed<T> Again()
    {
        return new Snippet.Boxed<T>(this.item);
    }

    public Snippet.Boxed<string> Label()
    {
        return new Snippet.Boxed<string>($"<{MistInvariant.Text(this.item)}>");
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Box<T>?(Snippet.Boxed<T>? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Box<T>)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Box<T>));
        __Snippet_Boxed_1__Snippet_Box_Access<T>.__item(copy) = __Snippet_Boxed_item(value);
        return copy;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "item")]
    private static extern ref T __Snippet_Boxed_item(Snippet.Boxed<T> target);

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]
    public static explicit operator Snippet.Boxed<T>?(Snippet.Box<T>? value)
    {
        if(value is null)
        {
            return null;
        }

        var copy = (Snippet.Boxed<T>)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Snippet.Boxed<T>));
        __Snippet_Boxed_item(copy) = __Snippet_Boxed_1__Snippet_Box_Access<T>.__item(value);
        return copy;
    }
}

file static class __Snippet_Boxed_1__Snippet_Box_Access<T>
{
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "item")]
    public static extern ref T __item(Snippet.Box<T> target);
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.IntBox box = new Snippet.IntBox(5);
        Snippet.IntBox again = box.Again();
        Snippet.Box<string> label = box.Label();
        Snippet.Boxed<bool> boxed = new Snippet.Boxed<bool>(true);
        Snippet.Boxed<string> relabelled = boxed.Label();
        System.Console.WriteLine(System.FormattableString.Invariant($"again={again.Get()} label={label.Get()} relabelled={relabelled.Get()}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
