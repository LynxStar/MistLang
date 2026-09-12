using Standard;

namespace Snippet;

public class TypeIdentity
{
    public static string AcceptFive(Snippet.FixedBuffer<Const_Int_5> buf)
    {
        return $"Accepted FixedBuffer<{buf.GetSize()}>";
    }

    public TypeIdentity()
    {
    }

    public static TypeIdentity __CreateDefault()
    {
        return new TypeIdentity();
    }
}
