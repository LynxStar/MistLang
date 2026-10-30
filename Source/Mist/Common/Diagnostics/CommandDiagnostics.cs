namespace Mist.Common.Diagnostics;

public static class CommandDiagnostics
{

    public static readonly MistDiagnostic MissingFlagValue = new(
        DiagnosticNumber.Mint("MIST0425"),
        Title: "Missing flag value",
        Template: "Missing value for {flag} flag.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("flag", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownMode = new(
        DiagnosticNumber.Mint("MIST0426"),
        Title: "Unknown compiler mode",
        Template: "Unknown mode: '{mode}'. Valid modes: compile, lex",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("mode", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownPipeline = new(
        DiagnosticNumber.Mint("MIST0427"),
        Title: "Unknown compiler pipeline",
        Template: "Unknown pipeline: '{pipeline}'. Valid pipelines: standard",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("pipeline", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownPlatform = new(
        DiagnosticNumber.Mint("MIST0428"),
        Title: "Unknown target platform",
        Template: "Unknown platform: '{platform}'. Valid platforms: windows, linux, macos",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("platform", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownFlag = new(
        DiagnosticNumber.Mint("MIST0429"),
        Title: "Unknown command flag",
        Template: "Unknown flag: {flag}",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("flag", SlotKind.Name)]);

    public static readonly MistDiagnostic ConflictingVerbosity = new(
        DiagnosticNumber.Mint("MIST0430"),
        Title: "Conflicting verbosity flags",
        Template: "The '--verbose' and '--quiet' flags are mutually exclusive.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LexCommandIncompatible = new(
        DiagnosticNumber.Mint("MIST0431"),
        Title: "Command does not support lex mode",
        Template: "The '{command}' command is not compatible with '--mode lex'.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("command", SlotKind.Enum)]);

    public static readonly MistDiagnostic MultipleProjectFolders = new(
        DiagnosticNumber.Mint("MIST0432"),
        Title: "Multiple project folders",
        Template: "Only a single project folder is accepted.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IndividualFileUnsupported = new(
        DiagnosticNumber.Mint("MIST0433"),
        Title: "Individual source path unsupported",
        Template: "Individual file paths are no longer accepted. Provide a project folder instead.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ProjectDirectoryMissing = new(
        DiagnosticNumber.Mint("MIST0434"),
        Title: "Project directory missing",
        Template: "Directory does not exist: '{path}'.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic SteeringBundleReadFailed = new(
        DiagnosticNumber.Mint("MIST0435"),
        Title: "Steering bundle read failed",
        Template: "Failed to read bundled steering files from '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringBundleMissing = new(
        DiagnosticNumber.Mint("MIST0436"),
        Title: "Steering bundle missing",
        Template: "No bundled steering files found at '{path}'. The compiler installation may be incomplete.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path)]);

    public static readonly MistDiagnostic SteeringBlockReadFailed = new(
        DiagnosticNumber.Mint("MIST0437"),
        Title: "Steering block read failed",
        Template: "Failed to read the bundled AGENTS.md block '{path}': {message}. The compiler installation may be incomplete.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringInstructionsReadFailed = new(
        DiagnosticNumber.Mint("MIST0438"),
        Title: "Project instructions read failed",
        Template: "Failed to read the project's AGENTS.md or CLAUDE.md: {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringMarkersMalformed = new(
        DiagnosticNumber.Mint("MIST0439"),
        Title: "Instruction markers malformed",
        Template: "Cannot update '{path}': {reason}. Fix or remove the markers and run again.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic SteeringDirectoryCreateFailed = new(
        DiagnosticNumber.Mint("MIST0440"),
        Title: "Steering directory create failed",
        Template: "Failed to create reference directory '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringFileInstallFailed = new(
        DiagnosticNumber.Mint("MIST0441"),
        Title: "Steering file install failed",
        Template: "Failed to install steering file '{file}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("file", SlotKind.Name), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringInstructionsWriteFailed = new(
        DiagnosticNumber.Mint("MIST0442"),
        Title: "Project instructions write failed",
        Template: "Failed to write '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringFilesEnumerateFailed = new(
        DiagnosticNumber.Mint("MIST0443"),
        Title: "Installed steering enumeration failed",
        Template: "Failed to enumerate existing steering files in '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringFileReadFailed = new(
        DiagnosticNumber.Mint("MIST0444"),
        Title: "Installed steering read failed",
        Template: "Failed to read existing steering file '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic SteeringFileRemoveFailed = new(
        DiagnosticNumber.Mint("MIST0445"),
        Title: "Managed steering remove failed",
        Template: "Failed to remove managed steering file '{path}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic UnsafeCleanDirectory = new(
        DiagnosticNumber.Mint("MIST0446"),
        Title: "Unsafe clean directory",
        Template: "'{directory}' is or contains the project folder '{project}'; cleaning it would delete the project.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("directory", SlotKind.Path), new Slot("project", SlotKind.Path)]);

    public static readonly MistDiagnostic SourceFilesMissing = new(
        DiagnosticNumber.Mint("MIST0447"),
        Title: "No source files",
        Template: "No .mist source files found.",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CleanDirectoriesFailed = new(
        DiagnosticNumber.Mint("MIST0448"),
        Title: "Build cleanup failed",
        Template: "Failed to clean build directories: {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic LexerRoundTripFailed = new(
        DiagnosticNumber.Mint("MIST0449"),
        Title: "Lexer round trip failed",
        Template: "Lexer round-trip failed for '{path}': original length {originalLength}, reconstructed length {reconstructedLength}, first difference at offset {offset} (expected '{expected}', got '{actual}').",
        Description: "The lexer reconstruction disagrees with its source. Report this compiler bug with the source file that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("path", SlotKind.Path), new Slot("originalLength", SlotKind.Quantity), new Slot("reconstructedLength", SlotKind.Quantity), new Slot("offset", SlotKind.Quantity), new Slot("expected", SlotKind.Optional, SlotKind.Name), new Slot("actual", SlotKind.Optional, SlotKind.Name)]);

    public static readonly MistDiagnostic RunLibraryProject = new(
        DiagnosticNumber.Mint("MIST0450"),
        Title: "Cannot run a library",
        Template: "'mist run' can only be used with executable projects. This project has output_type = \"library\".",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ExecutableArtifactMissing = new(
        DiagnosticNumber.Mint("MIST0451"),
        Title: "Executable artifact missing",
        Template: "Build succeeded but no executable artifact was produced.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic InspectTypeRequired = new(
        DiagnosticNumber.Mint("MIST0452"),
        Title: "Inspection type required",
        Template: "Provide a fully-qualified CLR type name to inspect (e.g., System.Text.Encoding).",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic InspectTypeNotQualified = new(
        DiagnosticNumber.Mint("MIST0453"),
        Title: "Inspection type is not qualified",
        Template: "Could not resolve type '{type}'. Provide a fully-qualified CLR type name (e.g., System.Text.Encoding).",
        Description: "The command cannot proceed with this input. Correct the named argument or project setting and retry.",
        Category: DiagnosticCategory.Cfg,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic InspectTypeNotFound = new(
        DiagnosticNumber.Mint("MIST0454"),
        Title: "Inspection type not found",
        Template: "Could not resolve type '{type}' from any loaded assembly.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic InspectTypeNotProjected = new(
        DiagnosticNumber.Mint("MIST0455"),
        Title: "Inspection type not projected",
        Template: "Could not resolve type '{type}': it is not projected into Mist.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic ProcessStartFailed = new(
        DiagnosticNumber.Mint("MIST0456"),
        Title: "Child process start failed",
        Template: "Failed to start '{executable}' process.",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("executable", SlotKind.Name)]);

    public static readonly MistDiagnostic ProcessLaunchFailed = new(
        DiagnosticNumber.Mint("MIST0457"),
        Title: "Child process launch failed",
        Template: "Failed to launch '{executable}': {message}",
        Description: "The command could not complete its file or process operation. Check the named resource and the operating system error before retrying.",
        Category: DiagnosticCategory.Iox,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("executable", SlotKind.Name), new Slot("message", SlotKind.Text)]);
}