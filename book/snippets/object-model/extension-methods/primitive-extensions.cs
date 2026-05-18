using System;

namespace StringUtils;

public static class StringExtensions_primitive_extensions_L5
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
