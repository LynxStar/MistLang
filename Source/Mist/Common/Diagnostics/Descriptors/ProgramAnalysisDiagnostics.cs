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
}