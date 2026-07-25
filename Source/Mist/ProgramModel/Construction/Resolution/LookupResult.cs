using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Resolution;

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
/// The name resolved to one or more methods (a method group).
/// Overload resolution is deferred to the invocation site.
/// </summary>
internal sealed record MethodGroupResult(IReadOnlyList<Method> Methods) : LookupResult;

/// <summary>
/// The name resolved to a namespace rather than to a member. A namespace is not a
/// value and not a type; it is only meaningful as the receiver of a further
/// qualification (<c>Simpliest</c> in <c>Simpliest.Program.RunDemo()</c>).
/// </summary>
/// <param name="NamespaceName">
/// The fully qualified namespace name accumulated so far, so the next qualification
/// step can probe <c>NamespaceName.Member</c> directly.
/// </param>
internal sealed record NamespaceResult(string NamespaceName) : LookupResult;

/// <summary>
/// The name resolved to multiple candidates at the same scope level.
/// </summary>
internal sealed record AmbiguousResult(IReadOnlyList<LookupResult> Candidates) : LookupResult;

/// <summary>
/// The name could not be resolved in any scope or via namespace imports.
/// </summary>
internal sealed record NotFoundResult : LookupResult;