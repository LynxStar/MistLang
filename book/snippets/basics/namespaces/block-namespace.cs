using Standard;

namespace Namespaces.Helpers;

public class MathHelper
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public MathHelper()
    {
    }

    public static MathHelper __CreateDefault()
    {
        return new MathHelper();
    }
}
