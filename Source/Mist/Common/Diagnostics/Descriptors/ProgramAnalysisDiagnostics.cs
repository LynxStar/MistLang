#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class ProgramAnalysisDiagnostics
{
    /// <summary>
    /// A switch expression some value of its scrutinee reaches no arm of. A WARNING, which is classic's
    /// severity for the same finding (MIST_TC_0090): the program compiles, and an unmatched value throws
    /// at runtime. Whether Mist should refuse instead is an open decision recorded on order 3 of
    /// backlog/analysis-v2-capability-progression.yaml, not one this descriptor gets to make.
    /// </summary>
    public static readonly DiagnosticDescriptor SwitchNotExhaustive = new(
        new DiagnosticId("MIST_PA_SWITCH_NOT_EXHAUSTIVE"),
        DiagnosticSeverity.Warning,
        PipelineStage.Analysis,
        "Switch expression is not exhaustive. Missing cases: {0}");

    public static readonly DiagnosticDescriptor CastHasNoConversion = new(
        new DiagnosticId("MIST_PA_CAST_HAS_NO_CONVERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "No conversion is defined from '{0}' to '{1}'. A cast reaches only a type the value already is, or one a declared cast operator reaches; to narrow a value, test it with an 'is' pattern instead.");

    public static readonly DiagnosticDescriptor EnumCastArgumentMismatch = new(
        new DiagnosticId("MIST_PA_ENUM_CAST_ARGUMENT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot cast '{0}' to enum '{1}'. A cast to an enum looks up the variant holding a value of its underlying type '{2}', and '{0}' does not convert to '{2}' implicitly.");

    public static readonly DiagnosticDescriptor CastArgumentMismatch = new(
        new DiagnosticId("MIST_PA_CAST_ARGUMENT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot cast '{0}' to '{1}'. The cast calls {2}, and '{0}' does not convert to its parameter type '{3}' implicitly.");

    public static readonly DiagnosticDescriptor NewtypeSourceDefinesNoCopy = new(
        new DiagnosticId("MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot cast '{0}' to '{1}'. '{2}' defines no copy to convert with; a newtype and its mutable class source convert only when the class implements 'IClonable<{2}>' with a Clone of its own.");

    public static readonly DiagnosticDescriptor CloneConversionDoesNotHold = new(
        new DiagnosticId("MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot cast '{0}' to '{1}'. The cast runs '{2}' with a '{3}' as this, and it does not hold at '{4}': {5}");

    public static readonly DiagnosticDescriptor AmbiguousCast = new(
        new DiagnosticId("MIST_PA_AMBIGUOUS_CAST"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The cast from '{0}' to '{1}' is ambiguous between {2}. Cast the value to one operator's parameter type first to choose it.");

    public static readonly DiagnosticDescriptor AmbiguousImplicitConversion = new(
        new DiagnosticId("MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The implicit conversion from '{0}' to '{1}' is ambiguous between {2}. Cast the value to choose one.");

    public static readonly DiagnosticDescriptor NoImplicitConversion = new(
        new DiagnosticId("MIST_PA_NO_IMPLICIT_CONVERSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert type '{0}' to '{1}'.");

    public static readonly DiagnosticDescriptor CastRequired = new(
        new DiagnosticId("MIST_PA_CAST_REQUIRED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot implicitly convert type '{0}' to '{1}'. An explicit cast exists (are you missing a cast?)");

    public static readonly DiagnosticDescriptor InaccessibleMember = new(
        new DiagnosticId("MIST_PA_INACCESSIBLE_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is inaccessible due to its protection level: {1} members are accessible only {2}.");

    public static readonly DiagnosticDescriptor InconsistentAccessibility = new(
        new DiagnosticId("MIST_PA_INCONSISTENT_ACCESSIBILITY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Inconsistent accessibility: {0} '{1}' is less accessible than {2} '{3}'.");

    public static readonly DiagnosticDescriptor SealedBaseType = new(
        new DiagnosticId("MIST_PA_SEALED_BASE_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot derive from sealed type '{0}'.");

    public static readonly DiagnosticDescriptor NewtypeOverSealedType = new(
        new DiagnosticId("MIST_PA_NEWTYPE_OVER_SEALED_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot declare a newtype over sealed type '{0}'.");

    public static readonly DiagnosticDescriptor SealedTypeAlias = new(
        new DiagnosticId("MIST_PA_SEALED_TYPE_ALIAS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'sealed' modifier is not valid on a type alias; it can only be applied to class or struct declarations.");

    public static readonly DiagnosticDescriptor EnumBaseNotReadonly = new(
        new DiagnosticId("MIST_PA_ENUM_BASE_NOT_READONLY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Enum '{0}' has a non-readonly base type '{1}'. Enum base types, and every constituent of a union base, must be readonly.");

    public static readonly DiagnosticDescriptor EnumDuplicateValue = new(
        new DiagnosticId("MIST_PA_ENUM_DUPLICATE_VALUE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Enum member '{0}' has the same value as member '{1}' in enum '{2}'.");

    public static readonly DiagnosticDescriptor DefaultEntryNotAField = new(
        new DiagnosticId("MIST_PA_DEFAULT_ENTRY_NOT_A_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' is not a field of '{1}', so a default block cannot assign it.");

    public static readonly DiagnosticDescriptor OperatorNotPublic = new(
        new DiagnosticId("MIST_PA_OPERATOR_NOT_PUBLIC"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' must be declared 'public static'.");

    public static readonly DiagnosticDescriptor OperatorUnpaired = new(
        new DiagnosticId("MIST_PA_OPERATOR_UNPAIRED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' on '{1}' requires a matching operator '{2}', with the same parameter and return types, to also be declared.");

    public static readonly DiagnosticDescriptor LogicalOperatorDeclared = new(
        new DiagnosticId("MIST_PA_LOGICAL_OPERATOR_DECLARED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' can be declared only on Boolean.");

    public static readonly DiagnosticDescriptor OperatorNotOnDeclaringType = new(
        new DiagnosticId("MIST_PA_OPERATOR_NOT_ON_DECLARING_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' must take '{1}', the type declaring it, as a parameter; an operator is found only through its operands' types.");

    public static readonly DiagnosticDescriptor RefinementNotComparable = new(
        new DiagnosticId("MIST_PA_REFINEMENT_NOT_COMPARABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A relational refinement predicate cannot constrain '{0}': its type '{1}' supports no comparison operators.");

    public static readonly DiagnosticDescriptor ConstraintNotSatisfied = new(
        new DiagnosticId("MIST_PA_CONSTRAINT_NOT_SATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type argument '{0}' does not satisfy the '{1}' constraint on type parameter '{2}'.");

    public static readonly DiagnosticDescriptor ConstructorConstraintNotSatisfied = new(
        new DiagnosticId("MIST_PA_CONSTRUCTOR_CONSTRAINT_NOT_SATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' must have a public constructor with parameters '({1})' to satisfy the 'new({1})' constraint on type parameter '{2}'.");

    public static readonly DiagnosticDescriptor NotDefaultable = new(
        new DiagnosticId("MIST_PA_NOT_DEFAULTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Type '{0}' has no default: it declares no default block or parameterless constructor, or a field or base of it has none. Give it a default block, or construct it explicitly.");

    public static readonly DiagnosticDescriptor SlotNotFilled = new(
        new DiagnosticId("MIST_PA_SLOT_NOT_FILLED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' does not fill slot '{1}' of '{2}': it declares no public member named '{3}'.");

    public static readonly DiagnosticDescriptor SlotSignatureMismatch = new(
        new DiagnosticId("MIST_PA_SLOT_SIGNATURE_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' does not fill slot '{1}' of '{2}': its '{3}' has a different signature.");

    public static readonly DiagnosticDescriptor PropertySlotAccessorMissing = new(
        new DiagnosticId("MIST_PA_PROPERTY_SLOT_ACCESSOR_MISSING"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}.{1}' does not implement the '{2}' accessor required by abstract type '{3}'.");

    public static readonly DiagnosticDescriptor ConformanceSlotUnanswered = new(
        new DiagnosticId("MIST_PA_CONFORMANCE_SLOT_UNANSWERED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The with block for '{0} : {1}' leaves slot '{2}' unanswered. Map a member onto it, or declare a public member of that name and signature.");

    public static readonly DiagnosticDescriptor RedundantConformance = new(
        new DiagnosticId("MIST_PA_REDUNDANT_CONFORMANCE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' already conforms to '{1}', so this with block is redundant. A type reaches an abstract once: remove the with block, or remove '{1}' from the base list.");

    public static readonly DiagnosticDescriptor TypeSlotUnfilled = new(
        new DiagnosticId("MIST_PA_TYPE_SLOT_UNFILLED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' declares no nested type named '{1}' to fill the associated type slot '{2}.{1}'.");

    public static readonly DiagnosticDescriptor TypeSlotConstraintNotSatisfied = new(
        new DiagnosticId("MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Nested type '{0}' does not satisfy constraint '{1}' of associated type slot '{2}.{3}'.");

    public static readonly DiagnosticDescriptor PatternNeverMatches = new(
        new DiagnosticId("MIST_PA_PATTERN_NEVER_MATCHES"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A value of type '{0}' can never be '{1}', so this pattern never matches.");

    public static readonly DiagnosticDescriptor PatternNarrowsToUnknown = new(
        new DiagnosticId("MIST_PA_PATTERN_NARROWS_TO_UNKNOWN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Narrowing to 'unknown' is not meaningful: the pattern is tautologically true and introduces no new type information. Remove the pattern or narrow to a concrete type.");

    public static readonly DiagnosticDescriptor PatternNotComparable = new(
        new DiagnosticId("MIST_PA_PATTERN_NOT_COMPARABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A relational pattern cannot be applied to a value of type '{0}', which supports no comparison operators.");

    public static readonly DiagnosticDescriptor PatternConstantMismatch = new(
        new DiagnosticId("MIST_PA_PATTERN_CONSTANT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A constant of type '{0}' cannot be compared with a value of type '{1}'.");

    public static readonly DiagnosticDescriptor PatternBoundNotConstant = new(
        new DiagnosticId("MIST_PA_PATTERN_BOUND_NOT_CONSTANT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The bound of a relational pattern must be a constant.");

    public static readonly DiagnosticDescriptor SwitchArmUnreachable = new(
        new DiagnosticId("MIST_PA_SWITCH_ARM_UNREACHABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "This switch arm is unreachable: an earlier arm already matches everything it could.");

    public static readonly DiagnosticDescriptor OperatorNotApplicable = new(
        new DiagnosticId("MIST_PA_OPERATOR_NOT_APPLICABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}'.");

    public static readonly DiagnosticDescriptor UnknownHasNoOperators = new(
        new DiagnosticId("MIST_PA_UNKNOWN_HAS_NO_OPERATORS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Operator '{0}' cannot be applied to operands of type '{1}' and '{2}': no operators are defined for 'unknown'. Narrow the value with an 'is' pattern first.");

    public static readonly DiagnosticDescriptor UnknownHasNoMembers = new(
        new DiagnosticId("MIST_PA_UNKNOWN_HAS_NO_MEMBERS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot access member '{0}' on a value of type 'unknown': 'unknown' has no members. Narrow the value with an 'is' pattern first.");

    public static readonly DiagnosticDescriptor ConditionalAccessOnNonOptional = new(
        new DiagnosticId("MIST_PA_CONDITIONAL_ACCESS_ON_NON_OPTIONAL"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The optional chaining operator '?.' requires a receiver of the form 'T | void', but found '{0}'.");

    public static readonly DiagnosticDescriptor CoalescingAssignmentNotAStatement = new(
        new DiagnosticId("MIST_PA_COALESCING_ASSIGNMENT_NOT_A_STATEMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The void-coalescing assignment '??=' is a statement and cannot be used as a sub-expression.");

    public static readonly DiagnosticDescriptor ReadonlyLocalReassigned = new(
        new DiagnosticId("MIST_PA_READONLY_LOCAL_REASSIGNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot reassign readonly local variable '{0}'.");

    public static readonly DiagnosticDescriptor StaticLambdaCapture = new(
        new DiagnosticId("MIST_PA_STATIC_LAMBDA_CAPTURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A static lambda cannot reference {0} '{1}'.");

    public static readonly DiagnosticDescriptor StaticLocalFunctionCapture = new(
        new DiagnosticId("MIST_PA_STATIC_LOCAL_FUNCTION_CAPTURE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A static local function cannot reference {0} '{1}'.");

    public static readonly DiagnosticDescriptor OptionalOfOptional = new(
        new DiagnosticId("MIST_PA_OPTIONAL_OF_OPTIONAL"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The optional suffix '?' cannot be applied to '{0}', which already contains 'void'.");

    public static readonly DiagnosticDescriptor LocalReadBeforeAssignment = new(
        new DiagnosticId("MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Local variable '{0}' is read before being definitely assigned.");

    public static readonly DiagnosticDescriptor LocalFunctionReadsUnassigned = new(
        new DiagnosticId("MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Local function '{0}' reads '{1}', which is not definitely assigned at this call.");

    public static readonly DiagnosticDescriptor FieldReadBeforeAssignment = new(
        new DiagnosticId("MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' is read before being definitely assigned in constructor '{1}'.");

    public static readonly DiagnosticDescriptor FieldNotDefinitelyAssigned = new(
        new DiagnosticId("MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Field '{0}' must be definitely assigned before constructor '{1}' completes. The field's type '{2}' is not defaultable and has no initializer.");

    public static readonly DiagnosticDescriptor ArrayRequiresInitializer = new(
        new DiagnosticId("MIST_PA_ARRAY_REQUIRES_INITIALIZER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot create a bare array 'new {0}[size]' because {1}. Use 'new Array<{0}, Size>(i => ...)' to supply a value for each element, or use an array literal with explicit elements.");

    public static readonly DiagnosticDescriptor StackallocElementNotStampable = new(
        new DiagnosticId("MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot use '{0}' as a stackalloc element type because {1}. Only primitives and structs whose fields are all stampable can be stack-allocated.");

    public static readonly DiagnosticDescriptor UseAfterMove = new(
        new DiagnosticId("MIST_PA_USE_AFTER_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Variable '{0}' has been {1} and cannot be accessed here.");

    public static readonly DiagnosticDescriptor LoopMove = new(
        new DiagnosticId("MIST_PA_LOOP_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Owned variable '{0}' is moved inside a loop. Re-assign it before the next iteration or remove the move.");

    public static readonly DiagnosticDescriptor LeaseViolation = new(
        new DiagnosticId("MIST_PA_LEASE_VIOLATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot {1} '{0}' while a {2} lease of it is outstanding.");

    public static readonly DiagnosticDescriptor IncompatibleLend = new(
        new DiagnosticId("MIST_PA_INCOMPATIBLE_LEND"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot pass {0} variable '{1}' to {2} parameter '{3}': a {2} parameter needs at least {2} access.");

    public static readonly DiagnosticDescriptor ThisTierMismatch = new(
        new DiagnosticId("MIST_PA_THIS_TIER_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot call '{0}', which requires {1} access to 'this': the receiver '{2}' provides {3} access.");

    public static readonly DiagnosticDescriptor ThisTierInsufficient = new(
        new DiagnosticId("MIST_PA_THIS_TIER_INSUFFICIENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Method '{0}' declares 'with {1}' but its body requires at least {2} access to 'this'.");

    public static readonly DiagnosticDescriptor SharedExtract = new(
        new DiagnosticId("MIST_PA_SHARED_EXTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot extract owned field '{0}' through unrestricted binding '{1}': extracting from an owned field requires an owned binding to its holder.");

    public static readonly DiagnosticDescriptor OwnedAbstract = new(
        new DiagnosticId("MIST_PA_OWNED_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'owned' modifier cannot be applied to abstract type '{0}'.");

    public static readonly DiagnosticDescriptor OwnedSubstructuralBinding = new(
        new DiagnosticId("MIST_PA_OWNED_SUBSTRUCTURAL_BINDING"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'owned' specifier cannot be applied to '{0}' of {1} type '{2}': a substructural type carries its own discipline.");

    public static readonly DiagnosticDescriptor ConflictingDiscipline = new(
        new DiagnosticId("MIST_PA_CONFLICTING_DISCIPLINE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' cannot be both {1} and 'owned': {2}.");

    public static readonly DiagnosticDescriptor FieldDisciplineContainment = new(
        new DiagnosticId("MIST_PA_FIELD_DISCIPLINE_CONTAINMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0} type '{1}' cannot contain {2} field '{3}': {4}.");

    public static readonly DiagnosticDescriptor FieldTypeParameterUnconstrained = new(
        new DiagnosticId("MIST_PA_FIELD_TYPE_PARAMETER_UNCONSTRAINED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "{0} type '{1}' contains field '{2}' of unconstrained type parameter '{3}'. Add a substructural constraint, such as 'where {3} : {4}'.");

    public static readonly DiagnosticDescriptor RefOnClass = new(
        new DiagnosticId("MIST_PA_REF_ON_CLASS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "The 'ref' modifier is only valid on struct declarations; '{0}' is a class.");

    public static readonly DiagnosticDescriptor RefStructField = new(
        new DiagnosticId("MIST_PA_REF_STRUCT_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct '{0}' cannot be a field of '{1}', which is not a ref struct.");

    public static readonly DiagnosticDescriptor RefStructTypeArgument = new(
        new DiagnosticId("MIST_PA_REF_STRUCT_TYPE_ARGUMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Ref struct '{0}' cannot be a type argument.");

    public static readonly DiagnosticDescriptor FieldAssignedInReadonlyMember = new(
        new DiagnosticId("MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign to field '{0}' within readonly member '{1}'.");

    public static readonly DiagnosticDescriptor FieldAssignedOnReadonlyParameter = new(
        new DiagnosticId("MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign to field '{0}' of readonly parameter '{1}'.");

    public static readonly DiagnosticDescriptor ReadonlyParameterCallsMutable = new(
        new DiagnosticId("MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot call '{0}' on readonly parameter '{1}': it is not readonly.");

    public static readonly DiagnosticDescriptor ReadonlyParameterSetsProperty = new(
        new DiagnosticId("MIST_PA_READONLY_PARAMETER_SETS_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot set '{0}' on readonly parameter '{1}': a readonly parameter admits no setter.");

    public static readonly DiagnosticDescriptor FieldAssignedInReadonlyBlock = new(
        new DiagnosticId("MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign to field '{0}' inside a readonly block, which admits no field write and suspends a constructor's privilege to make one.");

    public static readonly DiagnosticDescriptor ReadonlyContextWritesThroughIndex = new(
        new DiagnosticId("MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot write through an index into '{0}' from readonly {1}: a readonly context mutates nothing the value reaches.");

    public static readonly DiagnosticDescriptor ReadonlyContextCallsMutable = new(
        new DiagnosticId("MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot call '{0}' from readonly {1}: it is not readonly.");

    public static readonly DiagnosticDescriptor ReadonlyContextSetsProperty = new(
        new DiagnosticId("MIST_PA_READONLY_CONTEXT_SETS_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot set '{0}' from readonly {1}: a readonly context calls no setter.");

    public static readonly DiagnosticDescriptor ReadonlyContextAllocatesMutable = new(
        new DiagnosticId("MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot allocate '{0}' from readonly {1}: its constructor is not readonly.");

    public static readonly DiagnosticDescriptor ReadonlyFieldAssigned = new(
        new DiagnosticId("MIST_PA_READONLY_FIELD_ASSIGNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign to readonly field '{0}' outside a constructor of '{1}'.");

    public static readonly DiagnosticDescriptor StackSpanReturned = new(
        new DiagnosticId("MIST_PA_STACK_SPAN_RETURNED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "A stackalloc-backed span cannot be returned: it would escape its stack frame.");

    public static readonly DiagnosticDescriptor CapturedBinding = new(
        new DiagnosticId("MIST_PA_CAPTURED_BINDING"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot capture {0} variable '{1}' in a {2}: {3}.");

    public static readonly DiagnosticDescriptor RefinementViolated = new(
        new DiagnosticId("MIST_PA_REFINEMENT_VIOLATED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Value '{0}' does not satisfy refinement '{1}' on type '{2}'.");

    public static readonly DiagnosticDescriptor RefinementUnproven = new(
        new DiagnosticId("MIST_PA_REFINEMENT_UNPROVEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}' without proving its refinement '{2}': narrow the value with a pattern first.");

    public static readonly DiagnosticDescriptor RefinementNotImplied = new(
        new DiagnosticId("MIST_PA_REFINEMENT_NOT_IMPLIED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot assign '{0}' to '{1}': '{2}' does not imply '{3}'.");

    public static readonly DiagnosticDescriptor IndexOutOfBounds = new(
        new DiagnosticId("MIST_PA_INDEX_OUT_OF_BOUNDS"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Index {0} is outside the bounds of '{1}', which needs {2}.");

    public static readonly DiagnosticDescriptor IndexUnproven = new(
        new DiagnosticId("MIST_PA_INDEX_UNPROVEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Index '{0}' is not proven within the bounds of '{1}', which needs {2}.");

    public static readonly DiagnosticDescriptor ConstraintViolated = new(
        new DiagnosticId("MIST_PA_CONSTRAINT_VIOLATED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Value '{0}' violates the refinement constraint '{1}' on parameter '{2}'.");

    public static readonly DiagnosticDescriptor ConstraintUnproven = new(
        new DiagnosticId("MIST_PA_CONSTRAINT_UNPROVEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Argument '{0}' is not proven to satisfy the refinement constraint '{1}' on parameter '{2}'.");

    public static readonly DiagnosticDescriptor DependentArgumentViolated = new(
        new DiagnosticId("MIST_PA_DEPENDENT_ARGUMENT_VIOLATED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Dependent type argument '{0}' does not satisfy '{1}' on parameter '{2}'.");

    public static readonly DiagnosticDescriptor DependentForwardUnproven = new(
        new DiagnosticId("MIST_PA_DEPENDENT_FORWARD_UNPROVEN"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Dependent parameter '{0}' cannot be forwarded to '{1}': {2} is not proven to imply '{3}'.");

    public static readonly DiagnosticDescriptor ConstraintStrengthened = new(
        new DiagnosticId("MIST_PA_CONSTRAINT_STRENGTHENED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "'{0}' constrains parameter '{1}' more than the member it implements: '{2}' does not imply '{3}'.");
}