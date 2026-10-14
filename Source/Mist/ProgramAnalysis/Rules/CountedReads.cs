using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The members C# calls for a list pattern or a counted slice without the model spelling a call to any of
/// them, each once: a list pattern's slice reads the count its elements already read.
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