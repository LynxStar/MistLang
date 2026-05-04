using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Contracts;

// Feature: unknown-type (Phase C, Task 16.1)
//
// The Reachability Analyzer (Phase C) classifies every member of every generic
// type declaration w.r.t. each of its type parameters. The classification
// answers one question: "if this type parameter were bound to `unknown` at a
// call site, would invoking this member write to a storage slot whose declared
// type transitively contains T?" If yes, the member must be filtered on
// `Closed_Generic_With_Unknown` receivers; if no, it is safe to expose.
//
// This file defines the *contract* exposed to the Type Checker. The concrete
// classification algorithm, fixed-point iteration, and call-graph construction
// are implemented by `ReachabilityAnalyzer` (Task 17–19). Phase D and E do not
// need to know those details — they only need to look up classifications.

/// <summary>
/// Two-point lattice recording whether a member of a generic type writes to
/// storage whose declared type transitively contains a specific type
/// parameter <c>T</c>.
/// </summary>
/// <remarks>
/// <para>
/// The lattice has height 2:
/// <c><see cref="DoesNotWriteT"/> &lt; <see cref="WritesT"/></c>. Classifications
/// only ever move upward during fixed-point iteration (monotonicity,
/// Requirement 9.10).
/// </para>
/// <para>
/// The per-member classification is produced <em>per generic definition</em>,
/// not per closed construction: the Type Checker applies a classification of
/// <c>List&lt;T&gt;.Add</c> to receivers of <c>List&lt;unknown&gt;</c>,
/// <c>List&lt;int&gt;</c>, and <c>List&lt;string&gt;</c> uniformly
/// (filtering only when the type argument at T's position is
/// <see cref="UnknownType"/>).
/// </para>
/// </remarks>
public enum MemberClassification
{
    /// <summary>
    /// Invoking this member never writes to a storage slot whose declared type
    /// transitively contains the given type parameter <c>T</c>. Safe to expose
    /// on a <c>Closed_Generic_With_Unknown</c> receiver.
    /// </summary>
    DoesNotWriteT,

    /// <summary>
    /// Invoking this member would write to a storage slot whose declared type
    /// transitively contains the given type parameter <c>T</c>. Must be
    /// filtered on a <c>Closed_Generic_With_Unknown</c> receiver — exposing it
    /// would allow a write through an <c>unknown</c>-typed slot.
    /// </summary>
    WritesT,
}

/// <summary>
/// Classification pair for properties and indexers (Requirement 9.11). Reads
/// and writes are tracked separately because a property with a pure getter and
/// a storage-writing setter must filter the write but still expose the read —
/// e.g., <c>List&lt;unknown&gt;.Count</c> is a safe read even though a generic
/// collection may have T-writing setters elsewhere.
/// </summary>
/// <param name="Read">
/// Classification of the getter body. Almost always
/// <see cref="MemberClassification.DoesNotWriteT"/>; it is only
/// <see cref="MemberClassification.WritesT"/> if the getter's body performs a
/// write into T-typed storage (a rare but legal pattern).
/// </param>
/// <param name="Write">
/// Classification of the setter body and any backing storage. A property with
/// an auto-implemented setter over a T-typed backing field is
/// <see cref="MemberClassification.WritesT"/>; a setter whose body only writes
/// to non-T storage is <see cref="MemberClassification.DoesNotWriteT"/>.
/// </param>
public readonly record struct PropertyClassification(
    MemberClassification Read,
    MemberClassification Write);

/// <summary>
/// Lookup table produced by the Reachability Analyzer. The Type Checker
/// consults this map during member-access resolution when the receiver's
/// static type is a <c>Closed_Generic_With_Unknown</c>, to decide whether a
/// member should be filtered out (Requirement 10.1–10.4).
/// </summary>
/// <remarks>
/// <para>
/// The map is keyed by <c>(member, typeParameter)</c>. Callers must pass the
/// <em>generic definition's</em> member symbol (the one registered in the
/// generic's member scope), not a substituted copy — the classification is
/// computed once per definition and applies uniformly across all closed
/// constructions.
/// </para>
/// <para>
/// The map is always total over the members of every generic type
/// declaration it was built for; querying a member that belongs to a
/// non-generic type or to a different generic definition is a programming
/// error and implementations may throw.
/// </para>
/// </remarks>
public interface IReachabilityMap
{
    /// <summary>
    /// Returns the classification of a method or constructor with respect to
    /// the given type parameter. Methods do not have distinct read/write
    /// sides — a method either writes T or it does not — so this returns a
    /// single <see cref="MemberClassification"/>.
    /// </summary>
    /// <param name="member">
    /// A method or constructor symbol from the declaring generic's member
    /// scope. <see cref="ISymbol.Kind"/> must be
    /// <see cref="SymbolKind.Method"/> or <see cref="SymbolKind.Constructor"/>.
    /// </param>
    /// <param name="typeParameter">
    /// A type parameter of the generic type that declares
    /// <paramref name="member"/>.
    /// </param>
    MemberClassification GetMethodClassification(ISymbol member, TypeParameterType typeParameter);

    /// <summary>
    /// Returns the write-classification of a field with respect to the given
    /// type parameter. Reading a field never writes to storage, so the read
    /// side is always <see cref="MemberClassification.DoesNotWriteT"/> and is
    /// not represented in the interface.
    /// </summary>
    /// <param name="member">
    /// A field symbol from the declaring generic's member scope.
    /// <see cref="ISymbol.Kind"/> must be <see cref="SymbolKind.Field"/>.
    /// </param>
    /// <param name="typeParameter">
    /// A type parameter of the generic type that declares
    /// <paramref name="member"/>.
    /// </param>
    MemberClassification GetFieldClassification(ISymbol member, TypeParameterType typeParameter);

    /// <summary>
    /// Returns the (read, write) classification pair of a property or indexer
    /// with respect to the given type parameter. Required by Requirement 9.11:
    /// a property that reads without writing T must remain accessible on a
    /// <c>Closed_Generic_With_Unknown</c> receiver even when its setter is
    /// filtered.
    /// </summary>
    /// <param name="member">
    /// A property or indexer symbol from the declaring generic's member scope.
    /// <see cref="ISymbol.Kind"/> must be <see cref="SymbolKind.Property"/> or
    /// <see cref="SymbolKind.Indexer"/>.
    /// </param>
    /// <param name="typeParameter">
    /// A type parameter of the generic type that declares
    /// <paramref name="member"/>.
    /// </param>
    PropertyClassification GetPropertyClassification(ISymbol member, TypeParameterType typeParameter);
}
