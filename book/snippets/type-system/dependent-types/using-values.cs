using Standard;

namespace Snippet;

public class Matrix<TRows, TCols>
    where TRows : IConstInt
    where TCols : IConstInt
{
    public string Describe()
    {
        return $"Matrix<{TRows.Value},{TCols.Value}>";
    }

    public Matrix()
    {
    }

    public static Matrix<TRows, TCols> __CreateDefault()
    {
        return new Matrix<TRows, TCols>();
    }
}
