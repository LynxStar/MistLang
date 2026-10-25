using Lib;

namespace Bridge;

public static class Gateway
{
    public static string Describe(Widget widget)
    {
        return "bridge:" + widget.Name;
    }
}
