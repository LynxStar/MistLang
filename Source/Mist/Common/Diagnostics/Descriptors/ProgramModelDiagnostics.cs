#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the program model construction phase.
/// Covers namespace validation, type registration, type resolution,
/// base type resolution, and member resolution.
/// </summary>
public static class ProgramModelDiagnostics
{
    // ── Internal / driver-level errors ────────────────────────────────────

    /// <summary>AST array length does not match compilation unit array length.</summary>
    public static readonly DiagnosticDescriptor AstUnitMismatch = new(
        new DiagnosticId("MIST_INTERNAL_AST_UNIT_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: AST count ({0}) does not match compilation unit count ({1}). This indicates a driver bug where a failed lex/parse dropped an AST without dropping its corresponding compilation unit.");

    /// <summary>An unexpected namespace declaration node type was encountered.</summary>
    public static readonly DiagnosticDescriptor UnexpectedNamespaceNode = new(
        new DiagnosticId("MIST_INTERNAL_UNEXPECTED_NS_NODE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unexpected namespace declaration node type '{0}'.");

    /// <summary>An unrecognized type declaration node was encountered during type registration.</summary>
    public static readonly DiagnosticDescriptor UnknownTypeDeclaration = new(
        new DiagnosticId("MIST_INTERNAL_UNKNOWN_TYPE_DECL"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized type declaration node '{0}'.");

    // ── Namespace validation ─────────────────────────────────────────────

    /// <summary>More than one file-scoped namespace in the same file.</summary>
    public static readonly DiagnosticDescriptor DuplicateFileScopedNamespace = new(
        new DiagnosticId("MIST0301"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Only one file-scoped namespace is permitted per file.");

    /// <summary>File-scoped and block namespaces mixed in the same file.</summary>
    public static readonly DiagnosticDescriptor MixedNamespaceStyles = new(
        new DiagnosticId("MIST0302"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "File-scoped and block namespaces cannot be mixed in the same file.");

    /// <summary>File-scoped namespace inside a block namespace.</summary>
    public static readonly DiagnosticDescriptor FileScopedInsideBlock = new(
        new DiagnosticId("MIST0305"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "File-scoped namespace declarations cannot appear inside a block namespace.");

    // ── Type resolution ──────────────────────────────────────────────────

    /// <summary>A type reference could not be resolved in any reachable scope.</summary>
    public static readonly DiagnosticDescriptor UnresolvedType = new(
        new DiagnosticId("MIST_PM_UNRESOLVED_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The type '{0}' could not be found.");

    /// <summary>An unrecognized type reference AST node was encountered.</summary>
    public static readonly DiagnosticDescriptor UnknownTypeRefNode = new(
        new DiagnosticId("MIST_PM_UNKNOWN_TYPE_REF_NODE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized type reference node '{0}'.");

    // ── Base type resolution ─────────────────────────────────────────────

    /// <summary>A concrete type attempts to inherit from an incompatible kind.</summary>
    public static readonly DiagnosticDescriptor BaseTypeKindMismatch = new(
        new DiagnosticId("MIST_PM_BASE_TYPE_KIND_MISMATCH"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A {0} cannot inherit from a {1} ('{2}').");

    /// <summary>A concrete type appears in the abstract implementations list.</summary>
    public static readonly DiagnosticDescriptor ConcreteInAbstractList = new(
        new DiagnosticId("MIST_PM_CONCRETE_IN_ABSTRACT_LIST"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A concrete type ('{0}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.");

    /// <summary>A non-abstract type is used where only an abstract is permitted.</summary>
    public static readonly DiagnosticDescriptor InvalidBaseType = new(
        new DiagnosticId("MIST_PM_INVALID_BASE_TYPE"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "'{0}' is not an abstract and cannot be used as a base type.");

    // ── Member resolution ────────────────────────────────────────────────

    /// <summary>An unrecognized abstract member declaration node.</summary>
    public static readonly DiagnosticDescriptor UnknownAbstractMember = new(
        new DiagnosticId("MIST_PM_UNKNOWN_ABSTRACT_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized abstract member declaration node '{0}'.");

    /// <summary>An unrecognized concrete member declaration node.</summary>
    public static readonly DiagnosticDescriptor UnknownMember = new(
        new DiagnosticId("MIST_PM_UNKNOWN_MEMBER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Internal compiler error: unrecognized member declaration node '{0}'.");

    /// <summary>An expression-bodied property is not permitted on an abstract type.</summary>
    public static readonly DiagnosticDescriptor AbstractExpressionBodiedProperty = new(
        new DiagnosticId("MIST_PM_ABSTRACT_EXPR_PROPERTY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract types cannot declare expression-bodied properties ('{0}').");

    /// <summary>An abstract property accessor must be auto (no body).</summary>
    public static readonly DiagnosticDescriptor AbstractPropertyAccessorHasBody = new(
        new DiagnosticId("MIST_PM_ABSTRACT_ACCESSOR_BODY"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Accessors on abstract property '{0}' must be auto (no body).");

    /// <summary>A modifier on a getter accessor is not permitted.</summary>
    public static readonly DiagnosticDescriptor GetterCannotHaveModifier = new(
        new DiagnosticId("MIST_PM_GETTER_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "The getter on property '{0}' cannot have a modifier.");

    /// <summary>Only 'private' is permitted as a setter accessor modifier.</summary>
    public static readonly DiagnosticDescriptor SetterModifierMustBePrivate = new(
        new DiagnosticId("MIST_PM_SETTER_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Only 'private' is permitted as a modifier on the setter of property '{0}'.");

    /// <summary>An abstract property accessor cannot have a modifier.</summary>
    public static readonly DiagnosticDescriptor AbstractAccessorCannotHaveModifier = new(
        new DiagnosticId("MIST_PM_ABSTRACT_ACCESSOR_MODIFIER"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Accessors on abstract property '{0}' cannot have modifiers.");

    /// <summary>An init accessor is not permitted on an abstract property.</summary>
    public static readonly DiagnosticDescriptor AbstractPropertyInitAccessor = new(
        new DiagnosticId("MIST_PM_ABSTRACT_INIT_ACCESSOR"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Abstract types cannot declare init accessors on property '{0}' because abstract types cannot be instantiated.");
}