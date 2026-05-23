using Standard;
using System;

namespace Namespaces.Utilities;

public class StringHelper
{
    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }

    public static StringHelper __CreateDefault()
    {
        var inst = new StringHelper();
        return inst;
    }
}
