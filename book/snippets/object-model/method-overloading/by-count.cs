using System;

namespace Snippet;

public class MathHelper
{
    public static int Add(int a, int b)
    {
        return a + b;
    }

    public static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    public MathHelper()
    {
    }

    public static MathHelper __CreateDefault()
    {
        return new MathHelper();
    }
}
