namespace Mist.Driver;

internal static class SyntaxTreeMetrics
{
    public static int CountNodes(Storm.Runtime.SyntaxNode root)
    {
        var count = 1;
        foreach(var child in root.Children())
        {
            if(child is not null)
            {
                count += CountNodes(child);
            }
        }

        return count;
    }
}