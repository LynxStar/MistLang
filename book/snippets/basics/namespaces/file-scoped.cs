using Standard;

namespace Namespaces.Utilities;

public class StringHelper
{
    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }

    public StringHelper()
    {
    }

    public static StringHelper __CreateDefault()
    {
        return new StringHelper();
    }
}
