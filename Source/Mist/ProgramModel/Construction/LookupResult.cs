using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// The result of resolving a name through the unified lookup.
/// </summary>
internal abstract record LookupResult;

/// <summary>
/// The name resolved to a local variable or parameter.
/// </summary>
internal sealed record VariableResult(LocalVariable Variable) : LookupResult;

/// <summary>
/// The name resolved to a type.
/// </summary>
internal sealed record TypeResult(IMistType Type) : LookupResult;

/// <summary>
/// The name resolved to multiple candidates at the same scope level.
/// </summary>
internal sealed record AmbiguousResult(IReadOnlyList<LookupResult> Candidates) : LookupResult;

/// <summary>
/// The name could not be resolved in any scope or via namespace imports.
/// </summary>
internal sealed record NotFoundResult : LookupResult;