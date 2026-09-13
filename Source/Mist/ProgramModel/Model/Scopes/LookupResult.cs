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
/// <param name="BaseConstructions">
/// See <see cref="FieldResult.BaseConstructions"/>; empty for everything but a nested type reached
/// through a base of the type the lookup started from.
/// </param>
internal sealed record TypeResult(IMistType Type, IReadOnlyList<ConstructedType>? BaseConstructions = null) : LookupResult;

/// <summary>Overload resolution is deferred to the invocation site.</summary>
internal sealed record MethodGroupResult(
    IReadOnlyList<Method> Methods,
    IReadOnlyDictionary<Method, IReadOnlyList<ConstructedType>>? Paths = null) : LookupResult;

/// <param name="DeclaringType">
/// Where it was found, which is not necessarily where the lookup started -- it may be a base type.
/// </param>
/// <param name="BaseConstructions">
/// The constructed bases stepped through to reach it, outermost first, empty when it was declared on
/// the type the lookup started from. Recorded rather than applied because this layer holds no
/// substitution: <c>Derived&lt;T&gt; : Base&lt;T&gt;</c> binds Base's parameter to Derived's, which is
/// only half of what a member's type needs, the receiver's own arguments being the other half and
/// known only to the caller. Composing the two is
/// <see cref="Construction.Binding.TypeSubstitution.ForMember"/>.
/// </param>
internal sealed record FieldResult(
    Field Field, IMistType DeclaringType, IReadOnlyList<ConstructedType> BaseConstructions) : LookupResult;

/// <param name="DeclaringType">See <see cref="FieldResult.DeclaringType"/>.</param>
/// <param name="BaseConstructions">See <see cref="FieldResult.BaseConstructions"/>.</param>
internal sealed record PropertyResult(
    Property Property, IMistType DeclaringType, IReadOnlyList<ConstructedType> BaseConstructions) : LookupResult;

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
internal sealed record ReceiverResult(IMistType Type) : LookupResult;

/// <summary>
/// <c>this</c> where there is an enclosing type but no instance of it. Distinct from
/// <see cref="NotFoundResult"/>, which is <c>this</c> with no enclosing type at all, so the two
/// misplacements keep their own diagnostics without the binder inspecting the chain.
/// </summary>
internal sealed record StaticReceiverResult : LookupResult;

/// <summary>Several declarations in one scope, or two imports, matched the same name.</summary>
internal sealed record AmbiguousResult(IReadOnlyList<LookupResult> Candidates) : LookupResult;

internal sealed record NotFoundResult : LookupResult;
/// <summary>
/// A property found on an extension block rather than on the receiver's own type. Distinct from
/// <see cref="PropertyResult"/> because C# has no extension property: the ACCESS becomes an
/// invocation of the carrier's generated getter, so the site is reclassified rather than emitted as
/// written, and the block comes along because only it knows the carrier's name.
/// </summary>
internal sealed record ExtensionPropertyResult(Property Property, ExtensionBlock Block) : LookupResult;