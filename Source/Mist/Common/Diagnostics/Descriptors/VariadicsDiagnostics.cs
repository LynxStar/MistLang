#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for variadics: type-parameter packs (variadic generics)
/// and homogeneous value-level <c>params</c>. Codes occupy the dedicated
/// <c>MIST_VAR_*</c> range so they do not collide with existing ranges.
/// The stage of each descriptor matches the design's Error Handling table:
/// pack-declaration and member-shape checks are reported during semantic-model
/// construction, position/binding/spread/constraint checks during analysis,
/// and the arity ceiling during backend emission.
/// </summary>
public static class VariadicsDiagnostics
{
    /// <summary>
    /// More than one unbracketed pack (declared with <c>params</c>) appears in a
    /// single type-parameter or value-parameter list. At most one is allowed;
    /// additional packs must be written in the bracketed/explicit-tuple form.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleParamsPacks = new(
        new DiagnosticId("MIST_VAR_0001"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "More than one 'params' pack in a single {0} list; at most one is allowed.");

    /// <summary>
    /// A pack identifier is used in bare type position (for example
    /// <c>public TArgs Item;</c>). A pack is a value, not a type.
    /// </summary>
    public static readonly DiagnosticDescriptor PackInTypePosition = new(
        new DiagnosticId("MIST_VAR_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Pack '{0}' cannot be used in type position; a pack is a value, not a type. Index or spread it instead.");

    /// <summary>
    /// A pack of unknown length is indexed as <c>TArgs[i]</c> where the length is
    /// not proven to be at least <c>i + 1</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor UnprovenPackIndex = new(
        new DiagnosticId("MIST_VAR_0003"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot index pack '{0}': its length is not proven to be at least {1}.");

    /// <summary>
    /// A fixed-size pack <c>Type[N]</c> is indexed with a compile-time index outside
    /// the range <c>0 .. N-1</c> (for example <c>TArgs[N]</c>).
    /// </summary>
    public static readonly DiagnosticDescriptor PackIndexOutOfBounds = new(
        new DiagnosticId("MIST_VAR_0004"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Index {0} is out of bounds for fixed-size pack '{1}' of length {2}.");

    /// <summary>
    /// A flat argument list is too short to fill the fixed scalar and bracketed
    /// pack slots (the minimum required while the unbracketed pack absorbs zero
    /// arguments).
    /// </summary>
    public static readonly DiagnosticDescriptor TooFewArgumentsForBinding = new(
        new DiagnosticId("MIST_VAR_0005"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Too few arguments: cannot fill the fixed and bracketed slots of '{0}'.");

    /// <summary>
    /// A zip is attempted between two spread operands that are not proven to have
    /// equal length (different origin, and no equal-length proof).
    /// </summary>
    public static readonly DiagnosticDescriptor ZipLengthNotProven = new(
        new DiagnosticId("MIST_VAR_0006"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot zip operands that are not proven to have equal length.");

    /// <summary>
    /// A spread-generated member stem collides with another member: it equals an
    /// existing member name, uses it as a prefix, or two spread stems prefix one
    /// another.
    /// </summary>
    public static readonly DiagnosticDescriptor SpreadStemCollision = new(
        new DiagnosticId("MIST_VAR_0007"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "Spread member stem '{0}' collides with another member or spread stem.");

    /// <summary>
    /// A spread-generated member declaration has an empty stem.
    /// </summary>
    public static readonly DiagnosticDescriptor EmptySpreadStem = new(
        new DiagnosticId("MIST_VAR_0008"),
        DiagnosticSeverity.Error,
        PipelineStage.SemanticModelConstruction,
        "A spread member declaration requires a non-empty stem.");

    /// <summary>
    /// A pack element type does not satisfy a whole-pack constraint
    /// (<c>where TArgs : IConstraint</c>), which applies elementwise.
    /// </summary>
    public static readonly DiagnosticDescriptor PackElementConstraintNotSatisfied = new(
        new DiagnosticId("MIST_VAR_0009"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Pack element '{0}' does not satisfy the constraint '{1}'.");

    /// <summary>
    /// A construction exceeds the target CLR type's capacity: a
    /// <c>System.Func</c>/<c>System.Action</c> over 16 input parameters, or a
    /// non-nestable <c>System.ValueTuple</c> over 7 elements. This is a
    /// backend-only, per-target limitation; Mist core semantics are unbounded.
    /// </summary>
    public static readonly DiagnosticDescriptor ArityCeilingExceeded = new(
        new DiagnosticId("MIST_VAR_0010"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Construction exceeds the target CLR type capacity: {0}.");

    /// <summary>
    /// A value-side spread does not match its destination slots: the wrong number
    /// of slots, or an element not assignable to the corresponding slot. Includes a
    /// symbolic value pack (<c>args...</c>) spread into a fixed-arity destination
    /// that is not provably <c>(TArgs...)</c>-compatible for every instantiation.
    /// </summary>
    public static readonly DiagnosticDescriptor SpreadDestinationMismatch = new(
        new DiagnosticId("MIST_VAR_0011"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Spread does not match its destination slots: {0}.");
}