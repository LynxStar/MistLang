using System;

namespace Snippet;

public class FixedArray<TValue, TSize>
    where TSize : IConstInt
{
    public int GetSize()
    {
        return TSize.Value;
    }

    public string Describe()
    {
        return $"FixedArray<Size={TSize.Value}>";
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
    where TRows : IConstInt
    where TCols : IConstInt
{
    public int GetRows()
    {
        return TRows.Value;
    }

    public int GetCols()
    {
        return TCols.Value;
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
        Snippet.FixedArray<int, Const_Int_5> arr = new Snippet.FixedArray<int, Const_Int_5>();
        Console.WriteLine(arr.Describe());
        Snippet.Matrix<Const_Int_3, Const_Int_4> mat = new Snippet.Matrix<Const_Int_3, Const_Int_4>();
        Console.WriteLine($"Matrix<{mat.GetRows()}x{mat.GetCols()}>");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
