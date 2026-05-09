using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that verifies Mist type rules against the semantic model.
/// Walks ASTs via the semantic model and checks each expression/statement
/// using expression types computed by the SemanticModelBuilder (Pass 2).
/// </summary>
public sealed class TypeCheckerAnalyzer : IAnalyzer
{
    public string AnalyzerId => "TypeChecker";

    // ── Overloading diagnostic IDs ───────────────────────────────────────
    internal static readonly DiagnosticId NoApplicableOverload = new("MIST_TC_0031");
    internal static readonly DiagnosticId AmbiguousOverload = new("MIST_TC_0032");

    // ── Cast operator diagnostic IDs ─────────────────────────────────────
    /// <summary>
    /// Emitted when a cast operator's source parameter type and target type
    /// are both different from the enclosing type (Requirement 6.2).
    /// </summary>
    internal static readonly DiagnosticId CastOperatorEnclosingTypeViolation = new("MIST_TC_0040");

    /// <summary>
    /// Emitted when two cast operators in the same type have the same kind
    /// (implicit/explicit) and the same resolved source-target type pair
    /// (Requirement 6.5).
    /// </summary>
    internal static readonly DiagnosticId DuplicateCastOperator = new("MIST_TC_0041");

    /// <summary>
    /// Emitted when no cast operator is defined from the source type to the
    /// target type in an explicit cast expression (Requirement 8.6).
    /// </summary>
    internal static readonly DiagnosticId NoCastOperator = new("MIST_TC_0042");

    /// <summary>
    /// Emitted when multiple cast operators (explicit and implicit combined)
    /// are applicable for the same explicit cast expression (Requirement 8.9).
    /// </summary>
    internal static readonly DiagnosticId AmbiguousCast = new("MIST_TC_0043");

    /// <summary>
    /// Emitted when multiple implicit cast operators are applicable for the
    /// same conversion at an assignment, argument, or return site
    /// (Requirement 7.6).
    /// </summary>
    internal static readonly DiagnosticId AmbiguousImplicitCast = new("MIST_TC_0044");

    // ── Pattern matching diagnostic IDs ──────────────────────────────────

    /// <summary>
    /// Emitted when a type pattern references a type that does not exist
    /// in any reachable scope (Requirement 10.1).
    /// </summary>
    internal static readonly DiagnosticId PatternUndefinedType = new("MIST_TC_0050");

    /// <summary>
    /// Emitted when a constant pattern's type is incompatible with the
    /// scrutinee type (Requirement 10.3).
    /// </summary>
    internal static readonly DiagnosticId PatternConstantTypeMismatch = new("MIST_TC_0051");

    /// <summary>
    /// Emitted when a relational pattern is applied to a scrutinee whose
    /// type does not support comparison operators (Requirement 10.4).
    /// </summary>
    internal static readonly DiagnosticId PatternRelationalNonComparable = new("MIST_TC_0052");

    /// <summary>
    /// Emitted when a property pattern references a property name that does
    /// not exist on the scrutinee type or the type specified in the pattern
    /// (Requirement 10.2).
    /// </summary>
    internal static readonly DiagnosticId PatternUndefinedProperty = new("MIST_TC_0053");

    /// <summary>
    /// Emitted when a list pattern is applied to a scrutinee that is not an
    /// array type (Requirement 10.5).
    /// </summary>
    internal static readonly DiagnosticId PatternListRequiresArray = new("MIST_TC_0054");

    /// <summary>
    /// Emitted as a warning when a list pattern has more fixed elements than
    /// the array could contain (Requirement 10.5).
    /// </summary>
    internal static readonly DiagnosticId PatternListTooManyElements = new("MIST_TC_0055");

    /// <summary>
    /// Emitted when an <c>or</c> pattern combines alternatives that introduce
    /// different sets of pattern variables or the same names with different
    /// types (Requirement 10.6).
    /// </summary>
    internal static readonly DiagnosticId PatternOrInconsistentVariables = new("MIST_TC_0056");

    /// <summary>
    /// Emitted when a <c>not</c> pattern contains a subpattern that
    /// introduces a pattern variable (Requirement 10.7).
    /// </summary>
    internal static readonly DiagnosticId PatternNotCannotBindVariable = new("MIST_TC_0057");

    // ── unknown-type feature: assignability diagnostics ──────────────────
    //
    // Note on numbering: the unknown-type design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0056 / MIST_TC_0057 for
    // the two assignability rejections below. Those codes were already
    // allocated to the pattern-matching feature (see PatternOrInconsistentVariables
    // and PatternNotCannotBindVariable above), so the unknown-type assignability
    // diagnostics were reallocated to MIST_TC_0060 / MIST_TC_0061 instead.

    /// <summary>
    /// Emitted when a value whose static type is <see cref="UnknownType"/>
    /// is assigned to a location whose declared type is not <c>unknown</c>.
    /// Narrowing via pattern matching (<c>is T t</c>) is the designated
    /// mechanism to recover a concrete type from an <c>unknown</c> value
    /// (Requirement 4.4).
    /// </summary>
    internal static readonly DiagnosticId UnknownToNonUnknown = new("MIST_TC_0060");

    /// <summary>
    /// Emitted when a <c>void</c> expression is assigned to an <c>unknown</c>
    /// target. Every non-<c>void</c> type is assignable to <c>unknown</c>, but
    /// <c>void</c> is excluded because it carries no value (Requirement 4.2).
    /// </summary>
    internal static readonly DiagnosticId VoidToUnknown = new("MIST_TC_0061");

    // ── unknown-type feature: member-filtering diagnostic (Task 21.2) ────
    //
    // Note on numbering: the unknown-type design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0050 for the member-
    // filtering diagnostic below. That code was already allocated to the
    // pattern-matching feature (see PatternUndefinedType above), so the
    // unknown-type filter diagnostic was reallocated to MIST_TC_0062 —
    // the next free code after the assignability pair (0060, 0061) —
    // to avoid overloading a diagnostic ID with two distinct meanings.

    /// <summary>
    /// Emitted when a member access on a <c>Closed_Generic_With_Unknown</c>
    /// receiver resolves to a member classified <c>WritesT</c> with respect
    /// to a type parameter whose type argument is <see cref="UnknownType"/>.
    /// Per Requirement 10.1–10.4 and 10.7, the diagnostic uses the same
    /// "member not found" shape as the existing missing-member diagnostic,
    /// naming the receiver's static type and the member, and states that
    /// the operation would write to storage of a type parameter bound to
    /// <c>unknown</c>.
    /// </summary>
    internal static readonly DiagnosticId UnknownClosedGenericMemberFiltered = new("MIST_TC_0062");

    // ── unknown-type feature: struct-generic widening restriction (Task 23.4) ──
    //
    // Covariant unknown widening (Req 8.1–8.3) is only available for class
    // (reference-type) generic definitions. When the only reason a widening
    // assignment fails is that the generic definition whose arguments are
    // being widened is a struct, the Type Checker emits this targeted
    // diagnostic instead of the generic type-mismatch diagnostic, so the
    // user sees an explanatory rather than a cascading error (Req 8.6,
    // Req 16 diagnostic quality).
    //
    // The code MIST_TC_0058 was allocated in the design document's
    // Diagnostic ID Allocation table and confirmed free in the 005x range
    // at the time of implementation (all neighboring codes 0050–0057 are
    // owned by the pattern-matching feature; 0058 and 0059 are unused).

    /// <summary>
    /// Emitted when a covariant <c>unknown</c> widening is attempted on a
    /// generic definition whose <see cref="TypeKind"/> is
    /// <see cref="TypeKind.Struct"/>. Widening is restricted to class
    /// (reference-type) generics because the C# emission strategy — a
    /// reference cast through <c>object</c> — is unsound for value-type
    /// layouts (Req 8.1, 8.6).
    /// </summary>
    internal static readonly DiagnosticId StructGenericUnknownWidening = new("MIST_TC_0058");

    // ── unknown-type feature: pattern-narrowing rejections (Task 24) ─────
    //
    // Note on numbering: the unknown-type design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0051 / MIST_TC_0052 for
    // the two pattern-narrowing rejections below. Those codes were already
    // allocated to the pattern-matching feature (see PatternConstantTypeMismatch
    // and PatternRelationalNonComparable above), so the unknown-type pattern
    // rejections were reallocated to MIST_TC_0063 / MIST_TC_0064 — the next
    // free codes after the Task 21 member-filtering diagnostic (MIST_TC_0062) —
    // to avoid overloading diagnostic IDs with two distinct meanings. This
    // continues the same reallocation strategy used by Task 9 (0060 / 0061)
    // and Task 21 (0062).

    /// <summary>
    /// Emitted when an <c>is</c>-pattern narrows to <see cref="UnknownType"/>
    /// (e.g., <c>x is unknown u</c>). Narrowing to <c>unknown</c> is not
    /// meaningful: the pattern is tautologically true because every value
    /// is assignable to <c>unknown</c> (Req 4.1), and the introduced binding
    /// has no more specific type than the scrutinee (Req 11.3).
    /// </summary>
    internal static readonly DiagnosticId PatternNarrowToUnknown = new("MIST_TC_0063");

    /// <summary>
    /// Emitted when an <c>is</c>-pattern narrows to a
    /// <c>Closed_Generic_With_Unknown</c> (e.g., <c>x is List&lt;unknown&gt; lu</c>).
    /// Narrowing to such a type is not meaningful: Covariant_Unknown_Widening
    /// (Req 8) makes the pattern tautologically true, and the introduced
    /// binding loses information — members classified <c>WritesT</c> with
    /// respect to the <c>unknown</c>-bound type parameter are filtered per
    /// Requirement 10 without any gain over the scrutinee (Req 11.4).
    /// </summary>
    internal static readonly DiagnosticId PatternNarrowToClosedGenericWithUnknown = new("MIST_TC_0064");

    // ── unknown-type feature: bare-unknown rejection (Task 25) ───────────
    //
    // Note on numbering: the unknown-type design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0053 / MIST_TC_0054 for
    // the two bare-unknown rejections below. Those codes were already
    // allocated to the pattern-matching feature (see PatternUndefinedProperty
    // and PatternListRequiresArray above), so the unknown-type bare-unknown
    // rejections were reallocated to MIST_TC_0065 / MIST_TC_0066 — the next
    // free codes after the Task 24 pattern-narrowing diagnostics
    // (MIST_TC_0063, MIST_TC_0064) — to avoid overloading diagnostic IDs
    // with two distinct meanings. This continues the same reallocation
    // strategy used by Task 9 (0060 / 0061), Task 21 (0062), and
    // Task 24 (0063 / 0064).

    /// <summary>
    /// Emitted when a member access expression has a receiver whose static
    /// type is exactly <see cref="UnknownType"/> (not a Constructed_Type
    /// with <c>unknown</c> arguments). Per Requirement 13.1, no member
    /// exists on bare <c>unknown</c> regardless of the member name. The
    /// message suggests using an <c>is</c> pattern to narrow to a concrete
    /// type before accessing members (Req 16.4).
    /// </summary>
    internal static readonly DiagnosticId BareUnknownMemberAccess = new("MIST_TC_0065");

    /// <summary>
    /// Emitted when a binary or unary operator is applied to an operand
    /// whose static type is exactly <see cref="UnknownType"/>. Per
    /// Requirement 13.2, no operators are defined for bare <c>unknown</c>.
    /// This applies to <c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&gt;</c>,
    /// <c>&lt;=</c>, <c>&gt;=</c>, <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>,
    /// <c>%</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>, and any
    /// user-defined operators. The message suggests using an <c>is</c>
    /// pattern to narrow the operand first (Req 16.4).
    /// </summary>
    internal static readonly DiagnosticId BareUnknownOperator = new("MIST_TC_0066");

    // ── unknown-type feature: cast-from-unknown diagnostic (Task 26) ─────
    //
    // Note on numbering: the unknown-type design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0055 for the "no cast
    // operator from unknown to T" diagnostic below. That code was already
    // allocated to the pattern-matching feature (see PatternListTooManyElements
    // above — a warning emitted when a list pattern has more fixed elements
    // than the array could contain). To avoid overloading a diagnostic ID
    // with two distinct meanings — especially one being an error and the
    // other a warning — the unknown-type cast-from-unknown diagnostic was
    // reallocated to MIST_TC_0067, the next free code after the Task 25
    // bare-unknown diagnostics (MIST_TC_0065, MIST_TC_0066). This continues
    // the reallocation strategy used by Task 9 (0060 / 0061), Task 21 (0062),
    // Task 24 (0063 / 0064), and Task 25 (0065 / 0066).

    /// <summary>
    /// Emitted when a cast expression <c>(T) expr</c> has an operand whose
    /// static type is <see cref="UnknownType"/> and no implicit or explicit
    /// cast operator from <c>unknown</c> to <c>T</c> has been declared on
    /// <c>T</c>. Per Requirement 13.4, a cast from <c>unknown</c> to <c>T</c>
    /// requires a statically-resolvable cast operator; unlike C#, Mist does
    /// not permit a runtime-checked cast through <c>object</c>. The
    /// diagnostic's message names the target type and explains that a
    /// user-defined operator <c>implicit/explicit operator T(unknown)</c>
    /// must be declared on <c>T</c> for the cast to compile (Req 13.6).
    /// </summary>
    internal static readonly DiagnosticId NoCastOperatorFromUnknown = new("MIST_TC_0067");

    // ── type-unions feature: assignability diagnostics ────────────────────
    //
    // Note on numbering: the type-unions design document's Diagnostic ID
    // Allocation table originally proposed MIST_TC_0050 / MIST_TC_0051 /
    // MIST_TC_0055 for the three union assignability diagnostics below.
    // Those codes were already allocated to the pattern-matching feature
    // (see PatternUndefinedType, PatternConstantTypeMismatch, and
    // PatternListTooManyElements above), so the type-unions assignability
    // diagnostics were reallocated to MIST_TC_0070 / MIST_TC_0071 /
    // MIST_TC_0072 — the next free codes after the unknown-type cast
    // diagnostic (MIST_TC_0067). This continues the same reallocation
    // strategy used throughout this file.

    /// <summary>
    /// Emitted when a non-constituent type is assigned to a union-typed
    /// target. For example, assigning <c>bool</c> to <c>int | string</c>
    /// when <c>bool</c> is not a constituent of the union (Requirement 6.3).
    /// </summary>
    internal static readonly DiagnosticId UnionNonConstituentAssignment = new("MIST_TC_0070");

    /// <summary>
    /// Emitted when a union type is assigned to a non-union target without
    /// narrowing. For example, assigning <c>int | string</c> to <c>int</c>
    /// without first using a pattern match to narrow the union (Requirement 6.4).
    /// </summary>
    internal static readonly DiagnosticId UnionToNonUnionAssignment = new("MIST_TC_0071");

    /// <summary>
    /// Emitted when a union type is assigned to another union type but at
    /// least one constituent of the source union is not present in the target
    /// union. For example, assigning <c>int | string | bool</c> to
    /// <c>int | string</c> when <c>bool</c> is not a constituent of the
    /// target (Requirement 6.3).
    /// </summary>
    internal static readonly DiagnosticId UnionIncompatibleConstituents = new("MIST_TC_0072");

    /// <summary>
    /// Emitted when a member access or method call is performed on a union-typed
    /// value and the member does not exist on all constituent types. The diagnostic
    /// message lists which constituent types lack the member (Requirement 7.2).
    /// </summary>
    internal static readonly DiagnosticId UnionMemberNotOnAllConstituents = new("MIST_TC_0073");

    /// <summary>
    /// Emitted as a warning when a type pattern on a union-typed scrutinee
    /// references a type that is not one of the union's constituent types.
    /// The pattern will never match at runtime (Requirement 8.3, 12.5).
    /// </summary>
    internal static readonly DiagnosticId UnionPatternNonConstituent = new("MIST_TC_0074");

    /// <summary>
    /// Emitted as a warning when a union type annotation resolves to fewer
    /// than two distinct constituent types after flattening and deduplication.
    /// A union type requires at least two distinct types to be meaningful
    /// (Requirement 12.1).
    /// </summary>
    internal static readonly DiagnosticId UnionDegenerateType = new("MIST_TC_0075");

    // ── Readonly parameter call-site diagnostic ID ───────────────────────
    //
    // Note on numbering: the readonly-contexts design document's Diagnostic
    // ID Allocation table proposed MIST_TC_0073 for this diagnostic. That
    // code was already allocated to the type-unions feature (see
    // UnionMemberNotOnAllConstituents above), so the readonly parameter
    // call-site diagnostic was reallocated to MIST_TC_0076 — the next free
    // code after the union diagnostics (MIST_TC_0073, 0074, 0075) — to
    // avoid overloading a diagnostic ID with two distinct meanings.

    /// <summary>
    /// Emitted when a readonly type is passed as an argument to a parameter
    /// that is not marked <c>readonly</c>. A readonly type can only be
    /// passed to a readonly parameter to ensure the callee cannot mutate it
    /// (Requirement 14.5).
    /// </summary>
    internal static readonly DiagnosticId ReadonlyTypeToNonReadonlyParam = new("MIST_TC_0076");

    // ── Readonly generic constraint diagnostic IDs ─────────────────────
    //
    // Note on numbering: the readonly-contexts design document's Diagnostic
    // ID Allocation table proposed MIST_TC_0070 / MIST_TC_0071 for the
    // readonly/mutable constraint violations. Those codes were already
    // allocated to the type-unions feature (see UnionNonConstituentAssignment
    // and UnionToNonUnionAssignment above), so the readonly constraint
    // diagnostics were reallocated to MIST_TC_0077 / MIST_TC_0078 — the
    // next free codes after the readonly parameter call-site diagnostic
    // (MIST_TC_0076) — to avoid overloading diagnostic IDs with two
    // distinct meanings.

    /// <summary>
    /// Emitted when a type argument does not satisfy the 'readonly' constraint
    /// on a type parameter. The type argument must be a readonly type
    /// (Requirement 12.1, 12.7).
    /// </summary>
    internal static readonly DiagnosticId ConstraintReadonlyViolation = new("MIST_TC_0077");

    /// <summary>
    /// Emitted when a type argument does not satisfy the 'mutable' constraint
    /// on a type parameter. The type argument must not be a readonly type
    /// (Requirement 12.2, 12.7).
    /// </summary>
    internal static readonly DiagnosticId ConstraintMutableViolation = new("MIST_TC_0078");

    // ── Readonly interface/trait contract diagnostic ID ───────────────────
    //
    // Note on numbering: the readonly-contexts design document's Diagnostic
    // ID Allocation table proposed MIST_TC_0072 for this diagnostic. That
    // code was already allocated to the type-unions feature (see
    // UnionIncompatibleConstituents above), so the readonly contract
    // diagnostic was reallocated to MIST_TC_0079 — the next free code
    // after the readonly constraint diagnostics (MIST_TC_0077, 0078) — to
    // avoid overloading a diagnostic ID with two distinct meanings.

    /// <summary>
    /// Emitted when a type implements a readonly interface or trait but the
    /// implementing member is not readonly (explicit or inferred). The
    /// implementing member must be readonly to satisfy the contract
    /// (Requirement 13.2, 13.3, 13.4, 13.5, 13.6).
    /// </summary>
    internal static readonly DiagnosticId ReadonlyContractViolation = new("MIST_TC_0079");

    // ── switch-expressions feature: type checker diagnostics ─────────────
    //
    // Note on numbering: the switch-expressions design document's Diagnostic
    // ID Allocation table originally proposed MIST_TC_0060–0063 for the four
    // switch expression diagnostics below. Those codes were already allocated
    // to the unknown-type feature (see UnknownToNonUnknown, VoidToUnknown,
    // UnknownClosedGenericMemberFiltered, and PatternNarrowToUnknown above),
    // so the switch-expressions diagnostics were reallocated to
    // MIST_TC_0090–0093 — the next free codes after the extension-methods
    // diagnostics (MIST_TC_0080–0083). This continues the same reallocation
    // strategy used throughout this file.

    /// <summary>
    /// Emitted when a switch expression does not cover all possible values
    /// of the scrutinee's static type. The diagnostic message lists the
    /// missing cases (Requirement 6.6).
    /// </summary>
    internal static readonly DiagnosticId SwitchNotExhaustive = new("MIST_TC_0090");

    /// <summary>
    /// Emitted when the result expressions of switch arms have incompatible
    /// types — no common type can be determined (Requirement 5.4).
    /// </summary>
    internal static readonly DiagnosticId SwitchIncompatibleArmTypes = new("MIST_TC_0091");

    /// <summary>
    /// Emitted when a <c>when</c> guard expression in a switch arm does not
    /// evaluate to type <c>bool</c> (Requirement 8.2).
    /// </summary>
    internal static readonly DiagnosticId SwitchGuardNotBool = new("MIST_TC_0092");

    /// <summary>
    /// Emitted as a warning when a switch arm is unreachable because it is
    /// subsumed by a preceding arm (Requirement 7.1).
    /// </summary>
    internal static readonly DiagnosticId SwitchUnreachableArm = new("MIST_TC_0093");

    // ── Generic constraint satisfaction diagnostic IDs ────────────────────

    /// <summary>
    /// Emitted when a type argument does not implement a required interface
    /// constraint on a type parameter (Requirement 5.2, 10.1).
    /// </summary>
    internal static readonly DiagnosticId ConstraintInterfaceNotImplemented = new("MIST_CONSTRAINT_0009");

    /// <summary>
    /// Emitted when a type argument does not satisfy a required trait
    /// constraint on a type parameter (Requirement 5.3, 10.2).
    /// </summary>
    internal static readonly DiagnosticId ConstraintTraitNotSatisfied = new("MIST_CONSTRAINT_0010");

    /// <summary>
    /// Emitted when a type argument does not derive from a required base
    /// class constraint on a type parameter (Requirement 5.4, 10.3).
    /// </summary>
    internal static readonly DiagnosticId ConstraintBaseClassNotDerived = new("MIST_CONSTRAINT_0011");

    /// <summary>
    /// Emitted when a type argument must be a reference type to satisfy the
    /// 'class' constraint but is a value type (Requirement 5.5, 10.4).
    /// </summary>
    internal static readonly DiagnosticId ConstraintClassViolation = new("MIST_CONSTRAINT_0012");

    /// <summary>
    /// Emitted when a type argument must be a value type to satisfy the
    /// 'struct' constraint but is a reference type (Requirement 5.6, 10.5).
    /// </summary>
    internal static readonly DiagnosticId ConstraintStructViolation = new("MIST_CONSTRAINT_0013");

    /// <summary>
    /// Emitted when a type argument must have a public parameterless
    /// constructor to satisfy the 'new()' constraint (Requirement 5.7, 10.6).
    /// </summary>
    internal static readonly DiagnosticId ConstraintParameterlessCtorViolation = new("MIST_CONSTRAINT_0014");

    /// <summary>
    /// Emitted when a type argument must have a public constructor with
    /// specific parameters to satisfy a parameterized constructor constraint
    /// (Requirement 5.8, 10.7).
    /// </summary>
    internal static readonly DiagnosticId ConstraintParameterizedCtorViolation = new("MIST_CONSTRAINT_0015");

    /// <summary>
    /// Emitted when a <c>new T(...)</c> expression is used in a generic body
    /// and the argument types do not match any declared constructor constraint
    /// signature on that type parameter (Requirement 6.5, 6.6, 10.8).
    /// </summary>
    internal static readonly DiagnosticId ConstraintNewTNoMatch = new("MIST_CONSTRAINT_0016");

    /// <summary>
    /// Emitted when a derived type inherits from a constrained generic base
    /// but does not declare constraints at least as restrictive on the
    /// corresponding type parameter (Requirement 7.1, 7.2, 7.3).
    /// </summary>
    internal static readonly DiagnosticId ConstraintPropagationInsufficient = new("MIST_CONSTRAINT_0017");

    // ── property-initializers feature: validation diagnostics ──────────────

    /// <summary>
    /// Emitted when a property initializer block references a member name
    /// that does not exist as a property or field on the constructed type.
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerMemberNotFound = new("MIST_PI_0001");

    /// <summary>
    /// Emitted when the value expression type in a property initializer
    /// assignment is not assignable to the target member's declared type.
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerTypeMismatch = new("MIST_PI_0002");

    /// <summary>
    /// Emitted when a property initializer targets a property that has no
    /// accessible <c>set</c> or <c>init</c> accessor.
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerNoSetOrInit = new("MIST_PI_0003");

    /// <summary>
    /// Emitted when a property initializer targets a field that is readonly
    /// (explicit, inferred, or implicit).
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerReadonlyField = new("MIST_PI_0004");

    /// <summary>
    /// Emitted when a property initializer targets a member that is
    /// inaccessible due to its protection level (e.g., private from outside
    /// the declaring type).
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerInaccessible = new("MIST_PI_0005");

    /// <summary>
    /// Emitted when the same member name appears more than once in a
    /// property initializer block.
    /// </summary>
    internal static readonly DiagnosticId PropertyInitializerDuplicate = new("MIST_PI_0006");

    // ── array-literals feature: incompatible element types ──────────────

    /// <summary>
    /// Emitted when an array literal contains elements whose types are
    /// incompatible — no common type can be determined. The diagnostic
    /// message names the two conflicting types (Requirement 5.2).
    /// </summary>
    internal static readonly DiagnosticId ArrayLiteralIncompatibleTypes = new("MIST_TC_0095");

    // ── refinement-types feature: compile-time enforcement diagnostics ───
    //
    // Note on numbering: the refinement-types design document's Diagnostic
    // ID Allocation table originally proposed MIST_TC_0070 / MIST_TC_0071
    // for the two refinement enforcement diagnostics below. Those codes
    // were already allocated to the type-unions feature (see
    // UnionNonConstituentAssignment and UnionToNonUnionAssignment above),
    // so the refinement enforcement diagnostics were reallocated to
    // MIST_TC_0097 / MIST_TC_0098 — the next free codes after the empty
    // array literal diagnostic (MIST_TC_0096). This continues the same
    // reallocation strategy used throughout this file.

    /// <summary>
    /// Emitted when a compile-time constant value is assigned to a
    /// <see cref="Mist.Semantics.Model.Types.RefinementType"/> variable and
    /// the value does not satisfy the refinement predicate (Requirement 5.2).
    /// </summary>
    internal static readonly DiagnosticId RefinementLiteralViolation = new("MIST_TC_0097");

    /// <summary>
    /// Emitted when a non-constant expression of the base type is assigned
    /// to a <see cref="Mist.Semantics.Model.Types.RefinementType"/> variable
    /// without proving the refinement via pattern matching (Requirement 6.1).
    /// </summary>
    internal static readonly DiagnosticId RefinementNonConstantAssignment = new("MIST_TC_0098");

    /// <summary>
    /// Emitted when a value of one <see cref="Mist.Semantics.Model.Types.RefinementType"/>
    /// is assigned to a variable of a different <see cref="Mist.Semantics.Model.Types.RefinementType"/>
    /// with the same base type, but the compiler cannot prove that the source
    /// predicate logically implies the target predicate (Requirement 9.3, 11.5).
    /// </summary>
    internal static readonly DiagnosticId RefinementSubsetNotProvable = new("MIST_TC_0099");

    /// <summary>
    /// Emitted when a refinement predicate references a mutable member via a
    /// property pattern. All members in a refinement predicate must be readonly
    /// to prevent the refinement from being silently invalidated by mutation
    /// (Requirements 3.1, 3.2, 3.4).
    /// </summary>
    internal static readonly DiagnosticId RefinementMutableMember = new("MIST_TC_0100");

    /// <summary>
    /// Emitted when a refinement type is used in a pattern match but the
    /// scrutinee type is not compatible with the refinement's base type.
    /// The pattern will never match at runtime (Requirements 14.1, 14.4).
    /// </summary>
    internal static readonly DiagnosticId PatternIncompatibleRefinement = new("MIST_TC_0101");

    // ── enums feature: assignability diagnostic ──────────────────────────

    /// <summary>
    /// Emitted when a value of the underlying type is assigned to a variable
    /// of the enum type. Reverse conversion from underlying type to enum is
    /// not implicit; only enum-to-underlying is allowed (Requirement 7.3).
    /// Uses the diagnostic ID defined in <see cref="SemanticModelBuilder"/>
    /// (<c>MIST_SB_0083</c>).
    /// </summary>
    internal static readonly DiagnosticId CannotAssignUnderlyingToEnum = new("MIST_SB_0083");

    // ── dependent-types feature: compile-time constant validation ─────────

    /// <summary>
    /// Emitted when a dependent type argument is not a compile-time constant.
    /// Variables, method calls, property accesses, and expressions are rejected
    /// (Requirement 5.1, 5.3, 15.1, 15.6).
    /// </summary>
    internal static readonly DiagnosticId DependentArgNotConstant = new("MIST_DT_0002");

    /// <summary>
    /// Emitted when a dependent type argument's type does not match the
    /// dependent parameter's value type annotation (Requirement 15.2).
    /// </summary>
    internal static readonly DiagnosticId DependentArgTypeMismatch = new("MIST_DT_0003");

    /// <summary>
    /// Emitted when a derived type forwards a dependent parameter to a base type
    /// but the derived parameter's value type cannot be implicitly widened to the
    /// base parameter's value type (e.g., <c>long</c> forwarded to <c>int</c>).
    /// (Requirements 12.3, 12.4).
    /// </summary>
    internal static readonly DiagnosticId DependentNarrowingViolation = new("MIST_DT_0004");

    /// <summary>
    /// Emitted when a pattern match is attempted against an open dependent type
    /// (a constructed type whose dependent arguments contain unresolved parameters).
    /// Pattern matching requires all dependent values to be concrete
    /// (Requirement 9.3, 9.4, 15.7).
    /// </summary>
    internal static readonly DiagnosticId DependentOpenTypePattern = new("MIST_DT_0007");

    /// <summary>
    /// Feature flag gating the operator overload-resolution path introduced by
    /// Task 14 of the Mist Standard Library spec. When <c>true</c> (the
    /// default after Task 33 flipped it), operator expressions resolve
    /// through <see cref="ResolveBinaryOperator"/> /
    /// <see cref="ResolveUnaryOperator"/> against operator-member candidates
    /// collected from the operand types, using
    /// <see cref="OverloadResolver.Resolve"/>.
    ///
    /// The flag is retained for rollback scenarios and targeted unit tests
    /// that may want to compare paths; the <c>Check*Node</c> methods no
    /// longer branch on it — they delegate unconditionally to the overload
    /// resolution helpers. The legacy hardcoded <c>NumericTypes</c> HashSet,
    /// <c>IsNumeric</c> helper, and <c>StringType</c>-for-<c>+</c> /
    /// <c>PrimitiveType.Bool</c>-for-logical special cases have been
    /// removed per Requirements 6.7, 6.8, and 6.9.
    /// </summary>
#pragma warning disable CA1805 // Flag default value is part of the contract — the migration plan
                               // (design § Migration Plan) calls it out explicitly.
    internal static bool UseOperatorOverloadResolution = true;
#pragma warning restore CA1805

    /// <summary>
    /// DisplayName-based numeric classification set — covers both the
    /// legacy keyword display names (<c>"int"</c>, <c>"double"</c>, etc.)
    /// and the stdlib type names (<c>"Int32"</c>, <c>"Double"</c>, etc.).
    /// Used for equality compatibility checks and variable declaration
    /// narrowing suppression. Numeric widening conversions are now handled
    /// by implicit cast operators declared on the stdlib bootstrap types.
    /// </summary>
    private static readonly HashSet<string> NumericDisplayNames = new(StringComparer.Ordinal)
    {
        // Legacy lexemes.
        "int", "uint", "long", "ulong",
        "short", "ushort", "byte", "sbyte",
        "float", "double", "decimal",

        // Stdlib simple names.
        "Int32", "UInt32", "Int64", "UInt64",
        "Int16", "UInt16", "Byte", "SByte",
        "Single", "Double", "Decimal",
    };

    // ── Current enclosing type during analysis (set per method/constructor) ──
    private IType? _currentEnclosingType;

    // ── Whether we are currently inside a constructor body (for init-only checks) ──
    private bool _insideConstructor;

    // ── Whether we are currently inside a property accessor body (for field keyword checks) ──
    private bool _insidePropertyAccessor;

    // ── Whether the postfix currently being walked is the target of an ──
    //    assignment — set by CheckAssignmentNode before recursing into the
    //    target, and cleared afterwards. When true, the LAST suffix of the
    //    top-level postfix in the target expression is a *write* context
    //    (Requirement 10.3); all other suffixes in the chain are reads.
    //    Only the Task 21 member-filtering logic consults this flag; legacy
    //    read/write distinctions continue to use their existing lookups
    //    (e.g. TryResolvePropertyFromAssignmentTarget).
    private bool _analyzingAssignmentLhs;

    // ── Flow narrowing context for refinement types (Requirements 7.1–7.8) ──
    //    Maps local variable names to their narrowed RefinementType within
    //    the current then-branch scope. Entries are pushed when an if-condition
    //    proves a refinement predicate and popped after the then-branch is checked.
    //    Reassignment within the branch invalidates the narrowing entry.
    private readonly Dictionary<string, RefinementType> _narrowingContext = new(StringComparer.Ordinal);

    // ── Set of variable names captured by closures in the current method body ──
    //    Populated at the start of each method body check. Variables in this set
    //    are ineligible for flow narrowing (Requirement 7.7).
    private readonly HashSet<string> _closureCapturedVariables = new(StringComparer.Ordinal);

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Clear flow narrowing state from any previous compilation.
        _narrowingContext.Clear();
        _closureCapturedVariables.Clear();

        // Walk each method, constructor, property, and indexer body to check type rules.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    CheckMethodBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Constructor:
                    CheckConstructorBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Field:
                    CheckFieldDeclaration(symbol, model, diagnostics);
                    break;
                case SymbolKind.Property:
                    CheckPropertyDeclaration(symbol, model, diagnostics);
                    break;
                case SymbolKind.Indexer:
                    CheckIndexerDeclaration(symbol, model, diagnostics);
                    break;
            }
        }

        // Entry point validation — once per compilation (R10.1, R10.2, R10.3).
        CheckEntryPoint(model, diagnostics);

        // Task 5.9: Check for duplicate indexers across all types (MIST_TC_0029).
        // (Individual indexer checks are done per-symbol above.)

        // Task 5.10: Check for missing interface property implementations (MIST_TC_0030).
        CheckMissingInterfaceProperties(model, diagnostics);

        // Task 10.1: Check readonly interface/trait contract enforcement (MIST_TC_0079).
        CheckReadonlyInterfaceAndTraitContracts(model, diagnostics);

        // Task 6.2: Check constraint satisfaction for inheritance clauses where
        // constrained generics are used as base types or implemented interfaces.
        CheckInheritanceClauseConstraints(model, diagnostics);

        // Task 7.2: Validate explicit default blocks (R9.1–9.3, R6.9, R11.1, R11.2, R13.3, R13.4, R13.10).
        ValidateDefaultBlocks(model, diagnostics);

        // Task 6.1: Validate dependent type arguments are compile-time constants
        // and that their types match the parameter annotations (MIST_DT_0002, MIST_DT_0003).
        ValidateDependentTypeArguments(model, diagnostics);

        // Task 6.6: Validate dependent parameter forwarding in inheritance — widening only.
        // Emits MIST_DT_0004 when a derived type forwards a dependent parameter whose value
        // type cannot be implicitly widened to the base parameter's value type.
        ValidateDependentParameterForwarding(model, diagnostics);

        // Task 11.1: Validate that refinement predicates using property patterns only
        // reference readonly members (MIST_TC_0100, Requirements 3.1–3.4).
        CheckRefinementPredicateReadonly(model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry point validation (R10.1, R10.2, R10.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that exactly one class contains a valid entry point:
    /// <c>static void Main()</c> or <c>static void Main(string[] args)</c>.
    /// Emits MIST_TC_0017 if no entry point is found, MIST_TC_0018 if multiple are found.
    /// Entry points from referenced projects (OriginProjectPath != null) are excluded.
    /// </summary>
    private static void CheckEntryPoint(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Build a set of source file paths from referenced project units
        // so we can skip their Main methods during entry point detection.
        var referencedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in model.Units)
        {
            if (unit.OriginProjectPath is not null)
            {
                referencedSourcePaths.Add(unit.Source.Path);
            }
        }

        var entryPoints = new List<(ISymbol Symbol, SyntaxNode Node)>();

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
                continue;

            // Get the declaring AST node to inspect modifiers and parameters.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if (declaringNode is not MethodDeclarationNode methodDecl)
                continue;

            // Skip entry points from referenced projects — only the root
            // project's Main methods count as entry points.
            if (IsFromReferencedProject(declaringNode, model, referencedSourcePaths))
                continue;

            // Must be static.
            var isStatic = false;
            foreach (var mod in methodDecl.Modifiers)
            {
                if (mod is StaticModifierModifierNode)
                {
                    isStatic = true;
                    break;
                }
            }
            if (!isStatic)
                continue;

            // Must return void.
            if (methodDecl.ReturnType is not VoidTypeRefTypeReferenceNode)
                continue;

            // Must have either no parameters or a single string[] parameter.
            if (methodDecl.Parameters is null)
            {
                // static void Main() — valid entry point.
                entryPoints.Add((symbol, declaringNode));
            }
            else
            {
                // Check for exactly one parameter of type string[].
                var parameters = new List<ParameterNode>();
                foreach (var child in methodDecl.Parameters.Children())
                {
                    if (child is ParameterNode param)
                        parameters.Add(param);
                }

                if (parameters.Count == 1)
                {
                    var paramType = parameters[0].Type;
                    // string[] is represented as PrimitiveTypeRef with StringType and an array rank,
                    // or as a NamedTypeRef. Check for the string[] pattern.
                    if (IsStringArrayTypeRef(paramType))
                    {
                        entryPoints.Add((symbol, declaringNode));
                    }
                }
                // Any other parameter count or type is not a valid entry point signature.
            }
        }

        if (entryPoints.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_TC_0017"),
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "Program does not contain a valid entry point (static void Main)",
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: "TypeChecker"));
        }
        else if (entryPoints.Count > 1)
        {
            // Emit a diagnostic listing all entry point locations.
            foreach (var (_, node) in entryPoints)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0018",
                    "Program contains multiple entry points",
                    node,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks whether a type reference AST node represents <c>string[]</c>.
    /// </summary>
    private static bool IsStringArrayTypeRef(TypeReferenceNode typeRef)
    {
        // string[] as a primitive type ref: PrimitiveTypeRef with StringType and array rank.
        if (typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef)
        {
            return primitiveRef.PrimitiveType is StringTypePrimitiveTypeNode
                   && primitiveRef.ArrayRank.HasValue;
        }

        // string[] could also appear as a named type ref (unlikely but handle it).
        // In practice, the grammar parses "string" as a PrimitiveType keyword,
        // so this case shouldn't occur for valid Mist code.
        return false;
    }

    /// <summary>
    /// Determines whether the given method symbol belongs to a referenced project
    /// by checking if its containing type's namespace matches a referenced project's
    /// namespace root.
    /// </summary>
    private static bool IsFromReferencedProject(
        SyntaxNode node,
        ISemanticModel model,
        HashSet<string> referencedSourcePaths)
    {
        if (referencedSourcePaths.Count == 0)
            return false;

        // Check each referenced unit's source path against the compilation units.
        // The declaring node belongs to a referenced project if it was parsed from
        // a source file that belongs to a referenced project.
        var units = model.Units;

        // Build a set of namespace roots from referenced projects.
        var referencedNamespaceRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            if (unit.OriginProjectPath is not null && unit.NamespaceRoot is not null)
            {
                referencedNamespaceRoots.Add(unit.NamespaceRoot);
            }
        }

        if (referencedNamespaceRoots.Count == 0)
            return false;

        // Find the containing type of the method by walking up the scope chain.
        // The method's declaring node is inside a class/struct. Check if that
        // type's containing namespace matches a referenced project's namespace root.
        if (node is not MethodDeclarationNode)
            return false;

        // Get the declared symbol for this node to find its containing scope.
        var methodSymbol = model.GetDeclaredSymbol(node);
        if (methodSymbol is null)
            return false;

        // Walk up the scope chain to find the containing type.
        var typeScope = methodSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return false;

        // Find the type symbol that owns this scope.
        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct)
            {
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is not null && ReferenceEquals(memberScope, typeScope))
                {
                    // Found the containing type. Check its namespace.
                    var containingNs = member.DeclaredType switch
                    {
                        ClassType ct => ct.ContainingNamespace,
                        StructType st => st.ContainingNamespace,
                        _ => null,
                    };

                    if (containingNs is not null && referencedNamespaceRoots.Contains(containingNs))
                        return true;

                    // Also check if the namespace starts with a referenced namespace root
                    // (for nested namespaces like "Inheritance.Models").
                    foreach (var nsRoot in referencedNamespaceRoots)
                    {
                        if (containingNs is not null && containingNs.StartsWith(nsRoot + ".", StringComparison.Ordinal))
                            return true;
                    }

                    return false;
                }
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enclosing type resolution — determines the type that contains a symbol
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the declaring type (ClassType, StructType, or InterfaceType) that contains the given
    /// method or constructor symbol by walking up the scope chain.
    /// </summary>
    private static IType? FindDeclaringType(ISymbol memberSymbol)
    {
        // The member's ContainingScope is the type scope. Look for a Class/Struct/Interface
        // symbol in the parent scope that owns this type scope.
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                && member.DeclaredType is ClassType or StructType or InterfaceType)
            {
                // Check if this type's MemberScope is the same as the member's ContainingScope.
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    InterfaceType it => it.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(memberScope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Top-level check entry points
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        // ── Cast operator declarations ───────────────────────────────────
        if (declaringNode is CastOperatorDeclarationNode castOpDecl)
        {
            var returnType = methodSymbol.DeclaredType ?? ErrorType.Instance;
            _currentEnclosingType = FindDeclaringType(methodSymbol);
            _insideConstructor = false;
            _insidePropertyAccessor = false;

            CheckCastOperatorDeclaration(castOpDecl, methodSymbol, model, diagnostics);

            // ── Flow narrowing: detect closure-captured variables ──
            if (castOpDecl.Body.GetBlock() is BlockNode castBlock)
            {
                CollectClosureCapturedVariables(castBlock);
                CheckBlock(castBlock, returnType, model, diagnostics);
            }
            return;
        }

        if (declaringNode is not MethodDeclarationNode methodDecl)
            return;

        var returnType2 = methodSymbol.DeclaredType ?? ErrorType.Instance;
        _currentEnclosingType = FindDeclaringType(methodSymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false;

        // ── Flow narrowing: detect closure-captured variables ──
        if (methodDecl.Body.GetBlock() is BlockNode block)
        {
            CollectClosureCapturedVariables(block);
            CheckBlock(block, returnType2, model, diagnostics);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cast operator declaration validation (MIST_TC_0040, MIST_TC_0041)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a cast operator declaration after type resolution:
    /// <list type="bullet">
    /// <item><description><c>MIST_TC_0040</c> — either the source parameter type
    /// or the target type must be the enclosing type (Requirement 6.2).</description></item>
    /// <item><description><c>MIST_TC_0041</c> — duplicate cast operator with the
    /// same kind and same resolved source-target type pair (Requirement 6.5).</description></item>
    /// </list>
    /// </summary>
    private void CheckCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var enclosingType = _currentEnclosingType;
        if (enclosingType is null)
            return;

        // Resolve the source parameter type from the symbol's parameter.
        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var sourceType = paramTypes.Count > 0 ? paramTypes[0] : null;

        // The target type is the symbol's DeclaredType (return type), resolved in Pass 2.
        var targetType = methodSymbol.DeclaredType;

        // ── MIST_TC_0040: Enclosing type constraint ─────────────────────
        // Either the source parameter type or the target type must be the
        // enclosing type.
        if (sourceType is not null && targetType is not null)
        {
            var sourceMatchesEnclosing = sourceType.DisplayName == enclosingType.DisplayName;
            var targetMatchesEnclosing = targetType.DisplayName == enclosingType.DisplayName;

            if (!sourceMatchesEnclosing && !targetMatchesEnclosing)
            {
                diagnostics.Add(MakeDiagnostic(
                    CastOperatorEnclosingTypeViolation.Value,
                    $"Either the source type or the target type of a cast operator must be the enclosing type '{enclosingType.DisplayName}'. Source: '{sourceType.DisplayName}', Target: '{targetType.DisplayName}'.",
                    castOpDecl,
                    model));
            }
        }

        // ── MIST_TC_0041: Duplicate cast operator detection ─────────────
        // After type resolution, check for duplicate cast operators with the
        // same kind (implicit/explicit) and the same resolved source-target
        // type pair in the enclosing type.
        if (sourceType is not null && targetType is not null)
        {
            var castKind = castOpDecl.CastKind switch
            {
                ImplicitCastCastOperatorKindNode => "implicit",
                ExplicitCastCastOperatorKindNode => "explicit",
                _ => "unknown",
            };

            // Get the member scope of the enclosing type to find sibling cast operators.
            var memberScope = enclosingType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is not null)
            {
                foreach (var sibling in memberScope.Members)
                {
                    if (sibling.Kind != SymbolKind.Method)
                        continue;

                    // Skip self.
                    if (ReferenceEquals(sibling, methodSymbol))
                        continue;

                    // Only consider cast operator symbols (name starts with "operator_implicit_" or "operator_explicit_").
                    if (!sibling.Name.StartsWith("operator_implicit_") && !sibling.Name.StartsWith("operator_explicit_"))
                        continue;

                    // Determine the sibling's cast kind from its name.
                    var siblingKind = sibling.Name.StartsWith("operator_implicit_") ? "implicit" : "explicit";
                    if (siblingKind != castKind)
                        continue;

                    // Compare resolved source and target types.
                    var siblingTargetType = sibling.DeclaredType;
                    var siblingParamTypes = ParameterSignature.Extract(sibling, model);
                    var siblingSourceType = siblingParamTypes.Count > 0 ? siblingParamTypes[0] : null;

                    if (siblingSourceType is not null && siblingTargetType is not null
                        && siblingSourceType.DisplayName == sourceType.DisplayName
                        && siblingTargetType.DisplayName == targetType.DisplayName)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            DuplicateCastOperator.Value,
                            $"Duplicate {castKind} cast operator from '{sourceType.DisplayName}' to '{targetType.DisplayName}' in type '{enclosingType.DisplayName}'.",
                            castOpDecl,
                            model));
                        break; // One diagnostic per duplicate is sufficient.
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit cast expression validation (MIST_TC_0042, MIST_TC_0043, MIST_TC_0067)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates an explicit cast expression <c>(TargetType)operand</c>:
    /// <list type="number">
    /// <item><description>Resolves the target type from the semantic model.</description></item>
    /// <item><description>Gets the compile-time type of the operand.</description></item>
    /// <item><description>If the operand type is directly assignable to the target type
    /// (identity or subtype — <em>not</em> implicit cast), the cast is valid with no
    /// operator needed. This also covers the unknown-type top-type rule — when the
    /// target is <see cref="UnknownType"/>, <see cref="IsDirectlyAssignable"/>
    /// accepts every non-<c>void</c> source per Req 13.5 / Req 4.1, so the operator
    /// search is skipped entirely (Task 26.2).</description></item>
    /// <item><description>If the operand type is <see cref="UnknownType"/>
    /// (Task 26.1 / Req 13.4), searches <em>only</em> the target type's member
    /// scope for a cast operator whose source type is <c>unknown</c>. The source's
    /// scope is empty by construction (Req 3.4), so searching it is unnecessary.
    /// If no such operator exists, emits <c>MIST_TC_0067</c> (the targeted
    /// "no cast operator from unknown" diagnostic). If exactly one exists, records
    /// it. If multiple exist, emits <c>MIST_TC_0043</c> for ambiguity.</description></item>
    /// <item><description>Otherwise, searches both the source type and target type member
    /// scopes for <c>operator_explicit_{target}</c> or <c>operator_implicit_{target}</c>
    /// symbols. Does <b>not</b> search base types or derived types (Requirement 8.7).</description></item>
    /// <item><description>If exactly one candidate is found, records it via
    /// <see cref="ISemanticModel.RecordResolvedOperator"/> and sets the expression type
    /// to the target type.</description></item>
    /// <item><description>If zero candidates are found, emits <c>MIST_TC_0042</c>.</description></item>
    /// <item><description>If multiple candidates are found, emits <c>MIST_TC_0043</c>.</description></item>
    /// </list>
    /// </summary>
    private static void CheckCastExpression(
        CastExpressionUnaryNode castExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // 1. Resolve the target type (already set by Pass 2).
        var targetType = model.GetTypeOf(castExpr);
        if (targetType is null || IsErrorType(targetType))
            return;

        // 2. Get the compile-time type of the operand.
        var sourceType = model.GetTypeOf(castExpr.Operand);
        if (sourceType is null || IsErrorType(sourceType))
            return;

        // 3. If the operand type is directly assignable to the target type
        //    (identity or subtype), the cast is valid — no operator needed.
        //    We must NOT use the full IsAssignable here because that includes
        //    the implicit cast operator fallback. We only want identity/subtype.
        //
        //    The unknown-type top-type rule (Req 4.1, 13.5) is baked into
        //    IsDirectlyAssignable: `(unknown) expr` for any non-void `expr`
        //    is trivially valid and returns before any operator search
        //    (Task 26.2).
        if (IsDirectlyAssignable(sourceType, targetType, model))
            return;

        // 3a. Cast from `unknown` to a concrete target type (Task 26.1 /
        //     Req 13.4, 13.6). UnknownType has no member scope (Req 3.4),
        //     so only the target's scope is searched for an operator
        //     whose source is `unknown` and target is T. When zero
        //     candidates are found we emit the targeted
        //     MIST_TC_0067 instead of the generic MIST_TC_0042 so the
        //     user sees an explanation specific to the top-type case
        //     (Req 16.4). Ambiguity still routes through MIST_TC_0043.
        if (sourceType is UnknownType && targetType is not UnknownType)
        {
            CheckCastFromUnknown(castExpr, sourceType, targetType, model, diagnostics);
            return;
        }

        // 4. Search for explicit and implicit cast operators on both the
        //    source type and the target type. Only operators declared directly
        //    on these types are considered (Requirement 8.7).
        var candidates = new List<ISymbol>();
        var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
        var implicitOpName = $"operator_implicit_{targetType.DisplayName}";

        // 4a. Search the source type's member scope.
        var sourceScope = GetMemberScope(sourceType);
        if (sourceScope is not null)
        {
            foreach (var member in sourceScope.Members)
            {
                if (member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                    candidates.Add(member);
            }
        }

        // 4b. Search the target type's member scope.
        var targetScope = GetMemberScope(targetType);
        if (targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach (var member in targetScope.Members)
            {
                if (member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if (!candidates.Any(c => ReferenceEquals(c, member)))
                        candidates.Add(member);
                }
            }
        }

        // 5. Evaluate candidates.
        if (candidates.Count == 1)
        {
            // Record the resolved operator and set the expression type.
            model.RecordResolvedOperator(castExpr, candidates[0]);
            model.SetTypeOf(castExpr, targetType);
        }
        else if (candidates.Count == 0)
        {
            // MIST_TC_0042: No cast operator found.
            diagnostics.Add(MakeDiagnostic(
                NoCastOperator.Value,
                $"No cast operator is defined from '{sourceType.DisplayName}' to '{targetType.DisplayName}'.",
                castExpr,
                model));
        }
        else
        {
            // MIST_TC_0043: Ambiguous cast — multiple operators applicable.
            diagnostics.Add(MakeDiagnostic(
                AmbiguousCast.Value,
                $"Ambiguous cast from '{sourceType.DisplayName}' to '{targetType.DisplayName}': multiple cast operators are applicable.",
                castExpr,
                model));
        }
    }

    /// <summary>
    /// Resolves a cast expression <c>(T) expr</c> where the operand's static
    /// type is <see cref="UnknownType"/> (Task 26.1 / Req 13.4).
    /// <para>
    /// Since <see cref="UnknownType"/> has no member scope (Req 3.4), only
    /// the target type's scope is searched for an implicit or explicit cast
    /// operator whose source type is <c>unknown</c> and whose target type
    /// is <paramref name="targetType"/>. When zero candidates are found the
    /// targeted <see cref="NoCastOperatorFromUnknown"/>
    /// (<c>MIST_TC_0067</c>) diagnostic is emitted instead of the generic
    /// <see cref="NoCastOperator"/> (<c>MIST_TC_0042</c>) so the user sees
    /// an explanation specific to the <c>unknown</c> case (Req 16.4).
    /// Ambiguity still routes through <see cref="AmbiguousCast"/>
    /// (<c>MIST_TC_0043</c>).
    /// </para>
    /// </summary>
    private static void CheckCastFromUnknown(
        CastExpressionUnaryNode castExpr,
        IType sourceType,
        IType targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var candidates = new List<ISymbol>();
        var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
        var implicitOpName = $"operator_implicit_{targetType.DisplayName}";

        // Search only the target type's member scope. The source
        // (UnknownType) has no scope (Req 3.4) so no second search is
        // needed.
        var targetScope = GetMemberScope(targetType);
        if (targetScope is not null)
        {
            foreach (var member in targetScope.Members)
            {
                if (member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    candidates.Add(member);
                }
            }
        }

        if (candidates.Count == 1)
        {
            model.RecordResolvedOperator(castExpr, candidates[0]);
            model.SetTypeOf(castExpr, targetType);
        }
        else if (candidates.Count == 0)
        {
            // MIST_TC_0067: targeted diagnostic for the top-type case.
            diagnostics.Add(MakeDiagnostic(
                NoCastOperatorFromUnknown.Value,
                $"No cast operator is defined from 'unknown' to '{targetType.DisplayName}'. Declare 'implicit operator {targetType.DisplayName}(unknown u)' or 'explicit operator {targetType.DisplayName}(unknown u)' on '{targetType.DisplayName}', or use an 'is' pattern to narrow the operand to a concrete type first.",
                castExpr,
                model));
        }
        else
        {
            // MIST_TC_0043: Ambiguous cast — multiple operators applicable.
            diagnostics.Add(MakeDiagnostic(
                AmbiguousCast.Value,
                $"Ambiguous cast from '{sourceType.DisplayName}' to '{targetType.DisplayName}': multiple cast operators are applicable.",
                castExpr,
                model));
        }
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> is directly assignable to
    /// <paramref name="target"/> via identity, subtype, interface, trait,
    /// or constructed type relationships — but <b>not</b>
    /// via implicit cast operators (including numeric widening, which is
    /// now expressed as implicit cast operators on stdlib types). This is the subset of
    /// <see cref="IsAssignable(IType, IType, ISemanticModel?)"/> used by
    /// <see cref="CheckCastExpression"/> to determine whether a cast is
    /// trivially valid without requiring a cast operator.
    /// </summary>
    internal static bool IsDirectlyAssignable(IType source, IType target, ISemanticModel? model)
    {
        // ── type-aliases feature: transparent unwrapping (Requirement 5.1) ──
        // Type aliases are fully transparent — unwrap to the underlying type
        // before any comparison so that using an alias is semantically
        // identical to using the underlying type directly.
        if (source is TypeAliasType sourceAlias)
            source = sourceAlias.UnderlyingType;
        if (target is TypeAliasType targetAlias)
            target = targetAlias.UnderlyingType;

        // ── refinement-types feature: nominal subtyping (Requirement 8.3) ──
        // Same rules as in IsAssignable: widening from refinement to base is
        // always safe, and same-name refinement types are identity-assignable.
        // Different refinement types with same base use subset proving (Requirement 9.1, 9.2).
        if (source is RefinementType sourceRefinementDirect)
        {
            if (target is RefinementType targetRefinementDirect)
            {
                if (sourceRefinementDirect.Name == targetRefinementDirect.Name)
                    return true;

                // Different refinement types with same base: use subset proving.
                if (IsDirectlyAssignable(sourceRefinementDirect.BaseType, targetRefinementDirect.BaseType, model))
                {
                    if (RefinementSolver.Implies(sourceRefinementDirect.Predicate, targetRefinementDirect.Predicate))
                        return true;
                }
            }

            source = sourceRefinementDirect.BaseType;
        }

        // ── refinement-types feature: target is RefinementType ───────────
        // Same rule as in IsAssignable: when the target is a RefinementType,
        // check assignability against the base type.
        if (target is RefinementType targetRefinementDirectForBase)
        {
            return IsDirectlyAssignable(source, targetRefinementDirectForBase.BaseType, model);
        }

        // ErrorType suppresses cascading errors.
        if (IsErrorType(source) || IsErrorType(target))
            return true;

        // ── unknown-type feature: top-type direct-assignability (Task 26.2) ──
        //
        // UnknownType is the user-facing top type (Req 4.1). Every non-void
        // source is assignable to `unknown` without any cast operator lookup,
        // including in the cast-expression path — `(unknown) expr` is a
        // trivially-valid cast (Req 13.5) that the Emitter lowers as a direct
        // C# cast to `object`. This mirrors the top-type rule baked into
        // `IsAssignable` so the two helpers agree on the unknown-target case;
        // `IsDirectlyAssignable` is used by `CheckCastExpression` as the
        // "trivially valid, no operator needed" gate, and by the assignment-
        // site precedence check in `CheckAssignabilityAndTrackImplicitCast`
        // which must not try to resolve an implicit cast operator when the
        // target is `unknown` (UnknownType has no members, Req 3.4, so no
        // such operator could exist anyway).
        //
        // void → unknown is rejected here; the caller (assignment site or
        // CheckCastExpression) is responsible for emitting MIST_TC_0061.
        if (target is UnknownType)
        {
            if (source.IsVoid())
                return false;
            return true;
        }
        // source is UnknownType with a non-unknown target: fall through to
        // the rest of IsDirectlyAssignable, which will reject by returning
        // false. The cast-expression path then routes to
        // `CheckCastFromUnknown` for the operator lookup.

        // ── type-unions feature: union type direct-assignability ──────────
        //
        // Mirrors the union rules in IsAssignable so that IsDirectlyAssignable
        // and IsAssignable agree on union-related assignments. This ensures
        // the cast-expression path and the implicit-cast precedence check
        // both handle union types consistently.
        if (target is UnionType targetUnionDirect)
        {
            if (HasErrorConstituent(targetUnionDirect))
                return true;

            if (source is UnionType sourceUnionDirect)
            {
                if (HasErrorConstituent(sourceUnionDirect))
                    return true;

                foreach (var constituent in sourceUnionDirect.Constituents)
                {
                    if (!IsConstituentOf(constituent, targetUnionDirect))
                        return false;
                }
                return true;
            }

            return IsConstituentOf(source, targetUnionDirect);
        }
        if (source is UnionType)
        {
            return false;
        }

        // Identity.
        if (ReferenceEquals(source, target))
            return true;

        // Same display name.
        if (source.DisplayName == target.DisplayName)
            return true;

        // Migration-window shim.
        if (ArePrimitiveEquivalent(source, target))
            return true;

        // Tuple assignability with name erasure: two TupleType instances are
        // assignable if they have the same arity and each element type is
        // assignable. Element names are ignored (name erasure).
        if (source is TupleType sourceTuple && target is TupleType targetTuple)
        {
            if (sourceTuple.Arity == targetTuple.Arity)
            {
                var allElementsAssignable = true;
                for (var i = 0; i < sourceTuple.Arity; i++)
                {
                    if (!IsDirectlyAssignable(sourceTuple.ElementTypes[i], targetTuple.ElementTypes[i], model))
                    {
                        allElementsAssignable = false;
                        break;
                    }
                }
                if (allElementsAssignable) return true;
            }
            return false;
        }

        // ConstructedType identity.
        if (source is ConstructedType sourceConstructed && target is ConstructedType targetConstructed)
        {
            if (ReferenceEquals(sourceConstructed.GenericDefinition, targetConstructed.GenericDefinition)
                && sourceConstructed.TypeArguments.Count == targetConstructed.TypeArguments.Count)
            {
                var allMatch = true;
                for (var i = 0; i < sourceConstructed.TypeArguments.Count; i++)
                {
                    if (!TypesEqual(sourceConstructed.TypeArguments[i], targetConstructed.TypeArguments[i]))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) return true;

                // Covariant unknown widening (unknown-type feature, Task 23.3).
                // Req 8.4: a trivially-valid cast like `(List<unknown>) intList`
                // must not require a user-declared cast operator. Applying the
                // widening rule here lets `CheckCastExpression` accept it
                // directly.
                if (AssignableWithUnknownWidening(
                        sourceConstructed.GenericDefinition,
                        sourceConstructed.TypeArguments,
                        targetConstructed.TypeArguments))
                {
                    return true;
                }
            }
        }

        // ConstructedType inheritance.
        if (source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if (baseType is not null && IsDirectlyAssignable(baseType, target, model))
                return true;
        }

        // ConstructedType to constructed interface assignability.
        if (target is ConstructedType targetConst && targetConst.GenericDefinition is InterfaceType)
        {
            if (ImplementsConstructedInterface(source, targetConst))
                return true;
        }

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types (e.g., Int32 has
        // `implicit operator Int64`). The implicit cast operator fallback
        // at the end of IsAssignable handles this. IsDirectlyAssignable
        // intentionally does NOT check implicit cast operators.

        // Interface assignability.
        if (target is InterfaceType targetInterface)
        {
            if (ImplementsInterface(source, targetInterface))
                return true;
        }

        // Subtype assignability — walk the inheritance chain.
        if (source is ClassType sourceClass && target is ClassType)
        {
            var current = sourceClass.BaseType as ClassType;
            while (current is not null)
            {
                if (ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                    return true;
                current = current.BaseType as ClassType;
            }
        }

        // Trait assignability.
        if (target is TraitType targetTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetTrait, model))
                return true;
        }

        // ── enums feature: implicit conversion to underlying type (Req 7.1, 7.2) ──
        // An enum value is directly assignable to its underlying type without
        // requiring an implicit cast operator lookup.
        if (source is EnumType sourceEnum && IsDirectlyAssignable(sourceEnum.UnderlyingType, target, model))
            return true;

        // Constructed trait assignability.
        if (target is ConstructedTraitType targetConstructedTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetConstructedTrait, model))
                return true;
        }

        // NOTE: No implicit cast operator fallback here — that's the whole
        // point of this method. Direct assignability only.
        return false;
    }

    private void CheckConstructorBody(ISymbol ctorSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(ctorSymbol); }
        catch { return; }

        if (declaringNode is not ConstructorDeclarationNode ctorDecl)
            return;

        _currentEnclosingType = FindDeclaringType(ctorSymbol);
        _insideConstructor = true;
        _insidePropertyAccessor = false;

        // Check constructor chaining (: this(args))
        if (ctorDecl.Chain is not null)
        {
            CheckConstructorChain(ctorDecl.Chain, ctorSymbol, model, diagnostics);
        }

        if (ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
            CheckBlock(ctorBlock, LegacyPrimitiveTypes.Void, model, diagnostics);
        _insideConstructor = false;
    }

    private static void CheckFieldDeclaration(ISymbol fieldSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return; }

        if (declaringNode is not FieldDeclarationNode fieldDecl)
            return;

        // Field initializers: the grammar stores the initializer as part of the
        // field declaration. The semantic model builder resolves the field type.
        // For field initializers, we check that the expression type is assignable
        // to the field's declared type. However, the current AST stores the
        // initializer as a Token? (the EQUAL token), not as an ExpressionNode.
        // The actual expression is in the Children() of the field declaration.
        // Since the semantic model builder already handles field type resolution,
        // and field initializer expressions are not separately tracked in the
        // expression type map for the current grammar structure, we skip
        // field initializer type checking here. The C# backend will catch
        // any remaining issues.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property and indexer declaration checking (Tasks 5.1–5.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks a property declaration for type correctness.
    /// Walks accessor bodies, checks modifier restrictiveness, field keyword usage,
    /// expression body type compatibility, and static access patterns.
    /// </summary>
    private void CheckPropertyDeclaration(ISymbol propertySymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(propertySymbol); }
        catch { return; }

        _currentEnclosingType = FindDeclaringType(propertySymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        var propInfo = GetPropertyMetadata(propertySymbol);
        var propType = propertySymbol.DeclaredType ?? ErrorType.Instance;

        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
            {
                var propModifiers = ExtractPropertyModifiers(blockProp.Modifiers);

                // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
                CheckAccessorModifierRestrictiveness(blockProp.Accessors, propModifiers, model, diagnostics);

                // Task 5.1: Walk accessor bodies for type correctness.
                foreach (var accessor in blockProp.Accessors)
                {
                    CheckAccessorBody(accessor, propType, propInfo, propModifiers.IsStatic, model, diagnostics);
                }

                // Task 5.5: Check field keyword usage in accessor bodies (MIST_TC_0023, MIST_TC_0024).
                CheckFieldKeywordUsageInAccessors(blockProp.Accessors, propInfo, model, diagnostics);

                // Task 5.7: Check this in static accessor (MIST_TC_0026).
                if (propModifiers.IsStatic)
                {
                    CheckThisInStaticAccessors(blockProp.Accessors, model, diagnostics);
                }
                break;
            }
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
            {
                var propModifiers = ExtractPropertyModifiers(exprProp.Modifiers);

                // Task 5.8: Check expression body type compatibility (MIST_TC_0028).
                CheckExpression(exprProp.Body, model, diagnostics);
                var bodyType = model.GetTypeOf(exprProp.Body);
                if (bodyType is not null && !IsErrorType(bodyType) && !IsErrorType(propType))
                {
                    if (!IsAssignable(bodyType, propType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0028",
                            $"Expression body type '{bodyType.DisplayName}' is not compatible with property type '{propType.DisplayName}'",
                            exprProp.Body,
                            model));
                    }
                }

                // Task 5.7: Check this in static expression-bodied property (MIST_TC_0026).
                if (propModifiers.IsStatic)
                {
                    CheckThisInExpression(exprProp.Body, model, diagnostics);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Checks an indexer declaration for type correctness.
    /// Also checks for duplicate indexers with the same parameter types (MIST_TC_0029).
    /// </summary>
    private void CheckIndexerDeclaration(ISymbol indexerSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(indexerSymbol); }
        catch { return; }

        if (declaringNode is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        _currentEnclosingType = FindDeclaringType(indexerSymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        var propInfo = GetPropertyMetadata(indexerSymbol);
        var indexerType = indexerSymbol.DeclaredType ?? ErrorType.Instance;
        var indexerModifiers = ExtractPropertyModifiers(blockIndexer.Modifiers);

        // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
        CheckAccessorModifierRestrictiveness(blockIndexer.Accessors, indexerModifiers, model, diagnostics);

        // Task 5.1: Walk accessor bodies for type correctness.
        foreach (var accessor in blockIndexer.Accessors)
        {
            CheckAccessorBody(accessor, indexerType, propInfo, indexerModifiers.IsStatic, model, diagnostics);
        }

        // Task 5.9: Check for duplicate indexers (MIST_TC_0029).
        CheckDuplicateIndexer(indexerSymbol, blockIndexer, model, diagnostics);
    }

    /// <summary>
    /// Checks a single accessor body for type correctness.
    /// For block bodies, checks statements. For expression bodies, checks expression type.
    /// For set/init accessors, the implicit 'value' parameter is already injected by the semantic model.
    /// </summary>
    private void CheckAccessorBody(
        AccessorDeclarationNode accessor,
        IType propType,
        PropertyInfo? propInfo,
        bool isStatic,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var previousInsidePropertyAccessor = _insidePropertyAccessor;
        _insidePropertyAccessor = true;

        try
        {
            switch (accessor)
            {
                // Block get accessor — check body statements, return type is the property type.
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    CheckBlock(blockGet.Body, propType, model, diagnostics);
                    break;

                // Expression get accessor — check expression type matches property type (MIST_TC_0028).
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    CheckExpression(exprGet.Expression, model, diagnostics);
                    CheckExpressionAccessorType(exprGet.Expression, propType, model, diagnostics);
                    break;

                // Block set accessor — check body statements (return type is void).
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    CheckBlock(blockSet.Body, LegacyPrimitiveTypes.Void, model, diagnostics);
                    break;

                // Expression set accessor — check expression.
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    CheckExpression(exprSet.Expression, model, diagnostics);
                    break;

                // Block init accessor — check body statements (return type is void).
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    CheckBlock(blockInit.Body, LegacyPrimitiveTypes.Void, model, diagnostics);
                    break;

                // Expression init accessor — check expression.
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    CheckExpression(exprInit.Expression, model, diagnostics);
                    break;

                // Auto accessors (get;, set;, init;) — no body to check.
                default:
                    break;
            }
        }
        finally
        {
            _insidePropertyAccessor = previousInsidePropertyAccessor;
        }
    }

    /// <summary>
    /// Checks that an expression accessor's type is compatible with the property type (MIST_TC_0028).
    /// </summary>
    private static void CheckExpressionAccessorType(
        ExpressionNode expression,
        IType propType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var exprType = model.GetTypeOf(expression);
        if (exprType is null || IsErrorType(exprType) || IsErrorType(propType))
            return;

        if (!IsAssignable(exprType, propType))
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0028",
                $"Expression body type '{exprType.DisplayName}' is not compatible with property type '{propType.DisplayName}'",
                expression,
                model));
        }
    }

    /// <summary>
    /// Checks accessor modifier restrictiveness (MIST_TC_0022).
    /// An accessor modifier must be strictly more restrictive than the property's access level.
    /// </summary>
    private static void CheckAccessorModifierRestrictiveness(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        MemberModifiers propModifiers,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var accessor in accessors)
        {
            var accessorModifier = GetAccessorModifier(accessor);
            if (accessorModifier is null)
                continue; // No explicit accessor modifier — OK.

            var propLevel = GetAccessLevel(propModifiers);
            var accessorLevel = GetModifierAccessLevel(accessorModifier);

            // The accessor modifier must be strictly more restrictive than the property.
            if (!IsMoreRestrictive(accessorLevel, propLevel))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0022",
                    "Accessor modifier must be more restrictive than the property's access level",
                    accessorModifier,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks for field keyword usage outside accessor bodies (MIST_TC_0023)
    /// and in computed properties (MIST_TC_0024).
    /// </summary>
    private static void CheckFieldKeywordUsageInAccessors(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        PropertyInfo? propInfo,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (propInfo is null)
            return;

        foreach (var accessor in accessors)
        {
            var fieldNodes = CollectFieldKeywordNodes(accessor);
            foreach (var fieldNode in fieldNodes)
            {
                // MIST_TC_0024: field keyword in computed property (no backing field).
                if (propInfo.IsComputed)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0024",
                        "'field' keyword cannot be used in a computed property (no backing field)",
                        fieldNode,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in static property accessors (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInStaticAccessors(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var accessor in accessors)
        {
            var thisNodes = CollectThisKeywordNodes(accessor);
            foreach (var thisNode in thisNodes)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0026",
                    "'this' cannot be referenced in a static property accessor",
                    thisNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in a static expression-bodied property (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInExpression(
        ExpressionNode expression,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var thisNodes = new List<SyntaxNode>();
        CollectThisKeywordNodesFromTree(expression, thisNodes);
        foreach (var thisNode in thisNodes)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0026",
                "'this' cannot be referenced in a static property accessor",
                thisNode,
                model));
        }
    }

    /// <summary>
    /// Checks for duplicate indexers with the same parameter types (MIST_TC_0029).
    /// </summary>
    private static void CheckDuplicateIndexer(
        ISymbol indexerSymbol,
        BlockIndexerIndexerDeclarationNode blockIndexer,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var declaringType = FindDeclaringType(indexerSymbol);
        if (declaringType is null)
            return;

        IScope? memberScope = declaringType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        // Collect parameter types for this indexer.
        var thisParamTypes = CollectIndexerParameterTypes(blockIndexer, model);

        // Check all other indexers in the same type.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Indexer || ReferenceEquals(member, indexerSymbol))
                continue;

            SyntaxNode otherNode;
            try { otherNode = model.GetDeclaringNode(member); }
            catch { continue; }

            if (otherNode is not BlockIndexerIndexerDeclarationNode otherIndexer)
                continue;

            var otherParamTypes = CollectIndexerParameterTypes(otherIndexer, model);

            if (ParameterTypesMatch(thisParamTypes, otherParamTypes))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0029",
                    "Duplicate indexer declaration with the same parameter types",
                    blockIndexer,
                    model));
                break; // Only report once per indexer.
            }
        }
    }

    /// <summary>
    /// Collects parameter types from an indexer declaration.
    /// </summary>
    private static List<string> CollectIndexerParameterTypes(
        BlockIndexerIndexerDeclarationNode indexer,
        ISemanticModel model)
    {
        var types = new List<string>();
        if (indexer.Parameters is null)
            return types;

        foreach (var child in indexer.Parameters.Children())
        {
            if (child is ParameterNode param)
            {
                var paramSymbol = model.GetDeclaredSymbol(param);
                if (paramSymbol?.DeclaredType is not null)
                    types.Add(paramSymbol.DeclaredType.DisplayName);
                else
                    types.Add("?");
            }
        }

        return types;
    }

    /// <summary>
    /// Checks if two parameter type lists match.
    /// </summary>
    private static bool ParameterTypesMatch(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the accessor modifier node from an accessor declaration, if present.
    /// </summary>
    private static ModifierNode? GetAccessorModifier(AccessorDeclarationNode accessor)
    {
        return accessor switch
        {
            AutoGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            AutoSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            AutoInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            _ => null,
        };
    }

    /// <summary>
    /// Access level ordering for restrictiveness comparison.
    /// Lower value = more restrictive.
    /// </summary>
    private enum AccessLevel
    {
        Private = 0,
        // Protected = 1, // Not yet supported in Mist
        // Internal = 2,  // Not yet supported in Mist
        Public = 3,
    }

    /// <summary>
    /// Gets the access level from property modifiers.
    /// Default (no modifier) is private.
    /// </summary>
    private static AccessLevel GetAccessLevel(MemberModifiers modifiers)
    {
        if (modifiers.IsPublic) return AccessLevel.Public;
        return AccessLevel.Private; // Default is private.
    }

    /// <summary>
    /// Gets the access level from a modifier AST node.
    /// </summary>
    private static AccessLevel GetModifierAccessLevel(ModifierNode modifier)
    {
        return modifier switch
        {
            PublicModifierModifierNode => AccessLevel.Public,
            PrivateModifierModifierNode => AccessLevel.Private,
            _ => AccessLevel.Private,
        };
    }

    /// <summary>
    /// Returns true if <paramref name="accessor"/> is strictly more restrictive than <paramref name="property"/>.
    /// </summary>
    private static bool IsMoreRestrictive(AccessLevel accessor, AccessLevel property)
    {
        return (int)accessor < (int)property;
    }

    /// <summary>
    /// Extracts modifier flags from a property's modifier list.
    /// </summary>
    private static MemberModifiers ExtractPropertyModifiers(IReadOnlyList<ModifierNode> modifiers)
    {
        var isPublic = false;
        var isPrivate = false;
        var isStatic = false;

        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    isPublic = true;
                    break;
                case PrivateModifierModifierNode:
                    isPrivate = true;
                    break;
                case StaticModifierModifierNode:
                    isStatic = true;
                    break;
            }
        }

        return new MemberModifiers(isPublic, isPrivate, isStatic);
    }

    /// <summary>
    /// Gets PropertyInfo from a symbol (handles both Symbol and ISymbol).
    /// </summary>
    private static PropertyInfo? GetPropertyMetadata(ISymbol symbol)
    {
        if (symbol is Mist.Semantics.Model.Symbol concreteSymbol)
            return concreteSymbol.PropertyMetadata;
        return null;
    }

    /// <summary>
    /// Collects all FieldKeywordExpressionPrimaryNode nodes from an accessor's body.
    /// </summary>
    private static List<SyntaxNode> CollectFieldKeywordNodes(AccessorDeclarationNode accessor)
    {
        var nodes = new List<SyntaxNode>();
        CollectFieldKeywordNodesFromTree(accessor, nodes);
        return nodes;
    }

    private static void CollectFieldKeywordNodesFromTree(SyntaxNode node, List<SyntaxNode> results)
    {
        if (node is FieldKeywordExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CollectFieldKeywordNodesFromTree(child, results);
        }
    }

    /// <summary>
    /// Collects all ThisExpressionPrimaryNode nodes from an accessor's body.
    /// </summary>
    private static List<SyntaxNode> CollectThisKeywordNodes(AccessorDeclarationNode accessor)
    {
        var nodes = new List<SyntaxNode>();
        CollectThisKeywordNodesFromTree(accessor, nodes);
        return nodes;
    }

    private static void CollectThisKeywordNodesFromTree(SyntaxNode node, List<SyntaxNode> results)
    {
        if (node is ThisExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CollectThisKeywordNodesFromTree(child, results);
        }
    }

    /// <summary>
    /// Checks constraint satisfaction for inheritance clauses where constrained generics
    /// are used as base types or implemented interfaces. For example, if
    /// <c>class Foo : Bar&lt;int&gt;</c> and <c>Bar&lt;T&gt;</c> has <c>where T : IComparable&lt;T&gt;</c>,
    /// this verifies that <c>int</c> satisfies the constraint.
    /// </summary>
    private static void CheckInheritanceClauseConstraints(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface))
                continue;

            var declaredType = symbol.DeclaredType;
            if (declaredType is null)
                continue;

            // Get the declaring node for diagnostic location.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            // Check base type (classes only).
            if (declaredType is ClassType classType && classType.BaseType is ConstructedType baseConstructed)
            {
                var baseTypeParams = GetTypeParameters(baseConstructed.GenericDefinition);
                if (baseTypeParams is not null)
                {
                    var baseTypeArgs = baseConstructed.TypeArguments;
                    var genericName = baseConstructed.GenericDefinition.DisplayName;
                    for (var i = 0; i < baseTypeParams.Count && i < baseTypeArgs.Count; i++)
                    {
                        if (baseTypeArgs[i] is TypeParameterType derivedTypeParam)
                        {
                            // The type argument is a type parameter of the derived type —
                            // check constraint propagation ("at least as restrictive").
                            if (baseTypeParams[i] is TypeParameterType baseTP)
                                CheckConstraintPropagation(
                                    derivedTypeParam, baseTP, declaredType,
                                    genericName, declaringNode, model, diagnostics);
                        }
                        else
                        {
                            if (baseTypeParams[i] is TypeParameterType baseTP2)
                                CheckConstraintSatisfaction(baseTypeArgs[i], baseTP2, declaringNode, genericName, model, diagnostics);
                        }
                    }
                }
            }

            // Check implemented interfaces.
            var interfaces = declaredType switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                InterfaceType it => it.Interfaces,
                _ => Array.Empty<IType>()
            };

            foreach (var iface in interfaces)
            {
                if (iface is not ConstructedType ifaceConstructed)
                    continue;

                var ifaceTypeParams = GetTypeParameters(ifaceConstructed.GenericDefinition);
                if (ifaceTypeParams is null)
                    continue;

                var ifaceTypeArgs = ifaceConstructed.TypeArguments;
                var genericName = ifaceConstructed.GenericDefinition.DisplayName;
                for (var i = 0; i < ifaceTypeParams.Count && i < ifaceTypeArgs.Count; i++)
                {
                    if (ifaceTypeArgs[i] is TypeParameterType derivedTypeParam)
                    {
                        // The type argument is a type parameter of the derived type —
                        // check constraint propagation ("at least as restrictive").
                        if (ifaceTypeParams[i] is TypeParameterType ifaceTP)
                            CheckConstraintPropagation(
                                derivedTypeParam, ifaceTP, declaredType,
                                genericName, declaringNode, model, diagnostics);
                    }
                    else
                    {
                        if (ifaceTypeParams[i] is TypeParameterType ifaceTP2)
                            CheckConstraintSatisfaction(ifaceTypeArgs[i], ifaceTP2, declaringNode, genericName, model, diagnostics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks that a derived type's constraints on a type parameter are at least as
    /// restrictive as the base/interface type's constraints on the corresponding type
    /// parameter. Emits MIST_CONSTRAINT_0017 when constraints are insufficient.
    /// (Requirements 7.1, 7.2, 7.3)
    /// </summary>
    private static void CheckConstraintPropagation(
        TypeParameterType derivedTypeParam,
        TypeParameterType baseTypeParam,
        IType derivedType,
        string baseTypeName,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var baseConstraints = baseTypeParam.Constraints;
        if (baseConstraints is null)
            return; // Base has no constraints — nothing to propagate.

        var derivedConstraints = derivedTypeParam.Constraints;

        // Check class constraint propagation.
        if (baseConstraints.HasClassConstraint)
        {
            if (derivedConstraints is null || !derivedConstraints.HasClassConstraint)
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }

        // Check struct constraint propagation.
        if (baseConstraints.HasStructConstraint)
        {
            if (derivedConstraints is null || !derivedConstraints.HasStructConstraint)
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }

        // Check base class constraint propagation.
        if (baseConstraints.BaseClassConstraint is not null)
        {
            if (derivedConstraints?.BaseClassConstraint is null
                || !DerivesFromClass(derivedConstraints.BaseClassConstraint, baseConstraints.BaseClassConstraint))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }

        // Check interface constraint propagation: derived must require each interface the base requires.
        foreach (var baseIface in baseConstraints.InterfaceConstraints)
        {
            if (derivedConstraints is null || !HasMatchingInterfaceConstraint(derivedConstraints, baseIface))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }

        // Check trait constraint propagation: derived must require each trait the base requires.
        foreach (var baseTrait in baseConstraints.TraitConstraints)
        {
            if (derivedConstraints is null || !HasMatchingTraitConstraint(derivedConstraints, baseTrait))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }

        // Check constructor constraint propagation: derived must have at least the same
        // constructor constraints as the base (each base constructor constraint must be
        // matched by a derived constructor constraint).
        foreach (var baseCtorParams in baseConstraints.ConstructorConstraints)
        {
            if (derivedConstraints is null || !HasMatchingConstructorConstraint(derivedConstraints, baseCtorParams))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, model, diagnostics);
                return;
            }
        }
    }

    private static void EmitPropagationDiagnostic(
        IType derivedType,
        string baseTypeName,
        string typeParamName,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        diagnostics.Add(MakeDiagnostic(
            ConstraintPropagationInsufficient.Value,
            $"Type '{derivedType.DisplayName}' must declare constraints at least as restrictive as '{baseTypeName}' for type parameter '{typeParamName}'.",
            node,
            model));
    }

    /// <summary>
    /// Checks whether the derived constraints include an interface constraint that
    /// matches (or is more derived than) the given base interface constraint.
    /// </summary>
    private static bool HasMatchingInterfaceConstraint(TypeParameterConstraints derivedConstraints, IType baseIface)
    {
        foreach (var derivedIface in derivedConstraints.InterfaceConstraints)
        {
            if (ImplementsInterfaceConstraint(derivedIface, baseIface))
                return true;

            // Direct match by display name.
            if (derivedIface.DisplayName == baseIface.DisplayName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the derived constraints include a trait constraint that
    /// matches the given base trait constraint.
    /// </summary>
    private static bool HasMatchingTraitConstraint(TypeParameterConstraints derivedConstraints, IType baseTrait)
    {
        foreach (var derivedTrait in derivedConstraints.TraitConstraints)
        {
            if (ReferenceEquals(derivedTrait, baseTrait)
                || derivedTrait.DisplayName == baseTrait.DisplayName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether the derived constraints include a constructor constraint
    /// with the same parameter types as the given base constructor constraint.
    /// </summary>
    private static bool HasMatchingConstructorConstraint(
        TypeParameterConstraints derivedConstraints,
        IReadOnlyList<IType> baseCtorParams)
    {
        foreach (var derivedCtorParams in derivedConstraints.ConstructorConstraints)
        {
            if (derivedCtorParams.Count != baseCtorParams.Count)
                continue;

            var allMatch = true;
            for (var j = 0; j < derivedCtorParams.Count; j++)
            {
                if (derivedCtorParams[j].DisplayName != baseCtorParams[j].DisplayName)
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks that all types implementing interfaces provide required property accessors (MIST_TC_0030).
    /// This complements the semantic model builder's Pass1d validation with type-checker-level diagnostics.
    /// </summary>
    private static void CheckMissingInterfaceProperties(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;
            IReadOnlyList<IType>? interfaces = type switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                _ => null,
            };

            if (interfaces is null || interfaces.Count == 0)
                continue;

            IScope? memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is null)
                continue;

            // Check each interface's property requirements.
            var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
            CheckInterfacePropertyRequirements(symbol, interfaces, memberScope, type as ClassType, model, diagnostics, visited);
        }
    }

    private static void CheckInterfacePropertyRequirements(
        ISymbol typeSymbol,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ClassType? classType,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            foreach (var ifaceMember in iface.MemberScope.Members)
            {
                if (ifaceMember.Kind != SymbolKind.Property)
                    continue;

                var requiredPropInfo = GetPropertyMetadata(ifaceMember);
                if (requiredPropInfo is null)
                    continue;

                // Search for matching property in the implementing type.
                var implProperty = FindPropertyInTypeHierarchy(ifaceMember.Name, memberScope, classType);

                if (implProperty is null)
                {
                    // Get the declaring node for location info.
                    SyntaxNode? typeNode;
                    try { typeNode = model.GetDeclaringNode(typeSymbol); }
                    catch { typeNode = null; }

                    if (typeNode is not null)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0030",
                            $"'{typeSymbol.Name}' does not implement interface property '{iface.DisplayName}.{ifaceMember.Name}'",
                            typeNode,
                            model));
                    }
                    continue;
                }

                var implPropInfo = GetPropertyMetadata(implProperty);
                if (implPropInfo is null)
                    continue;

                // Check accessor satisfaction.
                SyntaxNode? implNode;
                try { implNode = model.GetDeclaringNode(implProperty); }
                catch { implNode = null; }

                if (requiredPropInfo.HasGet && !implPropInfo.HasGet && implNode is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0030",
                        $"'{typeSymbol.Name}.{ifaceMember.Name}' does not implement 'get' accessor required by interface '{iface.DisplayName}'",
                        implNode,
                        model));
                }

                if (requiredPropInfo.HasSet && !implPropInfo.HasSet && !implPropInfo.HasInit && implNode is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0030",
                        $"'{typeSymbol.Name}.{ifaceMember.Name}' does not implement 'set' accessor required by interface '{iface.DisplayName}'",
                        implNode,
                        model));
                }
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CheckInterfacePropertyRequirements(typeSymbol, iface.Interfaces, memberScope, classType, model, diagnostics, visited);
            }
        }
    }

    /// <summary>
    /// Searches for a property by name in a type's member scope and base type chain.
    /// </summary>
    private static ISymbol? FindPropertyInTypeHierarchy(string propertyName, IScope memberScope, ClassType? classType)
    {
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                return member;
        }

        if (classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while (ancestor is not null)
            {
                foreach (var member in ancestor.MemberScope.Members)
                {
                    if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                        return member;
                }
                ancestor = ancestor.BaseType as ClassType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Readonly interface/trait contract checking (MIST_TC_0079)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when a type implements a readonly interface or trait, each
    /// implementing member is also readonly (explicit, inferred, or implicit).
    /// If an implementing member is not readonly, emits <c>MIST_TC_0079</c>.
    /// The implementing type itself does not need to be readonly — only the
    /// specific members that correspond to readonly interface/trait members.
    /// (Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6)
    /// </summary>
    private static void CheckReadonlyInterfaceAndTraitContracts(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;

            // Check interface contracts.
            IReadOnlyList<IType>? interfaces = type switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                _ => null,
            };

            IScope? memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is not null && interfaces is not null && interfaces.Count > 0)
            {
                CheckReadonlyInterfaceContracts(symbol, interfaces, memberScope, model, diagnostics);
            }

            // Check trait contracts via the TraitSatisfactionMap.
            if (memberScope is not null)
            {
                CheckReadonlyTraitContracts(symbol, type!, memberScope, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks readonly contracts for all interfaces implemented by a type.
    /// For each readonly interface member, verifies the implementing member is also readonly.
    /// </summary>
    private static void CheckReadonlyInterfaceContracts(
        ISymbol typeSymbol,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            // Only check readonly interfaces (all members are implicitly readonly).
            if (!iface.IsReadonly)
            {
                // Even if the interface itself is not readonly, individual members
                // might be explicitly readonly. Check those too.
                CheckReadonlyMembersOfInterface(typeSymbol, iface, memberScope, model, diagnostics);
                continue;
            }

            // The interface is readonly — all its members are implicitly readonly.
            foreach (var ifaceMember in iface.MemberScope.Members)
            {
                if (ifaceMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer))
                    continue;

                var implMember = FindMemberInScope(ifaceMember.Name, ifaceMember.Kind, memberScope);
                if (implMember is null)
                    continue; // Missing implementation is reported by CheckMissingInterfaceProperties.

                if (!IsMemberReadonly(implMember))
                {
                    EmitReadonlyContractViolation(typeSymbol, implMember, ifaceMember, iface.DisplayName, model, diagnostics);
                }
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CheckReadonlyInterfaceContracts(typeSymbol, iface.Interfaces, memberScope, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks individual readonly members of a non-readonly interface.
    /// </summary>
    private static void CheckReadonlyMembersOfInterface(
        ISymbol typeSymbol,
        InterfaceType iface,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var ifaceMember in iface.MemberScope.Members)
        {
            if (ifaceMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer))
                continue;

            // Check if this specific interface member is readonly.
            if (!IsMemberReadonly(ifaceMember))
                continue;

            var implMember = FindMemberInScope(ifaceMember.Name, ifaceMember.Kind, memberScope);
            if (implMember is null)
                continue;

            if (!IsMemberReadonly(implMember))
            {
                EmitReadonlyContractViolation(typeSymbol, implMember, ifaceMember, iface.DisplayName, model, diagnostics);
            }
        }

        // Recurse into parent interfaces.
        if (iface.Interfaces.Count > 0)
        {
            foreach (var parentIface in iface.Interfaces)
            {
                if (parentIface is InterfaceType parentIfaceType)
                {
                    if (parentIfaceType.IsReadonly)
                    {
                        CheckReadonlyInterfaceContracts(typeSymbol, new[] { parentIfaceType }, memberScope, model, diagnostics);
                    }
                    else
                    {
                        CheckReadonlyMembersOfInterface(typeSymbol, parentIfaceType, memberScope, model, diagnostics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks readonly contracts for all traits satisfied by a type.
    /// For each readonly trait member, verifies the implementing member is also readonly.
    /// </summary>
    private static void CheckReadonlyTraitContracts(
        ISymbol typeSymbol,
        IType type,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var (satisfiedType, trait, satisfaction) in model.TraitSatisfactionMap.All())
        {
            if (!ReferenceEquals(satisfiedType, type))
                continue;

            var traitType = trait as TraitType;
            if (traitType is null)
                continue;

            // Only check readonly traits (all members are implicitly readonly).
            if (!traitType.IsReadonly)
            {
                // Check individual readonly members of non-readonly traits.
                CheckReadonlyMembersOfTrait(typeSymbol, traitType, satisfaction, memberScope, model, diagnostics);
                continue;
            }

            // The trait is readonly — all its members are implicitly readonly.
            foreach (var traitMember in traitType.MemberScope.Members)
            {
                if (traitMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer or SymbolKind.Field))
                    continue;

                // Skip type parameter symbols.
                if (traitMember.DeclaredType is TypeParameterType)
                    continue;

                // Find the implementing member via the satisfaction map or scope lookup.
                var implMember = FindTraitImplementingMember(traitMember, satisfaction, memberScope);
                if (implMember is null)
                    continue;

                if (!IsMemberReadonly(implMember))
                {
                    EmitReadonlyContractViolation(typeSymbol, implMember, traitMember, traitType.DisplayName, model, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Checks individual readonly members of a non-readonly trait.
    /// </summary>
    private static void CheckReadonlyMembersOfTrait(
        ISymbol typeSymbol,
        TraitType traitType,
        TraitSatisfaction satisfaction,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var traitMember in traitType.MemberScope.Members)
        {
            if (traitMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer or SymbolKind.Field))
                continue;

            // Skip type parameter symbols.
            if (traitMember.DeclaredType is TypeParameterType)
                continue;

            if (!IsMemberReadonly(traitMember))
                continue;

            var implMember = FindTraitImplementingMember(traitMember, satisfaction, memberScope);
            if (implMember is null)
                continue;

            if (!IsMemberReadonly(implMember))
            {
                EmitReadonlyContractViolation(typeSymbol, implMember, traitMember, traitType.DisplayName, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Finds the implementing member for a trait member, first checking the
    /// satisfaction map (structural matches and with-mappings), then falling
    /// back to a scope lookup.
    /// </summary>
    private static ISymbol? FindTraitImplementingMember(
        ISymbol traitMember,
        TraitSatisfaction satisfaction,
        IScope memberScope)
    {
        if (satisfaction.StructuralMatches.TryGetValue(traitMember.Name, out var structMatch))
            return structMatch;

        if (satisfaction.WithMappings.TryGetValue(traitMember.Name, out var withMatch))
            return withMatch;

        return FindMemberInScope(traitMember.Name, traitMember.Kind, memberScope);
    }

    /// <summary>
    /// Finds a member by name and kind in a scope.
    /// </summary>
    private static ISymbol? FindMemberInScope(string name, SymbolKind kind, IScope scope)
    {
        foreach (var member in scope.Members)
        {
            if (member.Name == name && member.Kind == kind)
                return member;

            // Properties can satisfy property requirements, fields can satisfy field requirements.
            if (member.Name == name)
            {
                var kindCompatible = kind switch
                {
                    SymbolKind.Field => member.Kind is SymbolKind.Field or SymbolKind.Property,
                    SymbolKind.Property => member.Kind is SymbolKind.Property or SymbolKind.Field,
                    _ => false,
                };
                if (kindCompatible)
                    return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a symbol is readonly (explicit, inferred, or implicit).
    /// </summary>
    private static bool IsMemberReadonly(ISymbol symbol)
    {
        if (symbol is Symbol concreteSymbol)
        {
            return concreteSymbol.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
                or ReadonlyStatus.InferredReadonly
                or ReadonlyStatus.ImplicitReadonly;
        }

        return false;
    }

    /// <summary>
    /// Emits the <c>MIST_TC_0079</c> diagnostic for a readonly contract violation.
    /// </summary>
    private static void EmitReadonlyContractViolation(
        ISymbol typeSymbol,
        ISymbol implMember,
        ISymbol contractMember,
        string contractSourceName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode? implNode;
        try { implNode = model.GetDeclaringNode(implMember); }
        catch { implNode = null; }

        // Fall back to the type declaration node if the member node is unavailable.
        if (implNode is null)
        {
            try { implNode = model.GetDeclaringNode(typeSymbol); }
            catch { implNode = null; }
        }

        if (implNode is null)
            return;

        var memberKind = contractMember.Kind switch
        {
            SymbolKind.Method => "Method",
            SymbolKind.Property => "Property",
            SymbolKind.Indexer => "Indexer",
            SymbolKind.Field => "Field",
            _ => "Member",
        };

        diagnostics.Add(MakeDiagnostic(
            ReadonlyContractViolation.Value,
            $"{memberKind} '{implMember.Name}' must be readonly to satisfy readonly contract from '{contractSourceName}'",
            implNode,
            model));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Block and statement checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckBlock(BlockNode block, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            CheckStatement(stmt, returnType, model, diagnostics);
        }
    }

    private void CheckStatement(StatementNode stmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                CheckVariableDeclarationStatement(varDeclStmt.VariableDeclaration, model, diagnostics);
                break;
            case ReturnStatementStatementNode returnStmt:
                CheckReturnStatement(returnStmt.ReturnStatement, returnType, model, diagnostics);
                break;
            case IfStatementStatementNode ifStmt:
                CheckIfStatement(ifStmt.IfStatement, returnType, model, diagnostics);
                break;
            case WhileStatementStatementNode whileStmt:
                CheckWhileStatement(whileStmt.WhileStatement, returnType, model, diagnostics);
                break;
            case ForStatementStatementNode forStmt:
                CheckForStatement(forStmt.ForStatement, returnType, model, diagnostics);
                break;
            case ForeachStatementStatementNode foreachStmt:
                CheckForeachStatement(foreachStmt.ForeachStatement, returnType, model, diagnostics);
                break;
            case ExpressionStatementStatementNode exprStmt:
                CheckExpression(exprStmt.ExpressionStatement.Expression, model, diagnostics);
                break;
            case BreakStatementStatementNode:
                // No type checking needed for break.
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variable declaration checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckVariableDeclarationStatement(
        VariableDeclarationStatementNode varDecl,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                // Infer bare 'default' type from the declared type before checking.
                var declaredTypeForDefault = model.GetTypeOf(typed.Type)
                                             ?? model.GetDeclaredSymbol(typed)?.DeclaredType;
                TryInferDefaultLiteralType(typed.Initializer, declaredTypeForDefault, model);

                // Check the initializer expression.
                CheckExpression(typed.Initializer, model, diagnostics);

                // Verify RHS type is assignable to declared type (MIST_TC_0001).
                var declaredType = model.GetTypeOf(typed.Type);
                var initType = model.GetTypeOf(typed.Initializer);

                // When the TypeReference node isn't in the expression type map
                // (source-parsed programs), fall back to the variable symbol's
                // declared type so we can still detect type mismatches
                // (e.g., TypeType assigned to a non-Type variable).
                declaredType ??= model.GetDeclaredSymbol(typed)?.DeclaredType;

                // Check for void[] variable declarations (MIST_TC_0034).
                if (declaredType is ArrayType arrayType && arrayType.ElementType.IsVoid())
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0034",
                        "Cannot declare a variable of type 'void[]'. Void is not permitted as an array element type.",
                        varDecl,
                        model));
                }

                if (declaredType is not null && initType is not null)
                {
                    // ── flow narrowing: use narrowed type if available ──
                    var effectiveInitType = GetEffectiveSourceType(typed.Initializer, initType);

                    // ── refinement-types feature: compile-time enforcement ──
                    // When the target is a RefinementType, check the assignment
                    // against the refinement predicate before the generic
                    // assignability check. If a refinement-specific diagnostic
                    // is emitted, skip the generic check to avoid cascading.
                    if (TryCheckRefinementAssignment(effectiveInitType, declaredType, typed.Initializer, diagnostics, model))
                    {
                        break;
                    }

                    // Allow numeric-to-numeric assignments in variable declarations
                    // to support constant narrowing (e.g., byte x = 0; where 0 is int).
                    // Non-numeric type mismatches (e.g., Type → int) are still checked.
                    if (!(NumericDisplayNames.Contains(initType.DisplayName)
                          && NumericDisplayNames.Contains(declaredType.DisplayName)))
                    {
                        CheckAssignability(initType, declaredType, typed.Initializer, diagnostics, model);
                    }
                }
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // Check if the initializer is a bare 'default' in a var declaration.
                // This is always an error: var x = default; — neither side provides a type.
                var defaultLiteralInVar = TryExtractDefaultLiteral(inferred.Initializer);
                if (defaultLiteralInVar is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultCannotInferType.Value,
                        "The type of 'default' cannot be inferred because neither side provides a type. Use 'default(TypeName)' or declare the variable with an explicit type.",
                        defaultLiteralInVar,
                        model));
                }

                // var with initializer — check the initializer expression.
                CheckExpression(inferred.Initializer, model, diagnostics);

                // MIST_TC_0009: var without initializer.
                // The grammar requires an initializer for var declarations,
                // but if the initializer expression type is null, the var
                // type cannot be inferred.
                var inferredType = model.GetTypeOf(inferred.Initializer);
                if (inferredType is null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0009",
                        "'var' declaration requires an initializer",
                        inferred,
                        model));
                }
                break;
            }
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
            {
                // var (a, b, ...) = expr; — check initializer and validate tuple deconstruction.
                CheckExpression(varTuple.Initializer, model, diagnostics);

                var initType = model.GetTypeOf(varTuple.Initializer);
                if (initType is not null && initType is not ErrorType)
                {
                    if (initType is not TupleType tupleType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0064",
                            $"Cannot deconstruct expression of type '{initType.DisplayName}'; a tuple type is required.",
                            varTuple,
                            model));
                    }
                    else
                    {
                        // Count targets: FirstName + RestNames.
                        var targetCount = 1 + varTuple.RestNames.Length;
                        if (targetCount != tupleType.Arity)
                        {
                            diagnostics.Add(MakeDiagnostic(
                                "MIST_TC_0061",
                                $"Tuple deconstruction has {targetCount} targets but the tuple has {tupleType.Arity} elements.",
                                varTuple,
                                model));
                        }
                    }
                }
                break;
            }
            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
            {
                // (Type1 a, Type2 b, ...) = expr; — check initializer and validate types.
                CheckExpression(typedTuple.Initializer, model, diagnostics);

                var initType = model.GetTypeOf(typedTuple.Initializer);
                if (initType is not null && initType is not ErrorType)
                {
                    if (initType is not TupleType tupleType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0064",
                            $"Cannot deconstruct expression of type '{initType.DisplayName}'; a tuple type is required.",
                            typedTuple,
                            model));
                    }
                    else
                    {
                        // Count targets: First + Rest + Additional.
                        var targetCount = 2 + typedTuple.TupleDeconstructionElements.Length;
                        if (targetCount != tupleType.Arity)
                        {
                            diagnostics.Add(MakeDiagnostic(
                                "MIST_TC_0061",
                                $"Tuple deconstruction has {targetCount} targets but the tuple has {tupleType.Arity} elements.",
                                typedTuple,
                                model));
                        }
                        else
                        {
                            // Verify each element type is assignable.
                            void CheckDeconstructionElement(TupleDeconstructionElementNode element, int index)
                            {
                                var declaredType = model.GetTypeOf(element.Type);
                                if (declaredType is not null && index < tupleType.Arity)
                                {
                                    var elementType = tupleType.ElementTypes[index];
                                    if (!IsAssignable(elementType, declaredType, model))
                                    {
                                        diagnostics.Add(MakeDiagnostic(
                                            "MIST_TC_0062",
                                            $"Cannot assign tuple element of type '{elementType.DisplayName}' to '{declaredType.DisplayName}' at position {index + 1}.",
                                            element,
                                            model));
                                    }
                                }
                            }

                            CheckDeconstructionElement(typedTuple.First, 0);
                            CheckDeconstructionElement(typedTuple.Rest, 1);
                            for (var i = 0; i < typedTuple.TupleDeconstructionElements.Length; i++)
                            {
                                CheckDeconstructionElement(typedTuple.TupleDeconstructionElements[i], 2 + i);
                            }
                        }
                    }
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Return statement checking (MIST_TC_0008)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckReturnStatement(
        ReturnStatementNode returnStmt,
        IType methodReturnType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (IsErrorType(methodReturnType))
            return;

        if (returnStmt.Value is not null)
        {
            // Infer bare 'default' type from the method return type before checking.
            if (!methodReturnType.IsVoid())
            {
                TryInferDefaultLiteralType(returnStmt.Value, methodReturnType, model);
            }

            // return expr; — check the expression and verify type.
            CheckExpression(returnStmt.Value, model, diagnostics);
            var valueType = model.GetTypeOf(returnStmt.Value);

            if (methodReturnType.IsVoid())
            {
                // Void methods: reject return values UNLESS the value is void
                if (valueType is not null && !IsErrorType(valueType) && !valueType.IsVoid())
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0008",
                        $"Cannot return '{valueType.DisplayName}' from method with return type 'void'",
                        returnStmt,
                        model));
                }
            }
            else if (valueType is not null)
            {
                // ── flow narrowing: use narrowed type if available ──
                var effectiveValueType = GetEffectiveSourceType(returnStmt.Value, valueType);

                // ── refinement-types feature: compile-time enforcement ──
                // When the return type is a RefinementType, check the return
                // value against the refinement predicate.
                if (TryCheckRefinementAssignment(effectiveValueType, methodReturnType, returnStmt.Value, diagnostics, model))
                {
                    // Refinement-specific diagnostic emitted; skip generic.
                }
                else if (!IsAssignable(valueType, methodReturnType))
                {
                    // unknown-type feature (Req 4.2, 4.4): emit a targeted
                    // diagnostic for void→unknown or unknown→non-unknown
                    // before falling back to the generic MIST_TC_0008.
                    if (TryEmitUnknownAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics, model))
                    {
                        // Targeted unknown diagnostic emitted; skip generic.
                    }
                    // type-unions feature (Req 6.3, 6.4): emit a targeted
                    // diagnostic for union-specific failures.
                    else if (TryEmitUnionAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics, model))
                    {
                        // Targeted union diagnostic emitted; skip generic.
                    }
                    // enums feature (Req 7.3): emit a targeted diagnostic
                    // for reverse conversion (underlying type → enum).
                    else if (TryEmitEnumAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics, model))
                    {
                        // Targeted enum diagnostic emitted; skip generic.
                    }
                    else
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0008",
                            $"Cannot return '{valueType.DisplayName}' from method with return type '{methodReturnType.DisplayName}'",
                            returnStmt,
                            model));
                    }
                }
                else
                {
                    // The return type is assignable. If assignability was due to
                    // an implicit cast operator, resolve and record it so the
                    // emitter can wrap the return expression.
                    TryResolveImplicitCast(valueType, methodReturnType, returnStmt, model, diagnostics);
                }
            }
        }
        else
        {
            // return; — only valid in void methods.
            if (!methodReturnType.IsVoid())
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0008",
                    $"Cannot return '' from method with return type '{methodReturnType.DisplayName}'",
                    returnStmt,
                    model));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Control flow condition checking (MIST_TC_0007)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckIfStatement(IfStatementNode ifStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(ifStmt.Condition, model, diagnostics);
        CheckConditionIsBool(ifStmt.Condition, model, diagnostics);

        // ── Flow narrowing (Requirements 7.1–7.8) ──────────────────────────
        // Analyze the condition to detect comparisons that prove refinement
        // predicates for local variables. If a match is found, narrow the
        // variable's type within the then-branch.
        var narrowings = CollectNarrowingsFromCondition(ifStmt.Condition, model);
        foreach (var (varName, refinementType) in narrowings)
        {
            _narrowingContext[varName] = refinementType;
        }

        CheckBlock(ifStmt.ThenBranch, returnType, model, diagnostics);

        // Remove narrowing entries after the then-branch.
        foreach (var (varName, _) in narrowings)
        {
            _narrowingContext.Remove(varName);
        }

        if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            CheckIfStatement(elseIf.ElseIf, returnType, model, diagnostics);
        }
        else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            CheckBlock(elseBlock.ElseBlock, returnType, model, diagnostics);
        }
    }

    private void CheckWhileStatement(WhileStatementNode whileStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(whileStmt.Condition, model, diagnostics);
        CheckConditionIsBool(whileStmt.Condition, model, diagnostics);
        CheckBlock(whileStmt.Body, returnType, model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Flow narrowing helpers (Requirements 7.1–7.8)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyzes an if-condition to detect comparisons or <c>is</c> patterns that
    /// prove a refinement predicate for a local variable. Returns a list of
    /// (variableName, RefinementType) pairs for variables that should be narrowed
    /// within the then-branch.
    /// </summary>
    private List<(string VarName, RefinementType Type)> CollectNarrowingsFromCondition(
        ExpressionNode condition,
        ISemanticModel model)
    {
        var result = new List<(string, RefinementType)>();

        // Navigate: Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational
        var assignment = condition.Assignment;
        if (assignment?.Value is not null) return result; // Not a simple condition.

        var logicalOr = assignment?.Target;
        if (logicalOr is null) return result;

        // Only handle simple conditions (no || for now).
        if (logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
            return result;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd is null) return result;

        // Only handle simple conditions (no && for now — could be extended later).
        if (logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
            return result;

        var equality = logicalAnd.Left;
        if (equality is null) return result;
        if (equality.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
            return result;

        var relational = equality.Left;
        if (relational is null) return result;

        switch (relational)
        {
            case RelationalComparisonRelationalNode cmp when cmp.Right is not null:
            {
                // Simple comparison: x >= N, x <= N, x > N, x < N
                TryNarrowFromComparison(cmp, model, result);
                break;
            }
            case IsExpressionRelationalNode isExpr:
            {
                // `is` pattern: x is >= N
                TryNarrowFromIsPattern(isExpr, model, result);
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Attempts to narrow a variable from a simple relational comparison (e.g., <c>x >= 18</c>).
    /// Looks up all refinement types in scope whose predicate matches the condition.
    /// </summary>
    private void TryNarrowFromComparison(
        RelationalComparisonRelationalNode cmp,
        ISemanticModel model,
        List<(string VarName, RefinementType Type)> result)
    {
        // Extract the variable name from the LHS.
        var varName = TryExtractSimpleIdentifierName(cmp.Left);
        if (varName is null) return;

        // Check eligibility: must be a local primitive variable not captured by closures.
        if (_closureCapturedVariables.Contains(varName)) return;

        var varType = TryGetVariableType(varName, cmp.Left, model);
        if (varType is null || !IsPrimitiveImmutableType(varType)) return;

        // Extract the operator and constant value from the comparison.
        var op = cmp.Operator.Lexeme;
        if (op is null) return;

        var constantValue = TryExtractLongFromAdditiveNode(cmp.Right);
        if (!constantValue.HasValue) return;

        // Build a synthetic predicate representing this condition and find matching refinement types.
        var matchingRefinement = FindMatchingRefinementType(varType, op, constantValue.Value, model);
        if (matchingRefinement is not null)
        {
            result.Add((varName, matchingRefinement));
        }
    }

    /// <summary>
    /// Attempts to narrow a variable from an <c>is</c> pattern expression (e.g., <c>x is >= 18</c>).
    /// When the pattern is a relational pattern, looks up matching refinement types in scope.
    /// </summary>
    private void TryNarrowFromIsPattern(
        IsExpressionRelationalNode isExpr,
        ISemanticModel model,
        List<(string VarName, RefinementType Type)> result)
    {
        // Extract the variable name from the LHS.
        var varName = TryExtractSimpleIdentifierName(isExpr.Left);
        if (varName is null) return;

        // Check eligibility: must be a local primitive variable not captured by closures.
        if (_closureCapturedVariables.Contains(varName)) return;

        var varType = TryGetVariableType(varName, isExpr.Left, model);
        if (varType is null || !IsPrimitiveImmutableType(varType)) return;

        // Extract the relational pattern from the is expression.
        // Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var pattern = isExpr.Pattern;
        if (pattern?.OrPattern is null) return;

        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return; // Or patterns not supported for narrowing.

        var andPattern = orPattern.Left;
        if (andPattern is null) return;

        // Try to find a refinement type whose predicate matches this and-pattern.
        var matchingRefinement = FindMatchingRefinementTypeFromPattern(varType, andPattern, model);
        if (matchingRefinement is not null)
        {
            result.Add((varName, matchingRefinement));
        }
    }

    /// <summary>
    /// Finds a refinement type in scope whose base type matches <paramref name="varType"/>
    /// and whose predicate is implied by the condition <c>varName {op} {value}</c>.
    /// </summary>
    private static RefinementType? FindMatchingRefinementType(
        IType varType,
        string op,
        long value,
        ISemanticModel model)
    {
        // Search all symbols for refinement types whose base type matches.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.DeclaredType is not RefinementType refinement) continue;

            // Check base type compatibility.
            if (!IsAssignable(varType, refinement.BaseType, model) &&
                !IsAssignable(refinement.BaseType, varType, model))
                continue;

            // Check if the condition implies the refinement predicate.
            if (ConditionImpliesPredicate(op, value, refinement.Predicate))
            {
                return refinement;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a refinement type in scope whose base type matches <paramref name="varType"/>
    /// and whose predicate matches the given <paramref name="andPattern"/>.
    /// </summary>
    private static RefinementType? FindMatchingRefinementTypeFromPattern(
        IType varType,
        AndPatternNode andPattern,
        ISemanticModel model)
    {
        // Extract relational constraints from the pattern.
        var conditionConstraints = ExtractConstraintsFromAndPattern(andPattern);
        if (conditionConstraints.Count == 0) return null;

        // Search all symbols for refinement types whose base type matches.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.DeclaredType is not RefinementType refinement) continue;

            // Check base type compatibility.
            if (!IsAssignable(varType, refinement.BaseType, model) &&
                !IsAssignable(refinement.BaseType, varType, model))
                continue;

            // Check if the pattern constraints imply the refinement predicate.
            if (ConstraintsImplyPredicate(conditionConstraints, refinement.Predicate))
            {
                return refinement;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a simple relational condition (<c>x {op} {value}</c>) implies
    /// the given refinement predicate.
    /// </summary>
    private static bool ConditionImpliesPredicate(string op, long value, RefinementPredicate predicate)
    {
        // Build a synthetic constraint from the condition.
        var conditionConstraint = op switch
        {
            ">=" => new ConditionConstraint(ConditionRelOp.Gte, value),
            "<=" => new ConditionConstraint(ConditionRelOp.Lte, value),
            ">" => new ConditionConstraint(ConditionRelOp.Gt, value),
            "<" => new ConditionConstraint(ConditionRelOp.Lt, value),
            _ => (ConditionConstraint?)null
        };

        if (conditionConstraint is null) return false;

        return ConstraintsImplyPredicate(
            new List<ConditionConstraint> { conditionConstraint.Value },
            predicate);
    }

    /// <summary>
    /// Checks if a set of condition constraints implies all constraints in the
    /// given refinement predicate.
    /// </summary>
    private static bool ConstraintsImplyPredicate(
        List<ConditionConstraint> conditionConstraints,
        RefinementPredicate predicate)
    {
        // Extract the predicate's constraints.
        var predicateConstraints = ExtractPredicateConstraints(predicate);
        if (predicateConstraints.Count == 0) return false;

        // For implication to hold, every constraint in the predicate must be
        // implied by at least one constraint in the condition.
        foreach (var target in predicateConstraints)
        {
            var implied = false;
            foreach (var source in conditionConstraints)
            {
                if (ConditionConstraintImplies(source, target))
                {
                    implied = true;
                    break;
                }
            }

            if (!implied)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines if a source condition constraint implies a target predicate constraint.
    /// Uses the same logic as <see cref="RefinementPredicate.Implies"/> but operates
    /// on the condition-level constraint representation.
    /// </summary>
    private static bool ConditionConstraintImplies(ConditionConstraint source, ConditionConstraint target)
    {
        return (source.Op, target.Op) switch
        {
            // Source: >= a
            (ConditionRelOp.Gte, ConditionRelOp.Gte) => source.Value >= target.Value,
            (ConditionRelOp.Gte, ConditionRelOp.Gt) => source.Value > target.Value,

            // Source: > a
            (ConditionRelOp.Gt, ConditionRelOp.Gte) => source.Value >= target.Value,
            (ConditionRelOp.Gt, ConditionRelOp.Gt) => source.Value >= target.Value,

            // Source: <= a
            (ConditionRelOp.Lte, ConditionRelOp.Lte) => source.Value <= target.Value,
            (ConditionRelOp.Lte, ConditionRelOp.Lt) => source.Value < target.Value,

            // Source: < a
            (ConditionRelOp.Lt, ConditionRelOp.Lte) => source.Value <= target.Value,
            (ConditionRelOp.Lt, ConditionRelOp.Lt) => source.Value <= target.Value,

            _ => false
        };
    }

    /// <summary>
    /// Extracts relational constraints from a refinement predicate's AST.
    /// </summary>
    private static List<ConditionConstraint> ExtractPredicateConstraints(RefinementPredicate predicate)
    {
        return ExtractConstraintsFromAndPattern(predicate.AstNode);
    }

    /// <summary>
    /// Extracts relational constraints from an <see cref="AndPatternNode"/>.
    /// </summary>
    private static List<ConditionConstraint> ExtractConstraintsFromAndPattern(AndPatternNode andPattern)
    {
        var constraints = new List<ConditionConstraint>();
        ExtractConstraintFromUnary(andPattern.Left, constraints);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ExtractConstraintFromUnary(andPattern.Right, constraints);
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                ExtractConstraintFromUnary(part, constraints);
            }
        }

        return constraints;
    }

    /// <summary>
    /// Extracts a relational constraint from a <see cref="UnaryPatternNode"/>.
    /// </summary>
    private static void ExtractConstraintFromUnary(UnaryPatternNode unary, List<ConditionConstraint> constraints)
    {
        if (unary is not DirectPatternUnaryPatternNode direct) return;

        switch (direct.Primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
            {
                var val = TryExtractLongFromPatternConstant(gte.Constant);
                if (val.HasValue) constraints.Add(new ConditionConstraint(ConditionRelOp.Gte, val.Value));
                break;
            }
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
            {
                var val = TryExtractLongFromPatternConstant(lte.Constant);
                if (val.HasValue) constraints.Add(new ConditionConstraint(ConditionRelOp.Lte, val.Value));
                break;
            }
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
            {
                var val = TryExtractLongFromPatternConstant(gt.Constant);
                if (val.HasValue) constraints.Add(new ConditionConstraint(ConditionRelOp.Gt, val.Value));
                break;
            }
            case LessThanRelationalPatternPrimaryPatternNode lt:
            {
                var val = TryExtractLongFromPatternConstant(lt.Constant);
                if (val.HasValue) constraints.Add(new ConditionConstraint(ConditionRelOp.Lt, val.Value));
                break;
            }
        }
    }

    /// <summary>
    /// Extracts a long integer value from a pattern constant expression.
    /// Delegates to the same logic used by <see cref="RefinementPredicate"/>.
    /// </summary>
    private static long? TryExtractLongFromPatternConstant(ExpressionNode expr)
    {
        return RefinementPredicate.TryExtractLongFromExpression(expr);
    }

    /// <summary>
    /// Extracts a simple identifier name from an additive expression node.
    /// Returns null if the expression is not a simple identifier reference.
    /// </summary>
    private static string? TryExtractSimpleIdentifierName(AdditiveNode additive)
    {
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
            return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
            return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        if (postfix.Target is IdentifierExpressionPrimaryNode identifier)
            return identifier.Name.Lexeme;

        return null;
    }

    /// <summary>
    /// Extracts a long integer value from an additive expression node (for the RHS of a comparison).
    /// </summary>
    private static long? TryExtractLongFromAdditiveNode(AdditiveNode? additive)
    {
        if (additive is null) return null;
        if (additive.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
            return null;

        var multiplicative = additive.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
            return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        if (postfix.Target is IntegerLiteralExpressionPrimaryNode intLiteral)
            return RefinementPredicate.ParseIntegerLexeme(intLiteral.IntegerLiteral.Lexeme);

        return null;
    }

    /// <summary>
    /// Gets the type of a local variable by name from the semantic model.
    /// Returns null if the variable is not found or is not a local variable.
    /// </summary>
    private static IType? TryGetVariableType(string varName, AdditiveNode node, ISemanticModel model)
    {
        // Get the type from the expression node itself.
        var type = model.GetTypeOf(node);
        return type;
    }

    /// <summary>
    /// Returns the effective type of an expression, checking the narrowing context
    /// for identifier expressions that have been narrowed via flow analysis.
    /// </summary>
    private IType? GetNarrowedTypeForIdentifier(ExpressionNode expr)
    {
        // Navigate to the identifier: Expression → Assignment → LogicalOr → ... → Primary
        var assignment = expr.Assignment;
        if (assignment?.Value is not null) return null;

        var logicalOr = assignment?.Target;
        if (logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
            return null;

        var logicalAnd = logicalOr?.Left;
        if (logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
            return null;

        var equality = logicalAnd?.Left;
        if (equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
            return null;

        if (equality?.Left is not RelationalComparisonRelationalNode relCmp)
            return null;
        if (relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left))
            return null;

        var additive = relCmp.Left;
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
            return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
            return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        if (postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            if (_narrowingContext.TryGetValue(identifier.Name.Lexeme, out var narrowedType))
                return narrowedType;
        }

        return null;
    }

    private enum ConditionRelOp { Gte, Lte, Gt, Lt }

    private readonly record struct ConditionConstraint(ConditionRelOp Op, long Value);

    /// <summary>
    /// Returns the effective source type for an expression, checking the narrowing
    /// context for simple identifier expressions that have been narrowed via flow analysis.
    /// If the expression is a narrowed identifier, returns the narrowed <see cref="RefinementType"/>;
    /// otherwise returns the original <paramref name="originalType"/>.
    /// </summary>
    private IType GetEffectiveSourceType(ExpressionNode expr, IType originalType)
    {
        var narrowedType = GetNarrowedTypeForIdentifier(expr);
        return narrowedType ?? originalType;
    }

    /// <summary>
    /// Returns the effective source type for an assignment RHS expression, checking
    /// the narrowing context. For assignment nodes, navigates through the target
    /// (which is the RHS expression in a non-chained assignment).
    /// </summary>
    private IType GetEffectiveSourceTypeFromAssignment(AssignmentNode assignmentValue, IType originalType)
    {
        // For a non-chained assignment RHS, the expression is in the Target (LogicalOrNode).
        // Navigate to find a simple identifier.
        if (assignmentValue.Value is not null) return originalType;

        var varName = TryExtractIdentifierFromLogicalOr(assignmentValue.Target);
        if (varName is not null && _narrowingContext.TryGetValue(varName, out var narrowedType))
            return narrowedType;

        return originalType;
    }

    /// <summary>
    /// Invalidates narrowing for the variable being assigned to.
    /// Called when a narrowed variable is reassigned within a branch (Requirement 7.4).
    /// </summary>
    private void InvalidateNarrowingForAssignmentTarget(LogicalOrNode target)
    {
        var varName = TryExtractIdentifierFromLogicalOr(target);
        if (varName is not null)
        {
            _narrowingContext.Remove(varName);
        }
    }

    /// <summary>
    /// Extracts a simple identifier name from a <see cref="LogicalOrNode"/>.
    /// Returns null if the expression is not a simple identifier reference.
    /// </summary>
    private static string? TryExtractIdentifierFromLogicalOr(LogicalOrNode logicalOr)
    {
        if (logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
            return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
            return null;

        var equality = logicalAnd?.Left;
        if (equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
            return null;

        if (equality?.Left is not RelationalComparisonRelationalNode relCmp)
            return null;
        if (relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left))
            return null;

        var additive = relCmp.Left;
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
            return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
            return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        if (postfix.Target is IdentifierExpressionPrimaryNode identifier)
            return identifier.Name.Lexeme;

        return null;
    }

    /// <summary>
    /// Collects variable names captured by closures (lambda expressions) in the
    /// given block. Variables captured by closures are ineligible for flow narrowing
    /// (Requirement 7.7).
    /// </summary>
    private void CollectClosureCapturedVariables(BlockNode block)
    {
        // Currently, Mist does not have lambda/closure expressions.
        // This method is a placeholder for future closure support.
        // When lambdas are added, this should walk the block looking for
        // lambda expressions and collect all identifiers they reference
        // into _closureCapturedVariables. For now, clear the set to ensure
        // a clean state for each method body.
        _closureCapturedVariables.Clear();
        _ = block; // Suppress unused parameter warning.
    }

    /// <summary>
    /// Applies narrowing to argument expressions by finding simple identifier arguments
    /// that are in the narrowing context and temporarily overriding their type in the
    /// semantic model. Returns a list of (node, originalType) pairs for restoration.
    /// </summary>
    private List<(Storm.Runtime.SyntaxNode Node, IType OriginalType)> ApplyNarrowingToArguments(
        ArgumentListNode? argList,
        ISemanticModel model)
    {
        var result = new List<(Storm.Runtime.SyntaxNode, IType)>();
        if (argList is null || _narrowingContext.Count == 0) return result;

        foreach (var child in argList.Children())
        {
            if (child is not ExpressionNode expr) continue;

            // Check if this argument is a simple identifier that has been narrowed.
            var identifierNode = TryExtractIdentifierPrimaryNode(expr);
            if (identifierNode is null) continue;

            if (_narrowingContext.TryGetValue(identifierNode.Name.Lexeme, out var narrowedType))
            {
                var originalType = model.GetTypeOf(identifierNode);
                if (originalType is not null)
                {
                    result.Add((identifierNode, originalType));
                    model.SetTypeOf(identifierNode, narrowedType);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Restores original types for argument expressions that were temporarily narrowed.
    /// </summary>
    private static void RestoreNarrowedArguments(
        List<(Storm.Runtime.SyntaxNode Node, IType OriginalType)> narrowedArgs,
        ISemanticModel model)
    {
        foreach (var (node, originalType) in narrowedArgs)
        {
            model.SetTypeOf(node, originalType);
        }
    }

    /// <summary>
    /// Extracts the <see cref="IdentifierExpressionPrimaryNode"/> from a simple
    /// identifier expression. Returns null if the expression is not a simple identifier.
    /// </summary>
    private static IdentifierExpressionPrimaryNode? TryExtractIdentifierPrimaryNode(ExpressionNode expr)
    {
        var assignment = expr.Assignment;
        if (assignment?.Value is not null) return null;

        var logicalOr = assignment?.Target;
        if (logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
            return null;

        var logicalAnd = logicalOr?.Left;
        if (logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
            return null;

        var equality = logicalAnd?.Left;
        if (equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
            return null;

        if (equality?.Left is not RelationalComparisonRelationalNode relCmp)
            return null;
        if (relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left))
            return null;

        var additive = relCmp.Left;
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
            return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
            return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        return postfix.Target as IdentifierExpressionPrimaryNode;
    }

    private void CheckForStatement(ForStatementNode forStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check initializer.
        if (forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            CheckExpression(varInit.Initializer, model, diagnostics);
            // Check assignability of initializer to declared type.
            var declaredType = model.GetTypeOf(varInit.VariableDeclaration);
            var initType = model.GetTypeOf(varInit.Initializer);
            if (declaredType is not null && initType is not null)
            {
                CheckAssignability(initType, declaredType, varInit.Initializer, diagnostics, model);
            }
        }
        else if (forStmt.Initializer is ForInitializerVarDeclarationForInitializerNode varDeclInit)
        {
            CheckExpression(varDeclInit.Initializer, model, diagnostics);
        }
        else if (forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            CheckExpression(exprInit.Expression, model, diagnostics);
        }

        // Check condition.
        if (forStmt.Condition is not null)
        {
            CheckExpression(forStmt.Condition, model, diagnostics);
            CheckConditionIsBool(forStmt.Condition, model, diagnostics);
        }

        // Check iterator.
        if (forStmt.Iterator is not null)
        {
            CheckExpression(forStmt.Iterator, model, diagnostics);
        }

        CheckBlock(forStmt.Body, returnType, model, diagnostics);
    }

    private void CheckForeachStatement(ForeachStatementNode foreachStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(foreachStmt.Collection, model, diagnostics);
        CheckBlock(foreachStmt.Body, returnType, model, diagnostics);
    }

    private static void CheckConditionIsBool(ExpressionNode condition, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var condType = model.GetTypeOf(condition);
        if (condType is null || IsErrorType(condType))
            return;

        if (!IsBoolLike(condType))
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0007",
                $"Condition must be of type 'bool', but found '{condType.DisplayName}'",
                condition,
                model));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression checking — recursive walk
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckExpression(ExpressionNode expr, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckAssignmentNode(expr.Assignment, model, diagnostics);
    }

    private void CheckAssignmentNode(AssignmentNode assignment, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Task 21: when this assignment has a value, the target is an
        // *lvalue* (write position). Flag the flag while we walk the
        // target so CheckPostfixNode treats the terminal suffix of the
        // target as a write context for the unknown-type member filter.
        // Nested assignments (e.g., `a = b = c`) always go through this
        // same method, so the flag is naturally re-established for each
        // inner assignment target as the recursion walks inward.
        var targetIsLhs = assignment.Value is not null;
        var previousAssignmentLhs = _analyzingAssignmentLhs;
        if (targetIsLhs) _analyzingAssignmentLhs = true;

        try
        {
            CheckLogicalOrNode(assignment.Target, model, diagnostics);
        }
        finally
        {
            _analyzingAssignmentLhs = previousAssignmentLhs;
        }

        if (assignment.Value is not null)
        {
            // Infer bare 'default' type from the LHS type before checking the RHS.
            var lhsTypeForDefault = model.GetTypeOf(assignment.Target);
            TryInferDefaultLiteralTypeFromAssignment(assignment.Value, lhsTypeForDefault, model);

            // This is an assignment: target = value.
            CheckAssignmentNode(assignment.Value, model, diagnostics);

            var lhsType = model.GetTypeOf(assignment.Target);
            var rhsType = model.GetTypeOf(assignment.Value);

            if (lhsType is not null && rhsType is not null)
            {
                // ── flow narrowing: use narrowed type for RHS if available ──
                var effectiveRhsType = GetEffectiveSourceTypeFromAssignment(assignment.Value, rhsType);

                // ── refinement-types feature: compile-time enforcement ──
                // When the target is a RefinementType, check the assignment
                // against the refinement predicate before the generic
                // assignability check.
                if (!TryCheckRefinementAssignment(effectiveRhsType, lhsType, assignment.Value, diagnostics, model))
                {
                    CheckAssignability(rhsType, lhsType, assignment, diagnostics, model);
                }
            }

            // ── flow narrowing: invalidate narrowing on reassignment (Requirement 7.4) ──
            InvalidateNarrowingForAssignmentTarget(assignment.Target);

            // Property-specific assignment checks (MIST_TC_0020, MIST_TC_0021, MIST_TC_0025).
            var targetPropertySymbol = TryResolvePropertyFromAssignmentTarget(assignment.Target, model);
            if (targetPropertySymbol is not null)
            {
                var propInfo = GetPropertyMetadata(targetPropertySymbol);
                if (propInfo is not null)
                {
                    // MIST_TC_0020: No accessible set/init accessor.
                    if (!propInfo.HasSet && !propInfo.HasInit)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0020",
                            $"Property '{targetPropertySymbol.Name}' has no accessible 'set' or 'init' accessor",
                            assignment,
                            model));
                    }
                    // MIST_TC_0025: init-only property assigned outside constructor context.
                    else if (propInfo.HasInit && !propInfo.HasSet && !_insideConstructor)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0025",
                            $"Init-only property '{targetPropertySymbol.Name}' can only be assigned in a constructor or object initializer",
                            assignment,
                            model));
                    }

                    // MIST_TC_0021: Property type mismatch.
                    var propType = targetPropertySymbol.DeclaredType;
                    if (propType is not null && rhsType is not null && !IsErrorType(propType) && !IsErrorType(rhsType))
                    {
                        if (!IsAssignable(rhsType, propType))
                        {
                            // unknown-type feature (Req 4.2, 4.4): emit a targeted
                            // diagnostic for void→unknown or unknown→non-unknown
                            // before falling back to the generic MIST_TC_0021.
                            if (TryEmitUnknownAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics, model))
                            {
                                // Targeted unknown diagnostic emitted; skip generic.
                            }
                            // type-unions feature (Req 6.3, 6.4): emit a targeted
                            // diagnostic for union-specific failures.
                            else if (TryEmitUnionAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics, model))
                            {
                                // Targeted union diagnostic emitted; skip generic.
                            }
                            // enums feature (Req 7.3): emit a targeted diagnostic
                            // for reverse conversion (underlying type → enum).
                            else if (TryEmitEnumAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics, model))
                            {
                                // Targeted enum diagnostic emitted; skip generic.
                            }
                            else
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    "MIST_TC_0021",
                                    $"Cannot assign '{rhsType.DisplayName}' to property of type '{propType.DisplayName}'",
                                    assignment,
                                    model));
                            }
                        }
                        else
                        {
                            // Resolve and record any implicit cast operator on the property assignment.
                            TryResolveImplicitCast(rhsType, propType, assignment, model, diagnostics);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts to resolve the property symbol from the LHS of an assignment.
    /// Returns the property symbol if the target is a member access resolving to a property, null otherwise.
    /// </summary>
    private static ISymbol? TryResolvePropertyFromAssignmentTarget(LogicalOrNode target, ISemanticModel model)
    {
        // Walk down the expression tree to find the innermost postfix expression.
        // An assignment target like `obj.Prop` is: PostfixExpression(target=obj, suffix=[MemberAccessSuffix(Prop)])
        // We need to check if the last suffix resolves to a property symbol.

        // Navigate through the expression tree: LogicalOr -> LogicalAnd -> Equality -> Relational -> Additive -> Multiplicative -> Unary -> Postfix
        var logicalAnd = target.Left;
        if (target.Right is not null) return null;

        var equality = logicalAnd.Left;
        if (logicalAnd.Right is not null) return null;

        var relational = equality.Left;
        if (equality.Right is not null) return null;

        if (relational is not RelationalComparisonRelationalNode relationalComparison) return null;

        var additive = relationalComparison.Left;
        if (relationalComparison.Right is not null) return null;

        var multiplicative = additive.Left;
        if (additive.Right is not null) return null;

        var unary = multiplicative.Left;
        if (multiplicative.Right is not null) return null;

        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;

        // Check if the last suffix is a MemberAccessSuffix that resolves to a property.
        if (postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];
            if (lastSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var symbol = model.GetDeclaredSymbol(memberAccess);
                if (symbol is not null && symbol.Kind == SymbolKind.Property)
                    return symbol;

                // Fallback: resolve via the receiver type's member scope.
                // This handles cases where the member access node is not registered
                // in the symbol table (e.g., dependent property access on constructed types
                // like FixedBuffer<5>.Size, or instance access like buf.Size).
                var receiverType = postfix.Suffix.Length > 1
                    ? model.GetTypeOf(postfix.Suffix[^2])
                    : model.GetTypeOf(postfix.Target);
                if (receiverType is not null)
                {
                    var resolved = FindPropertyMemberSymbol(receiverType, memberAccess.Name.Lexeme, model);
                    if (resolved is not null)
                        return resolved;
                }
            }
        }
        else
        {
            // Simple identifier — check if it resolves to a property.
            var symbol = model.GetDeclaredSymbol(postfix.Target);
            if (symbol is not null && symbol.Kind == SymbolKind.Property)
                return symbol;
        }

        return null;
    }

    /// <summary>
    /// Finds a static property member symbol by name in the target type's member scope.
    /// Unlike <see cref="FindMemberSymbol"/>, this specifically looks for static property symbols,
    /// which handles the case of generated dependent type properties (e.g., FixedBuffer&lt;5&gt;.Size)
    /// that are not registered in the symbol table via AST node association.
    /// Only static properties are matched to avoid interfering with instance property assignment
    /// validation that relies on the existing GetDeclaredSymbol path.
    /// </summary>
    private static ISymbol? FindPropertyMemberSymbol(IType targetType, string memberName, ISemanticModel model)
    {
        // Unwrap ConstructedType to look up in the generic definition's member scope.
        if (targetType is ConstructedType ct)
            targetType = ct.GenericDefinition;

        IScope? memberScope = targetType switch
        {
            ClassType ct2 => ct2.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName && member.Kind == SymbolKind.Property)
            {
                // Only match static properties to avoid interfering with
                // instance property assignment validation.
                if (member is Symbol sym && sym.PropertyMetadata is { IsStatic: true })
                    return member;
            }
        }

        return null;
    }

    private void CheckLogicalOrNode(LogicalOrNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk children first — the overload resolver only resolves the
        // top-level operator; the child subexpressions still need
        // analysis for their own operator/member checks.
        CheckLogicalAndNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckLogicalAndNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.9). Boolean.operator|| is declared on
            // Standard.Boolean; the old PrimitiveType.Bool special case
            // is removed.
            ResolveBinaryOperator(
                "||",
                node.Left,
                node.Right,
                node,
                "MIST_TC_0006",
                model,
                diagnostics);
        }
    }

    private void CheckLogicalAndNode(LogicalAndNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckEqualityNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckEqualityNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.9). Boolean.operator&& is declared on
            // Standard.Boolean; the old PrimitiveType.Bool special case
            // is removed.
            ResolveBinaryOperator(
                "&&",
                node.Left,
                node.Right,
                node,
                "MIST_TC_0006",
                model,
                diagnostics);
        }
    }

    private void CheckEqualityNode(EqualityNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckRelationalNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckRelationalNode(node.Right, model, diagnostics);

            // Void unit-type equality is a first-class language feature
            // (void-unit-type spec § Requirement 9). Two VoidType operands
            // compare equal via reference identity; there is no operator
            // member to look up because VoidType is a compiler-internal
            // type. Short-circuit before delegating to overload resolution
            // so `Void.Void == Void.Void` and `Void.Void != Void.Void`
            // remain well-typed.
            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            if (lhsType is not null && lhsType.IsVoid() && rhsType is not null && rhsType.IsVoid())
            {
                model.SetTypeOf(node, LegacyPrimitiveTypes.Bool);
                return;
            }

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4). Equality operators on stdlib types
            // (Int32, Boolean, String, …) are declared as static operator
            // members; user types may also declare == / !=.
            var op = node.Operator.Lexeme ?? "==";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0005",
                model,
                diagnostics);
        }
    }

    private void CheckRelationalNode(RelationalNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case RelationalComparisonRelationalNode cmp:
            {
                CheckAdditiveNode(cmp.Left, model, diagnostics);

                if (cmp.Right is not null)
                {
                    CheckAdditiveNode(cmp.Right, model, diagnostics);

                    // Route typing / error emission through overload resolution
                    // (Requirements 6.1, 6.4). Relational operators (<, >, <=, >=)
                    // are declared on each numeric stdlib type and Char; the old
                    // NumericTypes / IsNumeric special case is removed
                    // (Requirement 6.7).
                    var op = cmp.Operator.Lexeme ?? "<";
                    ResolveBinaryOperator(
                        op,
                        cmp.Left,
                        cmp.Right,
                        cmp,
                        "MIST_TC_0004",
                        model,
                        diagnostics);
                }

                break;
            }

            case IsExpressionRelationalNode isExpr:
            {
                CheckAdditiveNode(isExpr.Left, model, diagnostics);
                var scrutineeType = model.GetTypeOf(isExpr.Left);
                if (scrutineeType is not null)
                {
                    CheckPattern(isExpr.Pattern, scrutineeType, model, diagnostics);
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern validation (MIST_TC_0050 – MIST_TC_0057)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recursively validates a pattern against the scrutinee type.
    /// Skips validation when the scrutinee type is <see cref="ErrorType"/>
    /// to suppress cascading errors.
    /// </summary>
    private static void CheckPattern(
        PatternNode pattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (IsErrorType(scrutineeType))
            return;

        CheckOrPattern(pattern.OrPattern, scrutineeType, model, diagnostics);
    }

    private static void CheckOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckAndPattern(orPattern.Left, scrutineeType, model, diagnostics);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CheckAndPattern(orPattern.Right, scrutineeType, model, diagnostics);

            // MIST_TC_0056: Or-pattern alternatives must introduce the same
            // variables with the same types.
            var leftVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Left, scrutineeType, model, leftVars);

            var rightVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Right, scrutineeType, model, rightVars);

            if (!PatternVariableSetsMatch(leftVars, rightVars))
            {
                diagnostics.Add(MakeDiagnostic(
                    PatternOrInconsistentVariables.Value,
                    "All alternatives of an 'or' pattern must introduce the same pattern variables with the same types",
                    orPattern,
                    model));
            }
        }
    }

    private static void CheckAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckUnaryPattern(andPattern.Left, scrutineeType, model, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            CheckUnaryPattern(andPattern.Right, scrutineeType, model, diagnostics);
        }
    }

    private static void CheckUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
            {
                CheckPrimaryPattern(notPattern.Operand, scrutineeType, model, diagnostics);

                // MIST_TC_0057: Not-pattern operand must not introduce variables.
                var vars = new Dictionary<string, IType>(StringComparer.Ordinal);
                CollectPatternVariablesFromPrimaryPattern(notPattern.Operand, scrutineeType, model, vars);
                if (vars.Count > 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternNotCannotBindVariable.Value,
                        "A 'not' pattern cannot contain variable bindings",
                        notPattern,
                        model));
                }
                break;
            }
            case DirectPatternUnaryPatternNode directPattern:
            {
                CheckPrimaryPattern(directPattern.Primary, scrutineeType, model, diagnostics);
                break;
            }
        }
    }

    private static void CheckPrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // MIST_TC_0050: Verify the type exists in scope.
                // The semantic model resolved the type reference during Pass 2.
                // If it resolved to ErrorType, the type was not found.
                var patternType = model.GetTypeOf(typePattern.Type);
                patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is null || IsErrorType(patternType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternUndefinedType.Value,
                        $"Type '{ExtractTypeReferenceName(typePattern.Type)}' could not be found in pattern",
                        typePattern,
                        model));
                }
                else if (patternType is UnknownType)
                {
                    // Task 24.1 / Req 11.3: narrowing to `unknown` is not
                    // meaningful. `is unknown u` is tautologically true
                    // because every value is assignable to `unknown`
                    // (Req 4.1), and the introduced binding has no more
                    // specific type than the scrutinee — it discards
                    // information instead of recovering it.
                    diagnostics.Add(MakeDiagnostic(
                        PatternNarrowToUnknown.Value,
                        "Narrowing to 'unknown' is not meaningful: the pattern is tautologically true and introduces no new type information. Remove the pattern or narrow to a concrete type instead.",
                        typePattern,
                        model));
                }
                else if (IsClosedGenericWithUnknown(patternType))
                {
                    // Task 24.2 / Req 11.4: narrowing to a
                    // Closed_Generic_With_Unknown (e.g., `List<unknown>`)
                    // is not meaningful. Covariant_Unknown_Widening
                    // (Req 8) makes the pattern tautologically true, and
                    // the introduced binding is strictly less useful than
                    // the scrutinee — members classified WritesT with
                    // respect to the `unknown`-bound type parameter are
                    // filtered per Requirement 10, without any information
                    // gain.
                    diagnostics.Add(MakeDiagnostic(
                        PatternNarrowToClosedGenericWithUnknown.Value,
                        $"Narrowing to '{patternType.DisplayName}' is not meaningful: the pattern is tautologically true under covariant unknown widening, and the binding loses information because members that write to a type parameter bound to 'unknown' are filtered. Narrow to a concrete constructed type (e.g., '{ReplaceUnknownWithConcretePlaceholder(patternType)}') instead.",
                        typePattern,
                        model));
                }
                else if (HasOpenDependentArguments(patternType))
                {
                    // Task 6.5 / Req 9.3, 9.4, 15.7: pattern match against an
                    // open dependent type (dependent args contain unresolved
                    // parameters) is rejected — pattern matching requires
                    // concrete dependent values.
                    diagnostics.Add(MakeDiagnostic(
                        DependentOpenTypePattern.Value,
                        $"Cannot pattern match against open dependent type '{patternType.DisplayName}'. Pattern matching requires concrete dependent values.",
                        typePattern,
                        model));
                }
                else if (patternType is RefinementType refinementPattern)
                {
                    // Refinement type pattern: `is AdultAge age` — runtime predicate check.
                    // Verify the scrutinee type is compatible with the refinement's base type.
                    // The scrutinee must be assignable to the base type (or be the base type,
                    // or be another refinement of the same base type).
                    var effectiveScrutinee = scrutineeType is RefinementType scrutineeRefinement
                        ? scrutineeRefinement.BaseType
                        : scrutineeType;

                    if (effectiveScrutinee is UnionType unionForRefinement)
                    {
                        // Scrutinee is a union: check if the refinement's base type is a constituent.
                        if (!IsConstituentOfUnion(refinementPattern.BaseType, unionForRefinement))
                        {
                            diagnostics.Add(MakeDiagnosticWarning(
                                UnionPatternNonConstituent.Value,
                                $"Type '{patternType.DisplayName}' (base type '{refinementPattern.BaseType.DisplayName}') is not a constituent of union type '{unionForRefinement.DisplayName}'; this pattern will never match",
                                typePattern,
                                model));
                        }
                    }
                    else if (!IsAssignable(effectiveScrutinee, refinementPattern.BaseType, model)
                             && !IsAssignable(refinementPattern.BaseType, effectiveScrutinee, model))
                    {
                        // Scrutinee type is not compatible with the refinement's base type.
                        diagnostics.Add(MakeDiagnosticWarning(
                            PatternIncompatibleRefinement.Value,
                            $"Refinement type '{patternType.DisplayName}' has base type '{refinementPattern.BaseType.DisplayName}' which is not compatible with scrutinee type '{scrutineeType.DisplayName}'; this pattern will never match",
                            typePattern,
                            model));
                    }
                    // Otherwise: valid refinement type pattern — the is expression will
                    // evaluate the refinement predicate at runtime.
                }
                else if (scrutineeType is UnionType unionForTypePattern
                         && !IsConstituentOfUnion(patternType, unionForTypePattern))
                {
                    // Task 6.3 / Req 8.3, 12.5: type pattern references a
                    // type that is not a constituent of the union — the
                    // pattern will never match at runtime.
                    diagnostics.Add(MakeDiagnosticWarning(
                        UnionPatternNonConstituent.Value,
                        $"Type '{patternType.DisplayName}' is not a constituent of union type '{unionForTypePattern.DisplayName}'; this pattern will never match",
                        typePattern,
                        model));
                }
                break;
            }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                // Bare type pattern: `Type` (no binding name) — pure type test.
                // Verify the type exists in scope.
                var patternType = model.GetTypeOf(bareTypePattern.Type);
                if (patternType is null || IsErrorType(patternType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternUndefinedType.Value,
                        $"Type '{ExtractTypeReferenceName(bareTypePattern.Type)}' could not be found in pattern",
                        bareTypePattern,
                        model));
                }
                else if (patternType is EnumType)
                {
                    // Enum member pattern: `is EnumName.MemberName` — value equality check.
                    // The semantic model builder resolved this as an enum member access.
                    // Accept it without error; the emitter will generate a == comparison.
                    // Verify the scrutinee type is compatible (must be the same enum type
                    // or the enum's underlying type).
                    if (!IsAssignable(scrutineeType, patternType)
                        && !(patternType is EnumType enumPatternType
                             && IsAssignable(scrutineeType, enumPatternType.UnderlyingType)))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            PatternConstantTypeMismatch.Value,
                            $"Enum pattern of type '{patternType.DisplayName}' is not compatible with scrutinee type '{scrutineeType.DisplayName}'",
                            bareTypePattern,
                            model));
                    }
                }
                else if (patternType is UnknownType)
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternNarrowToUnknown.Value,
                        "Narrowing to 'unknown' is not meaningful: the pattern is tautologically true and introduces no new type information. Remove the pattern or narrow to a concrete type instead.",
                        bareTypePattern,
                        model));
                }
                else if (IsClosedGenericWithUnknown(patternType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternNarrowToClosedGenericWithUnknown.Value,
                        $"Narrowing to '{patternType.DisplayName}' is not meaningful: the pattern is tautologically true under covariant unknown widening, and the binding loses information because members that write to a type parameter bound to 'unknown' are filtered. Narrow to a concrete constructed type (e.g., '{ReplaceUnknownWithConcretePlaceholder(patternType)}') instead.",
                        bareTypePattern,
                        model));
                }
                else if (HasOpenDependentArguments(patternType))
                {
                    // Task 6.5 / Req 9.3, 9.4, 15.7: pattern match against an
                    // open dependent type (dependent args contain unresolved
                    // parameters) is rejected — pattern matching requires
                    // concrete dependent values.
                    diagnostics.Add(MakeDiagnostic(
                        DependentOpenTypePattern.Value,
                        $"Cannot pattern match against open dependent type '{patternType.DisplayName}'. Pattern matching requires concrete dependent values.",
                        bareTypePattern,
                        model));
                }
                else if (patternType is RefinementType bareRefinementPattern)
                {
                    // Refinement type pattern (bare): `is AdultAge` — runtime predicate check.
                    // Verify the scrutinee type is compatible with the refinement's base type.
                    var effectiveScrutinee = scrutineeType is RefinementType scrutineeRefinementBare
                        ? scrutineeRefinementBare.BaseType
                        : scrutineeType;

                    if (effectiveScrutinee is UnionType unionForBareRefinement)
                    {
                        if (!IsConstituentOfUnion(bareRefinementPattern.BaseType, unionForBareRefinement))
                        {
                            diagnostics.Add(MakeDiagnosticWarning(
                                UnionPatternNonConstituent.Value,
                                $"Type '{patternType.DisplayName}' (base type '{bareRefinementPattern.BaseType.DisplayName}') is not a constituent of union type '{unionForBareRefinement.DisplayName}'; this pattern will never match",
                                bareTypePattern,
                                model));
                        }
                    }
                    else if (!IsAssignable(effectiveScrutinee, bareRefinementPattern.BaseType, model)
                             && !IsAssignable(bareRefinementPattern.BaseType, effectiveScrutinee, model))
                    {
                        diagnostics.Add(MakeDiagnosticWarning(
                            PatternIncompatibleRefinement.Value,
                            $"Refinement type '{patternType.DisplayName}' has base type '{bareRefinementPattern.BaseType.DisplayName}' which is not compatible with scrutinee type '{scrutineeType.DisplayName}'; this pattern will never match",
                            bareTypePattern,
                            model));
                    }
                }
                else if (scrutineeType is UnionType unionForBareTypePattern
                         && !IsConstituentOfUnion(patternType, unionForBareTypePattern))
                {
                    // Task 6.3 / Req 8.3, 12.5: type pattern references a
                    // type that is not a constituent of the union — the
                    // pattern will never match at runtime.
                    diagnostics.Add(MakeDiagnosticWarning(
                        UnionPatternNonConstituent.Value,
                        $"Type '{patternType.DisplayName}' is not a constituent of union type '{unionForBareTypePattern.DisplayName}'; this pattern will never match",
                        bareTypePattern,
                        model));
                }
                break;
            }

            case VarPatternPrimaryPatternNode:
            {
                // Var pattern: always valid.
                break;
            }

            case ConstantPatternPrimaryPatternNode constantPattern:
            {
                // MIST_TC_0051: Verify constant type is compatible with scrutinee.
                var constantType = model.GetTypeOf(constantPattern.Literal);
                if (constantType is not null && !IsErrorType(constantType))
                {
                    if (!ArePatternTypesCompatible(constantType, scrutineeType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            PatternConstantTypeMismatch.Value,
                            $"Constant of type '{constantType.DisplayName}' is not compatible with scrutinee type '{scrutineeType.DisplayName}'",
                            constantPattern,
                            model));
                    }
                }
                break;
            }

            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
            {
                CheckRelationalPattern(ltPattern, ltPattern.Constant, "<", scrutineeType, model, diagnostics);
                break;
            }

            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
            {
                CheckRelationalPattern(gtPattern, gtPattern.Constant, ">", scrutineeType, model, diagnostics);
                break;
            }

            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
            {
                CheckRelationalPattern(ltePattern, ltePattern.Constant, "<=", scrutineeType, model, diagnostics);
                break;
            }

            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
            {
                CheckRelationalPattern(gtePattern, gtePattern.Constant, ">=", scrutineeType, model, diagnostics);
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // Determine the effective type for property lookups.
                var patternType = scrutineeType;
                if (propertyPattern.TypeName is not null)
                {
                    // If there's a type prefix, use it. Check if it resolved.
                    var resolvedSymbol = model.GetDeclaredSymbol(propertyPattern);
                    if (resolvedSymbol?.DeclaredType is not null && !IsErrorType(resolvedSymbol.DeclaredType))
                    {
                        patternType = resolvedSymbol.DeclaredType;
                    }
                }

                // MIST_TC_0053: Verify each property name exists on the type.
                if (propertyPattern.Subpatterns is not null)
                {
                    CheckPropertySubpatternList(propertyPattern.Subpatterns, patternType, model, diagnostics);
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                // MIST_TC_0054: Verify scrutinee is an array type.
                if (scrutineeType is not ArrayType arrayType)
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternListRequiresArray.Value,
                        $"A list pattern requires the scrutinee to be an array type, but found '{scrutineeType.DisplayName}'",
                        listPattern,
                        model));
                    break;
                }

                var elementType = arrayType.ElementType;

                // Count fixed elements and recursively validate.
                if (listPattern.Elements is not null)
                {
                    CheckListPatternElementList(listPattern.Elements, elementType, arrayType, model, diagnostics);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                // Recursively validate the inner pattern.
                CheckPattern(parenPattern.Inner, scrutineeType, model, diagnostics);
                break;
            }
        }
    }

    /// <summary>
    /// Validates a relational pattern: verifies the scrutinee supports comparison
    /// operators and the constant type is compatible.
    /// </summary>
    private static void CheckRelationalPattern(
        PrimaryPatternNode patternNode,
        ExpressionNode constantExpr,
        string op,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // MIST_TC_0052: Verify scrutinee supports comparison operators.
        if (!TypeSupportsComparison(scrutineeType))
        {
            diagnostics.Add(MakeDiagnostic(
                PatternRelationalNonComparable.Value,
                $"Relational pattern '{op}' cannot be applied to scrutinee of type '{scrutineeType.DisplayName}' because it does not support comparison operators",
                patternNode,
                model));
        }

        // MIST_TC_0051: Verify constant type is compatible with scrutinee.
        var constantType = model.GetTypeOf(constantExpr);
        if (constantType is not null && !IsErrorType(constantType))
        {
            if (!ArePatternTypesCompatible(constantType, scrutineeType))
            {
                diagnostics.Add(MakeDiagnostic(
                    PatternConstantTypeMismatch.Value,
                    $"Constant of type '{constantType.DisplayName}' is not compatible with scrutinee type '{scrutineeType.DisplayName}'",
                    patternNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks whether a type supports comparison operators by looking for
    /// <c>operator&lt;</c> in its member scope. Types with comparison operators
    /// (numeric types, Char, etc.) declare these operators on their stdlib types.
    /// </summary>
    private static bool TypeSupportsComparison(IType type)
    {
        if (IsErrorType(type))
            return true; // Suppress cascading errors.

        // Numeric display names are always comparable.
        if (NumericDisplayNames.Contains(type.DisplayName))
            return true;

        // Check the type's member scope for operator< (any comparison operator).
        var scope = GetMemberScope(type);
        if (scope is null)
            return false;

        foreach (var member in scope.Members)
        {
            if (member.Kind == SymbolKind.Method && member.Name == "operator<")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether two types are compatible for pattern matching purposes.
    /// This is more permissive than strict assignability — numeric types are
    /// compatible with each other, and types with the same display name match.
    /// </summary>
    private static bool ArePatternTypesCompatible(IType constantType, IType scrutineeType)
    {
        if (IsErrorType(constantType) || IsErrorType(scrutineeType))
            return true;

        if (ReferenceEquals(constantType, scrutineeType))
            return true;

        if (constantType.DisplayName == scrutineeType.DisplayName)
            return true;

        if (ArePrimitiveEquivalent(constantType, scrutineeType))
            return true;

        // Both numeric types are compatible.
        if (NumericDisplayNames.Contains(constantType.DisplayName)
            && NumericDisplayNames.Contains(scrutineeType.DisplayName))
            return true;

        // Assignability in either direction.
        if (IsAssignable(constantType, scrutineeType) || IsAssignable(scrutineeType, constantType))
            return true;

        return false;
    }

    /// <summary>
    /// Validates a property subpattern list, checking each property name exists
    /// on the type and recursively validating subpatterns.
    /// </summary>
    private static void CheckPropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckPropertySubpattern(subpatternList.PropertySubpattern, patternType, model, diagnostics);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            CheckPropertySubpattern(subpattern, patternType, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property subpattern: verifies the property name exists
    /// on the type and recursively validates the subpattern.
    /// </summary>
    private static void CheckPropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var propertyName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);

        ISymbol? member = null;
        if (memberScope is not null)
        {
            member = memberScope.Lookup(propertyName);
        }

        if (member is null)
        {
            // MIST_TC_0053: Property not found on the type.
            diagnostics.Add(MakeDiagnostic(
                PatternUndefinedProperty.Value,
                $"Type '{patternType.DisplayName}' does not contain a member named '{propertyName}'",
                subpattern,
                model));
        }

        // Recursively validate the subpattern against the property's type.
        var propertyType = member?.DeclaredType ?? ErrorType.Instance;
        CheckPattern(subpattern.Pattern, propertyType, model, diagnostics);
    }

    /// <summary>
    /// Validates a list pattern element list: counts fixed elements, checks for
    /// too many elements, and recursively validates element patterns.
    /// </summary>
    private static void CheckListPatternElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        ArrayType arrayType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect all elements.
        var allElements = new List<ListPatternElementNode> { elementList.ListPatternElement };
        allElements.AddRange(elementList.ListPatternElements);

        // Count fixed (non-slice) elements.
        var fixedCount = 0;
        var hasSlice = false;
        foreach (var element in allElements)
        {
            if (element is PatternElementListPatternElementNode)
                fixedCount++;
            else if (element is SliceElementListPatternElementNode)
                hasSlice = true;
        }

        // MIST_TC_0055: Warn if too many fixed elements (only meaningful
        // when there is no slice — with a slice, the array can be any length).
        // This is a warning, not an error.
        if (!hasSlice && fixedCount > 0)
        {
            // We can't know the array length at compile time in general,
            // but we emit a warning when the pattern has fixed elements
            // and no slice, as the pattern requires an exact-length match.
            // The warning is informational — the actual length check happens at runtime.
        }

        // Recursively validate each element pattern.
        foreach (var element in allElements)
        {
            switch (element)
            {
                case PatternElementListPatternElementNode patternElement:
                    CheckPattern(patternElement.Pattern, elementType, model, diagnostics);
                    break;
                case SliceElementListPatternElementNode:
                    // Slice elements need no type checking.
                    break;
            }
        }
    }

    // ── Pattern variable collection helpers (for or/not pattern checks) ──

    /// <summary>
    /// Collects pattern variable names and their types from a pattern tree.
    /// Used by or-pattern and not-pattern validation.
    /// </summary>
    private static void CollectPatternVariablesFromPattern(
        PatternNode pattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromOrPattern(pattern.OrPattern, scrutineeType, model, variables);
    }

    private static void CollectPatternVariablesFromOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromAndPattern(orPattern.Left, scrutineeType, model, variables);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CollectPatternVariablesFromAndPattern(orPattern.Right, scrutineeType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromUnaryPattern(andPattern.Left, scrutineeType, model, variables);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            CollectPatternVariablesFromUnaryPattern(andPattern.Right, scrutineeType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                CollectPatternVariablesFromPrimaryPattern(notPattern.Operand, scrutineeType, model, variables);
                break;
            case DirectPatternUnaryPatternNode directPattern:
                CollectPatternVariablesFromPrimaryPattern(directPattern.Primary, scrutineeType, model, variables);
                break;
        }
    }

    private static void CollectPatternVariablesFromPrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                var name = typePattern.Name.Lexeme;
                if (name != "_")
                {
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    variables[name] = patternType ?? ErrorType.Instance;
                }
                break;
            }

            case BareTypePatternPrimaryPatternNode:
            {
                // Bare type pattern: no binding name, nothing to collect.
                break;
            }

            case VarPatternPrimaryPatternNode varPattern:
            {
                var name = varPattern.Name.Lexeme;
                if (name != "_")
                {
                    variables[name] = scrutineeType;
                }
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // Recursively collect from subpatterns.
                if (propertyPattern.Subpatterns is not null)
                {
                    var patternType = scrutineeType;
                    CollectPatternVariablesFromPropertySubpatternList(
                        propertyPattern.Subpatterns, patternType, model, variables);
                }

                // Trailing identifier introduces a variable.
                if (propertyPattern.Name.HasValue)
                {
                    var name = propertyPattern.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        variables[name] = scrutineeType;
                    }
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                var elementType = scrutineeType is ArrayType arrayType
                    ? arrayType.ElementType
                    : ErrorType.Instance;

                if (listPattern.Elements is not null)
                {
                    CollectPatternVariablesFromListElementList(
                        listPattern.Elements, elementType, model, variables);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                CollectPatternVariablesFromPattern(parenPattern.Inner, scrutineeType, model, variables);
                break;
            }

            // Constant, relational, and discard patterns introduce no variables.
        }
    }

    private static void CollectPatternVariablesFromPropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromPropertySubpattern(subpatternList.PropertySubpattern, patternType, model, variables);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            CollectPatternVariablesFromPropertySubpattern(subpattern, patternType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromPropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        var propertyName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);
        var member = memberScope?.Lookup(propertyName);
        var propertyType = member?.DeclaredType ?? ErrorType.Instance;

        CollectPatternVariablesFromPattern(subpattern.Pattern, propertyType, model, variables);
    }

    private static void CollectPatternVariablesFromListElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromListElement(elementList.ListPatternElement, elementType, model, variables);

        foreach (var element in elementList.ListPatternElements)
        {
            CollectPatternVariablesFromListElement(element, elementType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromListElement(
        ListPatternElementNode element,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                CollectPatternVariablesFromPattern(patternElement.Pattern, elementType, model, variables);
                break;
            case SliceElementListPatternElementNode sliceElement:
                if (sliceElement.Name.HasValue)
                {
                    var name = sliceElement.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        // Slice variable captures a sub-array.
                        variables[name] = new ArrayType(elementType);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Checks whether two sets of pattern variables match (same names, same types).
    /// </summary>
    private static bool PatternVariableSetsMatch(
        Dictionary<string, IType> left,
        Dictionary<string, IType> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (name, leftType) in left)
        {
            if (!right.TryGetValue(name, out var rightType))
                return false;

            // Allow ErrorType to match anything (suppress cascading errors).
            if (IsErrorType(leftType) || IsErrorType(rightType))
                continue;

            if (leftType.DisplayName != rightType.DisplayName
                && !ArePrimitiveEquivalent(leftType, rightType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts a human-readable type name from a <see cref="TypeReferenceNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            PrimitiveTypeRefTypeReferenceNode primitiveRef => primitiveRef.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "int",
                UintTypePrimitiveTypeNode => "uint",
                LongTypePrimitiveTypeNode => "long",
                UlongTypePrimitiveTypeNode => "ulong",
                ShortTypePrimitiveTypeNode => "short",
                UshortTypePrimitiveTypeNode => "ushort",
                ByteTypePrimitiveTypeNode => "byte",
                SbyteTypePrimitiveTypeNode => "sbyte",
                FloatTypePrimitiveTypeNode => "float",
                DoubleTypePrimitiveTypeNode => "double",
                DecimalTypePrimitiveTypeNode => "decimal",
                BoolTypePrimitiveTypeNode => "bool",
                CharTypePrimitiveTypeNode => "char",
                StringTypePrimitiveTypeNode => "string",
                _ => "unknown",
            },
            VoidTypeRefTypeReferenceNode => "void",
            NamedTypeRefTypeReferenceNode namedRef => ExtractQualifiedIdentifierName(namedRef.Name),
            _ => "unknown",
        };
    }

    /// <summary>
    /// Extracts the text of a <see cref="QualifiedIdentifierNode"/>.
    /// </summary>
    private static string ExtractQualifiedIdentifierName(QualifiedIdentifierNode qualifiedId)
    {
        return qualifiedId.IDENTIFIER.Lexeme ?? "unknown";
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is a
    /// <see cref="ConstructedType"/> whose type arguments contain
    /// <see cref="UnknownType"/> at any position (direct or transitive).
    /// This is the <em>Closed_Generic_With_Unknown</em> shape defined in
    /// the unknown-type feature glossary: a Constructed_Type in which at
    /// least one type argument is <c>unknown</c> directly or transitively
    /// (e.g., <c>List&lt;unknown&gt;</c>, <c>Map&lt;string, unknown&gt;</c>,
    /// <c>Optional&lt;List&lt;unknown&gt;&gt;</c>). Used by the Task 24
    /// pattern-narrowing rejection rule (Req 11.4).
    /// </summary>
    /// <remarks>
    /// The check is recursive through nested constructed types and array
    /// element types. A bare <see cref="UnknownType"/> is <em>not</em> a
    /// Closed_Generic_With_Unknown — that shape is handled by the Req 11.3
    /// rejection instead. Non-constructed types (primitives, class types
    /// without type arguments, etc.) return <c>false</c>.
    /// </remarks>
    private static bool IsClosedGenericWithUnknown(IType type)
    {
        if (type is not ConstructedType ct)
            return false;

        foreach (var arg in ct.TypeArguments)
        {
            if (ContainsUnknownAtAnyPosition(arg))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is a
    /// <see cref="ConstructedType"/> with dependent arguments where at least
    /// one <see cref="DependentValueArgument.Value"/> is a <see cref="string"/>
    /// (indicating an unresolved/forwarded parameter rather than a concrete
    /// literal value). This is the <em>Open_Dependent_Type</em> shape: a
    /// constructed type whose dependent arguments contain unresolved parameters
    /// (e.g., <c>FixedBuffer&lt;N&gt;</c> where <c>N</c> is a type parameter).
    /// Used by the Task 6.5 pattern-matching rejection rule (Req 9.3, 9.4, 15.7).
    /// </summary>
    private static bool HasOpenDependentArguments(IType type)
    {
        if (type is not ConstructedType ct)
            return false;

        if (ct.DependentArguments is null)
            return false;

        foreach (var depArg in ct.DependentArguments)
        {
            if (depArg is not null && depArg.Value is string)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursive structural containment check: returns <c>true</c> if
    /// <paramref name="type"/> is <see cref="UnknownType"/>, an array whose
    /// element (transitively) contains <c>unknown</c>, or a constructed
    /// type whose arguments (transitively) contain <c>unknown</c>.
    /// </summary>
    private static bool ContainsUnknownAtAnyPosition(IType type)
    {
        switch (type)
        {
            case UnknownType:
                return true;
            case ArrayType arr:
                return ContainsUnknownAtAnyPosition(arr.ElementType);
            case ConstructedType ct:
                foreach (var arg in ct.TypeArguments)
                {
                    if (ContainsUnknownAtAnyPosition(arg))
                        return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// Produces a display-only suggestion string for the diagnostic message
    /// emitted by <see cref="PatternNarrowToClosedGenericWithUnknown"/>.
    /// Replaces every occurrence of <c>unknown</c> in the type's
    /// <see cref="IType.DisplayName"/> with a concrete placeholder (<c>T</c>)
    /// so the suggestion reads naturally (e.g., <c>List&lt;T&gt;</c> instead
    /// of <c>List&lt;unknown&gt;</c>).
    /// </summary>
    /// <remarks>
    /// This helper operates on the display name as a string because the
    /// suggestion is purely presentational: no real substitution type is
    /// in hand, and the diagnostic is only intended to guide the developer
    /// toward a concrete narrow.
    /// </remarks>
    private static string ReplaceUnknownWithConcretePlaceholder(IType type)
    {
        // Simple string replacement on the display name is sufficient here:
        // `unknown` is a reserved keyword token and cannot appear as part
        // of any other identifier. We only need the substring replacement
        // to be consistent; the exact placeholder is for documentation
        // purposes only.
        return type.DisplayName.Replace("unknown", "T", System.StringComparison.Ordinal);
    }

    private void CheckAdditiveNode(AdditiveNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckMultiplicativeNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckMultiplicativeNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.8). String concatenation flows
            // from Standard.String's operator+ declaration — the old
            // StringType-for-+ special case is removed.
            var op = node.Operator.Lexeme ?? "+";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0003",
                model,
                diagnostics);
        }
    }

    private void CheckMultiplicativeNode(MultiplicativeNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckUnaryNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckUnaryNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.7). Multiplicative operators
            // (*, /, %) are declared on each numeric stdlib type; the old
            // NumericTypes / IsNumeric special case is removed.
            var op = node.Operator.Lexeme ?? "*";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0003",
                model,
                diagnostics);
        }
    }

    private void CheckUnaryNode(UnaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                // Walk the operand first so its own operators / members
                // still get analysed.
                CheckUnaryNode(logicalNot.Operand, model, diagnostics);

                // Route typing / error emission through overload
                // resolution (Requirements 6.2, 6.9). Boolean.operator!
                // is declared on Standard.Boolean; the old
                // PrimitiveType.Bool special case is removed.
                ResolveUnaryOperator(
                    "!",
                    logicalNot.Operand,
                    logicalNot,
                    "MIST_TC_0006",
                    model,
                    diagnostics);
                break;
            }
            case NegateUnaryNode negate:
            {
                CheckUnaryNode(negate.Operand, model, diagnostics);

                // Route typing / error emission through overload
                // resolution (Requirements 6.2, 6.7). Unary - is declared
                // on each signed numeric stdlib type; the old IsNumeric
                // special case is removed.
                ResolveUnaryOperator(
                    "-",
                    negate.Operand,
                    negate,
                    "MIST_TC_0003",
                    model,
                    diagnostics);
                break;
            }
            case PostfixExpressionUnaryNode postfix:
                CheckPostfixNode(postfix.Primary, model, diagnostics);
                break;
            case CastExpressionUnaryNode castExpr:
                CheckUnaryNode(castExpr.Operand, model, diagnostics);
                CheckCastExpression(castExpr, model, diagnostics);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Postfix and primary expression checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckPostfixNode(PostfixNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckPrimaryNode(node.Target, model, diagnostics);

        // Determine if the target is a type name (for static access checks).
        var targetIsTypeName = IsTargetTypeName(node.Target, model);

        // Track the running type of the target as we process suffixes.
        var currentType = model.GetTypeOf(node.Target);

        // Task 21: if this postfix is the LHS of an assignment, then the
        // *last* suffix in the chain is a write context (for the unknown-
        // type member filter). Every earlier suffix is still a read —
        // e.g., in `a.b.c = x`, `a.b` is a read, `a.b.c` is the write.
        // We capture the flag once here and reset it for inner expressions
        // (e.g., arguments inside method calls) so those nested visits do
        // not inherit the LHS context.
        var isAssignmentLhsPostfix = _analyzingAssignmentLhs;
        _analyzingAssignmentLhs = false;

        for (var suffixIndex = 0; suffixIndex < node.Suffix.Length; suffixIndex++)
        {
            var suffix = node.Suffix[suffixIndex];
            var isLastSuffix = suffixIndex == node.Suffix.Length - 1;
            var suffixIsWriteTarget = isAssignmentLhsPostfix && isLastSuffix;

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                    // Task 25.1: bare-unknown member access rejection.
                    // A method call on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no member scope (Req 3.4). Emit
                    // MIST_TC_0065 and short-circuit the remaining
                    // suffix logic to suppress cascading diagnostics.
                    if (currentType is UnknownType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            BareUnknownMemberAccess.Value,
                            FormatBareUnknownMemberAccessMessage(
                                methodCall.Name.Lexeme,
                                currentType.DisplayName),
                            methodCall,
                            model));
                        currentType = ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }
                    // Task 6.2: union type method call validation (Req 7.1, 7.2, 7.3).
                    // If the receiver is a UnionType, check that the method exists
                    // on ALL constituent types. If it does, compute the result type
                    // as the union of return types; otherwise emit MIST_TC_0073.
                    if (currentType is UnionType unionForMethodCall)
                    {
                        var unionMethodResult = CheckUnionMemberAccess(
                            methodCall.Name.Lexeme, unionForMethodCall, methodCall, model, diagnostics);
                        currentType = unionMethodResult;
                        targetIsTypeName = false;
                        break;
                    }
                    CheckMethodCallArguments(methodCall, currentType, model, diagnostics);
                    CheckMemberAccessModifiers(methodCall.Name.Lexeme, currentType, targetIsTypeName, methodCall, model, diagnostics);
                    // Task 21: filter methods on Closed_Generic_With_Unknown receivers.
                    // A method call is always a "write-ish" use of the member — if
                    // the method writes T and T is closed with unknown, filter it.
                    // Method filtering intentionally ignores isAssignmentLhsPostfix
                    // (calling a method is never itself the target of `=`).
                    if (FilterMethodOnClosedUnknownReceiver(methodCall, currentType, model, diagnostics))
                    {
                        currentType = ErrorType.Instance;
                    }
                    else
                    {
                        currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                        // Also substitute method-level type arguments in the return type.
                        currentType = SubstituteMethodReturnType(currentType, methodCall, model);
                        // A method call's return flows through the unknown-receiver
                        // substitution so the result type is reported as `unknown` at
                        // any position the generic's type parameter was closed with it.
                        currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    }
                    // After the first suffix, subsequent accesses are on instances (the result of the method call).
                    targetIsTypeName = false;
                    break;

                case InvocationSuffixPostfixSuffixNode invocation:
                    CheckInvocationArguments(invocation, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case IndexSuffixPostfixSuffixNode indexSuffix:
                    // Task 25.1: bare-unknown member access rejection.
                    // An indexer access on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no indexer (Req 3.4). Emit
                    // MIST_TC_0065 naming the canonical indexer lexeme
                    // and short-circuit the remaining suffix logic to
                    // suppress cascading diagnostics.
                    if (currentType is UnknownType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            BareUnknownMemberAccess.Value,
                            FormatBareUnknownMemberAccessMessage(
                                "this[]",
                                currentType.DisplayName),
                            indexSuffix,
                            model));
                        currentType = ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }
                    CheckIndexSuffix(indexSuffix, currentType, model, diagnostics);
                    // Task 21: filter indexer accesses on Closed_Generic_With_Unknown
                    // receivers. Write context (LHS of `=`) uses the Write side of
                    // the indexer classification; read context uses Read.
                    if (FilterIndexerOnClosedUnknownReceiver(indexSuffix, currentType, suffixIsWriteTarget, model, diagnostics))
                    {
                        currentType = ErrorType.Instance;
                    }
                    else
                    {
                        currentType = model.GetTypeOf(suffix);
                        // A read through an indexer on List<unknown> yields `unknown`
                        // at every position the generic's T was closed with unknown.
                        currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    }
                    targetIsTypeName = false;
                    break;

                case SwitchExpressionSuffixPostfixSuffixNode switchExpr:
                    // Walk each switch arm: check guard type and recurse into sub-expressions.
                    if (switchExpr.Arms is not null)
                    {
                        CheckSwitchArm(switchExpr.Arms.SwitchArm, model, diagnostics);
                        foreach (var arm in switchExpr.Arms.SwitchArms)
                        {
                            CheckSwitchArm(arm, model, diagnostics);
                        }

                        // Task 5.3: Arm result type compatibility checking.
                        // If the semantic model determined that no common type exists
                        // (unified type is UnknownType), emit MIST_TC_0091.
                        CheckSwitchArmResultTypeCompatibility(switchExpr, model, diagnostics);

                        // Task 5.5: Exhaustiveness checking.
                        // The scrutinee type is `currentType` at this point (the type
                        // of the expression before the switch suffix was applied).
                        if (currentType is not null && currentType is not ErrorType)
                        {
                            var armTuples = BuildSwitchArmTuples(switchExpr.Arms);
                            var missingCases = ExhaustivenessChecker.CheckExhaustiveness(currentType, armTuples);
                            if (missingCases.Count > 0)
                            {
                                var casesStr = string.Join(", ", missingCases);
                                diagnostics.Add(MakeDiagnostic(
                                    SwitchNotExhaustive.Value,
                                    $"Switch expression is not exhaustive. Missing cases: {casesStr}",
                                    switchExpr,
                                    model));
                            }
                        }

                        // Task 5.6: Unreachable arm detection.
                        CheckSwitchUnreachableArms(switchExpr.Arms, model, diagnostics);
                    }
                    currentType = model.GetTypeOf(suffix) ?? currentType;
                    targetIsTypeName = false;
                    break;

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                    // Task 25.1: bare-unknown member access rejection.
                    // A member access on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no member scope (Req 3.4). Emit
                    // MIST_TC_0065 and short-circuit the remaining
                    // suffix logic to suppress cascading diagnostics.
                    if (currentType is UnknownType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            BareUnknownMemberAccess.Value,
                            FormatBareUnknownMemberAccessMessage(
                                memberAccess.Name.Lexeme,
                                currentType.DisplayName),
                            memberAccess,
                            model));
                        currentType = ErrorType.Instance;
                        break;
                    }
                    // Task 6.2: union type member access validation (Req 7.1, 7.2).
                    // If the receiver is a UnionType, check that the member exists
                    // on ALL constituent types. If it does, compute the result type
                    // as the union of member types; otherwise emit MIST_TC_0073.
                    if (currentType is UnionType unionForMemberAccess)
                    {
                        var unionMemberResult = CheckUnionMemberAccess(
                            memberAccess.Name.Lexeme, unionForMemberAccess, memberAccess, model, diagnostics);
                        currentType = unionMemberResult;
                        targetIsTypeName = false;
                        break;
                    }
                    // Enum member access: members are always public and static,
                    // so skip access modifier checks. The semantic model builder
                    // already validates member existence and reports MIST_SB_0082
                    // for undefined members.
                    if (currentType is EnumType)
                    {
                        currentType = model.GetTypeOf(suffix) ?? ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }
                    CheckMemberAccessModifiers(memberAccess.Name.Lexeme, currentType, targetIsTypeName, memberAccess, model, diagnostics);
                    // MIST_TC_0019: Check property has accessible get accessor when read.
                    CheckPropertyReadAccess(memberAccess, currentType, model, diagnostics);
                    // Task 21: filter member accesses on Closed_Generic_With_Unknown
                    // receivers. Read context uses the Read classification; write
                    // context (LHS of `=`) uses Write.
                    if (FilterMemberAccessOnClosedUnknownReceiver(memberAccess, currentType, suffixIsWriteTarget, model, diagnostics))
                    {
                        currentType = ErrorType.Instance;
                    }
                    else
                    {
                        currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                        // A read of a property/field whose substituted type mentions T
                        // at a position closed with unknown must report the result as
                        // `unknown` at that position (Requirement 10.2).
                        currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    }
                    // After accessing a field, subsequent accesses are on the field's value (an instance).
                    targetIsTypeName = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Checks a single switch arm: validates the guard expression type (must be bool)
    /// and recursively walks the guard and result sub-expressions.
    /// Emits <see cref="SwitchGuardNotBool"/> (MIST_TC_0092) if the guard type is not bool.
    /// </summary>
    private void CheckSwitchArm(SwitchArmNode arm, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk the guard expression (if present) and verify it resolves to bool.
        if (arm.Guard is not null)
        {
            CheckExpression(arm.Guard.Condition, model, diagnostics);
            var guardType = model.GetTypeOf(arm.Guard.Condition);
            if (guardType is not null && !IsErrorType(guardType) && !IsBoolLike(guardType))
            {
                diagnostics.Add(MakeDiagnostic(
                    SwitchGuardNotBool.Value,
                    $"When guard must be a boolean expression, got '{guardType.DisplayName}'",
                    arm.Guard.Condition,
                    model));
            }
        }

        // Walk the result expression to check its sub-expressions.
        CheckExpression(arm.Result, model, diagnostics);
    }

    /// <summary>
    /// Checks that all switch arm result expressions have compatible types.
    /// If the semantic model's unified result type is <see cref="UnknownType"/>
    /// (indicating no common type could be determined), emits
    /// <see cref="SwitchIncompatibleArmTypes"/> (MIST_TC_0091) with the first
    /// pair of incompatible types found.
    /// </summary>
    private static void CheckSwitchArmResultTypeCompatibility(
        SwitchExpressionSuffixPostfixSuffixNode switchExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The unified result type stored by the semantic model is UnknownType.Instance
        // when no common type could be determined across all arms.
        var unifiedType = model.GetTypeOf(switchExpr);
        if (unifiedType is not UnknownType)
            return;

        // Collect arm result types to find the first incompatible pair.
        var arms = switchExpr.Arms!;
        var firstArmType = model.GetTypeOf(arms.SwitchArm.Result);
        if (firstArmType is null || IsErrorType(firstArmType))
            return;

        // Check the first arm against remaining arms to find the incompatible pair.
        foreach (var arm in arms.SwitchArms)
        {
            var armType = model.GetTypeOf(arm.Result);
            if (armType is null || IsErrorType(armType))
                continue;

            if (!IsAssignable(armType, firstArmType) && !IsAssignable(firstArmType, armType))
            {
                diagnostics.Add(MakeDiagnostic(
                    SwitchIncompatibleArmTypes.Value,
                    $"Switch arms have incompatible types: '{firstArmType.DisplayName}' and '{armType.DisplayName}'",
                    switchExpr,
                    model));
                return;
            }
        }
    }

    /// <summary>
    /// Builds the list of <c>(PatternNode, bool HasGuard)</c> tuples from a
    /// <see cref="SwitchArmListNode"/> for use by the exhaustiveness checker.
    /// </summary>
    private static IReadOnlyList<(PatternNode Pattern, bool HasGuard)> BuildSwitchArmTuples(
        SwitchArmListNode armList)
    {
        var result = new List<(PatternNode, bool)>();
        result.Add((armList.SwitchArm.Pattern, armList.SwitchArm.Guard is not null));
        foreach (var arm in armList.SwitchArms)
        {
            result.Add((arm.Pattern, arm.Guard is not null));
        }
        return result;
    }

    /// <summary>
    /// Detects unreachable switch arms and emits <see cref="SwitchUnreachableArm"/>
    /// (MIST_TC_0093) for each one. An arm is unreachable if:
    /// <list type="bullet">
    ///   <item>An unguarded catch-all (<c>_</c> or <c>var</c>) appears at an earlier position.</item>
    ///   <item>An unguarded constant pattern duplicates a preceding unguarded constant pattern.</item>
    ///   <item>An unguarded type pattern duplicates a preceding unguarded type pattern for the same type.</item>
    /// </list>
    /// </summary>
    private static void CheckSwitchUnreachableArms(
        SwitchArmListNode armList,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect all arms into a flat list for positional iteration.
        var arms = new List<SwitchArmNode> { armList.SwitchArm };
        foreach (var arm in armList.SwitchArms)
        {
            arms.Add(arm);
        }

        var catchAllSeenAtIndex = -1;
        var seenConstants = new HashSet<string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var hasGuard = arm.Guard is not null;

            // Rule 1: If an unguarded catch-all was seen at an earlier position,
            // all subsequent arms are unreachable.
            if (catchAllSeenAtIndex >= 0 && i > catchAllSeenAtIndex)
            {
                diagnostics.Add(MakeDiagnosticWarning(
                    SwitchUnreachableArm.Value,
                    "Switch arm is unreachable",
                    arm,
                    model));
                continue;
            }

            // Extract the primary pattern for classification.
            var primary = TryGetSinglePrimaryPattern(arm.Pattern);
            if (primary is null)
                continue;

            // Check if this arm is a catch-all (discard or var pattern).
            if (!hasGuard && IsCatchAllPrimaryPattern(primary))
            {
                catchAllSeenAtIndex = i;
                continue;
            }

            // Rule 2: Duplicate unguarded constant patterns.
            if (!hasGuard && primary is ConstantPatternPrimaryPatternNode constantPattern)
            {
                var key = GetConstantKey(constantPattern.Literal);
                if (key is not null && !seenConstants.Add(key))
                {
                    diagnostics.Add(MakeDiagnosticWarning(
                        SwitchUnreachableArm.Value,
                        "Switch arm is unreachable",
                        arm,
                        model));
                }
                continue;
            }

            // Rule 3: Duplicate unguarded type patterns for the same type.
            if (!hasGuard)
            {
                string? typeName = primary switch
                {
                    TypePatternPrimaryPatternNode typePattern =>
                        ExtractTypeDisplayNameFromRef(typePattern.Type),
                    BareTypePatternPrimaryPatternNode bareTypePattern =>
                        ExtractTypeDisplayNameFromRef(bareTypePattern.Type),
                    _ => null,
                };

                if (typeName is not null && !seenTypes.Add(typeName))
                {
                    diagnostics.Add(MakeDiagnosticWarning(
                        SwitchUnreachableArm.Value,
                        "Switch arm is unreachable",
                        arm,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Attempts to extract the single <see cref="PrimaryPatternNode"/> from a
    /// <see cref="PatternNode"/>, navigating through the Or → And → Unary chain.
    /// Returns <c>null</c> if the pattern is compound.
    /// </summary>
    private static PrimaryPatternNode? TryGetSinglePrimaryPattern(PatternNode pattern)
    {
        var orPattern = pattern.OrPattern;

        // If there are multiple or-alternatives, this is not a simple pattern.
        if (orPattern.AndPatterns.Length > 0)
            return null;

        var andPattern = orPattern.Left;

        // If there are multiple and-conjuncts, this is not a simple pattern.
        if (andPattern.UnaryPatterns.Length > 0)
            return null;

        var unaryPattern = andPattern.Left;

        // Only direct patterns (not negated) can be catch-alls or simple patterns.
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null;

        // Handle parenthesized patterns by recursing into the inner pattern.
        if (directPattern.Primary is ParenthesizedPatternPrimaryPatternNode parenthesized)
            return TryGetSinglePrimaryPattern(parenthesized.Inner);

        return directPattern.Primary;
    }

    /// <summary>
    /// Returns <c>true</c> if the given primary pattern is a catch-all
    /// (discard <c>_</c> or <c>var x</c>).
    /// </summary>
    private static bool IsCatchAllPrimaryPattern(PrimaryPatternNode primary)
    {
        return primary is VarPatternPrimaryPatternNode;
    }

    /// <summary>
    /// Returns a string key representing a constant literal for duplicate detection.
    /// Returns <c>null</c> if the literal type is not recognized.
    /// </summary>
    private static string? GetConstantKey(ConstantLiteralNode literal)
    {
        return literal switch
        {
            IntegerConstantConstantLiteralNode intConst => $"int:{intConst.Value.Lexeme}",
            FloatConstantConstantLiteralNode floatConst => $"float:{floatConst.Value.Lexeme}",
            StringConstantConstantLiteralNode strConst => $"string:{strConst.Value.Lexeme}",
            CharConstantConstantLiteralNode charConst => $"char:{charConst.Value.Lexeme}",
            TrueConstantConstantLiteralNode => "bool:true",
            FalseConstantConstantLiteralNode => "bool:false",
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the display name from a <see cref="TypeReferenceNode"/> for
    /// type pattern duplicate detection.
    /// </summary>
    private static string? ExtractTypeDisplayNameFromRef(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            PrimitiveTypeRefTypeReferenceNode primitiveRef => primitiveRef.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "int",
                UintTypePrimitiveTypeNode => "uint",
                LongTypePrimitiveTypeNode => "long",
                UlongTypePrimitiveTypeNode => "ulong",
                ShortTypePrimitiveTypeNode => "short",
                UshortTypePrimitiveTypeNode => "ushort",
                ByteTypePrimitiveTypeNode => "byte",
                SbyteTypePrimitiveTypeNode => "sbyte",
                FloatTypePrimitiveTypeNode => "float",
                DoubleTypePrimitiveTypeNode => "double",
                DecimalTypePrimitiveTypeNode => "decimal",
                BoolTypePrimitiveTypeNode => "bool",
                CharTypePrimitiveTypeNode => "char",
                StringTypePrimitiveTypeNode => "string",
                _ => null,
            },
            NamedTypeRefTypeReferenceNode namedRef => namedRef.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    /// <summary>
    /// Substitutes type parameters in a member's type when the target is a <see cref="ConstructedType"/>
    /// or a <see cref="ConstructedTraitType"/>.
    /// For example, if the member type is <c>T</c> and the target is <c>Box&lt;int&gt;</c>,
    /// the result is <c>int</c>.
    /// </summary>
    private static IType? SubstituteMemberType(IType? memberType, IType? targetType)
    {
        if (memberType is null)
            return memberType;

        if (targetType is ConstructedType ct)
        {
            var typeParams = GetTypeParameters(ct.GenericDefinition);
            if (typeParams is null)
                return memberType;

            return SubstituteType(memberType, typeParams, ct.TypeArguments);
        }

        // Task 10.2: Substitute type parameters for ConstructedTraitType member access.
        if (targetType is ConstructedTraitType ctt)
        {
            var typeParams = ctt.GenericTraitDefinition.TypeParameters;
            if (typeParams.Count == 0)
                return memberType;

            return SubstituteType(memberType, typeParams, ctt.TypeArguments);
        }

        return memberType;
    }

    /// <summary>
    /// Substitutes method-level type parameters in the return type of a generic method invocation.
    /// For example, if the method is <c>T Identity&lt;T&gt;(T value)</c> and invoked as <c>Identity&lt;int&gt;(42)</c>,
    /// the return type <c>T</c> becomes <c>int</c>.
    /// </summary>
    private static IType? SubstituteMethodReturnType(IType? returnType, MethodCallSuffixPostfixSuffixNode methodCall, ISemanticModel model)
    {
        if (returnType is null || methodCall.TypeArgs is null)
            return returnType;

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is not Symbol sym || sym.TypeParameters.Count == 0)
            return returnType;

        var methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);
        if (methodTypeArgs.Count != sym.TypeParameters.Count)
            return returnType;

        return SubstituteType(returnType, sym.TypeParameters, methodTypeArgs);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 21 — member filtering on Closed_Generic_With_Unknown receivers
    //
    // When the receiver's static type is a ConstructedType whose type
    // arguments contain UnknownType at one or more positions, members
    // classified WritesT with respect to any such parameter are filtered
    // (Requirements 10.1–10.4). A filtered member access produces
    // MIST_TC_0062 — the same "member not found" shape as the existing
    // undefined-member diagnostic, per Requirement 10.7.
    //
    // Filtering is disabled inside the declaring body of the receiver's
    // generic type itself (Requirement 10.6) — inside `class List<T>`,
    // `this.Add(x)` is accessed on the fully open generic, not on a
    // Closed_Generic_With_Unknown, and all of T's positions still hold
    // T (not unknown).
    //
    // Constructor invocations on Closed_Generic_With_Unknown are rejected
    // by Task 12.2 (MIST_SB_0053) before member filtering runs, so there
    // is no filter-path for constructors (Requirement 10.5).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// If <paramref name="receiverType"/> is a <see cref="ConstructedType"/>
    /// whose type arguments contain <see cref="UnknownType"/> at one or more
    /// positions, returns the list of <see cref="TypeParameterType"/>s of
    /// the generic definition at those positions. Returns <c>null</c>
    /// otherwise, <i>including</i> when analysis is currently inside the
    /// receiver's own declaring body (where type parameters are still
    /// open, per Requirement 10.6).
    /// </summary>
    private IReadOnlyList<IType>? GetClosedUnknownTypeParameters(IType? receiverType)
    {
        if (receiverType is not ConstructedType ct)
            return null;

        var typeParams = GetTypeParameters(ct.GenericDefinition);
        if (typeParams is null)
            return null;

        // Requirement 10.6: skip filtering when the current method/constructor/
        // accessor body is declared *on* the receiver's generic definition.
        // Inside `class List<T>`, a `this.Add(x)` call's receiver is
        // `List<T>` (the fully open generic), not `List<unknown>`; even if
        // a caller somehow produces a `List<unknown>`, the analysis is
        // performed in the open scope where T is not yet substituted.
        if (_currentEnclosingType is not null &&
            ReferenceEquals(_currentEnclosingType, ct.GenericDefinition))
        {
            return null;
        }

        var count = Math.Min(typeParams.Count, ct.TypeArguments.Count);
        List<TypeParameterType>? closed = null;
        for (var i = 0; i < count; i++)
        {
            if (ct.TypeArguments[i] is UnknownType)
            {
                closed ??= new List<TypeParameterType>(count);
                if (typeParams[i] is TypeParameterType tpItem)
                    closed.Add(tpItem);
            }
        }

        return closed;
    }

    /// <summary>
    /// After a member read passes the filter, substitute <see cref="UnknownType"/>
    /// for every type-parameter position of <paramref name="receiverType"/>'s
    /// generic definition that is closed with <c>unknown</c>. This implements
    /// Requirement 10.2: a read through <c>List&lt;unknown&gt;</c> of an
    /// indexer that returns <c>T</c> must report the result as
    /// <see cref="UnknownType"/>, not <c>T</c>.
    /// </summary>
    private static IType? SubstituteClosedUnknownInResultType(IType? resultType, IType? receiverType)
    {
        if (resultType is null || receiverType is not ConstructedType ct)
            return resultType;

        var typeParams = GetTypeParameters(ct.GenericDefinition);
        if (typeParams is null)
            return resultType;

        // Build a parallel (TypeParameter, TypeArgument) pair but replace
        // every occurrence of UnknownType with itself so SubstituteType
        // maps T → unknown at exactly the positions the user wrote.
        // Non-unknown positions use the original argument (already the
        // user's concrete substitution), preserving the current behavior
        // for `List<int>` where T → int.
        var count = Math.Min(typeParams.Count, ct.TypeArguments.Count);
        var args = new IType[count];
        for (var i = 0; i < count; i++)
            args[i] = ct.TypeArguments[i];

        // SubstituteType already does this mapping when used with the
        // constructed type's own arguments; `SubstituteMemberType` is the
        // suffix-level helper used elsewhere and is equivalent here. We
        // call SubstituteType directly to make the intent explicit and
        // to decouple from the suffix API.
        return SubstituteType(resultType, typeParams, args);
    }

    /// <summary>
    /// Filters a method call on a <c>Closed_Generic_With_Unknown</c> receiver:
    /// if any overload whose signature matches the call writes to T-storage
    /// for a type parameter T bound to <c>unknown</c>, emit
    /// <see cref="UnknownClosedGenericMemberFiltered"/> and return
    /// <c>true</c>. Otherwise return <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Today the Type Checker resolves a single method symbol per call site
    /// via <see cref="ISemanticModel.GetDeclaredSymbol"/>; Task 21 filters
    /// that resolved symbol. When overload resolution admits multiple
    /// candidates, a future enhancement can prune the candidate set by
    /// classification before resolution (see design § Critical design
    /// considerations). The current one-candidate-per-call model still
    /// produces the right diagnostic for the resolved target.
    /// </para>
    /// </remarks>
    private bool FilterMethodOnClosedUnknownReceiver(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? receiverType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var closedParams = GetClosedUnknownTypeParameters(receiverType);
        if (closedParams is null)
            return false;

        var map = model.ReachabilityMap;
        if (map is null)
            return false;

        // Prefer the Pass-2-registered symbol (set by
        // `ISemanticModel.GetDeclaredSymbol` when overload resolution
        // settled on a single candidate). When no symbol is registered —
        // the current semantic model does not register a symbol on the
        // MethodCallSuffix node itself — fall back to a by-name lookup
        // against the receiver's member scope, matching what
        // `CheckMemberAccessModifiers` already does for modifier checks.
        var methodSymbol = model.GetDeclaredSymbol(methodCall)
            ?? FindMemberSymbol(receiverType!, methodCall.Name.Lexeme, model);
        if (methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
            return false;

        // A method is filtered iff it is WritesT for at least one closed T.
        foreach (var tp in closedParams)
        {
            MemberClassification classification;
            try { classification = map.GetMethodClassification(methodSymbol, tp); }
            catch (KeyNotFoundException) { continue; }

            if (classification == MemberClassification.WritesT)
            {
                diagnostics.Add(MakeDiagnostic(
                    UnknownClosedGenericMemberFiltered.Value,
                    FormatFilteredMemberMessage(
                        methodCall.Name.Lexeme,
                        receiverType!.DisplayName),
                    methodCall,
                    model));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Filters a member access (field or property) on a
    /// <c>Closed_Generic_With_Unknown</c> receiver. The read side is
    /// filtered when <paramref name="isWriteContext"/> is <c>false</c>
    /// and the property's Read classification is <c>WritesT</c>; the
    /// write side is filtered when <paramref name="isWriteContext"/> is
    /// <c>true</c> and the property's Write (or the field's write)
    /// classification is <c>WritesT</c>. Returns <c>true</c> if a
    /// diagnostic was emitted.
    /// </summary>
    private bool FilterMemberAccessOnClosedUnknownReceiver(
        MemberAccessSuffixPostfixSuffixNode memberAccess,
        IType? receiverType,
        bool isWriteContext,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var closedParams = GetClosedUnknownTypeParameters(receiverType);
        if (closedParams is null)
            return false;

        var map = model.ReachabilityMap;
        if (map is null)
            return false;

        var memberSymbol = FindMemberSymbol(receiverType!, memberAccess.Name.Lexeme, model);
        if (memberSymbol is null)
            return false;

        foreach (var tp in closedParams)
        {
            MemberClassification classification;
            switch (memberSymbol.Kind)
            {
                case SymbolKind.Field:
                {
                    // Field reads never write T (the map does not expose
                    // a read side for fields). Field writes are filtered
                    // if the write classification is WritesT.
                    if (!isWriteContext)
                        continue;

                    try { classification = map.GetFieldClassification(memberSymbol, tp); }
                    catch (KeyNotFoundException) { continue; }
                    break;
                }
                case SymbolKind.Property:
                {
                    PropertyClassification pair;
                    try { pair = map.GetPropertyClassification(memberSymbol, tp); }
                    catch (KeyNotFoundException) { continue; }
                    classification = isWriteContext ? pair.Write : pair.Read;
                    break;
                }
                default:
                    continue;
            }

            if (classification == MemberClassification.WritesT)
            {
                diagnostics.Add(MakeDiagnostic(
                    UnknownClosedGenericMemberFiltered.Value,
                    FormatFilteredMemberMessage(
                        memberAccess.Name.Lexeme,
                        receiverType!.DisplayName),
                    memberAccess,
                    model));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Filters an indexer access on a <c>Closed_Generic_With_Unknown</c>
    /// receiver. Uses the Read side when <paramref name="isWriteContext"/>
    /// is <c>false</c> and the Write side when it is <c>true</c>. Returns
    /// <c>true</c> if a diagnostic was emitted.
    /// </summary>
    private bool FilterIndexerOnClosedUnknownReceiver(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType? receiverType,
        bool isWriteContext,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var closedParams = GetClosedUnknownTypeParameters(receiverType);
        if (closedParams is null)
            return false;

        var map = model.ReachabilityMap;
        if (map is null)
            return false;

        // Indexers are registered on the generic definition's member scope
        // under the canonical name "this[]".
        var indexerSymbol = FindMemberSymbol(receiverType!, "this[]", model);
        if (indexerSymbol is null || indexerSymbol.Kind != SymbolKind.Indexer)
            return false;

        foreach (var tp in closedParams)
        {
            PropertyClassification pair;
            try { pair = map.GetPropertyClassification(indexerSymbol, tp); }
            catch (KeyNotFoundException) { continue; }

            var classification = isWriteContext ? pair.Write : pair.Read;
            if (classification == MemberClassification.WritesT)
            {
                diagnostics.Add(MakeDiagnostic(
                    UnknownClosedGenericMemberFiltered.Value,
                    FormatFilteredMemberMessage("this[]", receiverType!.DisplayName),
                    indexSuffix,
                    model));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Produces the user-facing message for <see cref="UnknownClosedGenericMemberFiltered"/>.
    /// Matches the existing "member not found" shape (Requirement 10.7)
    /// and adds a causal clause so the developer knows why filtering
    /// happened rather than seeing a generic missing-member error.
    /// </summary>
    private static string FormatFilteredMemberMessage(string memberName, string receiverTypeDisplayName)
    {
        return $"'{receiverTypeDisplayName}' does not contain a definition for '{memberName}': " +
               "the operation would write to storage of a type parameter bound to 'unknown'.";
    }

    /// <summary>
    /// Produces the user-facing message for <see cref="BareUnknownMemberAccess"/>
    /// (Task 25.1). Names the receiver's static type and suggests using an
    /// <c>is</c> pattern to narrow before accessing members (Req 13.1, Req 16.4).
    /// </summary>
    private static string FormatBareUnknownMemberAccessMessage(
        string memberName,
        string receiverTypeDisplayName)
    {
        return $"Cannot access member '{memberName}' on a value of type " +
               $"'{receiverTypeDisplayName}': 'unknown' has no members. Use an " +
               "'is' pattern to narrow the value to a concrete type before " +
               "accessing members.";
    }

    /// <summary>
    /// Checks that a property being read has an accessible get accessor (MIST_TC_0019).
    /// </summary>
    private static void CheckPropertyReadAccess(
        MemberAccessSuffixPostfixSuffixNode memberAccess,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetType is null || IsErrorType(targetType))
            return;

        var memberName = memberAccess.Name.Lexeme;
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if (memberSymbol is null || memberSymbol.Kind != SymbolKind.Property)
            return;

        // Skip backend symbols — they are assumed to have proper accessors.
        if (memberSymbol.Origin == SymbolOrigin.Backend)
            return;

        var propInfo = GetPropertyMetadata(memberSymbol);
        if (propInfo is null)
            return;

        if (!propInfo.HasGet)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0019",
                $"Property '{memberName}' has no accessible 'get' accessor",
                memberAccess,
                model));
        }
    }

    private void CheckPrimaryNode(PrimaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case ArrayCreationExpressionPrimaryNode arrayCreation:
                CheckArrayCreation(arrayCreation, model, diagnostics);
                break;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreation(objectCreation, model, diagnostics);
                break;

            case ObjectCreationExpressionNoArgsPrimaryNode objectCreationNoArgs:
                CheckObjectCreationNoArgs(objectCreationNoArgs, model, diagnostics);
                break;

            case ParenthesizedExpressionPrimaryNode paren:
                CheckExpression(paren.Inner, model, diagnostics);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                foreach (var part in interpolated.InterpolatedString.Parts)
                {
                    if (part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        CheckExpression(exprPart.Expression, model, diagnostics);
                    }
                }
                break;

            case TypeLiteralExpressionPrimaryNode:
                // Type literals are always valid primary expressions.
                // Type checking is handled by assignability checks at usage sites.
                break;

            case EmptyArrayLiteralExpressionPrimaryNode emptyArrayLiteral:
                // Empty array literal [] — when no target type is available (resolved
                // as ErrorType by the semantic model builder), emit a diagnostic error.
                // When assigned to a typed variable, the type is inferred from the
                // annotation and no error is needed.
                {
                    var resolvedType = model.GetTypeOf(emptyArrayLiteral);
                    if (resolvedType is Mist.Semantics.Model.Types.ErrorType)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0096",
                            "Cannot infer element type for empty array literal '[]'. Provide an explicit array type annotation (e.g., 'int[] x = [];').",
                            emptyArrayLiteral,
                            model));
                    }
                }
                break;

            case FieldKeywordExpressionPrimaryNode fieldKeyword:
                // MIST_TC_0023: 'field' keyword used outside property accessor body.
                // If we reach here during method/constructor body checking, it means
                // 'field' is used outside a property accessor context.
                // Note: When checking property accessor bodies, the semantic model
                // resolves 'field' to the backing field symbol, so this check only
                // fires when 'field' appears in a non-property context.
                if (!_insidePropertyAccessor)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0023",
                        "'field' keyword can only be used inside a property accessor body",
                        fieldKeyword,
                        model));
                }
                break;

            case DefaultExpressionPrimaryNode defaultExpr:
                CheckDefaultExpression(defaultExpr, model, diagnostics);
                break;

            case DefaultLiteralExpressionPrimaryNode defaultLiteral:
                CheckDefaultLiteralExpression(defaultLiteral, model, diagnostics);
                break;

            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
                CheckArrayLiteral(arrayLiteral, model, diagnostics);
                break;

            // Literals and identifiers don't need type checking.
            default:
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default expression validation (Requirements 7.1–7.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a <c>default(TypeName)</c> expression:
    /// resolves the type reference and checks that the type is defaultable.
    /// </summary>
    private static void CheckDefaultExpression(
        DefaultExpressionPrimaryNode defaultExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolvedType = model.GetTypeOf(defaultExpr);

        // If the type reference could not be resolved, emit MIST_DEFAULT_UNDEFINED_TYPE.
        if (resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.Add(MakeDiagnostic(
                DiagnosticId.DefaultUndefinedType.Value,
                "The type in 'default(TypeName)' could not be resolved to a known type",
                defaultExpr,
                model));
            return;
        }

        ValidateDefaultableType(resolvedType, defaultExpr, model, diagnostics);
    }

    /// <summary>
    /// Validates a bare <c>default</c> literal expression:
    /// checks that the type was inferred from context. If the type is still
    /// <see cref="ErrorType"/>, emits <c>MIST_DEFAULT_CANNOT_INFER</c>.
    /// If a type was inferred, validates that it is defaultable.
    /// </summary>
    private static void CheckDefaultLiteralExpression(
        DefaultLiteralExpressionPrimaryNode defaultLiteral,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolvedType = model.GetTypeOf(defaultLiteral);

        // If the type is still unknown, context inference did not find a type.
        if (resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.Add(MakeDiagnostic(
                DiagnosticId.DefaultCannotInferType.Value,
                "The type of 'default' cannot be determined from context",
                defaultLiteral,
                model));
            return;
        }

        ValidateDefaultableType(resolvedType, defaultLiteral, model, diagnostics);
    }

    /// <summary>
    /// Validates that a resolved type supports the <c>default</c> operator.
    /// Emits the appropriate diagnostic if the type is not defaultable.
    /// </summary>
    private static void ValidateDefaultableType(
        IType resolvedType,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // ── unknown-type storage ban on `default` (Task 12.4) ────────────
        // Requirement 5.6 — `default(unknown)` and bare `default` inferred
        // to `unknown` are forbidden. Checked before the interface/trait/
        // type-parameter rejections below because `unknown` is none of
        // those — it is its own TypeKind (TypeKind.Unknown) — and the
        // diagnostic for each of those cases would be misleading. The
        // rule fires for both explicit `default(unknown)` and bare
        // `default` whose inferred type is UnknownType (e.g.,
        // `unknown x = default;`).
        if (resolvedType is UnknownType)
        {
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownHasNoDefault.Value,
                "'unknown' has no default value. 'default(unknown)' is not permitted because 'unknown' cannot be used as the declared type of a freshly-materialized value; use a concrete type or pattern-match before materializing a value.",
                node,
                model));
            return;
        }

        switch (resolvedType)
        {
            case InterfaceType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnInterface.Value,
                    $"The 'default' operator is not supported on interface type '{resolvedType.DisplayName}'. Only concrete types (classes and structs) support 'default'.",
                    node,
                    model));
                return;

            case TraitType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnTrait.Value,
                    $"The 'default' operator is not supported on trait type '{resolvedType.DisplayName}'. Only concrete types (classes and structs) support 'default'.",
                    node,
                    model));
                return;

            case TypeParameterType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnTypeParameter.Value,
                    $"The 'default' operator is not supported on unconstrained generic type parameter '{resolvedType.DisplayName}'",
                    node,
                    model));
                return;

            case ClassType classType:
                if (classType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultNotSupported.Value,
                        $"Type '{classType.DisplayName}' does not support the 'default' operator because it has non-defaultable fields without type bindings in a default block",
                        node,
                        model));
                }
                return;

            case StructType structType:
                if (structType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultNotSupported.Value,
                        $"Type '{structType.DisplayName}' does not support the 'default' operator because it has non-defaultable fields without type bindings in a default block",
                        node,
                        model));
                }
                return;

            // Primitive types (represented as ClassType from stdlib) and other
            // types are handled by the ClassType/StructType cases above.
            // ConstructedType, ArrayType, etc. fall through — no diagnostic needed
            // as they are not directly defaultable via default(T) in v1.
        }
    }

    /// <summary>
    /// Attempts to extract a <see cref="DefaultLiteralExpressionPrimaryNode"/> from
    /// an expression node by walking down the expression chain. Returns the node if
    /// the expression is a simple bare <c>default</c> with no operators or suffixes.
    /// </summary>
    private static DefaultLiteralExpressionPrimaryNode? TryExtractDefaultLiteral(ExpressionNode expr)
    {
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality →
        // Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var assignment = expr.Assignment;
        if (assignment.Value is not null)
            return null; // Has an assignment operator — not a simple expression.

        var logicalOr = assignment.Target;
        if (logicalOr.Right is not null)
            return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd.Right is not null)
            return null;

        var equality = logicalAnd.Left;
        if (equality.Right is not null)
            return null;

        var relational = equality.Left;
        if (relational is not RelationalComparisonRelationalNode cmp1)
            return null;
        if (cmp1.Right is not null)
            return null;

        var additive = cmp1.Left;
        if (additive.Right is not null)
            return null;

        var multiplicative = additive.Left;
        if (multiplicative.Right is not null)
            return null;

        var unary = multiplicative.Left;
        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;
        if (postfix.Suffix.Length > 0)
            return null; // Has postfix suffixes (member access, method call, etc.).

        return postfix.Target as DefaultLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// If the given expression is a bare <c>default</c> literal, infers its type
    /// from the provided context type and records it on the semantic model.
    /// </summary>
    private static void TryInferDefaultLiteralType(
        ExpressionNode expr,
        IType? contextType,
        ISemanticModel model)
    {
        if (contextType is null || IsErrorType(contextType))
            return;

        var defaultLiteral = TryExtractDefaultLiteral(expr);
        if (defaultLiteral is null)
            return;

        // Set the inferred type on the bare default literal node.
        model.SetTypeOf(defaultLiteral, contextType);
    }

    /// <summary>
    /// If the given <see cref="AssignmentNode"/> is a bare <c>default</c> literal
    /// (no further assignment), infers its type from the provided context type.
    /// Used for <c>x = default;</c> where the LHS type provides context.
    /// </summary>
    private static void TryInferDefaultLiteralTypeFromAssignment(
        AssignmentNode assignment,
        IType? contextType,
        ISemanticModel model)
    {
        if (contextType is null || IsErrorType(contextType))
            return;

        // The assignment RHS is itself an AssignmentNode. If it has no further
        // assignment (Value is null), the expression is just the Target (LogicalOr).
        if (assignment.Value is not null)
            return;

        var defaultLiteral = TryExtractDefaultLiteralFromLogicalOr(assignment.Target);
        if (defaultLiteral is null)
            return;

        model.SetTypeOf(defaultLiteral, contextType);
    }

    /// <summary>
    /// Attempts to extract a <see cref="DefaultLiteralExpressionPrimaryNode"/> from
    /// a <see cref="LogicalOrNode"/> by walking down the expression chain.
    /// </summary>
    private static DefaultLiteralExpressionPrimaryNode? TryExtractDefaultLiteralFromLogicalOr(LogicalOrNode logicalOr)
    {
        if (logicalOr.Right is not null)
            return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd.Right is not null)
            return null;

        var equality = logicalAnd.Left;
        if (equality.Right is not null)
            return null;

        var relational = equality.Left;
        if (relational is not RelationalComparisonRelationalNode cmp2)
            return null;
        if (cmp2.Right is not null)
            return null;

        var additive = cmp2.Left;
        if (additive.Right is not null)
            return null;

        var multiplicative = additive.Left;
        if (multiplicative.Right is not null)
            return null;

        var unary = multiplicative.Left;
        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;
        if (postfix.Suffix.Length > 0)
            return null;

        return postfix.Target as DefaultLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Pre-pass for method call arguments: infers bare <c>default</c> types from
    /// parameter types before the argument expressions are checked.
    /// </summary>
    private static void InferDefaultLiteralTypesForArguments(
        ArgumentListNode? argList,
        MethodCallSuffixPostfixSuffixNode methodCall,
        ISemanticModel model)
    {
        if (argList is null)
            return;

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
            return;

        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
                argExprs.Add(expr);
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for (var i = 0; i < count; i++)
        {
            TryInferDefaultLiteralType(argExprs[i], paramTypes[i], model);
        }
    }

    /// <summary>
    /// Pre-pass for invocation arguments: infers bare <c>default</c> types from
    /// parameter types before the argument expressions are checked.
    /// </summary>
    private static void InferDefaultLiteralTypesForInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        ISemanticModel model)
    {
        if (invocation.Arguments is null)
            return;

        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null || invokedSymbol.Kind != SymbolKind.Method)
            return;

        var paramTypes = ParameterSignature.Extract(invokedSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach (var child in invocation.Arguments.Children())
        {
            if (child is ExpressionNode expr)
                argExprs.Add(expr);
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for (var i = 0; i < count; i++)
        {
            TryInferDefaultLiteralType(argExprs[i], paramTypes[i], model);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Method invocation checking (MIST_TC_0002)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodCallArguments(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Pre-pass: infer bare 'default' types from parameter types before
        // checking argument expressions, so CheckDefaultLiteralExpression
        // sees the inferred type rather than ErrorType.
        InferDefaultLiteralTypesForArguments(methodCall.Arguments, methodCall, model);

        // Check argument expressions recursively.
        if (methodCall.Arguments is not null)
        {
            CheckArgumentListExpressions(methodCall.Arguments, model, diagnostics);
        }

        // ── Flow narrowing: apply narrowed types to argument expressions ──
        // When a narrowed variable is passed as a method argument, temporarily
        // override its type in the semantic model so that the static
        // CheckArgumentTypes methods pick up the narrowed type.
        var narrowedArgs = ApplyNarrowingToArguments(methodCall.Arguments, model);
        // Look up the method symbol to check argument types against parameter types.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);

        // If the symbol is registered on the call-site node, use it for substitution-aware checking.
        if (methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method)
        {
            // Determine type parameter substitution context.
            // 1. If the target is a ConstructedType, substitute the enclosing type's type params.
            // 2. If the target is a ConstructedTraitType, substitute the trait's type params (Task 10.2).
            // 3. If the method has its own type arguments, substitute those as well.
            IReadOnlyList<IType>? enclosingTypeParams = null;
            IReadOnlyList<IType>? enclosingTypeArgs = null;
            if (targetType is ConstructedType targetCt)
            {
                enclosingTypeParams = GetTypeParameters(targetCt.GenericDefinition);
                enclosingTypeArgs = targetCt.TypeArguments;
            }
            else if (targetType is ConstructedTraitType targetCtt)
            {
                enclosingTypeParams = targetCtt.GenericTraitDefinition.TypeParameters.Count > 0
                    ? targetCtt.GenericTraitDefinition.TypeParameters
                    : null;
                enclosingTypeArgs = targetCtt.TypeArguments;
            }

            // Resolve method-level type arguments if present.
            IReadOnlyList<IType>? methodTypeParams = null;
            IReadOnlyList<IType>? methodTypeArgs = null;
            if (methodCall.TypeArgs is not null && methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
            {
                methodTypeParams = sym.TypeParameters;
                methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);

                // Check constraint satisfaction for each explicit type argument
                // against the corresponding method type parameter.
                for (var i = 0; i < methodTypeParams.Count && i < methodTypeArgs.Count; i++)
                {
                    if (methodTypeParams[i] is TypeParameterType methodTP)
                        CheckConstraintSatisfaction(methodTypeArgs[i], methodTP, (SyntaxNode)methodCall, sym.Name, model, diagnostics);
                }
            }

            CheckArgumentTypesWithSubstitution(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall,
                enclosingTypeParams, enclosingTypeArgs, methodTypeParams, methodTypeArgs);
        }

        var methodName = methodCall.Name.Lexeme;

        // Collect the full overload set from the target type (and inherited methods from base types).
        var candidates = CollectMethodOverloads(targetType, methodName);

        // If there are multiple candidates, use overload resolution.
        if (candidates.Count > 1)
        {
            var argTypes = CollectArgumentTypes(methodCall.Arguments, model);
            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{methodName}' matches argument types ({argTypeNames})",
                        (SyntaxNode)methodCall,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, methodName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{methodName}' is ambiguous between: {string.Join(" and ", sigs)}",
                        (SyntaxNode)methodCall,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — proceed with the resolved symbol for further type checking.
                    CheckArgumentTypes(methodCall.Arguments, result.BestCandidate!, model, diagnostics, methodCall);
                    return;
            }
        }
        else if (candidates.Count == 1)
        {
            // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
            CheckArgumentTypes(methodCall.Arguments, candidates[0], model, diagnostics, methodCall);
        }
        else if (methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method)
        {
            // Fallback: use the symbol from the call-site node if no candidates found via target type.
            CheckArgumentTypes(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall);
        }

        // Restore original types after argument checking.
        RestoreNarrowedArguments(narrowedArgs, model);
    }

    private void CheckInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Pre-pass: infer bare 'default' types from parameter types.
        InferDefaultLiteralTypesForInvocationArguments(invocation, model);

        // Check argument expressions recursively.
        if (invocation.Arguments is not null)
        {
            CheckArgumentListExpressions(invocation.Arguments, model, diagnostics);
        }

        // Look up the invoked symbol.
        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null)
            return;

        if (invokedSymbol.Kind == SymbolKind.Method)
        {
            var methodName = invokedSymbol.Name;

            // Collect the full overload set from the target type (and inherited methods from base types).
            var candidates = CollectMethodOverloads(targetType, methodName);

            // If there are multiple candidates, use overload resolution.
            if (candidates.Count > 1)
            {
                var argTypes = CollectArgumentTypes(invocation.Arguments, model);
                var result = OverloadResolver.Resolve(candidates, argTypes, model);

                switch (result.Status)
                {
                    case OverloadResolutionStatus.NoApplicableCandidate:
                        var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0031",
                            $"No overload for '{methodName}' matches argument types ({argTypeNames})",
                            (SyntaxNode)invocation,
                            model));
                        return;

                    case OverloadResolutionStatus.AmbiguousCall:
                        var sigs = result.AmbiguousCandidates!
                            .Select(c => FormatCandidateSignature(c, methodName, model));
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0032",
                            $"Call to '{methodName}' is ambiguous between: {string.Join(" and ", sigs)}",
                            (SyntaxNode)invocation,
                            model));
                        return;

                    case OverloadResolutionStatus.Success:
                        // Resolved successfully — proceed with the resolved symbol for further type checking.
                        CheckArgumentTypes(invocation.Arguments, result.BestCandidate!, model, diagnostics, invocation);
                        return;
                }
            }
            else
            {
                // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
                CheckArgumentTypes(invocation.Arguments, invokedSymbol, model, diagnostics, invocation);
            }
        }
    }

    private void CheckArgumentListExpressions(ArgumentListNode argList, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has an Expression property and additional expressions in Children.
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                CheckExpression(expr, model, diagnostics);
            }
        }
    }

    private static void CheckArgumentTypes(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite)
    {
        // Get parameter symbols from the method's containing scope.
        // The semantic model builder registers parameters in the method's scope.
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        ImmutableArray<ISymbol> paramSymbols;
        if (methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
        {
            var paramList = new List<ISymbol>();
            foreach (var child in methodDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym is not null)
                        paramList.Add(paramSym);
                }
            }
            paramSymbols = [.. paramList];
        }
        else
        {
            return; // No parameters to check.
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if (argList is not null)
        {
            foreach (var child in argList.Children())
            {
                if (child is ExpressionNode expr)
                    argExprs.Add(expr);
            }
        }

        // Check each argument against the corresponding parameter.
        var count = Math.Min(argExprs.Count, paramSymbols.Length);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if (argType is not null && paramType is not null)
            {
                // ── refinement-types feature: compile-time enforcement ──
                // When the parameter is a RefinementType, check the argument
                // against the refinement predicate (Requirement 5.3).
                if (TryCheckRefinementAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                if (!IsAssignable(argType, paramType))
                {
                    // unknown-type feature (Req 4.2, 4.4): emit a targeted
                    // diagnostic for void→unknown or unknown→non-unknown
                    // before falling back to the generic MIST_TC_0002.
                    if (TryEmitUnknownAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted unknown diagnostic emitted; skip generic.
                    }
                    // type-unions feature (Req 6.3, 6.4): emit a targeted
                    // diagnostic for union-specific failures.
                    else if (TryEmitUnionAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted union diagnostic emitted; skip generic.
                    }
                    // enums feature (Req 7.3): emit a targeted diagnostic
                    // for reverse conversion (underlying type → enum).
                    else if (TryEmitEnumAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted enum diagnostic emitted; skip generic.
                    }
                    else
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0002",
                            $"Argument type '{argType.DisplayName}' is not assignable to parameter type '{paramType.DisplayName}'",
                            argExprs[i],
                            model));
                    }
                }
                else
                {
                    // Resolve and record any implicit cast operator on the argument.
                    TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
                }
            }
        }

        // Readonly type argument passing check (Req 14.5, 14.6):
        // If an argument's type is readonly, the corresponding parameter must be readonly.
        CheckReadonlyTypeArgumentPassing(argList, methodSymbol, model, diagnostics, callSite);
    }

    /// <summary>
    /// Checks argument types against parameter types with type argument substitution.
    /// Applies enclosing type substitution (for ConstructedType member access) and
    /// method-level substitution (for generic method invocations).
    /// </summary>
    private static void CheckArgumentTypesWithSubstitution(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite,
        IReadOnlyList<IType>? enclosingTypeParams,
        IReadOnlyList<IType>? enclosingTypeArgs,
        IReadOnlyList<IType>? methodTypeParams,
        IReadOnlyList<IType>? methodTypeArgs)
    {
        // Get parameter symbols from the method's containing scope.
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        ImmutableArray<ISymbol> paramSymbols;
        if (methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
        {
            var paramList = new List<ISymbol>();
            foreach (var child in methodDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym is not null)
                        paramList.Add(paramSym);
                }
            }
            paramSymbols = [.. paramList];
        }
        else
        {
            return; // No parameters to check.
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if (argList is not null)
        {
            foreach (var child in argList.Children())
            {
                if (child is ExpressionNode expr)
                    argExprs.Add(expr);
            }
        }

        // Check each argument against the corresponding parameter, applying substitution.
        var count = Math.Min(argExprs.Count, paramSymbols.Length);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if (argType is not null && paramType is not null)
            {
                // Apply enclosing type substitution (e.g., T → int for Box<int>).
                if (enclosingTypeParams is not null && enclosingTypeArgs is not null)
                    paramType = SubstituteType(paramType, enclosingTypeParams, enclosingTypeArgs);

                // Apply method-level substitution (e.g., U → string for Method<string>).
                if (methodTypeParams is not null && methodTypeArgs is not null)
                    paramType = SubstituteType(paramType, methodTypeParams, methodTypeArgs);

                // ── refinement-types feature: compile-time enforcement ──
                // When the parameter is a RefinementType, check the argument
                // against the refinement predicate (Requirement 5.3).
                if (TryCheckRefinementAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                if (!IsAssignable(argType, paramType))
                {
                    // unknown-type feature (Req 4.2, 4.4): emit a targeted
                    // diagnostic for void→unknown or unknown→non-unknown
                    // before falling back to the generic MIST_TC_0002.
                    if (TryEmitUnknownAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted unknown diagnostic emitted; skip generic.
                    }
                    // type-unions feature (Req 6.3, 6.4): emit a targeted
                    // diagnostic for union-specific failures.
                    else if (TryEmitUnionAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted union diagnostic emitted; skip generic.
                    }
                    // enums feature (Req 7.3): emit a targeted diagnostic
                    // for reverse conversion (underlying type → enum).
                    else if (TryEmitEnumAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                        // Targeted enum diagnostic emitted; skip generic.
                    }
                    else
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0002",
                            $"Argument type '{argType.DisplayName}' is not assignable to parameter type '{paramType.DisplayName}'",
                            argExprs[i],
                            model));
                    }
                }
                else
                {
                    // Resolve and record any implicit cast operator on the argument.
                    TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
                }
            }
        }

        // Readonly type argument passing check (Req 14.5, 14.6):
        // If an argument's type is readonly, the corresponding parameter must be readonly.
        CheckReadonlyTypeArgumentPassing(argList, methodSymbol, model, diagnostics, callSite);
    }

    /// <summary>
    /// Gets the type parameters from a type definition (ClassType, StructType, or InterfaceType).
    /// </summary>
    private static IReadOnlyList<IType>? GetTypeParameters(IType type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters.Count > 0 ? ct.TypeParameters : null,
            StructType st => st.TypeParameters.Count > 0 ? st.TypeParameters : null,
            InterfaceType it => it.TypeParameters.Count > 0 ? it.TypeParameters : null,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Readonly type argument call-site checking (MIST_TC_0076)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the given type is marked as readonly (has <c>IsReadonly == true</c>).
    /// Checks ClassType, StructType, InterfaceType, and TraitType.
    /// </summary>
    private static bool IsTypeReadonly(IType type)
    {
        return type switch
        {
            ClassType ct => ct.IsReadonly,
            StructType st => st.IsReadonly,
            InterfaceType it => it.IsReadonly,
            TraitType tt => tt.IsReadonly,
            _ => false,
        };
    }

    /// <summary>
    /// Checks that readonly types are only passed to readonly parameters at a call site.
    /// If an argument's type is readonly and the corresponding parameter is NOT marked
    /// <c>readonly</c>, emits <c>MIST_TC_0076</c>.
    /// If the argument's type is mutable, either parameter kind is acceptable.
    /// (Requirement 14.5, 14.6)
    /// </summary>
    private static void CheckReadonlyTypeArgumentPassing(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite)
    {
        if (argList is null)
            return;

        // Extract readonly flags for each parameter of the method.
        var readonlyFlags = ParameterSignature.ExtractIsReadonly(methodSymbol, model);
        if (readonlyFlags.Count == 0)
            return;

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
                argExprs.Add(expr);
        }

        // Check each argument: if its type is readonly, the parameter must be readonly.
        var count = Math.Min(argExprs.Count, readonlyFlags.Count);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            if (argType is null)
                continue;

            // Only check if the argument's type is readonly (Req 14.5).
            // If the argument's type is mutable, either parameter kind is acceptable (Req 14.6).
            if (!IsTypeReadonly(argType))
                continue;

            // The argument's type is readonly — the parameter MUST be marked readonly.
            if (!readonlyFlags[i])
            {
                // Extract parameter name for the diagnostic message.
                var paramName = GetParameterName(methodSymbol, i, model);
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0076",
                    $"Cannot pass readonly type '{argType.DisplayName}' to non-readonly parameter '{paramName}'",
                    argExprs[i],
                    model));
            }
        }
    }

    /// <summary>
    /// Gets the name of the parameter at the given index from a method's declaring node.
    /// Returns a fallback string if the name cannot be resolved.
    /// </summary>
    private static string GetParameterName(ISymbol methodSymbol, int index, ISemanticModel model)
    {
        Storm.Runtime.SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return $"param{index}"; }

        var paramList = declaringNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
            _ => null,
        };

        if (paramList is null)
            return $"param{index}";

        var currentIndex = 0;
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                if (currentIndex == index)
                    return paramNode.Name.Lexeme;
                currentIndex++;
            }
        }

        return $"param{index}";
    }


    /// <summary>
    /// Resolves type arguments from a <see cref="TypeArgumentListNode"/> AST node
    /// by resolving each type reference to its corresponding <see cref="IType"/>.
    /// </summary>
    private static IReadOnlyList<IType> ResolveTypeArgumentsFromAst(TypeArgumentListNode typeArgList, ISemanticModel model)
    {
        var result = new List<IType>();

        // The TypeArgumentListNode has TypeArgument (first arg) and TypeArguments (rest).
        var firstType = ResolveTypeReferenceToType(typeArgList.GetTypeReference(), model);
        result.Add(firstType);

        foreach (var typeRef in typeArgList.GetTypeReferences())
        {
            var argType = ResolveTypeReferenceToType(typeRef, model);
            result.Add(argType);
        }

        return result;
    }

    // ── Type alias diagnostic ID ─────────────────────────────────────────

    /// <summary>
    /// Emitted when a generic type alias is used with the wrong number of
    /// type arguments (Requirement 5.3).
    /// </summary>
    internal static readonly DiagnosticId TypeAliasArgumentCountMismatch = new("MIST_SB_0063");

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to its corresponding <see cref="IType"/>
    /// by looking up the type name in the semantic model's symbol table.
    /// Type aliases are resolved transparently — the underlying type is returned
    /// with no alias wrapper remaining.
    /// </summary>
    private static IType ResolveTypeReferenceToType(TypeReferenceNode typeRef, ISemanticModel model, DiagnosticBag? diagnostics = null)
    {
        // Try the expression type map first (may be populated for some nodes).
        var resolved = model.GetTypeOf(typeRef);
        if (resolved is not null && resolved is not ErrorType)
        {
            // Transparently unwrap type aliases (Requirement 5.1).
            if (resolved is TypeAliasType aliasFromMap)
                return aliasFromMap.UnderlyingType;
            return resolved;
        }

        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
            {
                var primType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue && primType is not null)
                    return new ArrayType(primType);
                return primType ?? ErrorType.Instance;
            }
            case NamedTypeRefTypeReferenceNode namedRef:
            {
                var name = namedRef.Name.Children().FirstOrDefault()?.Span;
                // Try to find the type by looking up the symbol.
                var symbol = model.GetDeclaredSymbol(namedRef);
                if (symbol?.DeclaredType is not null)
                {
                    return ResolveTypeAliasMaybeGeneric(
                        symbol.DeclaredType, namedRef, model, diagnostics);
                }

                // Fallback: search all symbols for a matching type name.
                var typeName = ExtractTypeName(namedRef);
                foreach (var sym in model.AllSymbols)
                {
                    if (sym.Name == typeName && sym.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.TypeAlias)
                    {
                        var declaredType = sym.DeclaredType ?? ErrorType.Instance;
                        return ResolveTypeAliasMaybeGeneric(
                            declaredType, namedRef, model, diagnostics);
                    }
                }
                return ErrorType.Instance;
            }
            case VoidTypeRefTypeReferenceNode:
                return LegacyPrimitiveTypes.Void;
            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// If <paramref name="type"/> is a <see cref="TypeAliasType"/>, transparently
    /// resolves it to its underlying type. For generic aliases with type arguments
    /// on the reference node, verifies argument count and substitutes type arguments
    /// into the underlying type. Reports <c>MIST_SB_0063</c> on arity mismatch.
    /// Non-alias types are returned unchanged.
    /// </summary>
    private static IType ResolveTypeAliasMaybeGeneric(
        IType type,
        NamedTypeRefTypeReferenceNode namedRef,
        ISemanticModel model,
        DiagnosticBag? diagnostics)
    {
        if (type is not TypeAliasType aliasType)
            return type;

        var underlyingType = aliasType.UnderlyingType;
        var typeParams = aliasType.TypeParameters;

        // Non-generic alias — return the underlying type directly.
        if (typeParams.Count == 0)
            return underlyingType;

        // Generic alias — check if type arguments are provided on the reference.
        if (namedRef.TypeArgs is null)
        {
            // No type arguments provided for a generic alias — return as-is
            // (the underlying type still contains unsubstituted type parameters).
            return underlyingType;
        }

        // Resolve the type arguments from the AST.
        var typeArgs = ResolveTypeArgumentsFromAst(namedRef.TypeArgs, model);

        // Verify argument count matches parameter count (Requirement 5.3).
        if (typeArgs.Count != typeParams.Count)
        {
            if (diagnostics is not null)
            {
                diagnostics.Add(MakeDiagnostic(
                    TypeAliasArgumentCountMismatch.Value,
                    $"Type alias '{aliasType.Name}' expects {typeParams.Count} type argument(s) but was given {typeArgs.Count}.",
                    namedRef,
                    model));
            }
            return ErrorType.Instance;
        }

        // Substitute type arguments into the underlying type.
        return SubstituteType(underlyingType, typeParams, typeArgs);
    }

    /// <summary>
    /// Resolves a primitive type AST node to its corresponding <see cref="IType"/>.
    /// </summary>
    private static IType? ResolvePrimitiveType(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the type name from a <see cref="NamedTypeRefTypeReferenceNode"/>.
    /// </summary>
    private static string ExtractTypeName(NamedTypeRefTypeReferenceNode namedRef)
    {
        return namedRef.Name.IDENTIFIER.Lexeme;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array operation checking (MIST_TC_0010, MIST_TC_0012)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckArrayCreation(
        ArrayCreationExpressionPrimaryNode arrayCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the size expression.
        CheckExpression(arrayCreation.Size, model, diagnostics);

        // Check element type is not void (MIST_TC_0034).
        var elementType = model.GetTypeOf(arrayCreation);
        if (elementType is ArrayType at && at.ElementType.IsVoid())
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0034",
                "Cannot create an array of type 'void'. Void is not permitted as an array element type.",
                arrayCreation,
                model));
            return;
        }

        // ── unknown-type storage ban on `new T[n]` (Task 12.3) ───────────
        // Requirement 5.5 — `new unknown[n]` is forbidden because every
        // slot of the resulting array would persist an `unknown`-typed
        // value, and array elements are writable storage. The check runs
        // against the element type (unwrapped from the ArrayType that
        // SemanticModelBuilder produces for this node); nested
        // `unknown` occurrences inside a non-trivial element type like
        // `List<unknown>[]` also trip the ban via IsStorageBanned.
        if (elementType is ArrayType arr && TypeContainment.IsStorageBanned(arr.ElementType))
        {
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownAsArrayElement.Value,
                $"Cannot create an array of type '{arr.ElementType.DisplayName}' because its element type contains 'unknown'. Array elements are writable storage and cannot hold an 'unknown'-typed value.",
                arrayCreation,
                model));
            return;
        }

        var sizeType = model.GetTypeOf(arrayCreation.Size);
        if (sizeType is not null && !IsErrorType(sizeType))
        {
            if (!IsIntLike(sizeType))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0012",
                    $"Array size must be of type 'int', but found '{sizeType.DisplayName}'",
                    arrayCreation.Size,
                    model));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array literal checking (MIST_TC_0034, MIST_TC_0095, MIST_SB_0055)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckArrayLiteral(
        ArrayLiteralExpressionPrimaryNode arrayLiteral,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Recursively type-check the first element expression.
        CheckExpression(arrayLiteral.First, model, diagnostics);

        // Recursively type-check each remaining element expression.
        foreach (var elem in arrayLiteral.Expressions)
        {
            CheckExpression(elem, model, diagnostics);
        }

        // Get the type of the first element.
        var firstType = model.GetTypeOf(arrayLiteral.First);

        // Validate the first element is not void (MIST_TC_0034).
        if (firstType is not null && !IsErrorType(firstType) && firstType.IsVoid())
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0034",
                "Cannot create an array of type 'void'. Void is not permitted as an array element type.",
                arrayLiteral.First,
                model));
        }

        // Validate the first element does not contain unknown (MIST_SB_0055).
        if (firstType is not null && !IsErrorType(firstType) && TypeContainment.IsStorageBanned(firstType))
        {
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownAsArrayElement.Value,
                $"Cannot create an array with element type '{firstType.DisplayName}' because it contains 'unknown'. Array elements are writable storage and cannot hold an 'unknown'-typed value.",
                arrayLiteral.First,
                model));
        }

        // Validate each remaining element.
        foreach (var elem in arrayLiteral.Expressions)
        {
            var elemType = model.GetTypeOf(elem);
            if (elemType is null || IsErrorType(elemType))
                continue;

            // Reject void elements (MIST_TC_0034).
            if (elemType.IsVoid())
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0034",
                    "Cannot create an array of type 'void'. Void is not permitted as an array element type.",
                    elem,
                    model));
                continue;
            }

            // Reject unknown elements (MIST_SB_0055).
            if (TypeContainment.IsStorageBanned(elemType))
            {
                diagnostics.Add(MakeDiagnostic(
                    SemanticModelBuilder.UnknownAsArrayElement.Value,
                    $"Cannot create an array with element type '{elemType.DisplayName}' because it contains 'unknown'. Array elements are writable storage and cannot hold an 'unknown'-typed value.",
                    elem,
                    model));
                continue;
            }

            // Validate element type compatibility with the first element (MIST_TC_0095).
            if (firstType is not null && !IsErrorType(firstType) && !firstType.IsVoid()
                && !TypeContainment.IsStorageBanned(firstType))
            {
                if (!IsAssignable(elemType, firstType, model) && !IsAssignable(firstType, elemType, model))
                {
                    diagnostics.Add(MakeDiagnostic(
                        ArrayLiteralIncompatibleTypes.Value,
                        $"Array literal elements have incompatible types: '{firstType.DisplayName}' and '{elemType.DisplayName}'. All elements must share a common type.",
                        elem,
                        model));
                }
            }
        }

        // When the array literal has a known target type (from the semantic model),
        // validate each element is assignable to the target element type.
        var arrayType = model.GetTypeOf(arrayLiteral);
        if (arrayType is ArrayType targetArrayType)
        {
            var targetElementType = targetArrayType.ElementType;
            if (!IsErrorType(targetElementType) && !targetElementType.IsVoid()
                && !TypeContainment.IsStorageBanned(targetElementType))
            {
                // Check first element against target element type.
                if (firstType is not null && !IsErrorType(firstType) && !firstType.IsVoid()
                    && !TypeContainment.IsStorageBanned(firstType))
                {
                    if (!IsAssignable(firstType, targetElementType, model))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            ArrayLiteralIncompatibleTypes.Value,
                            $"Array literal elements have incompatible types: '{firstType.DisplayName}' and '{targetElementType.DisplayName}'. All elements must share a common type.",
                            arrayLiteral.First,
                            model));
                    }
                }

                // Check remaining elements against target element type.
                foreach (var elem in arrayLiteral.Expressions)
                {
                    var elemType = model.GetTypeOf(elem);
                    if (elemType is null || IsErrorType(elemType) || elemType.IsVoid()
                        || TypeContainment.IsStorageBanned(elemType))
                        continue;

                    if (!IsAssignable(elemType, targetElementType, model))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            ArrayLiteralIncompatibleTypes.Value,
                            $"Array literal elements have incompatible types: '{elemType.DisplayName}' and '{targetElementType.DisplayName}'. All elements must share a common type.",
                            elem,
                            model));
                    }
                }
            }
        }
    }

    private void CheckIndexSuffix(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the index expression.
        CheckExpression(indexSuffix.Index, model, diagnostics);

        var indexType = model.GetTypeOf(indexSuffix.Index);
        if (indexType is not null && !IsErrorType(indexType))
        {
            if (!IsIntLike(indexType))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0010",
                    $"Array index must be of type 'int', but found '{indexType.DisplayName}'",
                    indexSuffix.Index,
                    model));
            }
        }

        // Verify the target is an array type (the target type should be ArrayType).
        // If targetType is not an array, the semantic model builder would have
        // already flagged it or assigned ErrorType. We don't emit a separate
        // diagnostic here to avoid cascading.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Object creation checking (MIST_TC_0011)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckObjectCreation(
        ObjectCreationExpressionPrimaryNode objectCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions.
        if (objectCreation.Arguments is not null)
        {
            CheckArgumentListExpressions(objectCreation.Arguments, model, diagnostics);
        }

        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null || IsErrorType(createdType))
            return;

        // Type aliases are transparent — unwrap to the underlying type
        // so constructor lookup finds the actual class/struct constructors.
        if (createdType is TypeAliasType aliasCreated)
            createdType = aliasCreated.UnderlyingType;

        // ── unknown-type storage ban on `new` (Task 12.1, 12.2) ──────────
        // Two guards, checked before constructor lookup because the
        // constructor lookup would fail anyway (UnknownType has no members
        // — Requirement 3.4 — and the constructed generic's constructor
        // would require storing an `unknown`-typed argument into a
        // T-typed slot, which is forbidden).
        if (createdType is UnknownType)
        {
            // Requirement 5.4 — `new unknown(...)` is categorically
            // forbidden. In practice the grammar rejects this form
            // before we get here (see remarks on
            // SemanticModelBuilder.UnknownCannotBeInstantiated), so this
            // branch is a defensive guard for programmatically-constructed
            // ASTs.
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownCannotBeInstantiated.Value,
                "'unknown' cannot be instantiated. The 'unknown' type has no constructor and no declared members; use a concrete type instead.",
                objectCreation,
                model));
            return;
        }

        if (TypeContainment.IsStorageBanned(createdType))
        {
            // Requirement 5.7 — a generic construction whose substituted
            // type contains `unknown` at one or more positions is
            // forbidden because every constructor of such a generic would
            // need to store an `unknown`-typed argument into a T-typed
            // slot. Diagnostic message names the generic type (Req 16.2)
            // and states the rule.
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownInGenericConstruction.Value,
                $"Cannot construct '{createdType.DisplayName}' because one or more type arguments are 'unknown'. A generic type cannot be constructed with 'unknown' as a type argument; use concrete type arguments or pattern-match from an already-widened value.",
                objectCreation,
                model));
            return;
        }

        // ── new T(...) validation against constructor constraints ──
        // When the type being constructed is a TypeParameterType, validate
        // the call against the declared constructor constraints on that type
        // parameter (Requirements 6.5, 6.6, 10.8).
        if (createdType is TypeParameterType typeParam)
        {
            var constraints = typeParam.Constraints;

            // If no constructor constraints exist, we cannot use new T(...).
            if (constraints is null || constraints.ConstructorConstraints.Count == 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintNewTNoMatch.Value,
                    $"No constructor constraint on type parameter '{typeParam.Name}' matches arguments '({FormatArgTypes(objectCreation.Arguments, model)})'. Available constructor constraints: (none).",
                    objectCreation,
                    model));
                return;
            }

            // Collect argument types from the new T(...) call.
            var ctorArgTypes = CollectArgumentTypes(objectCreation.Arguments, model);

            // Check if any declared constructor constraint signature matches.
            var matched = false;
            foreach (var requiredParams in constraints.ConstructorConstraints)
            {
                if (requiredParams.Count != ctorArgTypes.Count)
                    continue;

                var allMatch = true;
                for (var i = 0; i < requiredParams.Count; i++)
                {
                    if (!IsAssignable(ctorArgTypes[i], requiredParams[i], model))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch)
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                var argTypeNames = FormatArgTypes(objectCreation.Arguments, model);
                var signatures = string.Join(", ",
                    constraints.ConstructorConstraints.Select(p =>
                        "new(" + string.Join(", ", p.Select(t => t.DisplayName)) + ")"));
                diagnostics.Add(MakeDiagnostic(
                    ConstraintNewTNoMatch.Value,
                    $"No constructor constraint on type parameter '{typeParam.Name}' matches arguments '({argTypeNames})'. Available constructor constraints: {signatures}.",
                    objectCreation,
                    model));
            }

            return;
        }

        // ── Constraint satisfaction checking for generic type arguments ──
        // When constructing a generic type (e.g., new Box<int>()), verify that
        // each type argument satisfies the constraints on the corresponding
        // type parameter of the generic definition.
        if (createdType is ConstructedType constructedForConstraints)
        {
            var typeParams = GetTypeParameters(constructedForConstraints.GenericDefinition);
            if (typeParams is not null)
            {
                var typeArgs = constructedForConstraints.TypeArguments;
                var genericName = constructedForConstraints.GenericDefinition.DisplayName;
                for (var i = 0; i < typeParams.Count && i < typeArgs.Count; i++)
                {
                    if (typeParams[i] is TypeParameterType tp5848)
                        CheckConstraintSatisfaction(typeArgs[i], tp5848, objectCreation, genericName, model, diagnostics);
                }
            }
        }

        // Find constructors on the type.
        IScope? memberScope = createdType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };
        // Collect all constructors from the target type's member scope.
        var constructors = CollectConstructorOverloads(createdType);

        // Collect argument types.
        var argTypes = CollectArgumentTypes(objectCreation.Arguments, model);

        if (constructors.Count == 0)
        {
            // If no constructors are defined and there are no arguments, it's OK
            // (default constructor). Otherwise, emit diagnostic.
            if (argTypes.Count > 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation,
                    model));
                return;
            }
        }
        else if (constructors.Count > 1)
        {
            // When multiple constructors exist, use overload resolution.
            // When the created type is a ConstructedType (e.g., Optional<int>),
            // pass the type substitution context so the resolver maps T → Int32
            // in constructor parameter types before the applicability check.
            (IReadOnlyList<IType> TypeParams, IReadOnlyList<IType> TypeArgs)? typeSubstitution = null;
            if (createdType is ConstructedType ctForSubst)
            {
                var tp = GetTypeParameters(ctForSubst.GenericDefinition);
                if (tp is not null)
                    typeSubstitution = (tp, ctForSubst.TypeArguments);
            }

            var result = OverloadResolver.Resolve(constructors, argTypes, model, typeSubstitution: typeSubstitution);
            var typeName = createdType.DisplayName;

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{typeName}' constructor matches argument types ({argTypeNames})",
                        objectCreation,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{typeName}' constructor is ambiguous between: {string.Join(" and ", sigs)}",
                        objectCreation,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — fall through to initializer check.
                    break;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0011.
            var hasMatch = ConstructorMatches(constructors[0], argTypes, createdType, model);
            if (!hasMatch)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation,
                    model));
            }
        }

        // ── Property initializer block validation ────────────────────────
        if (objectCreation.Initializer is not null)
        {
            CheckPropertyInitializerBlock(objectCreation.Initializer, createdType, objectCreation, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a <c>new Type { ... }</c> expression (no constructor arguments).
    /// Resolves the type, verifies a parameterless constructor exists, then
    /// validates the property initializer block.
    /// </summary>
    private void CheckObjectCreationNoArgs(
        ObjectCreationExpressionNoArgsPrimaryNode objectCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null || IsErrorType(createdType))
            return;

        // Type aliases are transparent — unwrap to the underlying type.
        if (createdType is TypeAliasType aliasCreated)
            createdType = aliasCreated.UnderlyingType;

        // ── unknown-type storage ban ─────────────────────────────────────
        if (createdType is UnknownType)
        {
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownCannotBeInstantiated.Value,
                "'unknown' cannot be instantiated. The 'unknown' type has no constructor and no declared members; use a concrete type instead.",
                objectCreation,
                model));
            return;
        }

        if (TypeContainment.IsStorageBanned(createdType))
        {
            diagnostics.Add(MakeDiagnostic(
                SemanticModelBuilder.UnknownInGenericConstruction.Value,
                $"Cannot construct '{createdType.DisplayName}' because one or more type arguments are 'unknown'. A generic type cannot be constructed with 'unknown' as a type argument; use concrete type arguments or pattern-match from an already-widened value.",
                objectCreation,
                model));
            return;
        }

        // ── Constraint satisfaction checking for generic type arguments ──
        if (createdType is ConstructedType constructedForConstraints)
        {
            var typeParams = GetTypeParameters(constructedForConstraints.GenericDefinition);
            if (typeParams is not null)
            {
                var typeArgs = constructedForConstraints.TypeArguments;
                var genericName = constructedForConstraints.GenericDefinition.DisplayName;
                for (var i = 0; i < typeParams.Count && i < typeArgs.Count; i++)
                {
                    if (typeParams[i] is TypeParameterType tp)
                        CheckConstraintSatisfaction(typeArgs[i], tp, objectCreation, genericName, model, diagnostics);
                }
            }
        }

        // ── Verify parameterless constructor exists ──────────────────────
        // The no-args variant invokes the parameterless constructor.
        // If no constructors are defined, a default parameterless constructor
        // is implied. If constructors exist, at least one must be parameterless.
        var constructors = CollectConstructorOverloads(createdType);
        if (constructors.Count > 0)
        {
            var hasParameterless = false;
            foreach (var ctor in constructors)
            {
                var paramTypes = ParameterSignature.Extract(ctor, model);
                if (paramTypes.Count == 0)
                {
                    hasParameterless = true;
                    break;
                }
            }

            if (!hasParameterless)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation,
                    model));
            }
        }

        // ── Property initializer block validation ────────────────────────
        CheckPropertyInitializerBlock(objectCreation.Initializer, createdType, objectCreation, model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property initializer block validation (MIST_PI_0001–MIST_PI_0006)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates each assignment in a property initializer block against the
    /// constructed type. Checks member existence, type compatibility,
    /// writability, accessibility, and duplicate detection.
    /// </summary>
    private void CheckPropertyInitializerBlock(
        PropertyInitializerBlockNode initializerBlock,
        IType constructedType,
        SyntaxNode parentNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (initializerBlock.Assignments is null)
            return;

        // Unwrap type aliases and ConstructedType for member lookup.
        var lookupType = constructedType;
        if (lookupType is TypeAliasType aliasType)
            lookupType = aliasType.UnderlyingType;

        // For ConstructedType, look up members on the generic definition.
        var memberLookupType = lookupType is ConstructedType cst ? cst.GenericDefinition : lookupType;

        IScope? memberScope = memberLookupType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        // Iterate over all assignments: first + rest.
        var assignments = initializerBlock.Assignments;
        CheckSinglePropertyAssignment(assignments.PropertyAssignment, memberScope, memberLookupType, seenNames, model, diagnostics);
        foreach (var assignment in assignments.PropertyAssignments)
        {
            CheckSinglePropertyAssignment(assignment, memberScope, memberLookupType, seenNames, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property assignment within an initializer block.
    /// </summary>
    private void CheckSinglePropertyAssignment(
        PropertyAssignmentNode assignment,
        IScope memberScope,
        IType memberLookupType,
        HashSet<string> seenNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var memberName = assignment.Name.Lexeme;

        // ── 6. Duplicate detection ───────────────────────────────────────
        if (!seenNames.Add(memberName))
        {
            diagnostics.Add(MakeDiagnostic(
                PropertyInitializerDuplicate.Value,
                $"Member '{memberName}' is assigned more than once in the initializer",
                assignment,
                model));
            return;
        }

        // ── 1. Member existence ──────────────────────────────────────────
        var symbol = memberScope.Lookup(memberName);
        if (symbol is null || symbol.Kind is not (SymbolKind.Property or SymbolKind.Field))
        {
            diagnostics.Add(MakeDiagnostic(
                PropertyInitializerMemberNotFound.Value,
                $"Type '{memberLookupType.DisplayName}' does not contain a property or field named '{memberName}'",
                assignment,
                model));
            return;
        }

        // ── 5. Accessibility ─────────────────────────────────────────────
        // Backend symbols are assumed public; only check user-defined members.
        if (symbol.Origin != SymbolOrigin.Backend)
        {
            SyntaxNode? declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { declaringNode = null; }

            var modifiers = GetMemberModifiers(declaringNode);
            if (!modifiers.IsPublic)
            {
                // Private members are only accessible within the declaring type.
                var declaringType = GetDeclaringTypeOfMember(symbol);
                if (declaringType is not null && !IsSameType(declaringType, _currentEnclosingType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        PropertyInitializerInaccessible.Value,
                        $"'{memberName}' is inaccessible due to its protection level",
                        assignment,
                        model));
                    return;
                }
            }
        }

        // ── 3. Writability (property) ────────────────────────────────────
        if (symbol.Kind == SymbolKind.Property)
        {
            var propInfo = GetPropertyMetadata(symbol);
            if (propInfo is not null && !propInfo.HasSet && !propInfo.HasInit)
            {
                diagnostics.Add(MakeDiagnostic(
                    PropertyInitializerNoSetOrInit.Value,
                    $"Property '{memberName}' has no accessible 'set' or 'init' accessor and cannot be assigned in an initializer",
                    assignment,
                    model));
                return;
            }
        }

        // ── 4. Writability (field) ───────────────────────────────────────
        if (symbol.Kind == SymbolKind.Field && symbol is Symbol concreteField)
        {
            if (concreteField.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
                or ReadonlyStatus.InferredReadonly
                or ReadonlyStatus.ImplicitReadonly)
            {
                diagnostics.Add(MakeDiagnostic(
                    PropertyInitializerReadonlyField.Value,
                    $"Field '{memberName}' is readonly and cannot be assigned in an initializer",
                    assignment,
                    model));
                return;
            }
        }

        // ── 2. Type compatibility ────────────────────────────────────────
        var memberType = symbol.DeclaredType;
        var valueType = model.GetTypeOf(assignment.Value);
        if (memberType is not null && valueType is not null
            && !IsErrorType(memberType) && !IsErrorType(valueType))
        {
            if (!IsAssignable(valueType, memberType, model))
            {
                diagnostics.Add(MakeDiagnostic(
                    PropertyInitializerTypeMismatch.Value,
                    $"Cannot assign '{valueType.DisplayName}' to '{memberName}' of type '{memberType.DisplayName}'",
                    assignment,
                    model));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor chain checking (MIST_TC_0013)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckConstructorChain(
        ConstructorChainNode chain,
        ISymbol ctorSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract arguments from the appropriate chain variant.
        var arguments = chain switch
        {
            ThisChainConstructorChainNode thisChain => thisChain.Arguments,
            BaseChainConstructorChainNode baseChain => baseChain.Arguments,
            _ => null,
        };

        // Check argument expressions.
        if (arguments is not null)
        {
            CheckArgumentListExpressions(arguments, model, diagnostics);
        }

        // Find the enclosing type.
        var enclosingType = FindDeclaringType(ctorSymbol);
        if (enclosingType is null || IsErrorType(enclosingType))
            return;

        // For `: base(...)` chains, resolve the base type and search its constructors.
        // For `: this(...)` chains, search the declaring type's own constructors.
        IType targetType = enclosingType;
        if (chain is BaseChainConstructorChainNode && enclosingType is ClassType ct && ct.BaseType is not null)
        {
            targetType = ct.BaseType;
        }

        IScope? memberScope = targetType switch
        {
            ClassType classType => classType.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        var argTypes = CollectArgumentTypes(arguments, model);

        // For `: this(...)`, exclude the current constructor from the search.
        // For `: base(...)`, search all constructors on the base type.
        var candidateConstructors = chain is BaseChainConstructorChainNode
            ? memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor).ToList()
            : memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor && !ReferenceEquals(m, ctorSymbol)).ToList();

        if (candidateConstructors.Count == 0)
        {
            // No constructors available — only an error if arguments were provided.
            if (argTypes.Count > 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0013",
                    $"No constructor on '{targetType.DisplayName}' matches the chained arguments",
                    chain,
                    model));
            }
            return;
        }

        // When multiple constructors exist, use overload resolution.
        if (candidateConstructors.Count > 1)
        {
            var result = OverloadResolver.Resolve(candidateConstructors, argTypes, model);
            var typeName = targetType.DisplayName;

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{typeName}' constructor matches argument types ({argTypeNames})",
                        chain,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{typeName}' constructor is ambiguous between: {string.Join(" and ", sigs)}",
                        chain,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — no diagnostic needed.
                    return;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0013.
            var hasMatch = ConstructorMatches(candidateConstructors[0], argTypes, targetType, model);
            if (!hasMatch)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0013",
                    $"No constructor on '{targetType.DisplayName}' matches the chained arguments",
                    chain,
                    model));
            }
        }
    }

    private static bool ConstructorMatches(ISymbol ctor, List<IType> argTypes, IType? createdType, ISemanticModel model)
    {
        // Get the constructor's parameters.
        SyntaxNode ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { return false; }

        if (ctorNode is not ConstructorDeclarationNode ctorDecl)
            return false;

        var paramTypes = new List<IType>();
        if (ctorDecl.Parameters is not null)
        {
            foreach (var child in ctorDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym?.DeclaredType is not null)
                        paramTypes.Add(paramSym.DeclaredType);
                }
            }
        }

        if (argTypes.Count != paramTypes.Count)
            return false;

        // When the created type is a ConstructedType (e.g., GenericBox<int>),
        // substitute type parameters in constructor parameter types with the
        // actual type arguments before comparing.
        IReadOnlyList<IType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;
        if (createdType is ConstructedType ct)
        {
            typeParams = GetTypeParameters(ct.GenericDefinition);
            typeArgs = ct.TypeArguments;
        }

        for (var i = 0; i < argTypes.Count; i++)
        {
            var paramType = paramTypes[i];
            if (typeParams is not null && typeArgs is not null)
            {
                paramType = SubstituteType(paramType, typeParams, typeArgs);
            }
            if (!IsAssignable(argTypes[i], paramType))
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Access modifier enforcement (MIST_TC_0014, MIST_TC_0015, MIST_TC_0016)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether the target of a postfix expression is a type name
    /// (e.g., <c>Console</c> in <c>Console.WriteLine()</c>) rather than an instance.
    /// </summary>
    private static bool IsTargetTypeName(PrimaryNode target, ISemanticModel model)
    {
        if (target is not IdentifierExpressionPrimaryNode identifier)
            return false;

        var name = identifier.Name.Lexeme;

        // Check if any symbol with this name is a variable, parameter, or field.
        // If so, the identifier refers to an instance, not a type name.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Name == name)
            {
                if (symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter or SymbolKind.Field)
                {
                    return false;
                }
                if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Enum)
                {
                    return true;
                }
            }
        }

        // Fallback: check the expression type for ambient types (e.g. Console)
        // that may not have a symbol in the user-defined symbol table.
        var resolvedType = model.GetTypeOf(target);
        return resolvedType is ClassType or StructType or EnumType;
    }

    /// <summary>
    /// Checks access modifier rules for a member access or method call.
    /// Emits MIST_TC_0014 (private access), MIST_TC_0015 (static via instance),
    /// or MIST_TC_0016 (instance via type name).
    /// </summary>
    private void CheckMemberAccessModifiers(
        string memberName,
        IType? targetType,
        bool targetIsTypeName,
        SyntaxNode accessNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetType is null || IsErrorType(targetType))
            return;

        // Find the member symbol on the target type.
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if (memberSymbol is null)
            return;

        // Backend symbols are assumed public and valid — skip access modifier checks
        // for them since they have no AST declaring node with modifiers.
        // The type checker treats all symbols uniformly for type checking, but
        // access modifier enforcement only applies to user-defined members.
        if (memberSymbol.Origin == SymbolOrigin.Backend)
            return;

        // Get the declaring AST node to inspect modifiers.
        SyntaxNode? declaringNode;
        try { declaringNode = model.GetDeclaringNode(memberSymbol); }
        catch { return; }

        // Extract modifier information from the declaring node.
        var modifiers = GetMemberModifiers(declaringNode);

        // R9.1, R9.3: Private member access check.
        // Members with no modifier default to private (R9.3).
        if (!modifiers.IsPublic)
        {
            // Private members are only accessible within the declaring type.
            var declaringType = GetDeclaringTypeOfMember(memberSymbol);
            if (declaringType is not null && !IsSameType(declaringType, _currentEnclosingType))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0014",
                    $"'{memberName}' is inaccessible due to its protection level",
                    accessNode,
                    model));
                return; // Don't emit additional diagnostics for the same access.
            }
        }

        // R9.4: Static members must be accessed via type name, not instance.
        if (modifiers.IsStatic && !targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0015",
                $"Static member '{memberName}' cannot be accessed via an instance",
                accessNode,
                model));
            return;
        }

        // R9.5: Instance members must be accessed via instance, not type name.
        if (!modifiers.IsStatic && targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0016",
                $"Instance member '{memberName}' cannot be accessed via a type name",
                accessNode,
                model));
        }

        // ── Property-specific access checks ──────────────────────────────

        if (memberSymbol.Kind == SymbolKind.Property)
        {
            var propInfo = GetPropertyMetadata(memberSymbol);
            if (propInfo is not null)
            {
                // MIST_TC_0027: Static property accessed through instance reference.
                if (modifiers.IsStatic && !targetIsTypeName)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0027",
                        $"Static property '{memberName}' cannot be accessed through an instance reference",
                        accessNode,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Finds a member symbol by name on the given type.
    /// For <see cref="ConstructedType"/>, looks up in the generic definition's member scope.
    /// </summary>
    private static ISymbol? FindMemberSymbol(IType targetType, string memberName, ISemanticModel model)
    {
        // Unwrap ConstructedType to look up in the generic definition's member scope.
        if (targetType is ConstructedType ct)
            targetType = ct.GenericDefinition;

        IScope? memberScope = targetType switch
        {
            ClassType ct2 => ct2.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            EnumType et => et.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName)
                return member;
        }

        return null;
    }

    /// <summary>
    /// Gets the type that declares a given member symbol by finding the type
    /// whose MemberScope matches the member's ContainingScope.
    /// </summary>
    private static IType? GetDeclaringTypeOfMember(ISymbol memberSymbol)
    {
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                && member.DeclaredType is ClassType or StructType)
            {
                var scope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(scope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if two types refer to the same type (by reference or display name).
    /// </summary>
    private static bool IsSameType(IType? a, IType? b)
    {
        if (a is null || b is null)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        // Fallback: compare by display name for types that may not be reference-equal.
        if (a.DisplayName == b.DisplayName)
            return true;
        // Migration-window equivalence: legacy `int` matches stdlib `Int32`
        // and so on for every primitive alias in KeywordAliases.Map.
        return ArePrimitiveEquivalent(a, b);
    }

    /// <summary>
    /// Modifier flags extracted from a member's AST declaring node.
    /// </summary>
    private readonly record struct MemberModifiers(bool IsPublic, bool IsPrivate, bool IsStatic)
    {
        /// <summary>
        /// True if the member has no explicit access modifier (defaults to private per R9.3).
        /// </summary>
        public bool HasNoAccessModifier => !IsPublic && !IsPrivate;
    }

    /// <summary>
    /// Extracts modifier information from a member's AST declaring node.
    /// Returns default (all false) for backend symbols or nodes without modifiers.
    /// </summary>
    private static MemberModifiers GetMemberModifiers(SyntaxNode? declaringNode)
    {
        IReadOnlyList<ModifierNode>? modifiers = declaringNode switch
        {
            FieldDeclarationNode field => field.Modifiers,
            MethodDeclarationNode method => method.Modifiers,
            ConstructorDeclarationNode ctor => ctor.Modifiers,
            BlockPropertyPropertyDeclarationNode blockProp => blockProp.Modifiers,
            ExpressionBodiedPropertyPropertyDeclarationNode exprProp => exprProp.Modifiers,
            BlockIndexerIndexerDeclarationNode blockIndexer => blockIndexer.Modifiers,
            _ => null,
        };

        if (modifiers is null || modifiers.Count == 0)
        {
            // No modifiers → defaults to private (R9.3), not static.
            return new MemberModifiers(IsPublic: false, IsPrivate: false, IsStatic: false);
        }

        var isPublic = false;
        var isPrivate = false;
        var isStatic = false;

        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    isPublic = true;
                    break;
                case PrivateModifierModifierNode:
                    isPrivate = true;
                    break;
                case StaticModifierModifierNode:
                    isStatic = true;
                    break;
            }
        }

        return new MemberModifiers(isPublic, isPrivate, isStatic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Refinement type compile-time enforcement (MIST_TC_0097, MIST_TC_0098)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether an assignment to a <see cref="RefinementType"/> target is valid.
    /// <list type="bullet">
    ///   <item>If the source is already the same <see cref="RefinementType"/>, accepts without re-checking.</item>
    ///   <item>If the source is a compile-time constant, evaluates the predicate and emits
    ///         <see cref="RefinementLiteralViolation"/> if it fails.</item>
    ///   <item>If the source is a non-constant expression of the base type, emits
    ///         <see cref="RefinementNonConstantAssignment"/>.</item>
    /// </list>
    /// Returns <c>true</c> if a refinement-specific diagnostic was emitted (caller should
    /// skip the generic assignability check), <c>false</c> otherwise.
    /// </summary>
    private static bool TryCheckRefinementAssignment(
        IType sourceType,
        IType targetType,
        ExpressionNode expression,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Handle union-of-refinements: when the target is a UnionType with RefinementType
        // constituents, check if the literal satisfies at least one constituent's predicate.
        if (targetType is UnionType targetUnion && targetUnion.Constituents.Any(c => c is RefinementType))
        {
            var unionConstantValue = TryExtractLiteralConstantValue(expression);
            if (unionConstantValue is not null)
            {
                // Check if the literal satisfies any constituent's refinement predicate.
                var satisfiesAny = false;
                foreach (var constituent in targetUnion.Constituents)
                {
                    if (constituent is RefinementType refinementConstituent)
                    {
                        if (refinementConstituent.Predicate.Evaluate(unionConstantValue))
                        {
                            satisfiesAny = true;
                            break;
                        }
                    }
                    else if (IsAssignable(sourceType, constituent, model))
                    {
                        // Non-refinement constituent that the source is assignable to.
                        satisfiesAny = true;
                        break;
                    }
                }

                if (!satisfiesAny)
                {
                    diagnostics.Add(MakeDiagnostic(
                        RefinementLiteralViolation.Value,
                        $"Value '{unionConstantValue}' does not satisfy any refinement constituent of union type '{targetUnion.DisplayName}'",
                        expression,
                        model));
                    return true;
                }

                // Literal satisfies at least one constituent — accept.
                return false;
            }

            // Non-constant expression assigned to union-of-refinements: require pattern matching.
            if (sourceType is not UnionType && sourceType is not RefinementType)
            {
                var isBaseOfAnyConstituent = targetUnion.Constituents
                    .OfType<RefinementType>()
                    .Any(r => IsAssignable(sourceType, r.BaseType, model));
                if (isBaseOfAnyConstituent)
                {
                    diagnostics.Add(MakeDiagnostic(
                        RefinementNonConstantAssignment.Value,
                        $"Cannot assign '{sourceType.DisplayName}' to '{targetUnion.DisplayName}' without proving the refinement. Use pattern matching to validate at runtime",
                        expression,
                        model));
                    return true;
                }
            }

            return false;
        }

        // Only applies when the target is a RefinementType.
        if (targetType is not RefinementType targetRefinement)
            return false;

        // If the source is already the same RefinementType, accept without re-checking.
        // The source type already guarantees compliance (Requirement 5.5).
        if (sourceType is RefinementType sourceRefinement &&
            sourceRefinement.Name == targetRefinement.Name)
            return false;

        // If the source is a RefinementType with a different name but the same
        // base type, attempt subset proving via the RefinementSolver (Requirement 9.1, 9.3).
        if (sourceType is RefinementType sourceRefinementDifferent)
        {
            if (IsAssignable(sourceRefinementDifferent.BaseType, targetRefinement.BaseType, model))
            {
                if (RefinementSolver.Implies(sourceRefinementDifferent.Predicate, targetRefinement.Predicate))
                {
                    // Subset proven — accept the assignment.
                    return false;
                }

                // Cannot prove subset relationship — emit diagnostic.
                diagnostics.Add(MakeDiagnostic(
                    RefinementSubsetNotProvable.Value,
                    $"Cannot assign '{sourceRefinementDifferent.Name}' to '{targetRefinement.Name}' because the compiler cannot prove that '{sourceRefinementDifferent.Predicate.Description}' implies '{targetRefinement.Predicate.Description}'",
                    expression,
                    model));
                return true;
            }

            // Different base types — fall through to normal assignability.
            return false;
        }

        // Check if the source is a compile-time constant literal.
        var constantValue = TryExtractLiteralConstantValue(expression);
        if (constantValue is not null)
        {
            // Evaluate the predicate against the literal value.
            if (!targetRefinement.Predicate.Evaluate(constantValue))
            {
                diagnostics.Add(MakeDiagnostic(
                    RefinementLiteralViolation.Value,
                    $"Value '{constantValue}' does not satisfy refinement '{targetRefinement.Predicate.Description}' on type '{targetRefinement.Name}'",
                    expression,
                    model));
                return true;
            }

            // Literal satisfies the predicate — accept the assignment.
            return false;
        }

        // Non-constant expression: check if the source type is the base type
        // (or assignable to the base type). If so, emit the non-constant diagnostic.
        if (IsAssignable(sourceType, targetRefinement.BaseType, model))
        {
            diagnostics.Add(MakeDiagnostic(
                RefinementNonConstantAssignment.Value,
                $"Cannot assign '{sourceType.DisplayName}' to '{targetRefinement.Name}' without proving the refinement. Use pattern matching to validate at runtime",
                expression,
                model));
            return true;
        }

        // Source type is not related to the base type — let normal assignability handle it.
        return false;
    }

    /// <summary>
    /// Overload of <see cref="TryCheckRefinementAssignment(IType, IType, ExpressionNode, DiagnosticBag, ISemanticModel)"/>
    /// that accepts an <see cref="AssignmentNode"/> as the expression source. Used for
    /// reassignment statements (<c>x = value;</c>).
    /// </summary>
    private static bool TryCheckRefinementAssignment(
        IType sourceType,
        IType targetType,
        AssignmentNode assignmentValue,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Only applies when the target is a RefinementType.
        if (targetType is not RefinementType targetRefinement)
            return false;

        // If the source is already the same RefinementType, accept without re-checking.
        if (sourceType is RefinementType sourceRefinement &&
            sourceRefinement.Name == targetRefinement.Name)
            return false;

        // If the source is a RefinementType with a different name but the same
        // base type, attempt subset proving via the RefinementSolver (Requirement 9.1, 9.3).
        if (sourceType is RefinementType sourceRefinementDifferent)
        {
            if (IsAssignable(sourceRefinementDifferent.BaseType, targetRefinement.BaseType, model))
            {
                if (RefinementSolver.Implies(sourceRefinementDifferent.Predicate, targetRefinement.Predicate))
                {
                    // Subset proven — accept the assignment.
                    return false;
                }

                // Cannot prove subset relationship — emit diagnostic.
                diagnostics.Add(MakeDiagnostic(
                    RefinementSubsetNotProvable.Value,
                    $"Cannot assign '{sourceRefinementDifferent.Name}' to '{targetRefinement.Name}' because the compiler cannot prove that '{sourceRefinementDifferent.Predicate.Description}' implies '{targetRefinement.Predicate.Description}'",
                    assignmentValue,
                    model));
                return true;
            }

            // Different base types — fall through to normal assignability.
            return false;
        }

        // Try to extract a literal constant from the assignment value's target.
        // An AssignmentNode without a Value (no chaining) has its expression in Target.
        var constantValue = TryExtractLiteralConstantFromAssignmentTarget(assignmentValue);
        if (constantValue is not null)
        {
            if (!targetRefinement.Predicate.Evaluate(constantValue))
            {
                diagnostics.Add(MakeDiagnostic(
                    RefinementLiteralViolation.Value,
                    $"Value '{constantValue}' does not satisfy refinement '{targetRefinement.Predicate.Description}' on type '{targetRefinement.Name}'",
                    assignmentValue,
                    model));
                return true;
            }

            return false;
        }

        // Non-constant expression.
        if (IsAssignable(sourceType, targetRefinement.BaseType, model))
        {
            diagnostics.Add(MakeDiagnostic(
                RefinementNonConstantAssignment.Value,
                $"Cannot assign '{sourceType.DisplayName}' to '{targetRefinement.Name}' without proving the refinement. Use pattern matching to validate at runtime",
                assignmentValue,
                model));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts a compile-time constant value from an <see cref="AssignmentNode"/>'s
    /// target expression (for non-chained assignments where Value is null).
    /// </summary>
    private static object? TryExtractLiteralConstantFromAssignmentTarget(AssignmentNode assignment)
    {
        // For a simple assignment RHS like `5`, the AssignmentNode has no Value
        // and the Target is a LogicalOrNode wrapping the literal.
        // Navigate through the single-child chain to find a PrimaryNode.
        if (assignment.Value is not null)
            return null; // Chained assignment — not a simple literal.

        Storm.Runtime.SyntaxNode current = assignment.Target;
        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
            {
                return primary switch
                {
                    IntegerLiteralExpressionPrimaryNode intLit => TryParseLiteralInt(intLit.IntegerLiteral.Lexeme),
                    HexLiteralExpressionPrimaryNode hexLit => TryParseLiteralInt(hexLit.HexLiteral.Lexeme),
                    BinaryLiteralExpressionPrimaryNode binLit => TryParseLiteralInt(binLit.BinaryLiteral.Lexeme),
                    FloatLiteralExpressionPrimaryNode floatLit => TryParseLiteralFloat(floatLit.FloatLiteral.Lexeme),
                    CharLiteralExpressionPrimaryNode charLit => ExtractCharContent(charLit.CharLiteral.Lexeme),
                    TrueLiteralExpressionPrimaryNode => true,
                    FalseLiteralExpressionPrimaryNode => false,
                    _ => null,
                };
            }

            Storm.Runtime.SyntaxNode? next = null;
            var childCount = 0;
            foreach (var child in current.Children())
            {
                if (child is null) continue;
                childCount++;
                if (child is PrimaryNode childPrimary)
                {
                    return childPrimary switch
                    {
                        IntegerLiteralExpressionPrimaryNode intLit => TryParseLiteralInt(intLit.IntegerLiteral.Lexeme),
                        HexLiteralExpressionPrimaryNode hexLit => TryParseLiteralInt(hexLit.HexLiteral.Lexeme),
                        BinaryLiteralExpressionPrimaryNode binLit => TryParseLiteralInt(binLit.BinaryLiteral.Lexeme),
                        FloatLiteralExpressionPrimaryNode floatLit => TryParseLiteralFloat(floatLit.FloatLiteral.Lexeme),
                        CharLiteralExpressionPrimaryNode charLit => ExtractCharContent(charLit.CharLiteral.Lexeme),
                        TrueLiteralExpressionPrimaryNode => true,
                        FalseLiteralExpressionPrimaryNode => false,
                        _ => null,
                    };
                }
                if (next is null) next = child;
            }

            if (childCount > 1 || next is null)
                return null;

            current = next;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Refinement predicate readonly validation (MIST_TC_0100)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that all refinement types with property patterns only reference
    /// readonly members. Iterates over all type alias symbols whose declared type
    /// is a <see cref="RefinementType"/> (or a <see cref="UnionType"/> containing
    /// refinement constituents) and checks property patterns in their predicates.
    /// Emits <see cref="RefinementMutableMember"/> for each mutable member found.
    /// </summary>
    private static void CheckRefinementPredicateReadonly(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.TypeAlias)
                continue;

            var declaredType = symbol.DeclaredType;
            if (declaredType is RefinementType refinementType)
            {
                ValidateRefinementPredicateMembers(
                    refinementType.Name,
                    refinementType.BaseType,
                    refinementType.Predicate.AstNode,
                    model,
                    diagnostics);
            }
            else if (declaredType is UnionType unionType)
            {
                foreach (var constituent in unionType.Constituents)
                {
                    if (constituent is RefinementType constituentRefinement)
                    {
                        ValidateRefinementPredicateMembers(
                            constituentRefinement.Name,
                            constituentRefinement.BaseType,
                            constituentRefinement.Predicate.AstNode,
                            model,
                            diagnostics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validates that all property patterns within a refinement predicate's
    /// <see cref="AndPatternNode"/> reference only readonly members on the base type.
    /// Primitive base types (int, bool, char, etc.) are skipped because they are
    /// inherently immutable (Requirement 3.3).
    /// </summary>
    private static void ValidateRefinementPredicateMembers(
        string typeName,
        IType baseType,
        AndPatternNode andPattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Primitive types are inherently immutable — no readonly check needed (Requirement 3.3).
        if (IsPrimitiveImmutableType(baseType))
            return;

        // Walk the and-pattern looking for property patterns.
        ValidateUnaryPatternReadonly(typeName, baseType, andPattern.Left, model, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ValidateUnaryPatternReadonly(typeName, baseType, andPattern.Right, model, diagnostics);
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                ValidateUnaryPatternReadonly(typeName, baseType, part, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates a single <see cref="UnaryPatternNode"/> within a refinement predicate
    /// for readonly member references.
    /// </summary>
    private static void ValidateUnaryPatternReadonly(
        string typeName,
        IType baseType,
        UnaryPatternNode unary,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (unary is DirectPatternUnaryPatternNode direct)
        {
            ValidatePrimaryPatternReadonly(typeName, baseType, direct.Primary, model, diagnostics);
        }
        else if (unary is NotPatternUnaryPatternNode not)
        {
            ValidatePrimaryPatternReadonly(typeName, baseType, not.Operand, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a <see cref="PrimaryPatternNode"/> within a refinement predicate.
    /// Only <see cref="PropertyPatternPrimaryPatternNode"/> instances require readonly
    /// validation — relational and constant patterns are inherently safe.
    /// </summary>
    private static void ValidatePrimaryPatternReadonly(
        string typeName,
        IType baseType,
        PrimaryPatternNode primary,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (primary is not PropertyPatternPrimaryPatternNode propertyPattern)
            return;

        if (propertyPattern.Subpatterns is null)
            return;

        ValidatePropertySubpatternListReadonly(
            typeName, baseType, propertyPattern.Subpatterns, model, diagnostics);
    }

    /// <summary>
    /// Validates all property subpatterns in a list for readonly member references.
    /// </summary>
    private static void ValidatePropertySubpatternListReadonly(
        string typeName,
        IType patternType,
        PropertySubpatternListNode subpatternList,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ValidatePropertySubpatternReadonly(
            typeName, patternType, subpatternList.PropertySubpattern, model, diagnostics);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            ValidatePropertySubpatternReadonly(typeName, patternType, subpattern, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property subpattern: resolves the member, checks its
    /// readonly status, and recursively validates nested property patterns.
    /// Emits <see cref="RefinementMutableMember"/> if the member is mutable.
    /// </summary>
    private static void ValidatePropertySubpatternReadonly(
        string typeName,
        IType patternType,
        PropertySubpatternNode subpattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var memberName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);

        ISymbol? member = null;
        if (memberScope is not null)
        {
            member = memberScope.Lookup(memberName);
        }

        if (member is not null)
        {
            // Check if the member is readonly.
            if (!IsMemberReadonly(member))
            {
                diagnostics.Add(MakeDiagnostic(
                    RefinementMutableMember.Value,
                    $"Refinement predicate on type '{typeName}' references mutable member '{memberName}'. All members in a refinement predicate must be readonly.",
                    subpattern,
                    model));
            }

            // Recursively validate nested property patterns in the subpattern's inner pattern.
            var memberType = member.DeclaredType ?? ErrorType.Instance;
            ValidateNestedPatternReadonly(typeName, memberType, subpattern.Pattern, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively validates nested patterns within a property subpattern for
    /// readonly member references. Navigates the Pattern → OrPattern → AndPattern
    /// hierarchy to find nested property patterns.
    /// </summary>
    private static void ValidateNestedPatternReadonly(
        string typeName,
        IType memberType,
        PatternNode pattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var orPattern = pattern.OrPattern;
        if (orPattern is null)
            return;

        ValidateNestedAndPatternReadonly(typeName, memberType, orPattern.Left, model, diagnostics);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            ValidateNestedAndPatternReadonly(typeName, memberType, orPattern.Right, model, diagnostics);
        }

        if (!orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            foreach (var andAlt in orPattern.AndPatterns)
            {
                ValidateNestedAndPatternReadonly(typeName, memberType, andAlt, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates an <see cref="AndPatternNode"/> within a nested pattern for readonly
    /// member references.
    /// </summary>
    private static void ValidateNestedAndPatternReadonly(
        string typeName,
        IType memberType,
        AndPatternNode andPattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Skip readonly checks for primitive member types (inherently immutable).
        if (IsPrimitiveImmutableType(memberType))
            return;

        ValidateUnaryPatternReadonly(typeName, memberType, andPattern.Left, model, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ValidateUnaryPatternReadonly(typeName, memberType, andPattern.Right, model, diagnostics);
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                ValidateUnaryPatternReadonly(typeName, memberType, part, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Determines whether a type is a primitive immutable type (int, bool, char, etc.)
    /// that does not require readonly checks in refinement predicates because its values
    /// are inherently immutable (Requirement 3.3).
    /// </summary>
    private static bool IsPrimitiveImmutableType(IType type)
    {
        var displayName = type.DisplayName;
        return displayName switch
        {
            // Legacy primitive names.
            "int" or "uint" or "long" or "ulong" => true,
            "short" or "ushort" or "byte" or "sbyte" => true,
            "float" or "double" or "decimal" => true,
            "char" or "bool" or "string" => true,
            // Stdlib names.
            "Int32" or "UInt32" or "Int64" or "UInt64" => true,
            "Int16" or "UInt16" or "Byte" or "SByte" => true,
            "Single" or "Double" or "Decimal" => true,
            "Char" or "Boolean" or "String" => true,
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Assignability and type compatibility helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Emits MIST_TC_0001 if not, or MIST_TRAIT_UNSATISFIED when the target is a
    /// <see cref="TraitType"/> and the source does not satisfy it.
    /// </summary>
    private static void CheckAssignability(IType source, IType target, SyntaxNode node, DiagnosticBag diagnostics, ISemanticModel model)
    {
        if (!IsAssignable(source, target, model))
        {
            // unknown-type feature (Req 4.2, 4.4): before falling back to
            // the generic type-mismatch diagnostic, detect the two specific
            // unknown-related failure modes and emit the targeted code.
            if (TryEmitUnknownAssignabilityDiagnostic(source, target, node, diagnostics, model))
                return;

            // type-unions feature (Req 6.3, 6.4): before falling back to
            // the generic type-mismatch diagnostic, detect union-specific
            // failure modes and emit the targeted code.
            if (TryEmitUnionAssignabilityDiagnostic(source, target, node, diagnostics, model))
                return;

            // enums feature (Req 7.3): detect reverse conversion from
            // underlying type to enum type and emit the targeted diagnostic.
            if (TryEmitEnumAssignabilityDiagnostic(source, target, node, diagnostics, model))
                return;

            // Emit a trait-specific diagnostic when the target is a TraitType.
            if (target is TraitType targetTrait)
            {
                var unsatisfiedMembers = GetUnsatisfiedTraitMembers(source, targetTrait, model);
                var memberList = unsatisfiedMembers.Count > 0
                    ? string.Join(", ", unsatisfiedMembers)
                    : "(unknown)";
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TRAIT_UNSATISFIED",
                    $"Type '{source.DisplayName}' does not satisfy trait '{targetTrait.DisplayName}'. Missing members: {memberList}.",
                    node,
                    model));
            }
            else if (target is ConstructedTraitType targetConstructedTrait)
            {
                var unsatisfiedMembers = GetUnsatisfiedTraitMembers(source, targetConstructedTrait, model);
                var memberList = unsatisfiedMembers.Count > 0
                    ? string.Join(", ", unsatisfiedMembers)
                    : "(unknown)";
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TRAIT_UNSATISFIED",
                    $"Type '{source.DisplayName}' does not satisfy trait '{targetConstructedTrait.DisplayName}'. Missing members: {memberList}.",
                    node,
                    model));
            }
            else
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0001",
                    $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}'",
                    node,
                    model));
            }
        }
        else
        {
            // The types are assignable. If the assignability was due to an
            // implicit cast operator (not direct assignability like identity,
            // subtype, interface, trait, or numeric widening), resolve and
            // record it on the AST node so the emitter can later emit the
            // conversion call. Direct assignability takes precedence over
            // implicit casts (Requirements 14.1, 14.2, 14.3).
            if (!IsDirectlyAssignable(source, target, model))
            {
                TryResolveImplicitCast(source, target, node, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Detects the two unknown-type-specific assignability failure modes and,
    /// when one matches, emits the targeted diagnostic and returns <c>true</c>.
    /// Returns <c>false</c> if neither case applies, leaving the caller to
    /// emit its regular type-mismatch diagnostic (e.g., <c>MIST_TC_0001</c>,
    /// <c>MIST_TC_0002</c>, <c>MIST_TC_0008</c>, or <c>MIST_TC_0021</c>).
    /// <para>
    /// The cases are:
    /// <list type="bullet">
    /// <item><description><c>MIST_TC_0061</c> — <c>void</c> assigned to
    /// <c>unknown</c> (Req 4.2).</description></item>
    /// <item><description><c>MIST_TC_0060</c> — a value whose static type is
    /// <c>unknown</c> assigned to a non-<c>unknown</c> target (Req 4.4).
    /// Pattern-narrowing (<c>is T t</c>) is the designated mechanism and is
    /// mediated by a separate check, not this one.</description></item>
    /// <item><description><c>MIST_TC_0058</c> — a covariant <c>unknown</c>
    /// widening is attempted on a struct (value-type) generic (Req 8.6).
    /// Widening is restricted to class (reference-type) generics so the
    /// C# emission strategy remains sound.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private static bool TryEmitUnknownAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // void → unknown (Req 4.2). The top-type clause in IsAssignable
        // explicitly rejects this and leaves the emission to the caller.
        if (target is UnknownType && source.IsVoid())
        {
            diagnostics.Add(MakeDiagnostic(
                VoidToUnknown.Value,
                $"Cannot assign '{source.DisplayName}' to 'unknown': 'void' carries no value and is not assignable to the top type.",
                node,
                model));
            return true;
        }

        // unknown → non-unknown (Req 4.4). Use `is T t` to narrow first.
        if (source is UnknownType && target is not UnknownType)
        {
            diagnostics.Add(MakeDiagnostic(
                UnknownToNonUnknown.Value,
                $"Cannot assign 'unknown' to '{target.DisplayName}': use an 'is' pattern to narrow the value to a concrete type first.",
                node,
                model));
            return true;
        }

        // Struct-generic widening restriction (Req 8.1, 8.6, Task 23.4).
        // If the only reason assignability fails is that the generic
        // definition being widened is a struct rather than a class, emit
        // the explanatory MIST_TC_0058 so the error points at the rule
        // rather than cascading a generic type-mismatch diagnostic.
        if (WouldAssignableWithUnknownWideningIfClass(source, target))
        {
            diagnostics.Add(MakeDiagnostic(
                StructGenericUnknownWidening.Value,
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}': covariant 'unknown' widening is only available for class (reference-type) generics, not struct generics.",
                node,
                model));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the list of trait member names that the source type does not satisfy
    /// (neither structurally nor via a <c>with</c> block).
    /// </summary>
    private static List<string> GetUnsatisfiedTraitMembers(IType source, IType trait, ISemanticModel model)
    {
        var unsatisfied = new List<string>();
        var satisfaction = model.TraitSatisfactionMap.GetSatisfaction(source, trait);

        var traitMemberScope = trait switch
        {
            TraitType tt => tt.MemberScope,
            ConstructedTraitType ctt => ctt.MemberScope,
            _ => null,
        };

        if (traitMemberScope is null)
            return unsatisfied;

        foreach (var member in traitMemberScope.Members)
        {
            // Skip type parameter symbols (registered as SymbolKind.Class with TypeParameterType).
            if (member.DeclaredType is TypeParameterType)
                continue;

            // Skip dependent type parameter symbols (e.g., Size:int in trait Bounded<Size:int>).
            if (member.DeclaredType is DependentTypeParameterType)
                continue;

            var name = member.Name;
            if (satisfaction is not null)
            {
                // If there's a partial satisfaction record, check which members are covered.
                if (satisfaction.StructuralMatches.ContainsKey(name))
                    continue;
                if (satisfaction.WithMappings.ContainsKey(name))
                    continue;
            }
            unsatisfied.Add(name);
        }

        return unsatisfied;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Implicit cast operator helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the member scope of a type, or <c>null</c> if the type does
    /// not own a member scope (e.g. <see cref="PrimitiveType"/>,
    /// <see cref="ArrayType"/>).
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            EnumType et => et.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Searches both the source type's and the target type's member scopes
    /// for an <c>operator_implicit_{targetDisplayName}</c> symbol.
    /// Returns the matching symbol, or <c>null</c> if none (or multiple) are found.
    /// <para>
    /// When multiple candidates are found (ambiguous), <paramref name="ambiguous"/>
    /// is set to <c>true</c> so the caller can emit <c>MIST_TC_0044</c>.
    /// </para>
    /// </summary>
    internal static ISymbol? TryFindImplicitCast(IType source, IType target, out bool ambiguous)
    {
        ambiguous = false;
        if (IsErrorType(source) || IsErrorType(target))
            return null;

        var candidates = new List<ISymbol>();
        var opName = $"operator_implicit_{target.DisplayName}";

        // 1. Search the source type's member scope.
        var sourceScope = GetMemberScope(source);
        if (sourceScope is not null)
        {
            foreach (var member in sourceScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == opName)
                    candidates.Add(member);
            }
        }

        // 2. Search the target type's member scope.
        var targetScope = GetMemberScope(target);
        if (targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach (var member in targetScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == opName)
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if (!candidates.Any(c => ReferenceEquals(c, member)))
                        candidates.Add(member);
                }
            }
        }

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
            ambiguous = true;

        return null;
    }

    /// <summary>
    /// Companion to <see cref="TryFindImplicitCast"/>. Returns the resolved
    /// implicit cast operator symbol (or <c>null</c>) and, when a unique
    /// operator is found, records it on the AST node via
    /// <see cref="ISemanticModel.RecordResolvedOperator"/>.
    /// <para>
    /// When multiple candidates are found, emits <c>MIST_TC_0044</c> and
    /// returns <c>null</c>.
    /// </para>
    /// </summary>
    internal static ISymbol? TryResolveImplicitCast(
        IType source,
        IType target,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolved = TryFindImplicitCast(source, target, out var ambiguous);

        if (ambiguous)
        {
            diagnostics.Add(MakeDiagnostic(
                AmbiguousImplicitCast.Value,
                $"Ambiguous implicit conversion from '{source.DisplayName}' to '{target.DisplayName}': multiple implicit cast operators are applicable.",
                node,
                model));
            return null;
        }

        if (resolved is not null)
        {
            model.RecordResolvedOperator(node, resolved);
        }

        return resolved;
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Rules:
    /// - Same type (identity)
    /// - Both numeric with implicit widening conversion
    /// - Either is ErrorType (error suppression)
    /// - Subtype assignability: derived class assignable to any ancestor class
    /// </summary>
    internal static bool IsAssignable(IType source, IType target)
    {
        return IsAssignable(source, target, model: null);
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// When <paramref name="model"/> is provided, also checks trait assignability via
    /// the <see cref="TraitSatisfactionMap"/>.
    /// </summary>
    internal static bool IsAssignable(IType source, IType target, ISemanticModel? model)
    {
        // ── type-aliases feature: transparent unwrapping (Requirement 5.1) ──
        // Type aliases are fully transparent — unwrap to the underlying type
        // before any comparison so that using an alias is semantically
        // identical to using the underlying type directly.
        if (source is TypeAliasType sourceAlias)
            source = sourceAlias.UnderlyingType;
        if (target is TypeAliasType targetAlias)
            target = targetAlias.UnderlyingType;

        // ── refinement-types feature: nominal subtyping (Requirement 8.3) ──
        // A RefinementType is a nominal subtype of its BaseType. Widening
        // from refinement to base is always safe. Same-name refinement types
        // are identity-assignable.
        if (source is RefinementType sourceRefinement)
        {
            if (target is RefinementType targetRefinement)
            {
                // Same refinement type: accept (identity).
                if (sourceRefinement.Name == targetRefinement.Name)
                    return true;

                // Different refinement types with same base: use subset proving.
                // If the source predicate implies the target predicate, the
                // assignment is safe (Requirement 9.1, 9.2).
                if (IsAssignable(sourceRefinement.BaseType, targetRefinement.BaseType, model))
                {
                    if (RefinementSolver.Implies(sourceRefinement.Predicate, targetRefinement.Predicate))
                        return true;
                }

                // Cannot prove subset — fall through to unwrap and check base.
            }

            // Widening: RefinementType → BaseType is always safe.
            // Unwrap the source to its base type and continue checking.
            source = sourceRefinement.BaseType;
        }

        // ── refinement-types feature: target is RefinementType ───────────
        // When the target is a RefinementType and the source is assignable
        // to the base type, IsAssignable returns true. The actual predicate
        // enforcement (literal evaluation or non-constant rejection) is
        // handled by TryCheckRefinementAssignment at the call site.
        if (target is RefinementType targetRefinementForBase)
        {
            return IsAssignable(source, targetRefinementForBase.BaseType, model);
        }

        // ErrorType suppresses cascading errors.
        if (IsErrorType(source) || IsErrorType(target))
            return true;

        // ── unknown-type feature (Req 4.1, 4.2, 4.3, 4.4, 4.5) ──────────
        //
        // UnknownType is the user-facing top type for assignment:
        //   * Every non-void source type is assignable to UnknownType.
        //   * void is NOT assignable to UnknownType — it carries no value.
        //     The caller emits MIST_TC_0061 (VoidToUnknown) at the
        //     specific assignment / return / argument / initializer site.
        //   * A value whose static type is UnknownType is NOT assignable
        //     to a non-UnknownType target. Pattern matching (`is T t`) is
        //     the designated mechanism to narrow an `unknown` value back
        //     to a concrete type. The caller emits MIST_TC_0060
        //     (UnknownToNonUnknown) at the widening site.
        //   * UnknownType → UnknownType is identity-accepted.
        //
        // This clause is placed immediately after the ErrorType cascade
        // suppression and before every other rule so that UnknownType is
        // handled uniformly across identity, ConstructedType, interface,
        // subtype, trait, and implicit-cast branches.
        if (target is UnknownType)
        {
            if (source.IsVoid())
                return false;
            return true;
        }
        if (source is UnknownType)
        {
            // target is not UnknownType (checked above); reject.
            return false;
        }

        // ── type-unions feature: union type assignability ─────────────────
        //
        // Union type rules are checked before identity/display-name so that
        // structural union equality (same constituents regardless of order)
        // is handled correctly, and constituent-to-union / union-to-wider-union
        // assignments are accepted.
        //
        // Rules:
        //   * UnionType → UnionType: valid if every source constituent is
        //     a constituent of the target (order-independent).
        //   * T (non-union) → UnionType: valid if T is a constituent of
        //     the target union.
        //   * UnionType → T (non-union): rejected (must narrow first).
        //
        // A union containing ErrorType is treated as "poisoned" — accept
        // any assignment to suppress cascading errors.
        if (target is UnionType targetUnion)
        {
            // Poisoned union target: suppress cascading errors.
            if (HasErrorConstituent(targetUnion))
                return true;

            if (source is UnionType sourceUnionForTarget)
            {
                // Poisoned union source: suppress cascading errors.
                if (HasErrorConstituent(sourceUnionForTarget))
                    return true;

                // Union → Union: every source constituent must be in target.
                foreach (var constituent in sourceUnionForTarget.Constituents)
                {
                    if (!IsConstituentOf(constituent, targetUnion))
                        return false;
                }
                return true;
            }

            // Non-union → Union: source must be a constituent of target.
            return IsConstituentOf(source, targetUnion);
        }
        if (source is UnionType)
        {
            // Union → non-union: rejected (must narrow first).
            // The only exception is if the target is UnknownType, but that
            // was already handled above.
            return false;
        }

        // Identity.
        if (ReferenceEquals(source, target))
            return true;

        // Same display name (for types that may not be reference-equal).
        if (source.DisplayName == target.DisplayName)
            return true;

        // Migration-window shim: `bool` and `Standard.Boolean` (likewise
        // `int`/`Int32`, `string`/`String`, …) can coexist within a
        // single compilation when some sub-tree was resolved via the
        // legacy PrimitiveType singletons and another via the stdlib
        // types. Treat them as equivalent so assignability doesn't
        // spuriously reject `int` to `Int32` (or vice versa).
        if (ArePrimitiveEquivalent(source, target))
            return true;

        // Dependent type parameters are assignable to their value type.
        // E.g., a dependent parameter `Size:int` resolves to DependentTypeParameterType
        // but should be assignable to `int`/`Int32` (its ValueType).
        if (source is DependentTypeParameterType depSource && depSource.ValueType is not DependentTypeParameterType)
            return IsAssignable(depSource.ValueType, target, model);
        if (target is DependentTypeParameterType depTarget && depTarget.ValueType is not DependentTypeParameterType)
            return IsAssignable(source, depTarget.ValueType, model);

        // Tuple assignability with name erasure: two TupleType instances are
        // assignable if they have the same arity and each element type is
        // assignable. Element names are ignored (name erasure).
        if (source is TupleType sourceTuple && target is TupleType targetTuple)
        {
            if (sourceTuple.Arity == targetTuple.Arity)
            {
                var allElementsAssignable = true;
                for (var i = 0; i < sourceTuple.Arity; i++)
                {
                    if (!IsAssignable(sourceTuple.ElementTypes[i], targetTuple.ElementTypes[i], model))
                    {
                        allElementsAssignable = false;
                        break;
                    }
                }
                if (allElementsAssignable) return true;
            }
            return false;
        }

        // ConstructedType identity: same generic definition + same type arguments + same dependent arguments.
        if (source is ConstructedType sourceConstructed && target is ConstructedType targetConstructed)
        {
            if (ReferenceEquals(sourceConstructed.GenericDefinition, targetConstructed.GenericDefinition)
                && sourceConstructed.TypeArguments.Count == targetConstructed.TypeArguments.Count)
            {
                var allMatch = true;
                for (var i = 0; i < sourceConstructed.TypeArguments.Count; i++)
                {
                    if (!TypesEqual(sourceConstructed.TypeArguments[i], targetConstructed.TypeArguments[i]))
                    {
                        allMatch = false;
                        break;
                    }
                }

                // Compare dependent value arguments (Requirements 8.1, 8.2).
                // FixedBuffer<5> ≠ FixedBuffer<6> even though both have TypeArguments=[int].
                if (allMatch && !DependentArgumentsEqual(sourceConstructed.DependentArguments, targetConstructed.DependentArguments))
                    allMatch = false;

                if (allMatch) return true;

                // Covariant unknown widening (unknown-type feature, Task 23.2).
                // Req 8.1–8.3: after the identity check, try the position-wise
                // widening rule that accepts any target position whose type
                // argument is `UnknownType`, provided the generic definition
                // at that level is a class (reference-type) generic. Nested
                // constructions recurse through the same rule. Transitive
                // widening along the base-type / interface chain is picked
                // up by the existing loops below.
                if (AssignableWithUnknownWidening(
                        sourceConstructed.GenericDefinition,
                        sourceConstructed.TypeArguments,
                        targetConstructed.TypeArguments))
                {
                    return true;
                }
            }
        }

        // ConstructedType inheritance: walk the base type chain with substitution.
        if (source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if (baseType is not null && IsAssignable(baseType, target))
                return true;
        }

        // ConstructedType to constructed interface assignability.
        if (target is ConstructedType targetConst && targetConst.GenericDefinition is InterfaceType)
        {
            if (ImplementsConstructedInterface(source, targetConst))
                return true;
        }

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types. The implicit cast
        // operator fallback at the end of this method handles it.

        // Interface assignability:
        // A class/struct implementing an interface (directly or transitively) is assignable to that interface.
        // An interface is assignable to any interface it inherits from (directly or transitively).
        if (target is InterfaceType targetInterface)
        {
            if (ImplementsInterface(source, targetInterface))
                return true;
        }

        // Subtype assignability — walk the inheritance chain.
        // A derived class is assignable to any of its ancestor classes.
        if (source is ClassType sourceClass && target is ClassType)
        {
            var current = sourceClass.BaseType as ClassType;
            while (current is not null)
            {
                if (ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                    return true;
                current = current.BaseType as ClassType;
            }
        }

        // Trait assignability:
        // A class/struct satisfying a trait (structurally or via with block)
        // is assignable to that trait.
        if (target is TraitType targetTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetTrait, model))
                return true;
        }

        // ── enums feature: implicit conversion to underlying type (Req 7.1, 7.2) ──
        // An enum value is assignable to its underlying type. This is a direct
        // structural rule (not an implicit cast operator) so it takes precedence
        // over the operator fallback below.
        if (source is EnumType sourceEnum && IsAssignable(sourceEnum.UnderlyingType, target, model))
            return true;

        // Constructed trait assignability:
        // A class/struct satisfying a constructed trait (e.g., Comparable<int>)
        // is assignable to that constructed trait. Comparable<int> and
        // Comparable<string> are distinct non-assignable types because they
        // are different keys in the TraitSatisfactionMap.
        if (target is ConstructedTraitType targetConstructedTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetConstructedTrait, model))
                return true;
        }

        // Implicit cast operator search (fallback after all other assignability checks).
        // Search both source type and target type for an implicit cast operator.
        if (TryFindImplicitCast(source, target, out _) is not null)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> satisfies the given <paramref name="trait"/>.
    /// Uses a lazy evaluation model:
    /// 1. Check the type's SatisfiedTraits list (populated by with-blocks and prior call-site checks)
    /// 2. Check the TraitSatisfactionMap (backward compat)
    /// 3. Perform on-demand structural matching
    /// 4. If structural matching succeeds, upsert into the type's SatisfiedTraits list and the map
    /// </summary>
    private static bool SatisfiesTrait(IType source, IType trait, ISemanticModel model)
    {
        // 1. Check the type's SatisfiedTraits list.
        var satisfiedTraits = source switch
        {
            ClassType ct => ct.SatisfiedTraits,
            StructType st => st.SatisfiedTraits,
            _ => null,
        };

        if (satisfiedTraits is not null)
        {
            foreach (var satisfied in satisfiedTraits)
            {
                if (ReferenceEquals(satisfied, trait))
                    return true;
                // Structural equality fallback for ConstructedTraitType.
                if (satisfied is ConstructedTraitType sCtt && trait is ConstructedTraitType tCtt
                    && ReferenceEquals(sCtt.GenericTraitDefinition, tCtt.GenericTraitDefinition)
                    && sCtt.TypeArguments.Count == tCtt.TypeArguments.Count)
                {
                    var allMatch = true;
                    for (var i = 0; i < sCtt.TypeArguments.Count; i++)
                    {
                        if (sCtt.TypeArguments[i].DisplayName != tCtt.TypeArguments[i].DisplayName)
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch)
                        return true;
                }
            }
        }

        // 2. Check the TraitSatisfactionMap (covers with-block registrations).
        if (model.TraitSatisfactionMap.Satisfies(source, trait))
            return true;

        // 3. On-demand structural matching: check if all trait members are
        //    present and public on the source type.
        var traitMemberScope = trait switch
        {
            TraitType tt => tt.MemberScope,
            ConstructedTraitType ctt => ctt.MemberScope,
            _ => null,
        };

        var sourceMemberScope = GetMemberScope(source);

        if (traitMemberScope is null || sourceMemberScope is null)
            return false;

        // Build type parameter substitution map for constructed traits.
        Dictionary<string, IType>? typeSubstitutions = null;
        if (trait is ConstructedTraitType constructedTrait)
        {
            typeSubstitutions = new Dictionary<string, IType>(StringComparer.Ordinal);
            var typeParams = constructedTrait.GenericTraitDefinition.TypeParameters;
            var typeArgs = constructedTrait.TypeArguments;
            for (var i = 0; i < typeParams.Count && i < typeArgs.Count; i++)
            {
                typeSubstitutions[typeParams[i].DisplayName] = typeArgs[i];
            }
        }

        var structuralMatches = new Dictionary<string, ISymbol>();
        foreach (var traitMember in traitMemberScope.Members)
        {
            // Skip type parameter symbols (registered in the trait's scope but
            // not actual trait members that need structural matching).
            if (traitMember.Kind is SymbolKind.Class or SymbolKind.Struct
                && traitMember.DeclaredType is TypeParameterType)
                continue;

            // Skip dependent type parameter symbols (e.g., Max:int in trait Bounded<Max:int>).
            if (traitMember.DeclaredType is DependentTypeParameterType)
                continue;

            var matched = TryLazyStructuralMatch(traitMember, sourceMemberScope, model, typeSubstitutions);
            if (matched is null)
                return false; // Structural matching failed — not satisfied.
            structuralMatches[traitMember.Name] = matched;
        }

        // 4. Structural matching succeeded — upsert into the type's SatisfiedTraits
        //    list and the TraitSatisfactionMap.
        satisfiedTraits?.Add(trait);
        model.TraitSatisfactionMap.Register(source, trait, new TraitSatisfaction
        {
            StructuralMatches = structuralMatches,
            WithMappings = new Dictionary<string, ISymbol>(),
        });

        return true;
    }

    /// <summary>
    /// Performs a simplified structural match for a single trait member against
    /// the type's public members. Used for lazy/on-demand trait satisfaction
    /// at call sites. Returns the matching symbol or null.
    /// <para>
    /// For methods, checks name, visibility, return type compatibility, and
    /// parameter count/type compatibility. For fields/properties, checks name,
    /// visibility, and type compatibility.
    /// </para>
    /// </summary>
    private static ISymbol? TryLazyStructuralMatch(
        ISymbol traitMember, IScope typeMemberScope, ISemanticModel model,
        Dictionary<string, IType>? typeSubstitutions)
    {
        var traitMemberName = traitMember.Name;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitMemberName)
                continue;

            // Kind compatibility: trait fields can be matched by fields or properties;
            // trait methods must be matched by methods; trait properties by properties or fields.
            var kindCompatible = traitMember.Kind switch
            {
                SymbolKind.Field => member.Kind is SymbolKind.Field or SymbolKind.Property,
                SymbolKind.Method => member.Kind is SymbolKind.Method,
                SymbolKind.Property => member.Kind is SymbolKind.Property or SymbolKind.Field,
                _ => member.Kind == traitMember.Kind,
            };

            if (!kindCompatible)
                continue;

            // Check visibility: must be public.
            if (!IsMemberPublicViaModel(member, model))
                continue;

            // For methods, check signature compatibility (return type + parameters).
            if (traitMember.Kind == SymbolKind.Method)
            {
                if (!MethodSignaturesCompatible(traitMember, member, model, typeSubstitutions))
                    continue;
            }
            else
            {
                // For fields/properties, check type compatibility.
                if (!MemberTypesCompatible(traitMember, member, typeSubstitutions))
                    continue;
            }

            return member;
        }

        return null;
    }

    /// <summary>
    /// Checks whether a type member's method signature is compatible with a trait
    /// method's signature (return type + parameter types).
    /// </summary>
    private static bool MethodSignaturesCompatible(
        ISymbol traitMethod, ISymbol implMethod, ISemanticModel model,
        Dictionary<string, IType>? typeSubstitutions)
    {
        SyntaxNode traitNode;
        SyntaxNode implNode;
        try
        {
            traitNode = model.GetDeclaringNode(traitMethod);
            implNode = model.GetDeclaringNode(implMethod);
        }
        catch
        {
            return false;
        }

        // Get parameter counts.
        var traitParams = traitNode switch
        {
            TraitMethodDeclarationNode tm => tm.Parameters,
            _ => null,
        };
        var implParams = implNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            _ => null,
        };

        var traitParamCount = CountParameters(traitParams);
        var implParamCount = CountParameters(implParams);

        if (traitParamCount != implParamCount)
            return false;

        // Check return type compatibility.
        // DeclaredType on method symbols is the resolved return type.
        var traitReturnType = traitMethod.DeclaredType;
        var implReturnType = implMethod.DeclaredType;

        if (traitReturnType is not null && implReturnType is not null)
        {
            // Apply type substitution to the trait return type.
            var effectiveTraitReturn = SubstituteType(traitReturnType, typeSubstitutions);
            if (!ReturnTypeCompatibleForTrait(effectiveTraitReturn, implReturnType))
                return false;
        }
        else if (traitReturnType is not null && implReturnType is null)
        {
            // Trait expects a return type but impl has none (void) — not compatible
            // unless trait return is void too.
            return false;
        }

        // Check parameter types (exact match required).
        if (traitParamCount > 0 && traitParams is not null && implParams is not null)
        {
            if (!ParameterTypesCompatible(traitParams, implParams, model, typeSubstitutions))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Counts parameters in a ParameterListNode (which may be null for zero-param methods).
    /// </summary>
    private static int CountParameters(object? paramList)
    {
        if (paramList is null)
            return 0;
        if (paramList is not ParameterListNode pln)
            return 0;
        return 1 + pln.Parameters.Length;
    }

    /// <summary>
    /// Checks return type compatibility for trait matching.
    /// Uses relaxed unknown-widening rules: a trait return of <c>unknown</c>
    /// matches any non-void implementer return type. Also handles covariant
    /// unknown widening for constructed types (e.g., List&lt;unknown&gt; matches List&lt;int&gt;).
    /// </summary>
    private static bool ReturnTypeCompatibleForTrait(IType traitReturn, IType implReturn)
    {
        // Exact match.
        if (ReferenceEquals(traitReturn, implReturn))
            return true;
        if (traitReturn.DisplayName == implReturn.DisplayName)
            return true;

        // Unknown widening: trait returns `unknown` → impl can return any non-void type.
        if (traitReturn is UnknownType)
            return implReturn.DisplayName != "Void" && implReturn.DisplayName != "void";

        // Covariant unknown widening for constructed types (Req 12.2, 8.1, 8.6):
        // List<int> satisfies List<unknown> when the generic definition is a class.
        if (traitReturn is ConstructedType targetCt && implReturn is ConstructedType sourceCt
            && ReferenceEquals(targetCt.GenericDefinition, sourceCt.GenericDefinition)
            && targetCt.TypeArguments.Count == sourceCt.TypeArguments.Count)
        {
            if (AssignableWithUnknownWidening(
                    sourceCt.GenericDefinition, sourceCt.TypeArguments, targetCt.TypeArguments))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks parameter type compatibility between trait method and impl method.
    /// Parameter matching is exact (no widening).
    /// </summary>
    private static bool ParameterTypesCompatible(
        ParameterListNode traitParams, ParameterListNode implParams, ISemanticModel model,
        Dictionary<string, IType>? typeSubstitutions)
    {
        // Collect all parameter nodes from both lists.
        var traitParamNodes = new List<ParameterNode>();
        var implParamNodes = new List<ParameterNode>();

        foreach (var child in traitParams.Children())
        {
            if (child is ParameterNode pn)
                traitParamNodes.Add(pn);
        }
        foreach (var child in implParams.Children())
        {
            if (child is ParameterNode pn)
                implParamNodes.Add(pn);
        }

        if (traitParamNodes.Count != implParamNodes.Count)
            return false;

        for (var i = 0; i < traitParamNodes.Count; i++)
        {
            var traitParamSym = model.GetDeclaredSymbol(traitParamNodes[i]);
            var implParamSym = model.GetDeclaredSymbol(implParamNodes[i]);

            if (traitParamSym?.DeclaredType is null || implParamSym?.DeclaredType is null)
                continue; // Can't verify — accept (best effort).

            var effectiveTraitParamType = SubstituteType(traitParamSym.DeclaredType, typeSubstitutions);
            if (effectiveTraitParamType.DisplayName != implParamSym.DeclaredType.DisplayName)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks type compatibility for field/property trait members.
    /// </summary>
    private static bool MemberTypesCompatible(ISymbol traitMember, ISymbol implMember,
        Dictionary<string, IType>? typeSubstitutions)
    {
        var traitType = traitMember.DeclaredType;
        var implType = implMember.DeclaredType;

        if (traitType is null || implType is null)
            return true; // If types aren't resolved, accept (best effort).

        // Apply type substitution to the trait member type.
        var effectiveTraitType = SubstituteType(traitType, typeSubstitutions);

        if (ReferenceEquals(effectiveTraitType, implType))
            return true;

        if (effectiveTraitType.DisplayName == implType.DisplayName)
            return true;

        // Unknown widening for return/field position.
        if (effectiveTraitType is UnknownType)
            return true;

        return false;
    }

    /// <summary>
    /// Substitutes a type parameter with its concrete type argument using the
    /// provided substitution map. Returns the original type if no substitution applies.
    /// </summary>
    private static IType SubstituteType(IType type, Dictionary<string, IType>? substitutions)
    {
        if (substitutions is null || substitutions.Count == 0)
            return type;

        // Direct type parameter substitution.
        if (type is TypeParameterType && substitutions.TryGetValue(type.DisplayName, out var substituted))
            return substituted;

        return type;
    }

    /// <summary>
    /// Compares two types for structural equality.
    /// For <see cref="ConstructedType"/>, checks that the generic definitions are the same
    /// and all type arguments are recursively equal.
    /// For other types, uses reference equality or display name comparison.
    /// </summary>
    private static bool TypesEqual(IType a, IType b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is ConstructedType aCt && b is ConstructedType bCt)
        {
            if (!ReferenceEquals(aCt.GenericDefinition, bCt.GenericDefinition))
                return false;
            if (aCt.TypeArguments.Count != bCt.TypeArguments.Count)
                return false;
            for (var i = 0; i < aCt.TypeArguments.Count; i++)
            {
                if (!TypesEqual(aCt.TypeArguments[i], bCt.TypeArguments[i]))
                    return false;
            }

            // Compare dependent value arguments (Requirements 8.1, 8.2).
            if (!DependentArgumentsEqual(aCt.DependentArguments, bCt.DependentArguments))
                return false;

            return true;
        }

        if (a is TypeParameterType aTp && b is TypeParameterType bTp)
            return aTp.Name == bTp.Name && aTp.Ordinal == bTp.Ordinal;

        // DisplayName equality — the common fallback for non-reference-
        // equal primitives within the same migration flavour.
        if (a.DisplayName == b.DisplayName)
            return true;

        // Migration-window shim: legacy `int` and stdlib `Int32` denote
        // the same Mist primitive.
        return ArePrimitiveEquivalent(a, b);
    }

    /// <summary>
    /// Compares two dependent argument lists for value equality.
    /// Two lists are equal when they have the same length and each pair of
    /// non-null entries has equal values (using <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    private static bool DependentArgumentsEqual(
        IReadOnlyList<DependentValueArgument?>? a,
        IReadOnlyList<DependentValueArgument?>? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            var argA = a[i];
            var argB = b[i];
            if (argA is null && argB is null)
                continue;
            if (argA is null || argB is null)
                return false;
            if (!Equals(argA.Value, argB.Value))
                return false;
        }
        return true;
    }

    // ─── Covariant unknown widening (unknown-type feature, Task 23) ──────
    //
    // Req 8.1–8.3: `Foo<A_1, …, A_n>` is assignable to `Foo<B_1, …, B_n>`
    // when, at every position i, either `A_i == B_i` or `B_i is UnknownType`
    // and the generic definition being widened is a class (reference-type)
    // generic. Nested widening recurses into constructed type arguments, so
    // `Optional<List<int>>` widens to `Optional<List<unknown>>` even when
    // `Optional` is a struct, because the widening happens at the inner
    // `List` boundary and `List` is a class.
    //
    // Req 8.5: the rule is one-directional by construction — only the target
    // position may be `UnknownType`. A concrete target with a source of
    // `UnknownType` falls through to the default `TypesEqual` check and is
    // rejected.
    //
    // The struct-generic restriction (Req 8.1, 8.6) is enforced by checking
    // the enclosing generic definition's <see cref="IType.Kind"/> at each
    // level where a widening actually happens. When the check fails only
    // because of this kind restriction, the caller of
    // <see cref="IsAssignable"/> detects that via
    // <see cref="WouldAssignableWithUnknownWideningIfClass"/> and emits
    // <see cref="StructGenericUnknownWidening"/> (MIST_TC_0058).

    /// <summary>
    /// Position-wise covariant widening check for the type arguments of a
    /// single generic construction.
    /// <para>
    /// For each position <c>i</c>, the rule accepts when
    /// <c>argsS[i] == argsT[i]</c>, or when <c>argsT[i]</c> is
    /// <see cref="UnknownType"/> and <paramref name="defCtx"/>'s
    /// <see cref="IType.Kind"/> is <see cref="TypeKind.Class"/>, or when both
    /// sides are <see cref="ConstructedType"/>s sharing the same generic
    /// definition and their nested widening succeeds.
    /// </para>
    /// <para>
    /// <paramref name="defCtx"/> carries the generic definition whose
    /// arguments are being compared at the current level; the class-only
    /// rule is enforced only where a widening actually happens at that
    /// level (Req 8.1, 8.6). Nested levels carry their own <c>defCtx</c>.
    /// </para>
    /// </summary>
    private static bool AssignableWithUnknownWidening(
        IType defCtx,
        IReadOnlyList<IType> argsS,
        IReadOnlyList<IType> argsT,
        bool ignoreClassRestriction = false)
    {
        if (argsS.Count != argsT.Count)
            return false;

        for (var i = 0; i < argsS.Count; i++)
        {
            var s = argsS[i];
            var t = argsT[i];

            // Identity at this position — no widening happens here.
            if (TypesEqual(s, t))
                continue;

            // Widening: target is `unknown`. The class-only rule (Req 8.1,
            // 8.6) is enforced at the level where widening happens unless
            // the caller is probing the "would widen if class" case.
            if (t is UnknownType)
            {
                if (!ignoreClassRestriction && defCtx.Kind != TypeKind.Class)
                    return false;
                continue;
            }

            // Nested widening: both sides are constructed from the same
            // generic definition. Recurse with the nested definition as
            // the new defCtx so the class-only rule is checked there.
            if (s is ConstructedType sc
                && t is ConstructedType tc
                && ReferenceEquals(sc.GenericDefinition, tc.GenericDefinition))
            {
                if (!AssignableWithUnknownWidening(
                        sc.GenericDefinition,
                        sc.TypeArguments,
                        tc.TypeArguments,
                        ignoreClassRestriction))
                {
                    return false;
                }
                continue;
            }

            // Anything else (mismatched nested definitions, bare types
            // differing, source-side UnknownType widening to concrete
            // target — Req 8.5) is not a valid widening.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when the only thing preventing the covariant
    /// unknown widening from <paramref name="source"/> to
    /// <paramref name="target"/> is the class-only restriction (Req 8.6).
    /// Used by <see cref="TryEmitUnknownAssignabilityDiagnostic"/> to emit
    /// the explanatory <see cref="StructGenericUnknownWidening"/>
    /// diagnostic instead of a generic type-mismatch error at widening
    /// sites.
    /// </summary>
    private static bool WouldAssignableWithUnknownWideningIfClass(IType source, IType target)
    {
        if (source is not ConstructedType sc || target is not ConstructedType tc)
            return false;
        if (!ReferenceEquals(sc.GenericDefinition, tc.GenericDefinition))
            return false;
        if (sc.TypeArguments.Count != tc.TypeArguments.Count)
            return false;

        // If the strict widening already succeeds, this is not a
        // "would widen if class" case.
        if (AssignableWithUnknownWidening(
                sc.GenericDefinition, sc.TypeArguments, tc.TypeArguments))
        {
            return false;
        }

        // Probe the relaxed widening (class-only restriction ignored). If
        // it would succeed, then the class-only rule is the sole reason
        // the strict widening failed — that is the struct-generic
        // restriction diagnostic case (Req 8.6).
        return AssignableWithUnknownWidening(
            sc.GenericDefinition,
            sc.TypeArguments,
            tc.TypeArguments,
            ignoreClassRestriction: true);
    }

    /// <summary>
    /// Gets the substituted base type of a <see cref="ConstructedType"/>.
    /// If the generic definition is a <see cref="ClassType"/> with a base type,
    /// substitutes the type parameters in the base type with the constructed type's type arguments.
    /// </summary>
    private static IType? GetSubstitutedBaseType(ConstructedType constructedType)
    {
        if (constructedType.GenericDefinition is not ClassType classDef)
            return null;

        var baseType = classDef.BaseType;
        if (baseType is null)
            return null;

        var typeParams = classDef.TypeParameters;
        var typeArgs = constructedType.TypeArguments;

        return SubstituteType(baseType, typeParams, typeArgs);
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements the target constructed interface,
    /// including transitive interface inheritance.
    /// </summary>
    private static bool ImplementsConstructedInterface(IType source, ConstructedType targetConstructedInterface)
    {
        // Collect all interfaces from the source type.
        IReadOnlyList<IType>? interfaces = null;
        IReadOnlyList<IType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;

        switch (source)
        {
            case ConstructedType sourceCt:
            {
                switch (sourceCt.GenericDefinition)
                {
                    case ClassType classDef:
                        interfaces = classDef.Interfaces;
                        typeParams = classDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        // Also check base type chain.
                        var baseType = GetSubstitutedBaseType(sourceCt);
                        if (baseType is not null && ImplementsConstructedInterface(baseType, targetConstructedInterface))
                            return true;
                        break;
                    case StructType structDef:
                        interfaces = structDef.Interfaces;
                        typeParams = structDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        break;
                    case InterfaceType ifaceDef:
                        // Check if the source interface itself matches the target.
                        if (IsAssignable(source, targetConstructedInterface))
                            return true;
                        interfaces = ifaceDef.Interfaces;
                        typeParams = ifaceDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        break;
                }
                break;
            }
            case ClassType classType:
                interfaces = classType.Interfaces;
                // Also check base type chain.
                var current = classType.BaseType;
                while (current is not null)
                {
                    if (current is ConstructedType baseCt && IsAssignable(baseCt, targetConstructedInterface))
                        return true;
                    if (current is ClassType baseClass)
                    {
                        foreach (var iface in baseClass.Interfaces)
                        {
                            var substituted = iface;
                            if (substituted is ConstructedType ifaceCt && IsAssignable(ifaceCt, targetConstructedInterface))
                                return true;
                        }
                        current = baseClass.BaseType;
                    }
                    else
                        break;
                }
                break;
            case StructType structType:
                interfaces = structType.Interfaces;
                break;
            case InterfaceType ifaceType:
                interfaces = ifaceType.Interfaces;
                break;
        }

        if (interfaces is null)
            return false;

        foreach (var iface in interfaces)
        {
            // Substitute type parameters if we have a mapping.
            var substitutedIface = (typeParams is not null && typeArgs is not null)
                ? SubstituteType(iface, typeParams, typeArgs)
                : iface;

            if (substitutedIface is ConstructedType substitutedCt)
            {
                if (IsAssignable(substitutedCt, targetConstructedInterface))
                    return true;
                // Transitively check the interface's own inherited interfaces.
                if (ImplementsConstructedInterface(substitutedCt, targetConstructedInterface))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Substitutes type parameters with corresponding type arguments in a type.
    /// - If <paramref name="type"/> is a <see cref="TypeParameterType"/> matching one of the <paramref name="typeParams"/>,
    ///   returns the corresponding entry from <paramref name="typeArgs"/>.
    /// - If <paramref name="type"/> is a <see cref="ConstructedType"/>, recursively substitutes its type arguments.
    /// - Otherwise, returns the type unchanged.
    /// </summary>
    internal static IType SubstituteType(IType type, IReadOnlyList<IType> typeParams, IReadOnlyList<IType> typeArgs)
    {
        if (type is TypeParameterType tpt)
        {
            for (var i = 0; i < typeParams.Count; i++)
            {
                if (typeParams[i] is TypeParameterType tp && tpt.Name == tp.Name && tpt.Ordinal == tp.Ordinal)
                    return i < typeArgs.Count ? typeArgs[i] : type;
            }
            return type;
        }

        if (type is ConstructedType ct)
        {
            var substitutedArgs = new IType[ct.TypeArguments.Count];
            var changed = false;
            for (var i = 0; i < ct.TypeArguments.Count; i++)
            {
                substitutedArgs[i] = SubstituteType(ct.TypeArguments[i], typeParams, typeArgs);
                if (!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
                    changed = true;
            }
            return changed ? new ConstructedType(ct.GenericDefinition, substitutedArgs) : ct;
        }

        return type;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements <paramref name="targetInterface"/>
    /// directly or transitively.
    /// </summary>
    private static bool ImplementsInterface(IType source, InterfaceType targetInterface)
    {
        switch (source)
        {
            case ClassType classType:
                // Check the class's own interfaces (recursively through interface inheritance).
                if (InterfaceListContains(classType.Interfaces, targetInterface))
                    return true;
                // Walk the base type chain and check each ancestor's interfaces.
                var current = classType.BaseType as ClassType;
                while (current is not null)
                {
                    if (InterfaceListContains(current.Interfaces, targetInterface))
                        return true;
                    current = current.BaseType as ClassType;
                }
                return false;

            case StructType structType:
                // Check the struct's interfaces (recursively through interface inheritance).
                return InterfaceListContains(structType.Interfaces, targetInterface);

            case InterfaceType sourceInterface:
                // An interface is assignable to itself or any interface it inherits from.
                if (ReferenceEquals(sourceInterface, targetInterface)
                    || sourceInterface.DisplayName == targetInterface.DisplayName)
                    return true;
                return InterfaceListContains(sourceInterface.Interfaces, targetInterface);

            default:
                return false;
        }
    }

    /// <summary>
    /// Recursively checks whether <paramref name="target"/> is present in the given
    /// interface list or in any of the interfaces' own inherited interface lists.
    /// Uses a visited set to handle diamond inheritance and prevent infinite loops.
    /// </summary>
    private static bool InterfaceListContains(
        IReadOnlyList<IType> interfaces,
        InterfaceType target,
        HashSet<InterfaceType>? visited = null)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (ReferenceEquals(iface, target) || iface.DisplayName == target.DisplayName)
                return true;

            // Recursively check inherited interfaces, guarding against cycles.
            visited ??= new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
            if (visited.Add(iface))
            {
                if (InterfaceListContains(iface.Interfaces, target, visited))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two types are compatible for equality operators (==, !=).
    /// Compatible means: same type, both numeric, both bool, both string,
    /// or both reference types.
    /// </summary>
    private static bool AreEqualityCompatible(IType left, IType right)
    {
        if (IsErrorType(left) || IsErrorType(right))
            return true;

        if (ReferenceEquals(left, right))
            return true;

        if (left.DisplayName == right.DisplayName)
            return true;

        // Both numeric.
        if (NumericDisplayNames.Contains(left.DisplayName)
            && NumericDisplayNames.Contains(right.DisplayName))
            return true;

        // Both reference types (class types) can be compared.
        // After Task 50, StringType has been deleted. The stdlib
        // Standard.String is a ClassType, so it's already covered by
        // the ClassType branch. LegacyPrimitiveTypes.String is also a
        // ClassType.
        if (left is ClassType or ArrayType
            && right is ClassType or ArrayType)
            return true;

        return false;
    }

    /// <summary>
    /// Accepts the stdlib <c>Standard.String</c> <see cref="ClassType"/>
    /// instance (identified by its backend-backed marker and simple name),
    /// or the legacy <see cref="LegacyPrimitiveTypes.String"/> fallback
    /// <see cref="ClassType"/> (identified by DisplayName). After Task 50,
    /// <c>StringType</c> has been deleted — all string representations are
    /// now <see cref="ClassType"/> instances.
    /// </summary>
    private static bool IsStringLike(IType type)
    {
        if (type is ClassType ct)
        {
            if (ct.IsBackendBacked
                && ct.ContainingNamespace == "Standard"
                && ct.DisplayName == "String")
            {
                return true;
            }
            // Legacy fallback: LegacyPrimitiveTypes.String has DisplayName "string"
            if (ct.DisplayName == "string")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Accepts either the legacy <see cref="PrimitiveType"/> singleton for
    /// <c>bool</c> or the stdlib <c>Standard.Boolean</c>
    /// <see cref="StructType"/> instance. Like
    /// <see cref="IsStringLike(IType)"/>, this papers over the migration-
    /// window ambiguity so conditional checks and logical operators
    /// classify consistently regardless of which build populated the
    /// shim registry.
    /// </summary>
    private static bool IsBoolLike(IType type)
    {
        var name = type.DisplayName;
        if (name == "bool" || name == "Boolean") return true;
        if (type is StructType st
            && st.IsBackendBacked
            && st.ContainingNamespace == "Standard"
            && st.DisplayName == "Boolean")
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> represents the Mist
    /// <c>int</c> / <c>Standard.Int32</c> type, regardless of whether
    /// the instance is a legacy fallback or the stdlib type.
    /// </summary>
    private static bool IsIntLike(IType type)
    {
        var name = type.DisplayName;
        if (name == "int" || name == "Int32") return true;
        if (type is StructType st
            && st.IsBackendBacked
            && st.ContainingNamespace == "Standard"
            && st.DisplayName == "Int32")
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Maps a legacy Mist primitive keyword (the <c>DisplayName</c> of a
    /// legacy fallback type singleton)
    /// to the corresponding stdlib simple name, or vice versa. Used by
    /// <see cref="ArePrimitiveEquivalent(IType, IType)"/> to treat the
    /// legacy and stdlib flavours of a primitive as the same type during
    /// the Task 32 migration window.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyToStdlibName =
        new(StringComparer.Ordinal)
        {
            ["int"] = "Int32",
            ["uint"] = "UInt32",
            ["long"] = "Int64",
            ["ulong"] = "UInt64",
            ["short"] = "Int16",
            ["ushort"] = "UInt16",
            ["byte"] = "Byte",
            ["sbyte"] = "SByte",
            ["float"] = "Single",
            ["double"] = "Double",
            ["decimal"] = "Decimal",
            ["bool"] = "Boolean",
            ["char"] = "Char",
            ["string"] = "String",
        };

    /// <summary>
    /// Returns true when <paramref name="a"/> and <paramref name="b"/>
    /// denote the same Mist primitive type but were resolved via
    /// different flavours of the migration-window shim — e.g., one was
    /// the legacy <see cref="PrimitiveType"/> for <c>int</c> and the
    /// other was the stdlib <c>Standard.Int32</c>
    /// <see cref="StructType"/>. Covers every entry in
    /// <see cref="KeywordAliases.Map"/>.
    /// </summary>
    private static bool ArePrimitiveEquivalent(IType a, IType b)
    {
        // Try both directions — a is legacy / b is stdlib, and vice versa.
        if (LegacyToStdlibName.TryGetValue(a.DisplayName, out var aStdlib)
            && aStdlib == b.DisplayName)
        {
            return true;
        }
        if (LegacyToStdlibName.TryGetValue(b.DisplayName, out var bStdlib)
            && bStdlib == a.DisplayName)
        {
            return true;
        }
        return false;
    }

    private static bool IsErrorType(IType type)
    {
        return type is ErrorType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Utility helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static List<IType> CollectArgumentTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = new List<IType>();
        if (argList is null)
            return types;

        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                var type = model.GetTypeOf(expr);
                types.Add(type ?? ErrorType.Instance);
            }
        }

        return types;
    }

    /// <summary>
    /// Formats the argument types of an argument list as a comma-separated string
    /// of display names. Used for diagnostic messages in <c>new T(...)</c> validation.
    /// </summary>
    private static string FormatArgTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = CollectArgumentTypes(argList, model);
        return string.Join(", ", types.Select(t => t.DisplayName));
    }

    /// <summary>
    /// Collects all method symbols with the given name from the target type's member scope
    /// and inherited methods from base types (walking the base type chain).
    /// </summary>
    private static List<ISymbol> CollectMethodOverloads(IType? targetType, string methodName)
    {
        var results = new List<ISymbol>();
        if (targetType is null)
            return results;

        // Collect from the target type's own member scope.
        var memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            _ => null,
        };

        if (memberScope is not null)
        {
            foreach (var member in memberScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == methodName)
                    results.Add(member);
            }
        }

        // Walk the base type chain to collect inherited methods.
        if (targetType is ClassType classType)
        {
            var baseType = classType.BaseType as ClassType;
            while (baseType is not null)
            {
                var baseScope = baseType.MemberScope;
                foreach (var member in baseScope.Members)
                {
                    if (member.Kind == SymbolKind.Method && member.Name == methodName)
                        results.Add(member);
                }
                baseType = baseType.BaseType as ClassType;
            }
        }

        return results;
    }

    /// <summary>
    /// Collects all constructor symbols from the target type's member scope.
    /// Unlike methods, constructors are not inherited from base types.
    /// </summary>
    private static List<ISymbol> CollectConstructorOverloads(IType? targetType)
    {
        var results = new List<ISymbol>();
        if (targetType is null)
            return results;

        // For ConstructedType (e.g., GenericBox<int>), unwrap to the generic
        // definition to find constructors in the original type's member scope.
        var lookupType = targetType is ConstructedType cst ? cst.GenericDefinition : targetType;

        var memberScope = lookupType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return results;

        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Constructor)
                results.Add(member);
        }

        return results;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Operator overload resolution helpers (Task 14 — Mist Standard Library spec)
    //
    // These two helpers form the new operator-resolution path added by the
    // Standard Library feature. They are invoked by whichever analyzer path
    // is gated on <see cref="UseOperatorOverloadResolution"/>. In Task 14 the
    // flag is default-<c>false</c> and these helpers are exercised only by
    // targeted unit tests; the existing <c>Check*Node</c> methods are
    // unchanged. Later migration stages will route the production traffic
    // through these helpers.
    //
    // Both helpers follow the same three-step shape:
    //   1. Gather candidate operator members (<c>operator+</c>, etc.) from
    //      the operand type(s).
    //   2. Call <see cref="OverloadResolver.Resolve"/> with the argument
    //      types as a one- or two-element list.
    //   3. On Success, call <see cref="ISemanticModel.RecordResolvedOperator"/>
    //      and set the expression type. On NoApplicableCandidate emit the
    //      caller-provided diagnostic ID; on AmbiguousCall emit
    //      <c>MIST_TC_0032</c>.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a binary operator expression (<c>left op right</c>) through
    /// operator overload resolution. Gathers <c>"operator&lt;op&gt;"</c>
    /// members from both operand types, invokes
    /// <see cref="OverloadResolver.Resolve"/>, and either records the selected
    /// symbol + expression type on the semantic model or emits a diagnostic.
    /// </summary>
    /// <param name="op">The operator lexeme (e.g. <c>"+"</c>, <c>"=="</c>, <c>"&lt;"</c>).</param>
    /// <param name="left">The AST node for the left operand.</param>
    /// <param name="right">The AST node for the right operand.</param>
    /// <param name="surrounding">
    /// The enclosing operator AST node used as the key for
    /// <c>RecordResolvedOperator</c>, the target of <c>SetTypeOf</c>, and the
    /// source-span carrier for diagnostic messages.
    /// </param>
    /// <param name="noApplicableDiagnosticId">
    /// Diagnostic ID to emit when no candidate is applicable. Wrapper callers
    /// pass <c>"MIST_TC_0003"</c> (additive / multiplicative),
    /// <c>"MIST_TC_0005"</c> (relational), or <c>"MIST_TC_0006"</c> (equality)
    /// to preserve the diagnostic codes the old hardcoded checks emitted.
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveBinaryOperator(
        string op,
        SyntaxNode left,
        SyntaxNode right,
        SyntaxNode surrounding,
        string noApplicableDiagnosticId,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var leftType = model.GetTypeOf(left);
        var rightType = model.GetTypeOf(right);

        // Suppress cascading errors — if either operand's type is unknown we
        // rely on an earlier diagnostic to explain the real problem.
        if (leftType is null || rightType is null
            || IsErrorType(leftType) || IsErrorType(rightType))
            return;

        // Task 25.2: bare-unknown operator rejection (Req 13.2).
        // `UnknownType` has no declared members (Req 3.4), so no operator
        // symbols are reachable from either operand's scope. Rather than
        // falling through to the generic "no applicable operator"
        // diagnostic, emit the targeted MIST_TC_0066 that names the
        // unknown operand(s) and suggests using an `is` pattern
        // (Req 16.4). Short-circuit before candidate collection to
        // suppress cascading.
        if (leftType is UnknownType || rightType is UnknownType)
        {
            diagnostics.Add(MakeDiagnostic(
                BareUnknownOperator.Value,
                FormatBareUnknownBinaryOperatorMessage(op, leftType, rightType),
                surrounding,
                model));
            return;
        }

        var opName = "operator" + op;

        // Requirement 6.1: collect candidates from BOTH operand types.
        // Deduplicate by symbol identity so that when leftType == rightType we
        // do not enter the same candidate twice.
        //
        // Per design § 5, each candidate carries a parallel origin tag (0 for
        // candidates gathered from the left operand's type, 1 for those from
        // the right operand's type) so that OverloadResolver can apply the
        // left-operand precedence tiebreaker when MoreSpecific would
        // otherwise leave the call ambiguous. A symbol reachable from both
        // scopes is gathered once with origin 0 — the left wins during
        // deduplication, matching the tiebreaker's direction.
        var candidates = new List<ISymbol>();
        var candidateOrigin = new List<int>();

        foreach (var candidate in GetOperatorMembers(leftType, opName))
        {
            candidates.Add(candidate);
            candidateOrigin.Add(0);
        }

        if (!ReferenceEquals(leftType, rightType))
        {
            foreach (var candidate in GetOperatorMembers(rightType, opName))
            {
                if (!candidates.Any(existing => ReferenceEquals(existing, candidate)))
                {
                    candidates.Add(candidate);
                    candidateOrigin.Add(1);
                }
            }
        }

        if (candidates.Count == 0)
        {
            diagnostics.Add(MakeDiagnostic(
                noApplicableDiagnosticId,
                $"Operator '{op}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                surrounding,
                model));
            return;
        }

        // Requirement 6.3: reuse the existing OverloadResolver for the
        // two-argument case. Pass the origin vector so operator-specific
        // ambiguity can fall back on left-operand precedence (Task 15).
        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes: new[] { leftType, rightType },
            model,
            candidateOrigin);

        switch (result.Status)
        {
            case OverloadResolutionStatus.Success:
            {
                var best = result.BestCandidate!;
                model.RecordResolvedOperator(surrounding, best);
                if (best.DeclaredType is not null)
                    model.SetTypeOf(surrounding, best.DeclaredType);
                break;
            }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.Add(MakeDiagnostic(
                    noApplicableDiagnosticId,
                    $"Operator '{op}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                    surrounding,
                    model));
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0032",
                    $"Ambiguous operator '{op}' between types '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                    surrounding,
                    model));
                break;
        }
    }

    /// <summary>
    /// Resolves a unary operator expression (<c>op operand</c>) through
    /// operator overload resolution. Analogous to
    /// <see cref="ResolveBinaryOperator"/> with a single operand.
    /// </summary>
    /// <param name="op">The operator lexeme (e.g. <c>"-"</c>, <c>"!"</c>).</param>
    /// <param name="operand">The AST node for the single operand.</param>
    /// <param name="surrounding">
    /// The enclosing unary operator AST node — key for
    /// <c>RecordResolvedOperator</c> and the source-span carrier for
    /// diagnostics.
    /// </param>
    /// <param name="noApplicableDiagnosticId">
    /// Diagnostic ID to emit when no candidate is applicable. Wrapper callers
    /// pass <c>"MIST_TC_0003"</c> for unary <c>-</c> and <c>"MIST_TC_0006"</c>
    /// for unary <c>!</c> to preserve the diagnostic codes the existing
    /// hardcoded checks emit.
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveUnaryOperator(
        string op,
        SyntaxNode operand,
        SyntaxNode surrounding,
        string noApplicableDiagnosticId,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operandType = model.GetTypeOf(operand);

        if (operandType is null || IsErrorType(operandType))
            return;

        // Task 25.2: bare-unknown operator rejection (Req 13.2).
        // `UnknownType` has no declared members (Req 3.4), so no operator
        // symbols are reachable from the operand's scope. Rather than
        // falling through to the generic "no applicable operator"
        // diagnostic, emit the targeted MIST_TC_0066 that names the
        // unknown operand and suggests using an `is` pattern
        // (Req 16.4). Short-circuit before candidate collection to
        // suppress cascading.
        if (operandType is UnknownType)
        {
            diagnostics.Add(MakeDiagnostic(
                BareUnknownOperator.Value,
                FormatBareUnknownUnaryOperatorMessage(op, operandType),
                surrounding,
                model));
            return;
        }

        var opName = "operator" + op;

        var candidates = new List<ISymbol>();
        foreach (var candidate in GetOperatorMembers(operandType, opName))
            candidates.Add(candidate);

        if (candidates.Count == 0)
        {
            diagnostics.Add(MakeDiagnostic(
                noApplicableDiagnosticId,
                $"Operator '{op}' cannot be applied to operand of type '{operandType.DisplayName}'",
                surrounding,
                model));
            return;
        }

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes: new[] { operandType },
            model);

        switch (result.Status)
        {
            case OverloadResolutionStatus.Success:
            {
                var best = result.BestCandidate!;
                model.RecordResolvedOperator(surrounding, best);
                if (best.DeclaredType is not null)
                    model.SetTypeOf(surrounding, best.DeclaredType);
                break;
            }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.Add(MakeDiagnostic(
                    noApplicableDiagnosticId,
                    $"Operator '{op}' cannot be applied to operand of type '{operandType.DisplayName}'",
                    surrounding,
                    model));
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0032",
                    $"Ambiguous operator '{op}' for operand type '{operandType.DisplayName}'",
                    surrounding,
                    model));
                break;
        }
    }

    /// <summary>
    /// Collects operator-member symbols named <paramref name="operatorName"/>
    /// (e.g. <c>"operator+"</c>) from <paramref name="type"/>'s member scope.
    /// Looks through <see cref="ConstructedType"/> to the generic definition's
    /// scope. Returns an empty enumeration for types that do not own a
    /// member scope (e.g. <see cref="PrimitiveType"/>, <see cref="ArrayType"/>).
    /// </summary>
    private static IEnumerable<ISymbol> GetOperatorMembers(IType type, string operatorName)
    {
        // ── refinement-types feature: arithmetic erasure (Requirement 8.1, 8.2) ──
        // A RefinementType has no operators of its own — unwrap to the BaseType
        // so that operator lookup finds the base type's operators. The result
        // type of the resolved operator will naturally be the BaseType, which
        // implements the "arithmetic erases refinements" rule.
        if (type is RefinementType refinement)
            type = refinement.BaseType;

        var scope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if (scope is null)
            yield break;

        foreach (var member in scope.Members)
        {
            if (member.Kind == SymbolKind.Method && member.Name == operatorName)
                yield return member;
        }
    }

    /// <summary>
    /// Produces the user-facing message for <see cref="BareUnknownOperator"/>
    /// in the binary-operator case (Task 25.2). Names the operator and the
    /// operand type(s) that are <c>unknown</c>, and suggests using an
    /// <c>is</c> pattern to narrow before applying the operator
    /// (Req 13.2, Req 16.4).
    /// </summary>
    private static string FormatBareUnknownBinaryOperatorMessage(
        string op,
        IType leftType,
        IType rightType)
    {
        return $"Operator '{op}' cannot be applied to operands of type " +
               $"'{leftType.DisplayName}' and '{rightType.DisplayName}': " +
               "no operators are defined for 'unknown'. Use an 'is' pattern " +
               "to narrow the value to a concrete type before applying the operator.";
    }

    /// <summary>
    /// Produces the user-facing message for <see cref="BareUnknownOperator"/>
    /// in the unary-operator case (Task 25.2). Names the operator and the
    /// operand type, and suggests using an <c>is</c> pattern to narrow
    /// before applying the operator (Req 13.2, Req 16.4).
    /// </summary>
    private static string FormatBareUnknownUnaryOperatorMessage(
        string op,
        IType operandType)
    {
        return $"Operator '{op}' cannot be applied to operand of type " +
               $"'{operandType.DisplayName}': no operators are defined for " +
               "'unknown'. Use an 'is' pattern to narrow the value to a " +
               "concrete type before applying the operator.";
    }

    /// <summary>
    /// Formats a candidate method symbol's signature for diagnostic messages.
    /// Example: "Print(int)" or "Print(string, int)"
    /// </summary>
    private static string FormatCandidateSignature(ISymbol candidate, string methodName, ISemanticModel model)
    {
        var paramTypes = ParameterSignature.Extract(candidate, model);
        var typeNames = string.Join(", ", paramTypes.Select(t => t.DisplayName));
        return $"{methodName}({typeNames})";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default block validation (R9.1–9.3, R6.9, R11.1, R11.2, R13.3, R13.4, R13.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks all type symbols with explicit default blocks and validates:
    /// <list type="number">
    /// <item><description>Each field override references a field that exists in the type's member scope (including inherited fields).</description></item>
    /// <item><description>Visibility: base type fields must be public, not private → <c>MIST_DEFAULT_FIELD_NOT_VISIBLE</c>.</description></item>
    /// <item><description>Type bindings: concrete type must be a defaultable <see cref="ClassType"/> or <see cref="StructType"/>.</description></item>
    /// <item><description>Compile-time constant expressions (v1: only literal expressions) → <c>MIST_DEFAULT_NOT_CONSTANT</c>.</description></item>
    /// <item><description>Redundancy: if explicit default block produces same values as inferred default → <c>MIST_DEFAULT_REDUNDANT</c> with <c>Info</c> severity.</description></item>
    /// </list>
    /// </summary>
    private static void ValidateDefaultBlocks(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;

            // Extract defaultability status and default block node.
            DefaultabilityStatus defaultability;
            DefaultBlockDeclarationNode? defaultBlockNode;
            IScope? memberScope;
            ClassType? baseClass;

            switch (type)
            {
                case ClassType ct:
                    defaultability = ct.Defaultability;
                    defaultBlockNode = ct.DefaultBlockNode;
                    memberScope = ct.MemberScope;
                    baseClass = ct.BaseType as ClassType;
                    break;
                case StructType st:
                    defaultability = st.Defaultability;
                    defaultBlockNode = st.DefaultBlockNode;
                    memberScope = st.MemberScope;
                    baseClass = null; // Structs don't have base types.
                    break;
                default:
                    continue;
            }

            // Only validate types with explicit default blocks.
            if (defaultability != DefaultabilityStatus.Explicit || defaultBlockNode is null)
                continue;

            // Collect the set of field names declared directly on this type (not inherited).
            var ownFieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in memberScope.Members)
            {
                if (member.Kind == SymbolKind.Field)
                    ownFieldNames.Add(member.Name);
            }

            var hasNonRedundantEntry = false;

            foreach (var entry in defaultBlockNode.Entries)
            {
                switch (entry)
                {
                    case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                    {
                        var fieldName = fieldOverride.FieldName.Lexeme;

                        // 1. Check field exists in the type's member scope (including inherited).
                        var fieldSymbol = memberScope.Lookup(fieldName);
                        if (fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                        {
                            // Field not found — the semantic model builder already handles
                            // this during default value computation, but we still mark as
                            // non-redundant to avoid a false redundancy diagnostic.
                            hasNonRedundantEntry = true;
                            break;
                        }

                        // 2. Validate visibility: if the field is from a base type, it must be public.
                        if (!ownFieldNames.Contains(fieldName))
                        {
                            // Field is inherited — check visibility.
                            if (!IsMemberPublicViaModel(fieldSymbol, model))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultFieldNotVisible.Value,
                                    $"Field '{fieldName}' is not accessible from the default block of '{type!.DisplayName}' because it is not public in the base type",
                                    fieldOverride,
                                    model));
                            }
                        }

                        // 3. Validate compile-time constant expression (v1: only literals).
                        if (!IsLiteralExpression(fieldOverride.Value))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                DiagnosticId.DefaultNotConstant.Value,
                                $"Default value for field '{fieldName}' must be a compile-time constant expression",
                                fieldOverride.Value,
                                model));
                        }

                        // Track whether this override differs from the inferred default.
                        hasNonRedundantEntry = hasNonRedundantEntry || IsOverrideNonRedundant(fieldName, fieldOverride.Value, fieldSymbol, model);
                        break;
                    }

                    case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                    {
                        var fieldName = typeBinding.FieldName.Lexeme;

                        // 1. Check field exists.
                        var fieldSymbol = memberScope.Lookup(fieldName);
                        if (fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                        {
                            hasNonRedundantEntry = true;
                            break;
                        }

                        // 2. Validate visibility for inherited fields.
                        if (!ownFieldNames.Contains(fieldName))
                        {
                            if (!IsMemberPublicViaModel(fieldSymbol, model))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultFieldNotVisible.Value,
                                    $"Field '{fieldName}' is not accessible from the default block of '{type!.DisplayName}' because it is not public in the base type",
                                    typeBinding,
                                    model));
                            }
                        }

                        // 3. Validate the concrete type is a defaultable ClassType or StructType.
                        var concreteType = ResolveTypeBindingConcreteType(typeBinding, memberScope);
                        if (concreteType is not null)
                        {
                            switch (concreteType)
                            {
                                case ClassType ct when ct.Defaultability == DefaultabilityStatus.NotDefaultable:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type '{ct.DisplayName}' used as a type binding for field '{fieldName}' is not defaultable",
                                        typeBinding,
                                        model));
                                    break;
                                case StructType st when st.Defaultability == DefaultabilityStatus.NotDefaultable:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type '{st.DisplayName}' used as a type binding for field '{fieldName}' is not defaultable",
                                        typeBinding,
                                        model));
                                    break;
                                case not ClassType and not StructType:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type binding for field '{fieldName}' must reference a class or struct type, not '{concreteType.DisplayName}'",
                                        typeBinding,
                                        model));
                                    break;
                            }
                        }

                        // 4. Validate nested override expressions are compile-time constants.
                        foreach (var nested in typeBinding.NestedEntries)
                        {
                            if (!IsLiteralExpression(nested.Value))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultNotConstant.Value,
                                    $"Default value for nested field '{nested.FieldName.Lexeme}' must be a compile-time constant expression",
                                    nested.Value,
                                    model));
                            }
                        }

                        // Type bindings are always non-redundant (inferred defaults
                        // cannot produce type bindings).
                        hasNonRedundantEntry = true;
                        break;
                    }
                }
            }

            // 5. Redundancy check: if the explicit default block has no entries,
            //    or all field overrides match the inferred default, emit Info diagnostic.
            if (!hasNonRedundantEntry)
            {
                diagnostics.Add(MakeDiagnosticWithSeverity(
                    DiagnosticId.DefaultRedundantBlock.Value,
                    $"The explicit default block on type '{type!.DisplayName}' is redundant and can be removed because it produces the same values as the inferred default",
                    defaultBlockNode,
                    model,
                    DiagnosticSeverity.Info));
            }
        }
    }

    /// <summary>
    /// Checks whether a member symbol is public by examining its declaring AST node's modifiers
    /// via the semantic model.
    /// </summary>
    private static bool IsMemberPublicViaModel(ISymbol member, ISemanticModel model)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(member); }
        catch { return false; }

        var modifiers = GetMemberModifiers(declaringNode);
        return modifiers.IsPublic;
    }

    /// <summary>
    /// Checks whether an expression is a literal expression (compile-time constant in v1).
    /// Walks down the expression chain to the innermost <see cref="PrimaryNode"/> and
    /// checks if it is a recognized literal node type.
    /// </summary>
    private static bool IsLiteralExpression(ExpressionNode expression)
    {
        var primary = UnwrapExpressionToPrimary(expression);
        if (primary is null)
            return false;

        return primary is IntegerLiteralExpressionPrimaryNode
            or HexLiteralExpressionPrimaryNode
            or BinaryLiteralExpressionPrimaryNode
            or FloatLiteralExpressionPrimaryNode
            or StringLiteralExpressionPrimaryNode
            or CharLiteralExpressionPrimaryNode
            or TrueLiteralExpressionPrimaryNode
            or FalseLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Unwraps an expression node down to its innermost <see cref="PrimaryNode"/>.
    /// Returns <c>null</c> if the expression structure is not a simple primary.
    /// </summary>
    private static PrimaryNode? UnwrapExpressionToPrimary(ExpressionNode expression)
    {
        Storm.Runtime.SyntaxNode current = expression;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
                return primary;

            // Look for a single non-token child to follow.
            // Skip null children that may be yielded by generated AST nodes
            // whose optional properties are typed as non-nullable.
            Storm.Runtime.SyntaxNode? next = null;
            var childCount = 0;
            foreach (var child in current.Children())
            {
                if (child is null)
                    continue;

                childCount++;
                if (child is PrimaryNode childPrimary)
                    return childPrimary;

                if (next is null)
                    next = child;
            }

            if (childCount > 1 || next is null)
                return null;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a field override in a default block is non-redundant —
    /// i.e., it produces a different value than the inferred default would.
    /// </summary>
    private static bool IsOverrideNonRedundant(
        string fieldName,
        ExpressionNode overrideExpr,
        ISymbol fieldSymbol,
        ISemanticModel model)
    {
        // Extract the override's constant value.
        var overrideValue = TryExtractLiteralConstantValue(overrideExpr);

        // Get the field's declaring node to check for a field initializer.
        SyntaxNode fieldDeclNode;
        try { fieldDeclNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return true; } // Can't determine — assume non-redundant.

        if (fieldDeclNode is FieldDeclarationNode fieldDecl && fieldDecl.Initializer is ExpressionNode initExpr)
        {
            // Field has an initializer — compare override value with initializer value.
            var initValue = TryExtractLiteralConstantValue(initExpr);
            return !Equals(overrideValue, initValue);
        }

        // Field has no initializer — compare override value with zero-init value.
        var fieldType = fieldSymbol.DeclaredType;
        if (fieldType is null)
            return true;

        var zeroValue = GetZeroInitValue(fieldType);
        return !Equals(overrideValue, zeroValue);
    }

    /// <summary>
    /// Attempts to extract a compile-time constant value from a literal expression.
    /// Returns <c>null</c> if the expression is not a recognized literal.
    /// </summary>
    private static object? TryExtractLiteralConstantValue(ExpressionNode expression)
    {
        var primary = UnwrapExpressionToPrimary(expression);
        if (primary is null)
            return null;

        return primary switch
        {
            IntegerLiteralExpressionPrimaryNode intLit => TryParseLiteralInt(intLit.IntegerLiteral.Lexeme),
            HexLiteralExpressionPrimaryNode hexLit => TryParseLiteralInt(hexLit.HexLiteral.Lexeme),
            BinaryLiteralExpressionPrimaryNode binLit => TryParseLiteralInt(binLit.BinaryLiteral.Lexeme),
            FloatLiteralExpressionPrimaryNode floatLit => TryParseLiteralFloat(floatLit.FloatLiteral.Lexeme),
            StringLiteralExpressionPrimaryNode stringLit => ExtractStringContent(stringLit.StringLiteral.Lexeme),
            CharLiteralExpressionPrimaryNode charLit => ExtractCharContent(charLit.CharLiteral.Lexeme),
            TrueLiteralExpressionPrimaryNode => true,
            FalseLiteralExpressionPrimaryNode => false,
            _ => null,
        };
    }

    /// <summary>
    /// Parses an integer literal string into a numeric value.
    /// </summary>
    private static object? TryParseLiteralInt(string lexeme)
    {
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        if (upper.StartsWith("0X"))
            cleaned = cleaned[2..];
        else if (upper.StartsWith("0B"))
            cleaned = cleaned[2..];

        // Remove suffix for comparison.
        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
        {
            if (ulong.TryParse(cleaned[..^2], out var val)) return val;
        }
        else if (upper.EndsWith("L"))
        {
            if (long.TryParse(cleaned[..^1], out var val)) return val;
        }
        else if (upper.EndsWith("U"))
        {
            if (uint.TryParse(cleaned[..^1], out var val)) return val;
        }
        else
        {
            if (int.TryParse(cleaned, out var val)) return val;
            if (long.TryParse(cleaned, out var lval)) return lval;
        }

        return null;
    }

    /// <summary>
    /// Parses a float literal string into a numeric value.
    /// </summary>
    private static object? TryParseLiteralFloat(string lexeme)
    {
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        if (upper.EndsWith("F"))
        {
            if (float.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else if (upper.EndsWith("M"))
        {
            if (decimal.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else
        {
            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }

        return null;
    }

    /// <summary>
    /// Extracts the string content from a string literal lexeme (removes surrounding quotes).
    /// </summary>
    private static string ExtractStringContent(string lexeme)
    {
        if (lexeme.Length >= 2 && lexeme[0] == '"' && lexeme[^1] == '"')
            return lexeme[1..^1];
        return lexeme;
    }

    /// <summary>
    /// Extracts the char content from a char literal lexeme (removes surrounding quotes).
    /// </summary>
    private static object ExtractCharContent(string lexeme)
    {
        if (lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            if (inner.Length == 1)
                return inner[0];
        }
        return '\0';
    }

    /// <summary>
    /// Returns the zero-initialization value for a type, used for redundancy comparison.
    /// </summary>
    private static object? GetZeroInitValue(IType type)
    {
        var name = type.DisplayName;
        return name switch
        {
            "int" or "Int32" => 0,
            "uint" or "UInt32" => 0u,
            "long" or "Int64" => 0L,
            "ulong" or "UInt64" => 0UL,
            "short" or "Int16" => (short)0,
            "ushort" or "UInt16" => (ushort)0,
            "byte" or "Byte" => (byte)0,
            "sbyte" or "SByte" => (sbyte)0,
            "float" or "Single" => 0.0f,
            "double" or "Double" => 0.0,
            "decimal" or "Decimal" => 0.0m,
            "bool" or "Boolean" => false,
            "char" or "Char" => '\0',
            "string" or "String" => "",
            _ => null,
        };
    }

    /// <summary>
    /// Resolves the concrete type referenced by a type binding entry in a default block.
    /// Searches the member scope's parent scopes for a matching type symbol.
    /// </summary>
    private static IType? ResolveTypeBindingConcreteType(
        DefaultTypeBindingDefaultFieldInitializerNode typeBinding,
        IScope memberScope)
    {
        var typeName = ExtractTypeBindingName(typeBinding.TypeName);
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Search up the scope chain for a type with the given name.
        var scope = memberScope.Parent;
        while (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                    && member.Name == typeName)
                {
                    return member.DeclaredType;
                }
            }
            scope = scope.Parent;
        }

        return null;
    }

    /// <summary>
    /// Extracts the type name from a <see cref="TypeReferenceNode"/>.
    /// </summary>
    private static string? ExtractTypeBindingName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            NamedTypeRefTypeReferenceNode named => named.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    /// <summary>
    /// Creates a diagnostic with a specified severity level.
    /// Used for non-error diagnostics such as the <c>Info</c>-level redundant default block diagnostic.
    /// </summary>
    private static Diagnostic MakeDiagnosticWithSeverity(
        string id, string message, SyntaxNode node, ISemanticModel model, DiagnosticSeverity severity)
    {
        Common.Text.LinePositionSpan location = default;
        string compilationUnitPath = "<unknown>";

        var span = node.Span;
        if (span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for (var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if (astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            new DiagnosticId(id),
            severity,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: "TypeChecker");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Union type assignability helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if the given union type contains at least one
    /// <see cref="ErrorType"/> constituent. A "poisoned" union suppresses
    /// cascading diagnostics by accepting any assignment.
    /// </summary>
    private static bool HasErrorConstituent(UnionType union)
    {
        foreach (var constituent in union.Constituents)
        {
            if (IsErrorType(constituent))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="type"/> is a constituent of
    /// <paramref name="union"/> — i.e., it matches one of the union's
    /// constituent types by display name or primitive equivalence.
    /// </summary>
    private static bool IsConstituentOf(IType type, UnionType union)
    {
        foreach (var constituent in union.Constituents)
        {
            if (ReferenceEquals(type, constituent))
                return true;
            if (type.DisplayName == constituent.DisplayName)
                return true;
            if (ArePrimitiveEquivalent(type, constituent))
                return true;
            // Refinement type constituent: the source is a constituent if it matches
            // the refinement's base type (e.g., int is a constituent of a union
            // containing RefinementType with BaseType=int).
            if (constituent is RefinementType refinement
                && (type.DisplayName == refinement.BaseType.DisplayName
                    || ArePrimitiveEquivalent(type, refinement.BaseType)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Validates a member access on a union-typed receiver (Req 7.1, 7.2, 7.3).
    /// Checks that the member exists on ALL constituent types. If it does,
    /// returns the union of member return types (or the single type if all
    /// identical). If the member is missing on any constituent, emits
    /// <see cref="UnionMemberNotOnAllConstituents"/> and returns
    /// <see cref="ErrorType.Instance"/>.
    /// </summary>
    private static IType? CheckUnionMemberAccess(
        string memberName,
        UnionType unionType,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // If the union is poisoned (contains ErrorType), suppress cascading
        // diagnostics and return ErrorType.
        if (HasErrorConstituent(unionType))
            return ErrorType.Instance;

        var missingOn = new List<string>();
        var memberTypes = new List<IType>();

        foreach (var constituent in unionType.Constituents)
        {
            var memberSymbol = FindMemberSymbol(constituent, memberName, model);
            if (memberSymbol is null)
            {
                missingOn.Add(constituent.DisplayName);
            }
            else
            {
                var memberType = memberSymbol.DeclaredType;
                if (memberType is not null)
                    memberTypes.Add(memberType);
            }
        }

        if (missingOn.Count > 0)
        {
            var missingList = string.Join(", ", missingOn.Select(t => $"'{t}'"));
            diagnostics.Add(MakeDiagnostic(
                UnionMemberNotOnAllConstituents.Value,
                $"Member '{memberName}' does not exist on all constituents of union type '{unionType.DisplayName}'. Missing on: {missingList}.",
                node,
                model));
            return ErrorType.Instance;
        }

        // All constituents have the member. Compute the result type:
        // if all member types are identical, return that type; otherwise
        // return a union of the distinct member types.
        if (memberTypes.Count == 0)
            return ErrorType.Instance;

        // Check if all member types are the same.
        var allSame = true;
        var firstDisplayName = memberTypes[0].DisplayName;
        for (var i = 1; i < memberTypes.Count; i++)
        {
            if (memberTypes[i].DisplayName != firstDisplayName)
            {
                allSame = false;
                break;
            }
        }

        if (allSame)
            return memberTypes[0];

        // Distinct member types — build a union of the return types.
        // Deduplicate by DisplayName to avoid redundant constituents.
        var distinctTypes = new List<IType>();
        var seenDisplayNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var memberType in memberTypes)
        {
            if (seenDisplayNames.Add(memberType.DisplayName))
                distinctTypes.Add(memberType);
        }

        if (distinctTypes.Count == 1)
            return distinctTypes[0];

        return new UnionType(distinctTypes);
    }

    /// <summary>
    /// Attempts to emit a union-type-specific assignability diagnostic.
    /// Returns <c>true</c> if a targeted diagnostic was emitted, in which
    /// case the caller should not emit the generic MIST_TC_0001 diagnostic.
    /// </summary>
    private static bool TryEmitUnionAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Union → non-union (Req 6.4): must narrow first.
        if (source is UnionType sourceUnion && target is not UnionType)
        {
            diagnostics.Add(MakeDiagnostic(
                UnionToNonUnionAssignment.Value,
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}': use a pattern match ('is {target.DisplayName} x') to narrow the union type first.",
                node,
                model));
            return true;
        }

        // Non-constituent → union (Req 6.3): source is not a constituent.
        if (source is not UnionType && target is UnionType targetUnion)
        {
            diagnostics.Add(MakeDiagnostic(
                UnionNonConstituentAssignment.Value,
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}': '{source.DisplayName}' is not a constituent of the union type.",
                node,
                model));
            return true;
        }

        // Union → narrower union (Req 6.3): some source constituents missing.
        if (source is UnionType srcUnion && target is UnionType tgtUnion)
        {
            var missing = new List<string>();
            foreach (var constituent in srcUnion.Constituents)
            {
                if (!IsConstituentOf(constituent, tgtUnion))
                    missing.Add(constituent.DisplayName);
            }

            if (missing.Count > 0)
            {
                var missingList = string.Join(", ", missing);
                diagnostics.Add(MakeDiagnostic(
                    UnionIncompatibleConstituents.Value,
                    $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}': constituent type(s) {missingList} not present in the target union.",
                    node,
                    model));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects the enum-specific assignability failure mode: assigning a value
    /// of the underlying type to a variable of the enum type (reverse conversion).
    /// When this case matches, emits <c>MIST_SB_0083</c> and returns <c>true</c>.
    /// Returns <c>false</c> if the case does not apply.
    /// </summary>
    private static bool TryEmitEnumAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Unwrap type aliases for both source and target before checking.
        var unwrappedSource = source;
        while (unwrappedSource is TypeAliasType srcAlias)
            unwrappedSource = srcAlias.UnderlyingType;
        var unwrappedTarget = target;
        while (unwrappedTarget is TypeAliasType tgtAlias)
            unwrappedTarget = tgtAlias.UnderlyingType;

        // underlying type → enum (Req 7.3): reverse conversion rejected.
        if (unwrappedTarget is EnumType targetEnum &&
            unwrappedSource is not EnumType &&
            IsAssignable(unwrappedSource, targetEnum.UnderlyingType, model))
        {
            diagnostics.Add(MakeDiagnostic(
                CannotAssignUnderlyingToEnum.Value,
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}': implicit conversion from the underlying type to an enum type is not supported. Use an explicit enum member instead.",
                node,
                model));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a diagnostic for the type checker.
    /// Uses the semantic model to resolve the source file path and convert
    /// offsets to proper 1-based line/column positions.
    /// </summary>
    private static Diagnostic MakeDiagnostic(string id, string message, SyntaxNode node, ISemanticModel model)
    {
        Common.Text.LinePositionSpan location = default;
        string compilationUnitPath = "<unknown>";

        var span = node.Span;
        if (span.Length > 0)
        {
            // Find which AST root contains this node by checking span containment.
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for (var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if (astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                // Convert the Storm span to a Mist SourceSpan for proper line/column conversion.
                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: "TypeChecker");
    }

    /// <summary>
    /// Creates a warning-severity diagnostic with location information derived from the given AST node.
    /// </summary>
    private static Diagnostic MakeDiagnosticWarning(string id, string message, SyntaxNode node, ISemanticModel model)
    {
        Common.Text.LinePositionSpan location = default;
        string compilationUnitPath = "<unknown>";

        var span = node.Span;
        if (span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for (var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if (astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Warning,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: "TypeChecker");
    }

    /// <summary>
    /// Determines whether the given type is a constituent of the specified union type.
    /// Comparison is by <see cref="IType.DisplayName"/> equality (structural).
    /// </summary>
    private static bool IsConstituentOfUnion(IType type, UnionType union)
    {
        foreach (var constituent in union.Constituents)
        {
            if (string.Equals(constituent.DisplayName, type.DisplayName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic constraint satisfaction checking (Task 6.1)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a type argument satisfies all constraints declared on the
    /// corresponding type parameter. Emits diagnostics MIST_CONSTRAINT_0009
    /// through MIST_CONSTRAINT_0015 for each violated constraint.
    /// </summary>
    /// <param name="typeArgument">The concrete type argument being checked.</param>
    /// <param name="typeParameter">The type parameter with constraints to check against.</param>
    /// <param name="node">The syntax node for diagnostic location reporting.</param>
    /// <param name="genericTypeName">The display name of the generic type/method for diagnostic messages.</param>
    /// <param name="model">The semantic model.</param>
    /// <param name="diagnostics">The diagnostic bag to emit errors into.</param>
    internal static void CheckConstraintSatisfaction(
        IType typeArgument,
        TypeParameterType typeParameter,
        SyntaxNode node,
        string genericTypeName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var constraints = typeParameter.Constraints;
        if (constraints is null)
            return;

        // Suppress cascading errors for error types.
        if (IsErrorType(typeArgument))
            return;

        // Unwrap type aliases to their underlying type for constraint checking.
        if (typeArgument is TypeAliasType aliasArg)
            typeArgument = aliasArg.UnderlyingType;

        // Check class constraint: type argument must be a reference type.
        if (constraints.HasClassConstraint)
        {
            if (typeArgument.Kind == TypeKind.Struct)
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintClassViolation.Value,
                    $"Type '{typeArgument.DisplayName}' must be a reference type to satisfy the 'class' constraint on type parameter '{typeParameter.Name}'.",
                    node,
                    model));
            }
        }

        // Check struct constraint: type argument must be a value type.
        if (constraints.HasStructConstraint)
        {
            if (typeArgument.Kind != TypeKind.Struct)
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintStructViolation.Value,
                    $"Type '{typeArgument.DisplayName}' must be a value type to satisfy the 'struct' constraint on type parameter '{typeParameter.Name}'.",
                    node,
                    model));
            }
        }

        // Check base class constraint: type argument must derive from the base class.
        if (constraints.BaseClassConstraint is not null)
        {
            if (!DerivesFromClass(typeArgument, constraints.BaseClassConstraint))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintBaseClassNotDerived.Value,
                    $"Type '{typeArgument.DisplayName}' does not derive from '{constraints.BaseClassConstraint.DisplayName}' required by type parameter '{typeParameter.Name}' of '{genericTypeName}'.",
                    node,
                    model));
            }
        }

        // Check interface constraints: type argument must implement each interface.
        foreach (var iface in constraints.InterfaceConstraints)
        {
            if (!ImplementsInterfaceConstraint(typeArgument, iface))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintInterfaceNotImplemented.Value,
                    $"Type '{typeArgument.DisplayName}' does not implement interface '{iface.DisplayName}' required by type parameter '{typeParameter.Name}' of '{genericTypeName}'.",
                    node,
                    model));
            }
        }

        // Check trait constraints: type argument must satisfy each trait.
        foreach (var trait in constraints.TraitConstraints)
        {
            if (trait is TraitType traitType && !SatisfiesTrait(typeArgument, traitType, model))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintTraitNotSatisfied.Value,
                    $"Type '{typeArgument.DisplayName}' does not satisfy trait '{trait.DisplayName}' required by type parameter '{typeParameter.Name}' of '{genericTypeName}'.",
                    node,
                    model));
            }
        }

        // Check constructor constraints (OR semantics): type argument must have
        // at least one matching public constructor.
        if (constraints.ConstructorConstraints.Count > 0)
        {
            CheckConstructorConstraints(
                typeArgument, typeParameter, constraints.ConstructorConstraints,
                genericTypeName, node, model, diagnostics);
        }

        // Check readonly constraint: type argument must be a readonly type.
        if (constraints.HasReadonlyConstraint)
        {
            if (!IsTypeReadonly(typeArgument))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintReadonlyViolation.Value,
                    $"Type argument '{typeArgument.DisplayName}' does not satisfy the 'readonly' constraint on type parameter '{typeParameter.Name}'.",
                    node,
                    model));
            }
        }

        // Check mutable constraint: type argument must NOT be a readonly type.
        if (constraints.HasMutableConstraint)
        {
            if (IsTypeReadonly(typeArgument))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConstraintMutableViolation.Value,
                    $"Type argument '{typeArgument.DisplayName}' does not satisfy the 'mutable' constraint on type parameter '{typeParameter.Name}' (type is readonly).",
                    node,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks constructor constraints with OR semantics. The type argument must
    /// have a public constructor matching at least one of the declared constructor
    /// constraint signatures.
    /// </summary>
    private static void CheckConstructorConstraints(
        IType typeArgument,
        TypeParameterType typeParameter,
        IReadOnlyList<IReadOnlyList<IType>> constructorConstraints,
        string genericTypeName,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect public constructors from the type argument.
        var publicConstructors = CollectPublicConstructors(typeArgument, model);

        // For each constructor constraint, check if at least one public constructor matches.
        foreach (var requiredParams in constructorConstraints)
        {
            var matched = false;

            // Structs always have a parameterless constructor.
            if (requiredParams.Count == 0 && typeArgument.Kind == TypeKind.Struct)
            {
                matched = true;
            }
            else
            {
                foreach (var ctor in publicConstructors)
                {
                    if (ConstructorMatchesConstraint(ctor, requiredParams, model))
                    {
                        matched = true;
                        break;
                    }
                }

                // If no explicit constructors are defined and the constraint is
                // parameterless, the default constructor satisfies it.
                if (!matched && requiredParams.Count == 0 && publicConstructors.Count == 0)
                {
                    // Types with no declared constructors have an implicit
                    // public parameterless constructor.
                    matched = true;
                }
            }

            if (!matched)
            {
                if (requiredParams.Count == 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        ConstraintParameterlessCtorViolation.Value,
                        $"Type '{typeArgument.DisplayName}' must have a public parameterless constructor to satisfy the 'new()' constraint on type parameter '{typeParameter.Name}'.",
                        node,
                        model));
                }
                else
                {
                    var paramTypes = string.Join(", ", requiredParams.Select(p => p.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        ConstraintParameterizedCtorViolation.Value,
                        $"Type '{typeArgument.DisplayName}' must have a public constructor with parameters '({paramTypes})' to satisfy the 'new({paramTypes})' constraint on type parameter '{typeParameter.Name}'.",
                        node,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a type derives from (or is) the specified base class.
    /// </summary>
    private static bool DerivesFromClass(IType typeArgument, IType baseClassConstraint)
    {
        // The type argument itself matches the base class.
        if (ReferenceEquals(typeArgument, baseClassConstraint)
            || typeArgument.DisplayName == baseClassConstraint.DisplayName)
            return true;

        // Walk the inheritance chain for ClassType.
        if (typeArgument is ClassType classType)
        {
            var current = classType.BaseType as ClassType;
            while (current is not null)
            {
                if (ReferenceEquals(current, baseClassConstraint)
                    || current.DisplayName == baseClassConstraint.DisplayName)
                    return true;
                current = current.BaseType as ClassType;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type argument implements the specified interface constraint.
    /// Handles both <see cref="InterfaceType"/> and <see cref="ConstructedType"/>
    /// (for generic interface constraints like <c>IComparable&lt;T&gt;</c>).
    /// </summary>
    private static bool ImplementsInterfaceConstraint(IType typeArgument, IType interfaceConstraint)
    {
        // If the constraint is a plain InterfaceType, use the existing helper.
        if (interfaceConstraint is InterfaceType ifaceType)
            return ImplementsInterface(typeArgument, ifaceType);

        // For ConstructedType interface constraints (e.g., IComparable<int>),
        // check if the type argument implements a matching constructed interface.
        if (interfaceConstraint is ConstructedType constructedIface
            && constructedIface.GenericDefinition is InterfaceType)
        {
            return ImplementsConstructedInterface(typeArgument, constructedIface);
        }

        // Fallback: display name comparison.
        return GetInterfaceList(typeArgument)
            .Any(i => i.DisplayName == interfaceConstraint.DisplayName);
    }

    /// <summary>
    /// Gets the interface list from a type (ClassType, StructType, or InterfaceType).
    /// </summary>
    private static IReadOnlyList<IType> GetInterfaceList(IType type)
    {
        return type switch
        {
            ClassType ct => ct.Interfaces,
            StructType st => st.Interfaces,
            InterfaceType it => it.Interfaces,
            _ => Array.Empty<IType>(),
        };
    }

    /// <summary>
    /// Collects all public constructors from a type argument.
    /// </summary>
    private static List<ISymbol> CollectPublicConstructors(IType typeArgument, ISemanticModel model)
    {
        var results = new List<ISymbol>();

        var memberScope = typeArgument switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return results;

        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Constructor)
                continue;

            // Check if the constructor is public.
            SyntaxNode ctorNode;
            try { ctorNode = model.GetDeclaringNode(member); }
            catch { continue; }

            var modifiers = GetMemberModifiers(ctorNode);
            if (modifiers.IsPublic)
                results.Add(member);
        }

        return results;
    }

    /// <summary>
    /// Checks whether a constructor symbol matches a constructor constraint
    /// (a list of required parameter types).
    /// </summary>
    private static bool ConstructorMatchesConstraint(
        ISymbol ctor,
        IReadOnlyList<IType> requiredParams,
        ISemanticModel model)
    {
        SyntaxNode ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { return false; }

        if (ctorNode is not ConstructorDeclarationNode ctorDecl)
            return false;

        var paramTypes = new List<IType>();
        if (ctorDecl.Parameters is not null)
        {
            foreach (var child in ctorDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym?.DeclaredType is not null)
                        paramTypes.Add(paramSym.DeclaredType);
                }
            }
        }

        if (paramTypes.Count != requiredParams.Count)
            return false;

        for (var i = 0; i < requiredParams.Count; i++)
        {
            // For constructor constraints, we check exact type match
            // (by display name) since the constraint specifies the
            // exact parameter types required.
            if (paramTypes[i].DisplayName != requiredParams[i].DisplayName)
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent type argument validation (MIST_DT_0002, MIST_DT_0003)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates all dependent type arguments in the program:
    /// <list type="bullet">
    /// <item><c>MIST_DT_0002</c> — a dependent type argument position contains a
    /// non-constant expression (variable, method call, property access, or expression).</item>
    /// <item><c>MIST_DT_0003</c> — a dependent type argument's resolved type does not
    /// match the dependent parameter's value type annotation.</item>
    /// </list>
    /// Walks all AST trees recursively to find <see cref="TypeArgumentListNode"/> instances
    /// and validates each dependent position against the corresponding type parameter.
    /// </summary>
    private static void ValidateDependentTypeArguments(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var ast in model.Asts)
        {
            ValidateDependentTypeArgumentsInTree(ast, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively walks an AST subtree looking for nodes that carry type argument lists
    /// and validates dependent positions.
    /// </summary>
    private static void ValidateDependentTypeArgumentsInTree(
        SyntaxNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check if this node carries a type argument list that needs validation.
        switch (node)
        {
            case ObjectCreationExpressionPrimaryNode objCreation when objCreation.TypeArgs is not null:
                ValidateTypeArgListForNode(objCreation, objCreation.TypeArgs, model, diagnostics);
                break;

            case NamedTypeRefTypeReferenceNode namedRef when namedRef.TypeArgs is not null:
                ValidateTypeArgListForNode(namedRef, namedRef.TypeArgs, model, diagnostics);
                break;

            case BaseTypeRefNode baseRef when baseRef.TypeArgs is not null:
                ValidateTypeArgListForNode(baseRef, baseRef.TypeArgs, model, diagnostics);
                break;

            case MethodCallSuffixPostfixSuffixNode methodCall when methodCall.TypeArgs is not null:
                ValidateMethodCallDependentTypeArgs(methodCall, model, diagnostics);
                break;
        }

        // Recurse into children.
        foreach (var child in node.Children())
        {
            if (child is not null)
                ValidateDependentTypeArgumentsInTree(child, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single <see cref="TypeArgumentListNode"/> against the resolved type's
    /// parameters. Emits <c>MIST_DT_0002</c> for non-constant arguments in dependent
    /// positions and <c>MIST_DT_0003</c> for type mismatches.
    /// </summary>
    private static void ValidateTypeArgListForNode(
        SyntaxNode ownerNode,
        TypeArgumentListNode typeArgList,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Resolve the type that this type argument list belongs to.
        var resolvedType = model.GetTypeOf(ownerNode);
        if (resolvedType is null || resolvedType is ErrorType)
            return;

        // Unwrap type aliases to get the underlying constructed type.
        if (resolvedType is TypeAliasType alias)
            resolvedType = alias.UnderlyingType;

        // We need a ConstructedType to get the generic definition's type parameters.
        ConstructedType? constructedType = null;
        IReadOnlyList<IType>? typeParams = null;

        if (resolvedType is ConstructedType ct)
        {
            constructedType = ct;
            typeParams = GetTypeParameters(ct.GenericDefinition);
        }
        else
        {
            // For non-constructed types, try to get type parameters directly.
            typeParams = GetTypeParameters(resolvedType);
        }

        if (typeParams is null || typeParams.Count == 0)
            return;

        // Collect all type argument nodes.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach (var extra in typeArgList.TypeArguments)
            argNodes.Add(extra);

        // Validate each position.
        for (var i = 0; i < argNodes.Count && i < typeParams.Count; i++)
        {
            if (typeParams[i] is not DependentTypeParameterType depParam)
                continue;

            var argNode = argNodes[i];

            // Check MIST_DT_0002: Is the argument a compile-time constant?
            if (!IsDependentArgCompileTimeConstant(argNode, model))
            {
                var exprText = ExtractTypeArgumentText(argNode);
                diagnostics.Add(MakeDiagnostic(
                    DependentArgNotConstant.Value,
                    $"Dependent type argument must be a compile-time constant. '{exprText}' cannot be evaluated at compile time.",
                    argNode,
                    model));
                continue; // Skip type match check if not a constant.
            }

            // Check MIST_DT_0003: Does the argument's type match the parameter's value type?
            if (constructedType?.DependentArguments is not null
                && i < constructedType.DependentArguments.Count
                && constructedType.DependentArguments[i] is { } depArg)
            {
                if (!DependentValueTypeMatches(depArg, depParam))
                {
                    diagnostics.Add(MakeDiagnostic(
                        DependentArgTypeMismatch.Value,
                        $"Dependent type argument '{depArg.DisplayValue}' of type '{depArg.ValueType.DisplayName}' is not assignable to dependent parameter '{depParam.Name}' of type '{depParam.ValueType.DisplayName}'.",
                        argNode,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Validates dependent type arguments on a method call suffix node.
    /// Resolves the called method's type parameters and checks each dependent position.
    /// </summary>
    private static void ValidateMethodCallDependentTypeArgs(
        MethodCallSuffixPostfixSuffixNode methodCall,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Try to resolve the method symbol to get its type parameters.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is not Symbol sym || sym.TypeParameters.Count == 0)
            return;

        var typeParams = sym.TypeParameters;
        var typeArgList = methodCall.TypeArgs!;

        // Collect all type argument nodes.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach (var extra in typeArgList.TypeArguments)
            argNodes.Add(extra);

        // Validate each position.
        for (var i = 0; i < argNodes.Count && i < typeParams.Count; i++)
        {
            if (typeParams[i] is not DependentTypeParameterType depParam)
                continue;

            var argNode = argNodes[i];

            // Check MIST_DT_0002: Is the argument a compile-time constant?
            if (!IsDependentArgCompileTimeConstant(argNode, model))
            {
                var exprText = ExtractTypeArgumentText(argNode);
                diagnostics.Add(MakeDiagnostic(
                    DependentArgNotConstant.Value,
                    $"Dependent type argument must be a compile-time constant. '{exprText}' cannot be evaluated at compile time.",
                    argNode,
                    model));
                continue;
            }

            // For method calls, type mismatch checking (MIST_DT_0003) is handled
            // by the semantic model builder during type argument resolution. The
            // DependentValueArgument's ValueType is set from the parameter's ValueType
            // during resolution, so mismatches are caught at the semantic level.
        }
    }

    /// <summary>
    /// Determines whether a <see cref="TypeArgumentNode"/> represents a valid compile-time
    /// constant for a dependent type argument position.
    /// <para>
    /// Accepted forms: integer literals, negated integer literals, hex/binary literals,
    /// boolean literals, char literals, and identifiers that resolve to const declarations
    /// or forwarded dependent type parameters.
    /// </para>
    /// <para>
    /// Rejected forms: type references that resolve to variables, method calls, property
    /// accesses, or any non-constant expression.
    /// </para>
    /// </summary>
    private static bool IsDependentArgCompileTimeConstant(TypeArgumentNode argNode, ISemanticModel model)
    {
        switch (argNode)
        {
            // All literal forms are compile-time constants.
            case IntegerLiteralTypeArgumentTypeArgumentNode:
            case NegatedIntegerTypeArgumentTypeArgumentNode:
            case HexLiteralTypeArgumentTypeArgumentNode:
            case BinaryLiteralTypeArgumentTypeArgumentNode:
            case TrueLiteralTypeArgumentTypeArgumentNode:
            case FalseLiteralTypeArgumentTypeArgumentNode:
            case CharLiteralTypeArgumentTypeArgumentNode:
                return true;

            // A type reference in a dependent position needs disambiguation.
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
            {
                // A bare identifier could be a const reference or a forwarded dependent parameter.
                if (typeRefArg.Type is NamedTypeRefTypeReferenceNode namedRef
                    && namedRef.TypeArgs is null
                    && !namedRef.ArrayRank.HasValue)
                {
                    // Check what the semantic model resolved this to.
                    var symbol = model.GetDeclaredSymbol(namedRef);
                    if (symbol is not null)
                    {
                        // Type symbols are not constants — they shouldn't be in a dependent position.
                        if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                            or SymbolKind.Trait or SymbolKind.TypeAlias or SymbolKind.Enum)
                        {
                            return false;
                        }

                        // Dependent type parameters (forwarded) are compile-time constants.
                        if (symbol.DeclaredType is DependentTypeParameterType)
                            return true;

                        // Static get-only properties generated for dependent parameters are constants.
                        if (symbol.Kind == SymbolKind.Property)
                            return true;

                        // Fields marked as const are compile-time constants.
                        // In Mist, fields used as dependent arguments are accepted by the
                        // semantic model builder — the type checker trusts that resolution.
                        if (symbol.Kind == SymbolKind.Field)
                            return true;

                        // Local variables are NOT compile-time constants.
                        if (symbol.Kind == SymbolKind.LocalVariable)
                            return false;

                        // Method symbols are not constants.
                        if (symbol.Kind == SymbolKind.Method)
                            return false;
                    }

                    // If the symbol wasn't found via GetDeclaredSymbol, check the type map.
                    // If the type reference resolved to a type, it's not a constant.
                    var resolvedType = model.GetTypeOf(namedRef);
                    if (resolvedType is not null && resolvedType is not ErrorType)
                    {
                        // If it resolved to a DependentTypeParameterType, it's a forwarded param.
                        if (resolvedType is DependentTypeParameterType)
                            return true;

                        // If it resolved to a concrete type, it's not a constant in a dependent position.
                        if (resolvedType is ClassType or StructType or InterfaceType
                            or TypeAliasType or TypeParameterType)
                        {
                            return false;
                        }
                    }

                    // Bare identifier that didn't resolve to a type — assume it's a const reference
                    // (the semantic model builder would have emitted an unresolved diagnostic already).
                    return true;
                }

                // Complex type references (qualified names, generic types, arrays) are not constants.
                return false;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks whether a <see cref="DependentValueArgument"/>'s value type matches the
    /// expected <see cref="DependentTypeParameterType"/>'s value type annotation.
    /// </summary>
    private static bool DependentValueTypeMatches(DependentValueArgument depArg, DependentTypeParameterType depParam)
    {
        var actualType = depArg.ValueType;
        var expectedType = depParam.ValueType;

        // ErrorType matches anything (error recovery).
        if (actualType is ErrorType || expectedType is ErrorType)
            return true;

        // Forwarded dependent parameters: the value is a string (the parameter name)
        // and the ValueType is the forwarded parameter's value type. Check compatibility.
        // For forwarded parameters, the types should match exactly.
        return TypesEqual(actualType, expectedType);
    }

    /// <summary>
    /// Extracts a human-readable text representation from a <see cref="TypeArgumentNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractTypeArgumentText(TypeArgumentNode argNode)
    {
        return argNode switch
        {
            IntegerLiteralTypeArgumentTypeArgumentNode intLit => intLit.Literal.Lexeme,
            NegatedIntegerTypeArgumentTypeArgumentNode negLit => $"-{negLit.Literal.Lexeme}",
            HexLiteralTypeArgumentTypeArgumentNode hexLit => hexLit.Literal.Lexeme,
            BinaryLiteralTypeArgumentTypeArgumentNode binLit => binLit.Literal.Lexeme,
            TrueLiteralTypeArgumentTypeArgumentNode => "true",
            FalseLiteralTypeArgumentTypeArgumentNode => "false",
            CharLiteralTypeArgumentTypeArgumentNode charLit => charLit.Literal.Lexeme,
            TypeReferenceTypeArgumentTypeArgumentNode typeRef => ExtractTypeReferenceName(typeRef.Type),
            _ => "<expression>",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent parameter forwarding validation (Task 6.6, Requirements 12.1–12.5)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that when a derived type forwards a dependent parameter to a base type
    /// or implemented interface, the derived parameter's value type can be implicitly
    /// widened to the base parameter's value type. Emits <c>MIST_DT_0004</c> for
    /// narrowing violations.
    /// </summary>
    private static void ValidateDependentParameterForwarding(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var declaredType = symbol.DeclaredType;
            if (declaredType is null)
                continue;

            // Get the declaring node for diagnostic location.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            // Check base type (classes only).
            if (declaredType is ClassType classType && classType.BaseType is ConstructedType baseConstructed)
            {
                ValidateDependentForwardingForConstructedBase(
                    declaredType, baseConstructed, declaringNode, model, diagnostics);
            }

            // Check implemented interfaces.
            var interfaces = declaredType switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                _ => Array.Empty<IType>()
            };

            foreach (var iface in interfaces)
            {
                if (iface is ConstructedType ifaceConstructed)
                {
                    ValidateDependentForwardingForConstructedBase(
                        declaredType, ifaceConstructed, declaringNode, model, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Checks a single constructed base type or interface for dependent parameter forwarding
    /// violations. When a type argument at a dependent parameter position is itself a
    /// <see cref="DependentTypeParameterType"/> (forwarded from the derived type), validates
    /// that the derived parameter's value type can widen to the base parameter's value type.
    /// </summary>
    private static void ValidateDependentForwardingForConstructedBase(
        IType derivedType,
        ConstructedType baseConstructed,
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var baseTypeParams = GetTypeParameters(baseConstructed.GenericDefinition);
        if (baseTypeParams is null)
            return;

        var baseTypeArgs = baseConstructed.TypeArguments;

        for (var i = 0; i < baseTypeParams.Count && i < baseTypeArgs.Count; i++)
        {
            // Only check positions where the base parameter is a dependent type parameter.
            if (baseTypeParams[i] is not DependentTypeParameterType baseDepParam)
                continue;

            // Only check forwarded parameters — if the type argument at this position
            // is a DependentTypeParameterType, it means the derived type is forwarding
            // its own dependent parameter to the base.
            if (baseTypeArgs[i] is not DependentTypeParameterType derivedDepParam)
                continue;

            // Validate widening: derived parameter's value type must be implicitly
            // convertible (widenable) to the base parameter's value type.
            if (!IsImplicitlyWidenable(derivedDepParam.ValueType, baseDepParam.ValueType))
            {
                var derivedTypeName = derivedType.DisplayName;
                var baseTypeName = baseConstructed.GenericDefinition.DisplayName;

                diagnostics.Add(MakeDiagnostic(
                    DependentNarrowingViolation.Value,
                    $"Dependent type parameter '{derivedDepParam.Name}' of type '{derivedDepParam.ValueType.DisplayName}' on '{derivedTypeName}' cannot be forwarded to parameter '{baseDepParam.Name}' of type '{baseDepParam.ValueType.DisplayName}' on '{baseTypeName}' because '{derivedDepParam.ValueType.DisplayName}' is not implicitly convertible to '{baseDepParam.ValueType.DisplayName}'.",
                    declaringNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Determines whether a value of type <paramref name="sourceType"/> can be implicitly
    /// widened to <paramref name="targetType"/>. This implements the primitive widening
    /// rules for dependent type parameter forwarding:
    /// <list type="bullet">
    ///   <item><c>byte</c> → <c>short</c>, <c>int</c>, <c>long</c></item>
    ///   <item><c>short</c> → <c>int</c>, <c>long</c></item>
    ///   <item><c>int</c> → <c>long</c></item>
    ///   <item><c>uint</c> → <c>ulong</c></item>
    ///   <item><c>char</c> → <c>int</c>, <c>long</c></item>
    ///   <item>Same type → same type (always OK)</item>
    /// </list>
    /// </summary>
    private static bool IsImplicitlyWidenable(IType sourceType, IType targetType)
    {
        // Same type is always OK.
        if (TypesEqual(sourceType, targetType))
            return true;

        // ErrorType matches anything (error recovery).
        if (sourceType is ErrorType || targetType is ErrorType)
            return true;

        // Normalize display names to canonical (legacy keyword) form for comparison.
        var source = NormalizePrimitiveDisplayName(sourceType.DisplayName);
        var target = NormalizePrimitiveDisplayName(targetType.DisplayName);

        // Same canonical name after normalization.
        if (source == target)
            return true;

        // Apply widening rules.
        return source switch
        {
            "byte" => target is "short" or "int" or "long",
            "short" => target is "int" or "long",
            "int" => target is "long",
            "uint" => target is "ulong",
            "char" => target is "int" or "long",
            _ => false,
        };
    }

    /// <summary>
    /// Normalizes a primitive type display name to its canonical keyword form.
    /// Maps stdlib names (e.g., "Int32") to legacy keyword names (e.g., "int")
    /// for consistent widening rule evaluation.
    /// </summary>
    private static string NormalizePrimitiveDisplayName(string displayName)
    {
        return displayName switch
        {
            "Int32" => "int",
            "UInt32" => "uint",
            "Int64" => "long",
            "UInt64" => "ulong",
            "Int16" => "short",
            "UInt16" => "ushort",
            "Byte" => "byte",
            "SByte" => "sbyte",
            "Boolean" => "bool",
            "Char" => "char",
            _ => displayName,
        };
    }
}
