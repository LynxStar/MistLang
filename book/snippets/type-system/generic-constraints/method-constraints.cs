using Standard;

namespace Snippet;

internal class MethodConstraints
{
    public static T CreateItem<T>(System.Func<T> __construct_T_0)
        where T : new()
    {
        return __construct_T_0();
    }

    public MethodConstraints()
    {
    }

    public static MethodConstraints __CreateDefault()
    {
        return new MethodConstraints();
    }
}
