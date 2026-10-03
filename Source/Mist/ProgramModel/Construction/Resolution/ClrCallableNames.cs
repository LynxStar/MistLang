namespace Mist.ProgramModel.Construction.Resolution;

internal static class ClrCallableNames
{
    public static string? StandardFamily(string stem, int arity)
    {
        return stem switch
        {
            "System.Func" when arity is >= 1 and <= 17 => "Standard.Func",
            "System.Action" when arity is >= 0 and <= 16 => "Standard.Action",
            _ => null,
        };
    }
}