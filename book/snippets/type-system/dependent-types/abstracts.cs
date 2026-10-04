using Standard;

namespace Snippet;

public interface IFixedCollection<TSize>
    where TSize : IConstIntegral
{
    public static int Size => ((int)TSize.Value);

    int GetSize();

    string GetInfo();
}

public class TinyCollection : Snippet.IFixedCollection<Const_8>
{
    public int GetSize()
    {
        return 8;
    }

    public string GetInfo()
    {
        return System.FormattableString.Invariant($"TinyCollection(size={this.GetSize()})");
    }

    public TinyCollection()
    {
    }

    public static TinyCollection __CreateDefault()
    {
        return new TinyCollection();
    }
}
