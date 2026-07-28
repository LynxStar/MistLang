using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// What a name resolved to.
/// </summary>
internal abstract record LookupResult;

internal sealed record VariableResult(LocalVariable Variable) : LookupResult;

/// <summary>A nested type, a type parameter, or a type found by namespace.</summary>
internal sealed record TypeResult(IMistType Type) : LookupResult;

/// <summary>Overload resolution is deferred to the invocation site.</summary>
internal sealed record MethodGroupResult(IReadOnlyList<Method> Methods) : LookupResult;

/// <param name="DeclaringType">
/// Where it was found, which is not necessarily where the lookup started -- it may be a base type.
/// </param>
internal sealed record FieldResult(Field Field, IMistType DeclaringType) : LookupResult;

/// <param name="DeclaringType">See <see cref="FieldResult.DeclaringType"/>.</param>
internal sealed record PropertyResult(Property Property, IMistType DeclaringType) : LookupResult;

/// <summary>
/// A variant of an enum. Not a field: there is no instance to read it from, and its type is the
/// enum itself.
/// </summary>
/// <param name="DeclaringType">The enum, which is also the expression's type.</param>
internal sealed record EnumMemberResult(EnumMember Member, IMistType DeclaringType) : LookupResult;

/// <summary>
/// Neither a value nor a type, only meaningful as the receiver of a further qualification.
/// <see cref="NamespaceName"/> carries what has accumulated so far for the next step to probe.
/// </summary>
internal sealed record NamespaceResult(string NamespaceName) : LookupResult;

/// <summary>
/// The enclosing instance, for <c>this</c>. Carries the innermost enclosing type, since that is the
/// scope that answered.
/// </summary>
internal sealed record ReceiverResult(MemberType Type) : LookupResult;

/// <summary>
/// <c>this</c> where there is an enclosing type but no instance of it. Distinct from
/// <see cref="NotFoundResult"/>, which is <c>this</c> with no enclosing type at all, so the two
/// misplacements keep their own diagnostics without the binder inspecting the chain.
/// </summary>
internal sealed record StaticReceiverResult : LookupResult;

/// <summary>Several declarations in one scope, or two imports, matched the same name.</summary>
internal sealed record AmbiguousResult(IReadOnlyList<LookupResult> Candidates) : LookupResult;

internal sealed record NotFoundResult : LookupResult;