using System;

namespace DependentTypes;

public class TypeIdentity
{
    public static string AcceptFive(FixedBuffer<Const_Int_5> buf)
    {
        return $"Accepted FixedBuffer<{buf.GetSize()}>";
    }

    public static TypeIdentity __CreateDefault()
    {
        var inst = new TypeIdentity();
        return inst;
    }
}
