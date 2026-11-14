using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// C# calls these members implicitly; a list slice shares its elements' count read, so count is checked once.
/// </summary>
internal static class CountedReads
{
    public static IReadOnlyList<object> Of(CountedIndexer? reads, CountedSlice? slice)
    {
        var members = new List<object>();

        foreach(var member in new object?[] { reads?.Count, reads?.Indexer, slice?.Count, slice?.Indexer, slice?.Method })
        {
            if(member is not null && !members.Contains(member, ReferenceEqualityComparer.Instance))
            {
                members.Add(member);
            }
        }

        return members;
    }
}