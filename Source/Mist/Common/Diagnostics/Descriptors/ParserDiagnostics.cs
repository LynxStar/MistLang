#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the parser stage.
/// </summary>
public static class ParserDiagnostics
{
    public static readonly MistDiagnostic ParseStub = new(
        DiagnosticNumber.Mint("MIST0121"),
        Title: "Parser not implemented",
        Template: "Parser is not yet implemented (Storm-generated parser is skeletal). Source file was lexed but not parsed.",
        Description: "The parser Storm generated for this grammar is still a skeleton without parse methods, so the source was tokenized but produced no syntax tree. Regenerate or complete the parser before expecting any semantic results.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    public static readonly MistDiagnostic ParseError = new(
        DiagnosticNumber.Mint("MIST0122"),
        Title: "Parse error",
        Template: "{0}",
        Description: "The Storm parser runtime rejected the token sequence at this position and the message is the runtime's own. Correct the syntax it names; later analysis runs only on what parsed.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ParseIncomplete = new(
        DiagnosticNumber.Mint("MIST0123"),
        Title: "Parser stopped before end of file",
        Template: "Parser did not consume all input. Unparseable content begins at line {0}.",
        Description: "Parsing succeeded for a prefix of the file, but tokens remain from the stated line onward that no grammar rule accepts. Look at that line for an unbalanced brace or a stray token, since it usually marks the real mistake.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic StackallocSize = new(
        DiagnosticNumber.Mint("MIST0124"),
        Title: "Stackalloc without size",
        Template: "A 'stackalloc' expression requires an element type and a bracketed size, "
            + "for example 'stackalloc T[n]'.",
        Description: "A stackalloc expression names an element type but omits the bracketed count saying how many elements to allocate. Write the count in brackets after the type, as in stackalloc byte[16].",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RemovedInterfaceKeyword = new(
        DiagnosticNumber.Mint("MIST0125"),
        Title: "Removed interface keyword",
        Template: "The 'interface' keyword has been removed. Use 'abstract' instead.",
        Description: "Mist no longer has interfaces as a distinct construct; abstract types serve that purpose. Declare the type with abstract in place of interface.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RemovedTraitKeyword = new(
        DiagnosticNumber.Mint("MIST0126"),
        Title: "Removed trait keyword",
        Template: "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance.",
        Description: "Mist no longer has traits as a distinct construct; abstract types cover both nominal and retroactive conformance. Declare an abstract type and either list it in the conforming type's base list or attach it afterwards with a with block.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AbstractPropertyDefault = new(
        DiagnosticNumber.Mint("MIST0127"),
        Title: "Property slot with default body",
        Template: "Property slots cannot have default implementations. Only methods can have defaults.",
        Description: "A property declared in an abstract type carries an implementation, but only method slots may supply a default; every conforming type must fill a property slot itself. Remove the body from the property.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MultipleAccessModifiers = new(
        DiagnosticNumber.Mint("MIST0128"),
        Title: "Multiple access modifiers",
        Template: "A declaration cannot have multiple access modifiers. "
            + "Mist supports four standalone levels (public, private, protected, internal) "
            + "and no combined levels. Use exactly one.",
        Description: "A declaration lists more than one access modifier, but Mist has only four standalone visibility levels and no combinations such as protected internal, so a single modifier fully determines visibility. Keep exactly one of public, private, protected or internal.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}