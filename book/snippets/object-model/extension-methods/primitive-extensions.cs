using System;

namespace StringUtils;

public static class stringExtensions_StringUtils_L5
{
    public static bool IsNullOrEmpty(this string self)
    {
        return self.Length == 0;
    }

    public static string Reverse(this string self)
    {
        return self;
    }
}
