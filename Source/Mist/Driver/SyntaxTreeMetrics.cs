namespace Mist.Driver;

/// <summary>
/// Utility for computing metrics over syntax trees.
/// </summary>
internal static class SyntaxTreeMetrics
{
    /// <summary>
    /// Counts all nodes in the tree rooted at <paramref name="root"/>,
    /// including the root itself.
    /// </summary>
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