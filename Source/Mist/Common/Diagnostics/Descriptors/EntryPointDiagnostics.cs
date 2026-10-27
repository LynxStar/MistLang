#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for entry-point validation.
/// </summary>
public static class EntryPointDiagnostics
{
    public static readonly MistDiagnostic MissingEntryPoint = new(
        DiagnosticNumber.Mint("MIST0108"),
        Title: "Missing entry point",
        Template: "Project '{project}' produces an executable but declares no entry point. Add a 'static void Main()' method, or set output type to library.",
        Description: "The project's output type is executable, yet no file has top-level statements and no class declares a static Main. Give the program an entry point, or change output_type to library in mist.toml if it is not meant to run on its own.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("project", SlotKind.Name)]);

    public static readonly MistDiagnostic MultipleTopLevelFiles = new(
        DiagnosticNumber.Mint("MIST0109"),
        Title: "Multiple top-level statement files",
        Template: "Multiple files contain top-level statements. Only one file per project may have top-level statements. Files: {files}",
        Description: "More than one source file in the project has statements outside any type, and a single such file is the most a program can treat as its entry point. Move the extra statements into methods, keeping top-level statements in one file.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("files", SlotKind.Sequence, SlotKind.Path)]);

    public static readonly MistDiagnostic TopLevelStatementsBesideMain = new(
        DiagnosticNumber.Mint("MIST0110"),
        Title: "Top-level statements beside Main",
        Template: "Project has both top-level statements (in '{file}') and a static void Main (in '{classes}'). Add entry_point = \"top-level\" to mist.toml to resolve this conflict.",
        Description: "The project has both a file of top-level statements and a class with a static Main, and the compiler will not guess which one is the program. Select the statements with entry_point = \"top-level\" in mist.toml, or remove one of the two candidates.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("file", SlotKind.Path), new Slot("classes", SlotKind.Sequence, SlotKind.Name)]);

    public static readonly MistDiagnostic ClassEntryPointBesideTopLevelStatements = new(
        DiagnosticNumber.Mint("MIST0111"),
        Title: "Class entry point with top-level statements",
        Template: "Cannot specify entry_point = \"{class}\" when a top-level statement file exists. A top-level statement file must always be the entry point. Use entry_point = \"top-level\" instead.",
        Description: "mist.toml names a class as the entry point, but the project also has a file of top-level statements, and such a file is always the entry point whenever it is present. Change entry_point to \"top-level\", or move the top-level statements into a Main method.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("class", SlotKind.Name)]);

    public static readonly MistDiagnostic MultipleMains = new(
        DiagnosticNumber.Mint("MIST0112"),
        Title: "Multiple Main methods",
        Template: "Multiple classes contain static void Main: {classes}. Add entry_point = \"{first}\" to mist.toml to specify which one to use.",
        Description: "Several classes each declare a static Main and nothing in the project configuration chooses between them. Set entry_point in mist.toml to the fully qualified name of the intended class, or remove the extra Main methods.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("classes", SlotKind.Sequence, SlotKind.Name), new Slot("first", SlotKind.Name)]);

    public static readonly MistDiagnostic TopLevelEntryPointWithoutStatements = new(
        DiagnosticNumber.Mint("MIST0113"),
        Title: "Top-level entry point without statements",
        Template: "entry_point = \"top-level\" specified in mist.toml but no file contains top-level statements.",
        Description: "The project configuration selects top-level statements as the entry point, but no source file has any. Add the statements to a file, point entry_point at the class holding Main instead, or remove the setting.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TopLevelStatementsWithBlockNamespace = new(
        DiagnosticNumber.Mint("MIST0114"),
        Title: "Top-level statements beside block namespace",
        Template: "File '{file}' has both top-level statements and a block namespace declaration: {namespaces}. Top-level statements cannot coexist with block namespace declarations in the same file. Use a file-scoped namespace (namespace X;) instead.",
        Description: "A file mixes statements outside any type with a namespace declared using braces, and the two forms cannot share a file. Switch the namespace to the file-scoped form ending in a semicolon, or move the statements to another file.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("file", SlotKind.Path), new Slot("namespaces", SlotKind.Sequence, SlotKind.Name)]);

    public static readonly MistDiagnostic EntryPointClassNotFound = new(
        DiagnosticNumber.Mint("MIST0115"),
        Title: "Entry point class not found",
        Template: "entry_point specifies '{class}' but no class with that name contains a static void Main method.",
        Description: "The class named by entry_point in mist.toml either does not exist in the project or has no static Main. Correct the name, including its namespace, or add a Main to that class.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("class", SlotKind.Name)]);

    public static readonly MistDiagnostic LibraryWithTopLevelStatements = new(
        DiagnosticNumber.Mint("MIST0116"),
        Title: "Top-level statements in library",
        Template: "Library projects cannot have top-level statements. Files: {files}",
        Description: "The project is a library, yet a source file holds statements outside any type, which imply an executable entry point that a library cannot have. Move the statements into a method, or change output_type to executable in mist.toml.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("files", SlotKind.Sequence, SlotKind.Path)]);

    public static readonly MistDiagnostic DuplicateEntryPoint = new(
        DiagnosticNumber.Mint("MIST0117"),
        Title: "Multiple entry points in class",
        Template: "'{class}' is the program's entry point but declares more than one 'Main' that can be one. A program can have only one entry point.",
        Description: "The class chosen as the program's entry point declares more than one Main overload that qualifies, and unlike a choice between classes, entry_point in mist.toml cannot pick a method within one class. Keep exactly one qualifying Main in that class.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("class", SlotKind.Name)]);
}