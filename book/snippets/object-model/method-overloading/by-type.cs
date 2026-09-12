using Standard;

namespace Snippet;

public class MathHelper
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public static double Add(double a, double b)
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
