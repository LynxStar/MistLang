using Standard;

namespace Snippet;

public class FixedArray<TValue, TSize>
    where TSize : IConstIntegral
{
    public static int Size => ((int)TSize.Value);

    public int GetSize()
    {
        return ((int)TSize.Value);
    }

    public string Describe()
    {
        return System.FormattableString.Invariant($"FixedArray<Size={((int)TSize.Value)}>");
    }

    public FixedArray()
    {
    }

    public static FixedArray<TValue, TSize> __CreateDefault()
    {
        return new FixedArray<TValue, TSize>();
    }
}

public class Matrix<TRows, TCols>
    where TRows : IConstIntegral
    where TCols : IConstIntegral
{
    public static int Rows => ((int)TRows.Value);
    public static int Cols => ((int)TCols.Value);

    public int GetRows()
    {
        return ((int)TRows.Value);
    }

    public int GetCols()
    {
        return ((int)TCols.Value);
    }

    public Matrix()
    {
    }

    public static Matrix<TRows, TCols> __CreateDefault()
    {
        return new Matrix<TRows, TCols>();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.FixedArray<int, Const_5> arr = new Snippet.FixedArray<int, Const_5>();
        System.Console.WriteLine(arr.Describe());
        Snippet.Matrix<Const_3, Const_4> mat = new Snippet.Matrix<Const_3, Const_4>();
        System.Console.WriteLine(System.FormattableString.Invariant($"Matrix<{mat.GetRows()}x{mat.GetCols()}>"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
