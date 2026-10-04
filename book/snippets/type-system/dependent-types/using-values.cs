using Standard;

namespace Snippet;

public class Matrix<TRows, TCols>
    where TRows : IConstIntegral
    where TCols : IConstIntegral
{
    public static int Rows => ((int)TRows.Value);
    public static int Cols => ((int)TCols.Value);

    public string Describe()
    {
        return System.FormattableString.Invariant($"Matrix<{((int)TRows.Value)},{((int)TCols.Value)}>");
    }

    public Matrix()
    {
    }

    public static Matrix<TRows, TCols> __CreateDefault()
    {
        return new Matrix<TRows, TCols>();
    }
}
