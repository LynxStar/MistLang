#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the semantic model construction stage.
/// Covers errors raised during symbol resolution, type resolution,
/// abstract type validation, and inheritance cycle detection.
/// </summary>
public static class SemanticDiagnostics
{
    /// <summary>
    /// An unhandled exception occurred during semantic model construction.
    /// </summary>
    public static readonly DiagnosticDescriptor SemanticError = new(
        new DiagnosticId("MIST_SEMANTIC_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "{0}");

    /// <summary>
    /// Semantic model construction failed with an unhandled exception in the driver pipeline.
    /// </summary>
    public static readonly DiagnosticDescriptor SemanticFailure = new(
        new DiagnosticId("MIST_SEMANTIC_FAILURE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Semantic model construction failed: {0}");

    /// <summary>
    /// An abstract type referenced in a base type list could not be found.
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedAbstract = new(
        new DiagnosticId("MIST_UNDEFINED_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The abstract type '{0}' could not be found.");

    /// <summary>
    /// A type referenced in an abstract type list is not actually an abstract type.
    /// </summary>
    public static readonly DiagnosticDescriptor NotAnAbstract = new(
        new DiagnosticId("MIST_NOT_AN_ABSTRACT"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not an abstract type; only abstract types can appear in an abstract type list.");

    /// <summary>
    /// A circular inheritance chain was detected among abstract types.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularAbstractInheritance = new(
        new DiagnosticId("MIST_CIRCULAR_ABSTRACT_INHERITANCE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Circular abstract type inheritance detected: '{0}'.");

    /// <summary>
    /// A circular inheritance chain was detected among class types.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularInheritance = new(
        new DiagnosticId("MIST_CIRCULAR_INHERITANCE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Circular inheritance detected: '{0}'.");

    /// <summary>
    /// An implementation method's ref parameter kinds differ from the abstract type method's ref kinds.
    /// </summary>
    public static readonly DiagnosticDescriptor RefKindMismatch = new(
        new DiagnosticId("MIST_REF_0012"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "{0}");

    /// <summary>
    /// A type does not implement a required abstract type property.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingAbstractProperty = new(
        new DiagnosticId("MIST_MISSING_ABSTRACT_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' does not implement abstract type property '{1}.{2}'.");

    /// <summary>
    /// A type's property implementation is missing a required accessor (get or set)
    /// from the abstract type.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingAbstractPropertyAccessor = new(
        new DiagnosticId("MIST_MISSING_ABSTRACT_PROPERTY_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}.{1}' does not implement '{2}' accessor required by abstract type '{3}'.");

    /// <summary>
    /// A derived class redeclares a member already defined in an ancestor.
    /// </summary>
    public static readonly DiagnosticDescriptor MemberRedeclaration = new(
        new DiagnosticId("MIST_MEMBER_REDECLARATION"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' cannot redeclare member '{1}' already defined in '{2}'.");

    /// <summary>
    /// A derived class does not satisfy the base class's constructor requirements.
    /// </summary>
    public static readonly DiagnosticDescriptor BaseCtorUnsatisfied = new(
        new DiagnosticId("MIST_BASE_CTOR_UNSATISFIED"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Base class '{0}' requires constructor arguments that are not provided.");

    /// <summary>
    /// A global using directive references a namespace that does not exist.
    /// </summary>
    public static readonly DiagnosticDescriptor GlobalUsingUndefinedNamespace = new(
        new DiagnosticId("MIST_GLOBAL_USING_UNDEFINED_NAMESPACE"),
        DiagnosticSeverity.Warning,
        PipelineStage.SemanticModelConstruction,
        "Global using namespace '{0}' could not be found.");

    /// <summary>
    /// A type name is ambiguous between multiple imported namespaces during
    /// semantic model construction.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousType = new(
        new DiagnosticId("MIST_AMBIGUOUS_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type name '{0}' is ambiguous between namespaces '{1}' and '{2}'.");

    /// <summary>
    /// A type reference could not be resolved to any known type.
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedType = new(
        new DiagnosticId("MIST_UNDEFINED_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type '{0}' could not be found.");

    /// <summary>
    /// An abstract class cannot be directly instantiated with <c>new</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractInstantiation = new(
        new DiagnosticId("MIST_ABSTRACT_INSTANTIATION"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Cannot create an instance of abstract type '{0}'.");

    /// <summary>
    /// A member access could not be resolved on the target type.
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedMember = new(
        new DiagnosticId("MIST_UNDEFINED_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' does not contain a definition for '{1}'.");

    /// <summary>
    /// A member access on a tuple type references a name that does not exist.
    /// </summary>
    public static readonly DiagnosticDescriptor UndefinedTupleMember = new(
        new DiagnosticId("MIST_TC_0063"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Tuple type '{0}' does not contain a member '{1}'.");

    /// <summary>
    /// An extension method resolution is ambiguous between multiple equally-specific
    /// extension blocks.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousExtensionMethod = new(
        new DiagnosticId("MIST_TC_0082"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "{0}");

    /// <summary>
    /// A local variable is declared with the same name as another variable already
    /// in the same scope.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateDeclaration = new(
        new DiagnosticId("MIST_DUPLICATE_DECLARATION"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A local variable named '{0}' is already declared in this scope.");
}