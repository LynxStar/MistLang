namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A user type's sub-sequence reached through offsets C# computes from the receiver's Length, or its Count
/// when it has no Length: a list pattern's slice read through the type's Range indexer, or any slice read
/// through its Slice(int, int), which C# hands the start and the length rather than the range. Exactly one of
/// <see cref="Indexer"/> and <see cref="Method"/> is set, and a range written into a Range indexer is an
/// ordinary indexer access instead, the count taking no part in it.
/// </summary>
internal sealed record CountedSlice(Property Count, Indexer? Indexer, Method? Method);