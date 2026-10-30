namespace Mist.Common.Diagnostics;

public static partial class DriverDiagnostics
{
    public static readonly MistDiagnostic GeneratorProjectInvalid = new(
        DiagnosticNumber.Mint("MIST0459"),
        Title: "Invalid generator project",
        Template: "Invalid source-generator project: {reason}.",
        Description: "The generator project declares an unsupported project type or library output. Declare source_generator with executable output, or use regular for an ordinary project.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("reason", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorReferenceInvalid = new(
        DiagnosticNumber.Mint("MIST0460"),
        Title: "Invalid generator reference",
        Template: "Invalid source-generator reference '{name}': {reason}.",
        Description: "A generator declaration has an invalid name, path or protocol option, or targets an ordinary project. Correct the declaration and declare generators separately from ordinary references.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("reason", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorCycle = new(
        DiagnosticNumber.Mint("MIST0461"),
        Title: "Source generator cycle",
        Template: "Source-generator dependency cycle: {chain}.",
        Description: "A dependency chain containing a generator edge returns to an active project. Remove an edge in the reported chain before building either project.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("chain", SlotKind.Sequence, SlotKind.Path)]);

    public static readonly MistDiagnostic GeneratorOutputInvalid = new(
        DiagnosticNumber.Mint("MIST0462"),
        Title: "Invalid generator output",
        Template: "Invalid source-generator output '{path}': {reason}.",
        Description: "The generator's output list does not describe a complete set of discoverable Mist sources within staging. Correct the generator to list each emitted source exactly once using safe relative paths.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("reason", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorRunFailed = new(
        DiagnosticNumber.Mint("MIST0463"),
        Title: "Source generator run failed",
        Template: "Source generator '{name}' failed: {reason}.",
        Description: "The generator failed to run or exceeded its limit. Correct the generator or its declared time limit before compiling the consumer.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("reason", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorBuildFailed = new(
        DiagnosticNumber.Mint("MIST0464"),
        Title: "Source generator build failed",
        Template: "Source generator '{name}' did not compile.",
        Description: "The generator project could not be compiled by this toolchain. Correct the separately reported generator diagnostics and retry the consumer build.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic GeneratorInputNotFound = new(
        DiagnosticNumber.Mint("MIST0465"),
        Title: "Source generator input missing",
        Template: "Source-generator input '{path}' does not exist.",
        Description: "A declared input is absent. Restore the file or correct its path in the consumer manifest.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic GeneratorReportedError = new(
        DiagnosticNumber.Mint("MIST0466"),
        Title: "Source generator reported error",
        Template: "{message}",
        Description: "The generator reported an error through its diagnostic protocol. Correct the input at the reported location using the provider code for details.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorReportedWarning = new(
        DiagnosticNumber.Mint("MIST0467"),
        Title: "Source generator reported warning",
        Template: "{message}",
        Description: "The generator reported a warning through its diagnostic protocol. Review the input at the reported location; a closed build may promote this warning to an error.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic GeneratorSeedUsed = new(
        DiagnosticNumber.Mint("MIST0468"),
        Title: "Source generation seed used",
        Template: "Source-generation seed '{path}' is in use.",
        Description: "This build consumed a declared stage-zero tool or captured generated sources. Replace the seed declaration with a Mist generator before claiming a closed later-stage build; this warning can be promoted to an error.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning,
        Slots: [new Slot("path", SlotKind.Path)]);

}