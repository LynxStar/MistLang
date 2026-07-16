#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the type checker analyzer.
/// Covers overload resolution, cast operators, pattern matching, unknown-type assignability,
/// type unions, generic constraints, switch expressions, property initializers, refinement types,
/// regex validation, structured concurrency, lambda/closure validation, delegate conversions,
/// and as-operator diagnostics.
/// </summary>
public static class TypeCheckDiagnostics
{
    // ── Overloading diagnostics ──────────────────────────────────────────

    /// <summary>No method or constructor overload matches the supplied argument types.</summary>
    public static readonly DiagnosticDescriptor NoApplicableOverload = new(
        new DiagnosticId("MIST_TC_0031"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No overload for '{0}' matches argument types ({1})");

    /// <summary>Multiple overloads are applicable for the same call, creating ambiguity.</summary>
    public static readonly DiagnosticDescriptor AmbiguousOverload = new(
        new DiagnosticId("MIST_TC_0032"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Call to '{0}' is ambiguous between: {1}");

    // ── Cast operator diagnostics ────────────────────────────────────────

    /// <summary>
    /// A cast operator's source parameter type and target type are both different
    /// from the enclosing type.
    /// </summary>
    public static readonly DiagnosticDescriptor CastOperatorEnclosingTypeViolation = new(
        new DiagnosticId("MIST_TC_0040"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Either the source type or the target type of a cast operator must be the enclosing type '{0}'. Source: '{1}', Target: '{2}'.");

    /// <summary>
    /// Two cast operators in the same type have the same kind (implicit/explicit)
    /// and the same resolved source-target type pair.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateCastOperator = new(
        new DiagnosticId("MIST_TC_0041"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Duplicate {0} cast operator from '{1}' to '{2}' in type '{3}'.");


    /// <summary>
    /// No cast operator is defined from the source type to the target type
    /// in an explicit cast expression.
    /// </summary>
    public static readonly DiagnosticDescriptor NoCastOperator = new(
        new DiagnosticId("MIST_TC_0042"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No cast operator is defined from '{0}' to '{1}'.");

    /// <summary>
    /// Multiple cast operators (explicit and implicit combined) are applicable
    /// for the same explicit cast expression.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousCast = new(
        new DiagnosticId("MIST_TC_0043"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ambiguous cast from '{0}' to '{1}': multiple cast operators are applicable.");

    /// <summary>
    /// Multiple implicit cast operators are applicable for the same conversion
    /// at an assignment, argument, or return site.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousImplicitCast = new(
        new DiagnosticId("MIST_TC_0044"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to the non-void portion '{1}' of '{2}'");

    // ── Pattern matching diagnostics ─────────────────────────────────────

    /// <summary>A type pattern references a type that does not exist in any reachable scope.</summary>
    public static readonly DiagnosticDescriptor PatternUndefinedType = new(
        new DiagnosticId("MIST_TC_0050"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' could not be found in pattern");

    /// <summary>A constant pattern's type is incompatible with the scrutinee type.</summary>
    public static readonly DiagnosticDescriptor PatternConstantTypeMismatch = new(
        new DiagnosticId("MIST_TC_0051"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Constant of type '{0}' is not compatible with scrutinee type '{1}'");

    /// <summary>
    /// A relational pattern is applied to a scrutinee whose type does not support
    /// comparison operators.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternRelationalNonComparable = new(
        new DiagnosticId("MIST_TC_0052"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Relational pattern '{0}' cannot be applied to scrutinee of type '{1}' because it does not support comparison operators");

    /// <summary>
    /// A property pattern references a property name that does not exist on
    /// the scrutinee type or the type specified in the pattern.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternUndefinedProperty = new(
        new DiagnosticId("MIST_TC_0053"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Property '{0}' does not exist on type '{1}'");

    /// <summary>A list pattern is applied to a scrutinee that is not an array type.</summary>
    public static readonly DiagnosticDescriptor PatternListRequiresArray = new(
        new DiagnosticId("MIST_TC_0054"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A list pattern requires the scrutinee to be an array type, but found '{0}'");

    /// <summary>
    /// A list pattern has more fixed elements than the array could contain.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternListTooManyElements = new(
        new DiagnosticId("MIST_TC_0055"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "List pattern has {0} elements but the array can hold at most {1} elements");

    /// <summary>
    /// An <c>or</c> pattern combines alternatives that introduce different sets of
    /// pattern variables or the same names with different types.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternOrInconsistentVariables = new(
        new DiagnosticId("MIST_TC_0056"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Or pattern alternatives must introduce the same pattern variables with the same types");

    /// <summary>
    /// A <c>not</c> pattern contains a subpattern that introduces a pattern variable.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternNotCannotBindVariable = new(
        new DiagnosticId("MIST_TC_0057"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A 'not' pattern cannot introduce pattern variables");

    // ── Unknown-type assignability diagnostics ───────────────────────────

    /// <summary>
    /// Covariant <c>unknown</c> widening is attempted on a generic definition whose
    /// type kind is struct. Widening is restricted to class (reference-type) generics.
    /// </summary>
    public static readonly DiagnosticDescriptor StructGenericUnknownWidening = new(
        new DiagnosticId("MIST_TC_0058"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}': covariant 'unknown' widening is only available for class (reference-type) generics, not struct generics.");

    /// <summary>
    /// A value whose static type is <c>unknown</c> is assigned to a location whose
    /// declared type is not <c>unknown</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownToNonUnknown = new(
        new DiagnosticId("MIST_TC_0060"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign 'unknown' to '{0}': use an 'is' pattern to narrow the value to a concrete type first.");

    /// <summary>
    /// A <c>void</c> expression is assigned to an <c>unknown</c> target.
    /// </summary>
    public static readonly DiagnosticDescriptor VoidToUnknown = new(
        new DiagnosticId("MIST_TC_0061"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to 'unknown': 'void' carries no value and is not assignable to the top type.");

    /// <summary>
    /// An <c>is</c>-pattern narrows to <c>unknown</c>, which is not meaningful.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternNarrowToUnknown = new(
        new DiagnosticId("MIST_TC_0063"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Narrowing to 'unknown' is not meaningful: the pattern is tautologically true and introduces no new type information. Remove the pattern or narrow to a concrete type instead.");

    /// <summary>
    /// An <c>is</c>-pattern narrows to a closed generic with <c>unknown</c> arguments,
    /// which is not meaningful under covariant unknown widening.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternNarrowToClosedGenericWithUnknown = new(
        new DiagnosticId("MIST_TC_0064"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Narrowing to '{0}' is not meaningful: the pattern is tautologically true under covariant unknown widening, and the binding loses information because members that write to a type parameter bound to 'unknown' are filtered. Narrow to a concrete constructed type (e.g., '{1}') instead.");

    /// <summary>
    /// A member access expression has a receiver whose static type is exactly
    /// <c>unknown</c>. No member exists on bare <c>unknown</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor BareUnknownMemberAccess = new(
        new DiagnosticId("MIST_TC_0065"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot access member '{0}' on a value of type '{1}': 'unknown' has no members. Use an 'is' pattern to narrow the value to a concrete type before accessing members.");

    /// <summary>
    /// A binary or unary operator is applied to an operand whose static type is
    /// exactly <c>unknown</c>. No operators are defined for bare <c>unknown</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor BareUnknownOperator = new(
        new DiagnosticId("MIST_TC_0066"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}': no operators are defined for 'unknown'. Use an 'is' pattern to narrow the value to a concrete type before applying the operator.");

    /// <summary>
    /// A cast expression has an operand whose static type is <c>unknown</c> and no
    /// cast operator from <c>unknown</c> to the target type has been declared.
    /// </summary>
    public static readonly DiagnosticDescriptor NoCastOperatorFromUnknown = new(
        new DiagnosticId("MIST_TC_0067"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No cast operator is defined from 'unknown' to '{0}'. Declare 'implicit operator {0}(unknown u)' or 'explicit operator {0}(unknown u)' on '{0}', or use an 'is' pattern to narrow the operand to a concrete type first.");

    // ── Type-unions diagnostics ──────────────────────────────────────────

    /// <summary>
    /// A non-constituent type is assigned to a union-typed target.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionNonConstituentAssignment = new(
        new DiagnosticId("MIST_TC_0070"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}': '{0}' is not a constituent of the union type.");

    /// <summary>
    /// A union type is assigned to a non-union target without narrowing.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionToNonUnionAssignment = new(
        new DiagnosticId("MIST_TC_0071"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}': use a pattern match ('is {1} x') to narrow the union type first.");

    /// <summary>
    /// A union type is assigned to another union type but at least one constituent
    /// of the source union is not present in the target union.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionIncompatibleConstituents = new(
        new DiagnosticId("MIST_TC_0072"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}': constituent type(s) {2} not present in the target union.");

    /// <summary>
    /// A member access or method call is performed on a union-typed value and the
    /// member does not exist on all constituent types.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionMemberNotOnAllConstituents = new(
        new DiagnosticId("MIST_TC_0073"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Member '{0}' does not exist on all constituents of union type '{1}'. Missing on: {2}.");

    /// <summary>
    /// A type pattern on a union-typed scrutinee references a type that is not one
    /// of the union's constituent types. The pattern will never match at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionPatternNonConstituent = new(
        new DiagnosticId("MIST_TC_0074"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Type '{0}' is not a constituent of union type '{1}'; this pattern will never match");

    /// <summary>
    /// A union type annotation resolves to fewer than two distinct constituent types
    /// after flattening and deduplication.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionDegenerateType = new(
        new DiagnosticId("MIST_TC_0075"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Union type '{0}' has fewer than two distinct constituent types after deduplication");


    // ── Readonly generic constraint diagnostics ──────────────────────────

    /// <summary>
    /// A type argument does not satisfy the 'readonly' constraint on a type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintReadonlyViolation = new(
        new DiagnosticId("MIST_TC_0077"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type argument '{0}' does not satisfy the 'readonly' constraint on type parameter '{1}'.");

    /// <summary>
    /// A type argument does not satisfy the 'mutable' constraint on a type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintMutableViolation = new(
        new DiagnosticId("MIST_TC_0078"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type argument '{0}' does not satisfy the 'mutable' constraint on type parameter '{1}' (type is readonly).");

    // ── Readonly abstract contract diagnostic ────────────────────────────

    /// <summary>
    /// A type implements a readonly abstract but the implementing member is not readonly.
    /// </summary>
    public static readonly DiagnosticDescriptor ReadonlyContractViolation = new(
        new DiagnosticId("MIST_TC_0079"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Member '{0}' in type '{1}' must be readonly to satisfy the readonly contract on abstract '{2}'.");

    // ── Switch-expressions diagnostics ───────────────────────────────────

    /// <summary>
    /// A switch expression does not cover all possible values of the scrutinee's
    /// static type.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchNotExhaustive = new(
        new DiagnosticId("MIST_TC_0090"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Switch expression is not exhaustive. Missing cases: {0}");

    /// <summary>
    /// The result expressions of switch arms have incompatible types.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchIncompatibleArmTypes = new(
        new DiagnosticId("MIST_TC_0091"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Switch arms have incompatible types: '{0}' and '{1}'");

    /// <summary>
    /// A <c>when</c> guard expression in a switch arm does not evaluate to type <c>bool</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchGuardNotBool = new(
        new DiagnosticId("MIST_TC_0092"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "When guard must be a boolean expression, got '{0}'");

    /// <summary>A switch arm is unreachable because it is subsumed by a preceding arm.</summary>
    public static readonly DiagnosticDescriptor SwitchUnreachableArm = new(
        new DiagnosticId("MIST_TC_0093"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Switch arm is unreachable");

    // ── Delegate overload ambiguity diagnostic ───────────────────────────

    /// <summary>
    /// Overload resolution cannot disambiguate between multiple delegate-typed
    /// overloads for the same argument.
    /// </summary>
    public static readonly DiagnosticDescriptor DelegateOverloadAmbiguity = new(
        new DiagnosticId("MIST_TC_0094"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ambiguous call: argument is compatible with both '{0}' and '{1}'");

    // ── Array literals diagnostic ────────────────────────────────────────

    /// <summary>
    /// An array literal contains elements whose types are incompatible.
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayLiteralIncompatibleTypes = new(
        new DiagnosticId("MIST_TC_0095"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Array literal elements have incompatible types: '{0}' and '{1}'. All elements must share a common type.");

    // ── Refinement-types diagnostics ─────────────────────────────────────

    /// <summary>
    /// A compile-time constant value is assigned to a refinement-typed variable
    /// and does not satisfy the refinement predicate.
    /// </summary>
    public static readonly DiagnosticDescriptor RefinementLiteralViolation = new(
        new DiagnosticId("MIST_TC_0097"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Value '{0}' does not satisfy refinement '{1}' on type '{2}'");

    /// <summary>
    /// A non-constant expression of the base type is assigned to a refinement-typed
    /// variable without proving the refinement via pattern matching.
    /// </summary>
    public static readonly DiagnosticDescriptor RefinementNonConstantAssignment = new(
        new DiagnosticId("MIST_TC_0098"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}' without proving the refinement. Use pattern matching to validate at runtime");

    /// <summary>
    /// A value of one refinement type is assigned to a variable of a different refinement
    /// type with the same base type, but the compiler cannot prove subset relationship.
    /// </summary>
    public static readonly DiagnosticDescriptor RefinementSubsetNotProvable = new(
        new DiagnosticId("MIST_TC_0099"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}' because the compiler cannot prove that '{2}' implies '{3}'");

    /// <summary>
    /// A refinement predicate references a mutable member via a property pattern.
    /// </summary>
    public static readonly DiagnosticDescriptor RefinementMutableMember = new(
        new DiagnosticId("MIST_TC_0100"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Refinement predicate on type '{0}' references mutable member '{1}'. All members in a refinement predicate must be readonly.");

    /// <summary>
    /// A refinement type is used in a pattern match but the scrutinee type is not
    /// compatible with the refinement's base type.
    /// </summary>
    public static readonly DiagnosticDescriptor PatternIncompatibleRefinement = new(
        new DiagnosticId("MIST_TC_0101"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Refinement type '{0}' has base type '{1}' which is not compatible with scrutinee type '{2}'; this pattern will never match");

    // ── Enum base type diagnostics ───────────────────────────────────────

    /// <summary>
    /// An enum declaration's base type (explicit or inferred) is not readonly.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumBaseTypeNotReadonly = new(
        new DiagnosticId("MIST_TC_0102"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Enum '{0}' has a non-readonly base type '{1}'. Enum base types must be readonly.");

    /// <summary>
    /// A constituent type within a union base type of an enum is not readonly.
    /// </summary>
    public static readonly DiagnosticDescriptor EnumUnionConstituentNotReadonly = new(
        new DiagnosticId("MIST_TC_0103"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Enum '{0}' has a non-readonly union constituent type '{1}'. All constituent types in an enum's union base must be readonly.");

    // ── Structured-concurrency diagnostics ───────────────────────────────

    /// <summary>
    /// A method declaration has both the <c>async</c> and <c>intrinsic</c> modifiers.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncIntrinsicIncompatible = new(
        new DiagnosticId("MIST_TC_0104"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method '{0}' cannot be both 'async' and 'intrinsic'");

    /// <summary>
    /// The <c>await</c> functor is applied to an expression whose type is not
    /// <c>Task&lt;T&gt;</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor AwaitNonTaskOperand = new(
        new DiagnosticId("MIST_TC_0105"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'await' functor requires an operand of type 'Task<T>', but got '{0}'");

    /// <summary>
    /// An <c>async</c> method explicitly declares <c>Task&lt;T&gt;</c> as its return type.
    /// The compiler wraps the return type automatically.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncExplicitTaskReturnType = new(
        new DiagnosticId("MIST_TC_0106"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Async method '{0}' must not explicitly declare 'Task<T>' as its return type; the compiler wraps the return type automatically");

    /// <summary>
    /// The <c>async</c> functor is applied to an expression that is not a method invocation.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncFunctorNonInvocationOperand = new(
        new DiagnosticId("MIST_TC_0107"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'async' functor requires a method invocation operand, but got {0}");

    /// <summary>
    /// The <c>async</c> modifier is applied to a constructor or operator declaration.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncOnConstructorOrOperator = new(
        new DiagnosticId("MIST_TC_0108"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'async' modifier is not valid on {0}");

    /// <summary>
    /// The <c>async</c> functor is applied to an async method in an async context,
    /// producing <c>Task&lt;Task&lt;T&gt;&gt;</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncFunctorDoubleWrap = new(
        new DiagnosticId("MIST_TC_0109"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Applying 'async' functor to an async method in an async context produces 'Task<Task<T>>' -- this is likely unintended");

    // ── Optional-types diagnostics ───────────────────────────────────────

    /// <summary>
    /// The optional chaining operator <c>?.</c> is applied to an expression whose type
    /// does not contain <c>void</c> as a union constituent.
    /// </summary>
    public static readonly DiagnosticDescriptor OptionalChainingRequiresOptionalType = new(
        new DiagnosticId("MIST_TC_0111"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Optional chaining operator '?.' requires a type of the form 'T | void', but found '{0}'");

    // ── Tuple-pattern-matching diagnostics ───────────────────────────────

    /// <summary>
    /// A tuple positional pattern is applied to a scrutinee whose type is not a tuple.
    /// </summary>
    public static readonly DiagnosticDescriptor TuplePositionalPatternRequiresTupleType = new(
        new DiagnosticId("MIST_TC_0113"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Positional pattern requires a tuple type, but the scrutinee has type '{0}'");

    /// <summary>
    /// A tuple positional pattern has a different number of elements than the
    /// scrutinee's tuple type arity.
    /// </summary>
    public static readonly DiagnosticDescriptor TuplePositionalPatternArityMismatch = new(
        new DiagnosticId("MIST_TC_0114"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Tuple positional pattern has {0} elements, but the tuple type '{1}' has {2} elements");


    // ── First-class-functions: lambda validation diagnostics ─────────────

    /// <summary>
    /// A parenthesized lambda parameter list mixes typed and untyped parameters.
    /// </summary>
    public static readonly DiagnosticDescriptor MixedLambdaParameterTypes = new(
        new DiagnosticId("MIST_TC_0115"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Lambda parameters must be all typed or all untyped; mixing typed and untyped parameters is not allowed");

    /// <summary>
    /// A lambda has implicit (untyped) parameters and no target Func type is available
    /// to infer parameter types from.
    /// </summary>
    public static readonly DiagnosticDescriptor CannotInferLambdaParameterTypes = new(
        new DiagnosticId("MIST_TC_0116"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot infer lambda parameter types; provide explicit parameter types or assign to a variable with an explicit Func type");

    /// <summary>
    /// A block lambda has multiple return statements with inconsistent types that
    /// cannot be unified.
    /// </summary>
    public static readonly DiagnosticDescriptor InconsistentLambdaReturnTypes = new(
        new DiagnosticId("MIST_TC_0117"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Block lambda has inconsistent return types; all return statements must return compatible types");

    /// <summary>
    /// A block lambda's target return type is non-void but the block contains no
    /// return statement with a value.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingReturnInNonVoidLambda = new(
        new DiagnosticId("MIST_TC_0118"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Block lambda with return type '{0}' must contain a return statement with a value");

    // ── First-class-functions: Func invocation diagnostics ───────────────

    /// <summary>
    /// A Func-typed expression is invoked with the wrong number of arguments.
    /// </summary>
    public static readonly DiagnosticDescriptor FuncInvocationArgumentCountMismatch = new(
        new DiagnosticId("MIST_TC_0119"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Func invocation expects {0} argument(s) but was given {1}");

    /// <summary>
    /// A Func-typed expression is invoked with an argument whose type is not
    /// assignable to the corresponding Func parameter type.
    /// </summary>
    public static readonly DiagnosticDescriptor FuncInvocationArgumentTypeMismatch = new(
        new DiagnosticId("MIST_TC_0120"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Argument {0} of type '{1}' is not assignable to Func parameter type '{2}'");

    /// <summary>
    /// A method group conversion is ambiguous -- multiple overloads match the target
    /// Func type's signature.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousMethodGroupConversion = new(
        new DiagnosticId("MIST_TC_0121"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method group '{0}' is ambiguous; multiple overloads match the target type '{1}': {2}");

    /// <summary>
    /// A static lambda references an enclosing variable, instance field, or <c>this</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor StaticLambdaCaptureViolation = new(
        new DiagnosticId("MIST_TC_0122"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A static lambda cannot reference {0}");

    // ── First-class-functions: capture restriction diagnostics ────────────

    /// <summary>
    /// A lambda captures a variable whose type is a ref struct.
    /// </summary>
    public static readonly DiagnosticDescriptor CaptureRefStruct = new(
        new DiagnosticId("MIST_TC_0123"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot capture variable '{0}' of ref struct type '{1}' in a lambda; ref struct values cannot live on the heap");

    /// <summary>
    /// A lambda captures a variable with <c>owned</c> ownership tier.
    /// </summary>
    public static readonly DiagnosticDescriptor CaptureOwned = new(
        new DiagnosticId("MIST_TC_0124"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot capture owned variable '{0}' in a lambda; ownership transfer semantics conflict with closure capture");

    /// <summary>
    /// A lambda captures a variable with <c>borrowed</c>, <c>frozen</c>, or
    /// <c>readonly ref</c> ownership tier.
    /// </summary>
    public static readonly DiagnosticDescriptor CaptureBorrowedOrFrozen = new(
        new DiagnosticId("MIST_TC_0125"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot capture {0} variable '{1}' in a lambda; its lifetime is bounded to the current scope");

    /// <summary>
    /// An <c>await</c> expression appears inside a non-async lambda body.
    /// </summary>
    public static readonly DiagnosticDescriptor AwaitInNonAsyncLambda = new(
        new DiagnosticId("MIST_TC_0126"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use 'await' in a non-async lambda; add the 'async' modifier to the lambda");

    /// <summary>
    /// A type argument does not satisfy the 'owned' constraint on a type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintOwnedViolation = new(
        new DiagnosticId("MIST_TC_0127"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type argument '{0}' does not satisfy the 'owned' constraint on type parameter '{1}'.");

    // ── Delegate-func-conversion diagnostics ─────────────────────────────

    /// <summary>
    /// A value typed as <c>System.Delegate</c> is invoked directly without a cast
    /// to a specific delegate or func type.
    /// </summary>
    public static readonly DiagnosticDescriptor BaseDelegateNotInvocable = new(
        new DiagnosticId("MIST_TC_0128"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot invoke a value of type 'System.Delegate' directly: cast to a specific delegate type or func<...> first");

    /// <summary>
    /// A func or lambda's signature is incompatible with the target delegate type.
    /// </summary>
    public static readonly DiagnosticDescriptor DelegateSignatureMismatch = new(
        new DiagnosticId("MIST_TC_0129"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot convert '{0}' to delegate type '{1}': {2}");

    /// <summary>
    /// An assignment or conversion is attempted between two distinct named delegates.
    /// Named delegates are nominally distinct even when their signatures match.
    /// </summary>
    public static readonly DiagnosticDescriptor DelegateNominalMismatch = new(
        new DiagnosticId("MIST_TC_0130"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot convert delegate '{0}' to '{1}': named delegates are nominally distinct even when their signatures match");

    /// <summary>
    /// <c>System.Delegate</c> is implicitly narrowed to a specific delegate or func type.
    /// </summary>
    public static readonly DiagnosticDescriptor DelegateCannotNarrowBaseDelegate = new(
        new DiagnosticId("MIST_TC_0131"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert 'System.Delegate' to '{0}': use a cast to a specific delegate or func type");

    /// <summary>
    /// A method group is used in a position targeting a delegate type but no overload
    /// of the method has a compatible signature.
    /// </summary>
    public static readonly DiagnosticDescriptor MethodGroupNoMatchingOverload = new(
        new DiagnosticId("MIST_TC_0132"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method group '{0}' has no overload with a signature compatible with delegate type '{1}'");

    /// <summary>
    /// A method group is used in a position targeting a delegate type and multiple
    /// overloads have compatible signatures.
    /// </summary>
    public static readonly DiagnosticDescriptor MethodGroupMultipleMatchingOverloads = new(
        new DiagnosticId("MIST_TC_0133"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method group '{0}' is ambiguous: multiple overloads match delegate type '{1}'");

    // ── As-operator diagnostics ──────────────────────────────────────────

    /// <summary>
    /// An <c>as</c> expression is used between two types that have no type relationship.
    /// </summary>
    public static readonly DiagnosticDescriptor AsOperatorNoRelationship = new(
        new DiagnosticId("MIST_TC_0160"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'as' operator cannot be used between '{0}' and '{1}' because no type relationship exists. Use an explicit cast operator if a user-defined conversion is needed.");

    /// <summary>An <c>as</c> expression targets <c>void</c>.</summary>
    public static readonly DiagnosticDescriptor AsOperatorVoidTarget = new(
        new DiagnosticId("MIST_TC_0161"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'as void' is not a valid conversion.");

    /// <summary>
    /// The left operand of an <c>as</c> expression has a <c>void</c> static type.
    /// </summary>
    public static readonly DiagnosticDescriptor AsOperatorVoidSource = new(
        new DiagnosticId("MIST_TC_0162"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A void-typed expression cannot be used with the 'as' operator.");

    // ── Regex-type diagnostics ───────────────────────────────────────────

    /// <summary>
    /// A regex literal pattern is not a syntactically valid .NET regular expression.
    /// </summary>
    public static readonly DiagnosticDescriptor RegexPatternInvalid = new(
        new DiagnosticId("MIST_TC_REGEX_INVALID"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Invalid regex pattern: {0} at offset {1}");

    /// <summary>
    /// A regex literal pattern is used in an <c>is</c> expression or switch arm but
    /// the scrutinee type is not <c>string</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor RegexScrutineeNotString = new(
        new DiagnosticId("MIST_TC_REGEX_SCRUTINEE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Regex patterns require a 'string' scrutinee, but found '{0}'");

    /// <summary>
    /// A regex refinement predicate is applied to a non-<c>string</c> base type.
    /// </summary>
    public static readonly DiagnosticDescriptor RegexRefinementOnNonString = new(
        new DiagnosticId("MIST_TC_REGEX_REFINEMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Regex refinement predicates are only valid on 'string' types, but found '{0}'");


    // ── Generic constraint satisfaction diagnostics ───────────────────────

    /// <summary>
    /// A type argument does not implement a required abstract constraint on a type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintAbstractNotImplemented = new(
        new DiagnosticId("MIST_CONSTRAINT_0009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not implement abstract type '{1}' required by type parameter '{2}' of '{3}'.");

    /// <summary>
    /// A type argument does not derive from a required base class constraint on a type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintBaseClassNotDerived = new(
        new DiagnosticId("MIST_CONSTRAINT_0011"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not derive from '{1}' required by type parameter '{2}' of '{3}'.");

    /// <summary>
    /// A type argument must be a reference type to satisfy the 'class' constraint
    /// but is a value type.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintClassViolation = new(
        new DiagnosticId("MIST_CONSTRAINT_0012"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must be a reference type to satisfy the 'class' constraint on type parameter '{1}'.");

    /// <summary>
    /// A type argument must be a value type to satisfy the 'struct' constraint
    /// but is a reference type.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintStructViolation = new(
        new DiagnosticId("MIST_CONSTRAINT_0013"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must be a value type to satisfy the 'struct' constraint on type parameter '{1}'.");

    /// <summary>
    /// A type argument must have a public parameterless constructor to satisfy
    /// the 'new()' constraint.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintParameterlessCtorViolation = new(
        new DiagnosticId("MIST_CONSTRAINT_0014"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must have a public parameterless constructor to satisfy the 'new()' constraint on type parameter '{1}'.");

    /// <summary>
    /// A type argument must have a public constructor with specific parameters to
    /// satisfy a parameterized constructor constraint.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintParameterizedCtorViolation = new(
        new DiagnosticId("MIST_CONSTRAINT_0015"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must have a public constructor with parameters '({1})' to satisfy the 'new({1})' constraint on type parameter '{2}'.");

    /// <summary>
    /// A <c>new T(...)</c> expression is used in a generic body and the argument types
    /// do not match any declared constructor constraint signature on that type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintNewTNoMatch = new(
        new DiagnosticId("MIST_CONSTRAINT_0016"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No constructor constraint on type parameter '{0}' matches arguments '({1})'. Available constructor constraints: {2}.");

    /// <summary>
    /// A derived type inherits from a constrained generic base but does not declare
    /// constraints at least as restrictive on the corresponding type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintPropagationInsufficient = new(
        new DiagnosticId("MIST_CONSTRAINT_0017"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must declare constraints at least as restrictive as '{1}' for type parameter '{2}'.");

    /// <summary>
    /// A type argument's substructural discipline does not satisfy the substructural
    /// constraint on the corresponding type parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor ConstraintSubstructuralViolation = new(
        new DiagnosticId("MIST_CONSTRAINT_0018"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not satisfy the '{1}' constraint on type parameter '{2}' of '{3}'.");

    // ── Property-initializer diagnostics ─────────────────────────────────

    /// <summary>
    /// A property initializer block references a member name that does not exist
    /// as a property or field on the constructed type.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerMemberNotFound = new(
        new DiagnosticId("MIST_PI_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not contain a property or field named '{1}'");

    /// <summary>
    /// The value expression type in a property initializer assignment is not
    /// assignable to the target member's declared type.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerTypeMismatch = new(
        new DiagnosticId("MIST_PI_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}' of type '{2}'");

    /// <summary>
    /// A property initializer targets a property that has no accessible <c>set</c>
    /// or <c>init</c> accessor.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerNoSetOrInit = new(
        new DiagnosticId("MIST_PI_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Property '{0}' has no accessible 'set' or 'init' accessor and cannot be assigned in an initializer");

    /// <summary>
    /// A property initializer targets a field that is readonly (explicit, inferred,
    /// or implicit).
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerReadonlyField = new(
        new DiagnosticId("MIST_PI_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' is readonly and cannot be assigned in an initializer");

    /// <summary>
    /// A property initializer targets a member that is inaccessible due to its
    /// protection level.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerInaccessible = new(
        new DiagnosticId("MIST_PI_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level");

    /// <summary>
    /// The same member name appears more than once in a property initializer block.
    /// </summary>
    public static readonly DiagnosticDescriptor PropertyInitializerDuplicate = new(
        new DiagnosticId("MIST_PI_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Member '{0}' is assigned more than once in the initializer");

    // ── Core type-check diagnostics (MIST_TC_0001–0030) ─────────────────

    /// <summary>An implicit conversion does not exist between two types.</summary>
    public static readonly DiagnosticDescriptor ImplicitConversionNotExists = new(
        new DiagnosticId("MIST_TC_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert type '{0}' to '{1}'. An explicit cast exists (are you missing a cast?)");

    /// <summary>Expression body type is not assignable to expected return type.</summary>
    public static readonly DiagnosticDescriptor ExpressionBodyTypeMismatch = new(
        new DiagnosticId("MIST_TC_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Expression lambda body type '{0}' is not assignable to expected return type '{1}'");

    /// <summary>Condition expression must be of type bool.</summary>
    public static readonly DiagnosticDescriptor ConditionNotBool = new(
        new DiagnosticId("MIST_TC_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Condition must be of type 'bool', but found '{0}'");

    /// <summary>Cannot return a value from a void method.</summary>
    public static readonly DiagnosticDescriptor ReturnValueInVoidMethod = new(
        new DiagnosticId("MIST_TC_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot return '{0}' from method with return type 'void'");

    /// <summary>A var declaration requires an initializer.</summary>
    public static readonly DiagnosticDescriptor VarRequiresInitializer = new(
        new DiagnosticId("MIST_TC_0009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'var' declaration requires an initializer");

    /// <summary>Indexer parameter type mismatch.</summary>
    public static readonly DiagnosticDescriptor IndexerParameterTypeMismatch = new(
        new DiagnosticId("MIST_TC_0010"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Indexer parameter must be of type '{0}', but found '{1}'");

    /// <summary>Array constructor requires exactly one function argument.</summary>
    public static readonly DiagnosticDescriptor ArrayCtorRequiresOneArg = new(
        new DiagnosticId("MIST_TC_0011"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'new Array<{0}, Size>(fn)' requires exactly one argument: a function of type 'Func<int, {0}>'.");

    /// <summary>Array size must be of type int.</summary>
    public static readonly DiagnosticDescriptor ArraySizeNotInt = new(
        new DiagnosticId("MIST_TC_0012"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Array size must be of type 'int', but found '{0}'");

    /// <summary>No constructor matches the provided arguments.</summary>
    public static readonly DiagnosticDescriptor NoMatchingConstructor = new(
        new DiagnosticId("MIST_TC_0013"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No constructor on '{0}' matches the chained arguments");

    /// <summary>Member is inaccessible due to protection level.</summary>
    public static readonly DiagnosticDescriptor MemberInaccessible = new(
        new DiagnosticId("MIST_TC_0014"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level");

    /// <summary>Static member cannot be accessed via an instance.</summary>
    public static readonly DiagnosticDescriptor StaticMemberViaInstance = new(
        new DiagnosticId("MIST_TC_0015"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Static member '{0}' cannot be accessed via an instance");

    /// <summary>Instance member cannot be accessed via a type name.</summary>
    public static readonly DiagnosticDescriptor InstanceMemberViaType = new(
        new DiagnosticId("MIST_TC_0016"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Instance member '{0}' cannot be accessed via a type name");

    /// <summary>Program does not contain a valid entry point.</summary>
    public static readonly DiagnosticDescriptor NoValidEntryPoint = new(
        new DiagnosticId("MIST_TC_0017"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Program does not contain a valid entry point (static void Main)");

    /// <summary>Program contains multiple entry points.</summary>
    public static readonly DiagnosticDescriptor MultipleEntryPoints = new(
        new DiagnosticId("MIST_TC_0018"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Program contains multiple entry points");

    /// <summary>Property has no accessible get accessor.</summary>
    public static readonly DiagnosticDescriptor PropertyNoGetAccessor = new(
        new DiagnosticId("MIST_TC_0019"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Property '{0}' has no accessible 'get' accessor");

    /// <summary>Property has no accessible set or init accessor.</summary>
    public static readonly DiagnosticDescriptor PropertyNoSetAccessor = new(
        new DiagnosticId("MIST_TC_0020"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Property '{0}' has no accessible 'set' or 'init' accessor");

    /// <summary>Cannot assign type to property type.</summary>
    public static readonly DiagnosticDescriptor PropertyAssignmentTypeMismatch = new(
        new DiagnosticId("MIST_TC_0021"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to property of type '{1}'");

    /// <summary>Accessor modifier must be more restrictive than property access level.</summary>
    public static readonly DiagnosticDescriptor AccessorModifierNotRestrictive = new(
        new DiagnosticId("MIST_TC_0022"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Accessor modifier must be more restrictive than the property's access level");

    /// <summary>Field keyword can only be used inside a property accessor body.</summary>
    public static readonly DiagnosticDescriptor FieldKeywordOutsideAccessor = new(
        new DiagnosticId("MIST_TC_0023"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'field' keyword can only be used inside a property accessor body");

    /// <summary>Field keyword cannot be used in a computed property.</summary>
    public static readonly DiagnosticDescriptor FieldKeywordInComputedProperty = new(
        new DiagnosticId("MIST_TC_0024"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'field' keyword cannot be used in a computed property (no backing field)");

    /// <summary>Init-only property can only be assigned in constructor or initializer.</summary>
    public static readonly DiagnosticDescriptor InitOnlyPropertyOutsideCtor = new(
        new DiagnosticId("MIST_TC_0025"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Init-only property '{0}' can only be assigned in a constructor or object initializer");

    /// <summary>this cannot be referenced in a static property accessor.</summary>
    public static readonly DiagnosticDescriptor ThisInStaticAccessor = new(
        new DiagnosticId("MIST_TC_0026"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'this' cannot be referenced in a static property accessor");

    /// <summary>Static property cannot be accessed through an instance reference.</summary>
    public static readonly DiagnosticDescriptor StaticPropertyViaInstance = new(
        new DiagnosticId("MIST_TC_0027"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Static property '{0}' cannot be accessed through an instance reference");

    /// <summary>Expression body type is not compatible with property type.</summary>
    public static readonly DiagnosticDescriptor PropertyBodyTypeMismatch = new(
        new DiagnosticId("MIST_TC_0028"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Expression body type '{0}' is not compatible with property type '{1}'");

    /// <summary>Duplicate indexer declaration with same parameter types.</summary>
    public static readonly DiagnosticDescriptor DuplicateIndexer = new(
        new DiagnosticId("MIST_TC_0029"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Duplicate indexer declaration with the same parameter types");

    /// <summary>Type does not implement abstract property.</summary>
    public static readonly DiagnosticDescriptor MissingAbstractPropertyImpl = new(
        new DiagnosticId("MIST_TC_0030"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' does not implement abstract property '{1}'");

    /// <summary>Void-coalescing assignment cannot be used as a sub-expression.</summary>
    public static readonly DiagnosticDescriptor VoidCoalescingNotStatement = new(
        new DiagnosticId("MIST_TC_0047"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The void-coalescing assignment '??=' is a statement and cannot be used as a sub-expression");

    /// <summary>Cannot assign tuple element type at position.</summary>
    public static readonly DiagnosticDescriptor TupleDeconstructionTypeMismatch = new(
        new DiagnosticId("MIST_TC_0062"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign tuple element of type '{0}' to '{1}' at position {2}.");

    // ── Extension method diagnostics ─────────────────────────────────────

    /// <summary>Type parameter inference failed for a generic method call.</summary>
    public static readonly DiagnosticDescriptor TypeInferenceConflict = new(
        new DiagnosticId("MIST_TC_0081"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type parameter '{0}' of method '{1}' has conflicting inferred types: '{2}' and '{3}'. Provide explicit type arguments.");

    /// <summary>Type parameters cannot be inferred from arguments.</summary>
    public static readonly DiagnosticDescriptor TypeInferenceFailed = new(
        new DiagnosticId("MIST_TC_0080"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type parameter {0} of method '{1}' cannot be inferred from the arguments. Provide explicit type arguments.");

    /// <summary>Cannot infer element type for empty array literal.</summary>
    public static readonly DiagnosticDescriptor EmptyArrayLiteralNoType = new(
        new DiagnosticId("MIST_TC_0096"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot infer element type for empty array literal '[]'. Provide an explicit array type annotation (e.g., 'int[] x = [];').");

    // ── Stack-only types diagnostics ─────────────────────────────────────

    /// <summary>Cannot assign stackalloc result to incompatible target.</summary>
    public static readonly DiagnosticDescriptor StackallocAssignmentMismatch = new(
        new DiagnosticId("MIST_TC_0150"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign stackalloc result of type '{0}' to target type '{1}'. A stackalloc expression can only be assigned to a 'var' declaration, 'Span<T>', or 'ReadOnlySpan<T>'.");

    /// <summary>Stackalloc size must be of type int.</summary>
    public static readonly DiagnosticDescriptor StackallocSizeNotInt = new(
        new DiagnosticId("MIST_TC_0151"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Stackalloc size must be of type 'int', but found '{0}'");

    /// <summary>Cannot use ref struct as a stackalloc element type.</summary>
    public static readonly DiagnosticDescriptor StackallocRefStructElement = new(
        new DiagnosticId("MIST_TC_0154"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use ref struct '{0}' as a stackalloc element type. Ref structs cannot be stored in contiguous stack-allocated memory.");

    /// <summary>Cannot assign through a ReadOnlySpan indexer.</summary>
    public static readonly DiagnosticDescriptor ReadOnlySpanIndexerAssignment = new(
        new DiagnosticId("MIST_TC_0156"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign through a ReadOnlySpan indexer; ReadOnlySpan elements are read-only");

    /// <summary>Span kind mismatch between mutable Span and ReadOnlySpan.</summary>
    public static readonly DiagnosticDescriptor SpanKindMismatch = new(
        new DiagnosticId("MIST_TC_0157"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Span kind mismatch: cannot assign '{0}' to '{1}'. Mutable Span and ReadOnlySpan are not interchangeable.");

    /// <summary>Span element type mismatch between two spans of the same kind.</summary>
    public static readonly DiagnosticDescriptor SpanElementTypeMismatch = new(
        new DiagnosticId("MIST_TC_0157"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Span element type mismatch: cannot assign '{0}' to '{1}'. The element types are not compatible.");

    // ── Foreach diagnostics ──────────────────────────────────────────────

    /// <summary>Type does not implement IEnumerable and cannot be used in foreach.</summary>
    public static readonly DiagnosticDescriptor ForeachNotEnumerable = new(
        new DiagnosticId("MIST_TC_FOREACH_NOT_ENUMERABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' does not implement 'IEnumerable<TElement>' and cannot be used in a foreach statement");

    /// <summary>Cannot convert element type to declared type in foreach.</summary>
    public static readonly DiagnosticDescriptor ForeachTypeMismatch = new(
        new DiagnosticId("MIST_TC_FOREACH_TYPE_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot convert element type '{0}' to declared type '{1}' in foreach statement");

    // ── Ref struct restriction diagnostics ───────────────────────────────

    /// <summary>Ref struct cannot be used as a field in non-ref-struct type.</summary>
    public static readonly DiagnosticDescriptor RefStructFieldRestriction = new(
        new DiagnosticId("MIST2002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct type '{0}' cannot be used as a field in non-ref-struct type '{1}'");

    /// <summary>Ref struct cannot be used as a property type in non-ref-struct type.</summary>
    public static readonly DiagnosticDescriptor RefStructPropertyRestriction = new(
        new DiagnosticId("MIST2003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct type '{0}' cannot be used as a property type in non-ref-struct type '{1}'");

    /// <summary>Ref struct cannot be used as a generic type argument.</summary>
    public static readonly DiagnosticDescriptor RefStructGenericArgRestriction = new(
        new DiagnosticId("MIST2004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct type '{0}' cannot be used as a generic type argument");

    /// <summary>Ref struct value cannot be converted to abstract type.</summary>
    public static readonly DiagnosticDescriptor RefStructToAbstract = new(
        new DiagnosticId("MIST2007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct value cannot be converted to abstract type '{0}'");

    // ── Abstract instantiation diagnostic ────────────────────────────────

    /// <summary>Cannot create an instance of an abstract type.</summary>
    public static readonly DiagnosticDescriptor AbstractInstantiation = new(
        new DiagnosticId("MIST_ABSTRACT_INSTANTIATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create an instance of abstract type '{0}'.");

    // ── Anonymous type diagnostics ───────────────────────────────────────

    /// <summary>Cannot compare anonymous types with different shapes.</summary>
    public static readonly DiagnosticDescriptor AnonEqualityMismatch = new(
        new DiagnosticId("MIST_ANON_EQUALITY_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot compare anonymous types with different shapes");

    /// <summary>Anonymous types cannot be used as field types.</summary>
    public static readonly DiagnosticDescriptor AnonEscapeField = new(
        new DiagnosticId("MIST_ANON_ESCAPE_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Anonymous types cannot be used as field types");

    /// <summary>Anonymous types cannot be used as generic type arguments.</summary>
    public static readonly DiagnosticDescriptor AnonEscapeGeneric = new(
        new DiagnosticId("MIST_ANON_ESCAPE_GENERIC"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Anonymous types have no denotable name and cannot be used as generic type arguments");

    /// <summary>Anonymous types cannot appear in method signatures.</summary>
    public static readonly DiagnosticDescriptor AnonEscapeSignature = new(
        new DiagnosticId("MIST_ANON_ESCAPE_SIGNATURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Anonymous types cannot appear in method signatures");

    /// <summary>Anonymous type properties are read-only.</summary>
    public static readonly DiagnosticDescriptor AnonReadonly = new(
        new DiagnosticId("MIST_ANON_READONLY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Anonymous type properties are read-only");

    // ── Readonly local diagnostics ───────────────────────────────────────

    /// <summary>Cannot reassign a readonly local variable.</summary>
    public static readonly DiagnosticDescriptor ReadonlyLocalReassignment = new(
        new DiagnosticId("MIST_READONLY_LOCAL_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot reassign readonly local variable '{0}'");

    /// <summary>Cannot pass a readonly local variable by mutable reference.</summary>
    public static readonly DiagnosticDescriptor ReadonlyLocalRefArgument = new(
        new DiagnosticId("MIST_READONLY_LOCAL_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot pass readonly local variable '{0}' by mutable reference");

    // ── Ref parameter and ref local diagnostics ──────────────────────────

    /// <summary>A ref argument must be an assignable storage location.</summary>
    public static readonly DiagnosticDescriptor RefArgNotStorageLocation = new(
        new DiagnosticId("MIST_REF_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A ref argument must be an assignable variable, field, or array element; '{0}' is not a valid storage location");

    /// <summary>Argument for ref parameter must be prefixed with ref keyword.</summary>
    public static readonly DiagnosticDescriptor RefKeywordMissing = new(
        new DiagnosticId("MIST_REF_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Argument for ref parameter '{0}' must be prefixed with the 'ref' keyword");

    /// <summary>Parameter is not ref; remove the ref keyword.</summary>
    public static readonly DiagnosticDescriptor RefKeywordOnNonRefParam = new(
        new DiagnosticId("MIST_REF_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Parameter '{0}' is not declared as ref; remove the 'ref' keyword from the argument");

    /// <summary>Cannot assign through a ref readonly reference.</summary>
    public static readonly DiagnosticDescriptor RefReadonlyAssignment = new(
        new DiagnosticId("MIST_REF_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign through a ref readonly reference");

    /// <summary>Cannot return a reference to local variable or non-ref parameter.</summary>
    public static readonly DiagnosticDescriptor RefReturnLocalEscape = new(
        new DiagnosticId("MIST_REF_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot return a reference to local variable or non-ref parameter '{0}'; its storage is destroyed when the method returns");

    /// <summary>Ref argument type must match parameter type exactly.</summary>
    public static readonly DiagnosticDescriptor RefArgTypeMismatch = new(
        new DiagnosticId("MIST_REF_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref argument type '{0}' must match parameter type '{1}' exactly; no implicit conversions are allowed for ref arguments");

    /// <summary>Cannot store a ref in a field.</summary>
    public static readonly DiagnosticDescriptor RefStoredInField = new(
        new DiagnosticId("MIST_REF_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot store a ref in a field; refs are stack-only aliases");

    /// <summary>Cannot assign through a ref readonly return value.</summary>
    public static readonly DiagnosticDescriptor RefReadonlyReturnAssignment = new(
        new DiagnosticId("MIST_REF_0009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign through a ref readonly return value");

    /// <summary>A ref local variable must be initialized at declaration.</summary>
    public static readonly DiagnosticDescriptor RefLocalMustInit = new(
        new DiagnosticId("MIST_REF_0010"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A ref local variable must be initialized at declaration");

    // ── Struct base constructor mismatch ─────────────────────────────────

    /// <summary>No constructor on struct matches the chained arguments.</summary>
    public static readonly DiagnosticDescriptor StructBaseCtorMismatch = new(
        new DiagnosticId("MIST_STRUCT_BASE_CTOR_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No constructor on struct '{0}' accepts the provided arguments.");

    // ── Undefined member diagnostic ──────────────────────────────────────

    /// <summary>Type does not contain a definition for a member.</summary>
    public static readonly DiagnosticDescriptor UndefinedMember = new(
        new DiagnosticId("MIST_UNDEFINED_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' does not contain a definition for '{1}'");

    // ── Dependent-type diagnostics ───────────────────────────────────────

    /// <summary>Dependent type argument is not a compile-time constant.</summary>
    public static readonly DiagnosticDescriptor DependentArgNotConstant = new(
        new DiagnosticId("MIST_DT_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Dependent type argument must be a compile-time constant; '{0}' is not a constant expression");

    /// <summary>Dependent type argument type does not match the parameter value type.</summary>
    public static readonly DiagnosticDescriptor DependentArgTypeMismatch = new(
        new DiagnosticId("MIST_DT_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Dependent type argument of type '{0}' does not match expected value type '{1}'");

    /// <summary>Forwarded dependent parameter value type cannot be narrowed to base parameter type.</summary>
    public static readonly DiagnosticDescriptor DependentNarrowingViolation = new(
        new DiagnosticId("MIST_DT_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Dependent parameter '{0}' of type '{1}' cannot be forwarded to base parameter '{2}' of type '{3}': implicit narrowing from '{1}' to '{3}' is not allowed");

    /// <summary>Pattern match on open dependent type is not allowed.</summary>
    public static readonly DiagnosticDescriptor DependentOpenTypePattern = new(
        new DiagnosticId("MIST_DT_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot pattern match against open dependent type '{0}': all dependent values must be concrete");

    // ── Adhoc refinement constraint diagnostics ──────────────────────────

    /// <summary>Dependent type argument violates adhoc refinement constraint.</summary>
    public static readonly DiagnosticDescriptor RefinementConstraintViolation = new(
        new DiagnosticId("MIST_REFINEMENT_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Value '{0}' violates the refinement constraint on parameter '{1}'");

    /// <summary>Compile-time constant argument violates adhoc refinement on parameter.</summary>
    public static readonly DiagnosticDescriptor ParameterRefinementViolation = new(
        new DiagnosticId("MIST_REFINEMENT_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Value '{0}' violates the refinement constraint on parameter '{1}'");

    /// <summary>Non-constant argument requires pattern matching to prove adhoc refinement.</summary>
    public static readonly DiagnosticDescriptor ParameterRefinementNonConstant = new(
        new DiagnosticId("MIST_REFINEMENT_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Argument for parameter '{0}' does not provably satisfy the refinement constraint; use pattern matching to establish the constraint at runtime");

    /// <summary>Forwarded dependent parameter constraint does not imply target constraint.</summary>
    public static readonly DiagnosticDescriptor RefinementImplicationFailure = new(
        new DiagnosticId("MIST_REFINEMENT_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Forwarded parameter '{0}' constraint does not provably imply target parameter '{1}' constraint");

    /// <summary>Refinement predicate uses relational patterns on non-comparable type.</summary>
    public static readonly DiagnosticDescriptor RefinementPredicateIncompatible = new(
        new DiagnosticId("MIST_REFINEMENT_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Refinement predicate on type '{0}' uses relational patterns but type '{1}' does not support comparison");

    /// <summary>Forwarded dependent parameter has no adhoc constraint but target requires one.</summary>
    public static readonly DiagnosticDescriptor RefinementUnconstrainedForwarding = new(
        new DiagnosticId("MIST_REFINEMENT_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Forwarded parameter '{0}' has no refinement constraint but target parameter '{1}' requires one");

    // ── Enum assignability diagnostic ────────────────────────────────────

    /// <summary>Cannot assign underlying type value to enum type.</summary>
    public static readonly DiagnosticDescriptor CannotAssignUnderlyingToEnum = new(
        new DiagnosticId("MIST_SB_0083"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to enum type '{1}': reverse conversion from underlying type to enum is not implicit");

    // ── Operator resolution diagnostics ──────────────────────────────────

    /// <summary>Arithmetic/additive operator cannot be applied to operand types.</summary>
    public static readonly DiagnosticDescriptor OperatorNotApplicableArithmetic = new(
        new DiagnosticId("MIST_TC_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'");

    /// <summary>Relational comparison operator cannot be applied to operand types.</summary>
    public static readonly DiagnosticDescriptor OperatorNotApplicableRelational = new(
        new DiagnosticId("MIST_TC_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'");

    /// <summary>Comparison operator cannot be applied to operand types.</summary>
    public static readonly DiagnosticDescriptor OperatorNotApplicableComparison = new(
        new DiagnosticId("MIST_TC_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'");

    /// <summary>Logical/equality operator cannot be applied to operand types.</summary>
    public static readonly DiagnosticDescriptor OperatorNotApplicableLogical = new(
        new DiagnosticId("MIST_TC_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'");

    // ── Additional stack-only type diagnostics ───────────────────────────

    /// <summary>Cannot use reference type as stackalloc element type.</summary>
    public static readonly DiagnosticDescriptor StackallocReferenceTypeElement = new(
        new DiagnosticId("MIST_TC_0152"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use reference type '{0}' as a stackalloc element type. Only value types (stampable structs and primitives) can be stack-allocated.");

    /// <summary>Cannot use non-stampable struct as stackalloc element type.</summary>
    public static readonly DiagnosticDescriptor StackallocNonStampableElement = new(
        new DiagnosticId("MIST_TC_0153"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use '{0}' as a stackalloc element type because it is not stampable. Only primitive types and structs whose fields are all stampable can be stack-allocated.");

    /// <summary>Cannot use type parameter as stackalloc element type.</summary>
    public static readonly DiagnosticDescriptor StackallocTypeParameterElement = new(
        new DiagnosticId("MIST_TC_0155"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use type parameter '{0}' as a stackalloc element type. The type parameter is not constrained to be a stampable type.");

    // ── unknown-type storage ban diagnostics ─────────────────────────────

    /// <summary>Cannot construct a generic type with unknown type arguments.</summary>
    public static readonly DiagnosticDescriptor UnknownInGenericConstruction = new(
        new DiagnosticId("MIST_SB_0053"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot construct '{0}' because one or more type arguments are 'unknown'. A generic type cannot be constructed with 'unknown' as a type argument; use concrete type arguments or pattern-match from an already-widened value.");

    /// <summary>Cannot create an array with element type containing unknown.</summary>
    public static readonly DiagnosticDescriptor UnknownAsArrayElement = new(
        new DiagnosticId("MIST_SB_0055"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create an array with element type '{0}' because it contains 'unknown'. Array elements are writable storage and cannot hold an 'unknown'-typed value.");

    /// <summary>unknown cannot be instantiated.</summary>
    public static readonly DiagnosticDescriptor UnknownCannotBeInstantiated = new(
        new DiagnosticId("MIST_SB_0056"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'unknown' cannot be instantiated. The 'unknown' type has no constructor and no declared members; use a concrete type instead.");

    /// <summary>unknown has no default value.</summary>
    public static readonly DiagnosticDescriptor UnknownHasNoDefault = new(
        new DiagnosticId("MIST_SB_0057"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'unknown' has no default value. 'default(unknown)' is not permitted because 'unknown' cannot be used as the declared type of a freshly-materialized value; use a concrete type or pattern-match before materializing a value.");

    /// <summary>Type alias argument count does not match parameter count.</summary>
    public static readonly DiagnosticDescriptor TypeAliasArgumentCountMismatch = new(
        new DiagnosticId("MIST_SB_0063"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type alias '{0}' expects {1} type argument(s) but was given {2}.");

    // ── Accessibility enforcement diagnostics ────────────────────────────

    /// <summary>
    /// A private member is referenced from outside its declaring type's accessibility domain.
    /// </summary>
    public static readonly DiagnosticDescriptor PrivateOutsideDomain = new(
        new DiagnosticId("MIST_TC_0170"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level (private members are accessible only within the declaring type)");

    /// <summary>
    /// A protected member is referenced from a type that neither declares nor derives from
    /// the declaring type.
    /// </summary>
    public static readonly DiagnosticDescriptor ProtectedNonDerived = new(
        new DiagnosticId("MIST_TC_0171"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level (protected members are accessible only within the declaring type or derived types)");

    /// <summary>
    /// An internal member is referenced from an external assembly (cross-compilation boundary).
    /// </summary>
    public static readonly DiagnosticDescriptor InternalCrossAssembly = new(
        new DiagnosticId("MIST_TC_0172"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level (internal members are accessible only within the same compilation)");

    /// <summary>
    /// A declaration exposes a type in its signature that is less accessible than itself.
    /// </summary>
    public static readonly DiagnosticDescriptor InconsistentAccessibility = new(
        new DiagnosticId("MIST_TC_0173"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Inconsistent accessibility: {0} '{1}' is less accessible than {2} '{3}'");

    /// <summary>
    /// A base class or implemented interface is less accessible than the deriving or
    /// implementing type.
    /// </summary>
    public static readonly DiagnosticDescriptor BaseLessAccessible = new(
        new DiagnosticId("MIST_TC_0174"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Inconsistent accessibility: base type '{0}' is less accessible than type '{1}'");

    /// <summary>
    /// An override or interface implementation violates the accessibility rules for its position.
    /// </summary>
    public static readonly DiagnosticDescriptor OverrideAccessibilityViolation = new(
        new DiagnosticId("MIST_TC_0175"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}': cannot change access modifiers when overriding '{1}' member '{2}'");

    /// <summary>
    /// A nested type is referenced from outside its computed accessibility domain.
    /// </summary>
    public static readonly DiagnosticDescriptor NestedTypeOutsideDomain = new(
        new DiagnosticId("MIST_TC_0176"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level (nested type is outside its accessibility domain)");

    /// <summary>
    /// A type attempts to derive from a sealed class or sealed struct.
    /// </summary>
    public static readonly DiagnosticDescriptor SealedInheritance = new(
        new DiagnosticId("MIST_TC_0177"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot derive from sealed type '{0}'");

    /// <summary>
    /// A newtype wraps a sealed type, which is not permitted.
    /// </summary>
    public static readonly DiagnosticDescriptor NewtypeOverSealed = new(
        new DiagnosticId("MIST_TC_0178"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot declare a newtype over sealed type '{0}'");

    /// <summary>
    /// The <c>sealed</c> modifier is applied to a type alias or a non-type member,
    /// where it is not valid.
    /// </summary>
    public static readonly DiagnosticDescriptor SealedOnNonType = new(
        new DiagnosticId("MIST_TC_0179"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'sealed' modifier is not valid on {0}; it can only be applied to class or struct declarations");

    /// <summary>
    /// A <c>protected</c> member is declared inside a <c>sealed</c> type. The declaration
    /// compiles but produces a warning because the sealed type cannot be inherited.
    /// This mirrors C# CS0628.
    /// </summary>
    public static readonly DiagnosticDescriptor ProtectedInSealed = new(
        new DiagnosticId("MIST_TC_0180"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "'{0}' is a new protected member declared in a sealed type; protected members in sealed types are unnecessary because the type cannot be inherited");
}