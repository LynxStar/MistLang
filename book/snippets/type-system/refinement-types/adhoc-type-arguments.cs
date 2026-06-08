using System;

namespace Snippet;

[DependentType("Size", typeof(int))]
public class FixedArray<TValue, TSize>
    where TSize : IConstInt
{
    public static int Size => TSize.Value;

    public int GetSize()
    {
        return TSize.Value;
    }

    public string Describe()
    {
        return $"FixedArray<Size={TSize.Value}>";
    }
}
[DependentType("Rows", typeof(int))]
[DependentType("Cols", typeof(int))]
public class Matrix<TRows, TCols>
    where TRows : IConstInt
    where TCols : IConstInt
{
    public static int Rows => TRows.Value;

    public static int Cols => TCols.Value;

    public int GetRows()
    {
        return TRows.Value;
    }

    public int GetCols()
    {
        return TCols.Value;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var arr = new FixedArray<int, Const_Int_5>();
        Console.WriteLine(arr.Describe());
        var mat = new Matrix<Const_Int_3, Const_Int_4>();
        Console.WriteLine($"Matrix<{mat.GetRows()}x{mat.GetCols()}>");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
