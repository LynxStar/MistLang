#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the parser stage.
/// </summary>
public static class ParserDiagnostics
{
    /// <summary>
    /// The Storm-generated parser is skeletal and has not yet implemented parse methods.
    /// Source was lexed but not parsed.
    /// </summary>
    public static readonly DiagnosticDescriptor ParseStub = new(
        new DiagnosticId("MIST_PARSE_STUB"),
        DiagnosticSeverity.Warning,
        PipelineStage.Parsing,
        "Parser is not yet implemented (Storm-generated parser is skeletal). Source file was lexed but not parsed.");

    /// <summary>
    /// A parse error was reported by the Storm parser runtime.
    /// The message is provided by the Storm parser diagnostics.
    /// </summary>
    public static readonly DiagnosticDescriptor ParseError = new(
        new DiagnosticId("MIST_PARSE_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "{0}");

    /// <summary>
    /// The parser did not consume all input tokens; unparseable content remains.
    /// </summary>
    public static readonly DiagnosticDescriptor ParseIncomplete = new(
        new DiagnosticId("MIST_PARSE_INCOMPLETE"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "Parser did not consume all input. Unparseable content begins at line {0}.");

    /// <summary>
    /// A <c>stackalloc</c> expression is missing the required bracketed size
    /// (e.g. <c>stackalloc T[n]</c>).
    /// </summary>
    public static readonly DiagnosticDescriptor StackallocSize = new(
        new DiagnosticId("MIST_PARSE_STACKALLOC_SIZE"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "A 'stackalloc' expression requires an element type and a bracketed size, "
            + "for example 'stackalloc T[n]'.");

    /// <summary>
    /// The <c>interface</c> keyword has been removed from Mist.
    /// Users should use <c>abstract</c> instead.
    /// </summary>
    public static readonly DiagnosticDescriptor RemovedInterfaceKeyword = new(
        new DiagnosticId("MIST_REMOVED_INTERFACE_KEYWORD"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "The 'interface' keyword has been removed. Use 'abstract' instead.");

    /// <summary>
    /// The <c>trait</c> keyword has been removed from Mist.
    /// Users should use <c>abstract</c> with nominal typing or a <c>with</c> block.
    /// </summary>
    public static readonly DiagnosticDescriptor RemovedTraitKeyword = new(
        new DiagnosticId("MIST_REMOVED_TRAIT_KEYWORD"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance.");

    /// <summary>
    /// A property slot in an abstract type has a default implementation (body),
    /// which is not allowed. Only methods may have default implementations.
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractPropertyDefault = new(
        new DiagnosticId("MIST_ABSTRACT_PROPERTY_DEFAULT"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "Property slots cannot have default implementations. Only methods can have defaults.");

    /// <summary>
    /// A declaration has more than one access modifier (e.g. <c>public private</c>).
    /// Mist has four standalone access levels — <c>public</c>, <c>private</c>,
    /// <c>protected</c>, and <c>internal</c> — and no combined levels. Exactly one
    /// access modifier is allowed per declaration.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleAccessModifiers = new(
        new DiagnosticId("MIST_PARSE_MULTIPLE_ACCESS"),
        DiagnosticSeverity.Error,
        PipelineStage.Parsing,
        "A declaration cannot have multiple access modifiers. "
            + "Mist supports four standalone levels (public, private, protected, internal) "
            + "and no combined levels. Use exactly one.");
}