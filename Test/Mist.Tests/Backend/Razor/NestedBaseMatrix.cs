namespace Mist.Tests.Backend.Razor;

internal static class NestedBaseMatrix
{
    public static readonly string[] Modes = ["NonGeneric", "Open", "String", "Constructed", "Nested"];

    public static string Argument(string mode)
    {
        return mode switch
        {
            "Open" => "T",
            "Constructed" => "Dictionary<int, string>",
            "Nested" => "Payload.Box<int>.Item<string>",
            _ => "string",
        };
    }

    public static string BaseText(int depth, string mode, bool genericInner)
    {
        if(mode == "NonGeneric")
        {
            return "MatrixOuter" + (depth == 2 ? ".Middle" : "") + (genericInner ? ".GenericInner<bool>" : ".Inner");
        }

        var outer = "MatrixBox<" + Argument(mode) + ">.";
        var middle = depth == 1 ? "" : "Middle<" + (mode == "Open" ? "U" : "int") + ">.";
        return outer + middle + (genericInner ? "GenericInner<" + (mode == "Open" ? "V" : "bool") + ">" : "Inner");
    }
}