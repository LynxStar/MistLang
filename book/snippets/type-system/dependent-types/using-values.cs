using System;

namespace DependentTypes;

[DependentType("Rows", typeof(int))]
[DependentType("Cols", typeof(int))]
public class Matrix<TRows, TCols>
    where TRows : IConstInt
    where TCols : IConstInt
{
    public static int Rows => TRows.Value;

    public static int Cols => TCols.Value;

    public string Describe() { return $"Matrix<{Rows},{Cols}>"; }
}
