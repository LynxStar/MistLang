using Standard;

namespace Snippet;

public class TypeIdentity
{
    public static string AcceptFive(Snippet.FixedBuffer<Const_5> buf)
    {
        return System.FormattableString.Invariant($"Accepted FixedBuffer<{buf.GetSize()}>");
    }

    public TypeIdentity()
    {
    }

    public static TypeIdentity __CreateDefault()
    {
        return new TypeIdentity();
    }
}
