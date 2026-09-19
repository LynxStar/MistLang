using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// An int indexer reached through a position C# computes rather than an argument written: an Index that
/// no indexer takes is counted back from the receiver's Length, or its Count when it has no Length, and a
/// list pattern reads each element at a position within that same count. The value the indexer's
/// parameter meets is therefore that property less the offset, which is what a rule judging the parameter
/// needs and what the expression, holding only the Index, cannot say.
/// </summary>
internal sealed record CountedIndexer(Indexer Indexer, IMistType ParameterType, Property Count);