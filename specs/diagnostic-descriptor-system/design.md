# Design Document

## Overview

This design introduces a `DiagnosticDescriptor` type, a set of per-area registry classes, and a `Report` extension on `DiagnosticBag` that together replace the current pattern of manually constructing `Diagnostic` records at every emission site. The migration is incremental — the existing `Add(Diagnostic)` API remains functional throughout, allowing per-file migration without a big-bang rewrite.

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Descriptor Registry Layer                      │
│                                                                   │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────┐  │
│  │OwnershipDiagnostics│ │TypeCheckDiagnostics│ │EntryPointDiag.│  │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬────────┘  │
│           │                      │                    │           │
│  ┌────────┴─────────┐  ┌────────┴─────────┐  ┌──────┴────────┐  │
│  │DriverDiagnostics  │ │BackendDiagnostics │ │GenericsDiag.  │  │
│  └────────┬─────────┘  └────────┬─────────┘  └──────┬────────┘  │
│           │                      │                    │           │
│           └──────────────┬───────┴────────────────────┘           │
│                          │                                        │
│                 ┌────────┴─────────┐                              │
│                 │ CommonDiagnostics │                              │
│                 └──────────────────┘                              │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼ references
┌─────────────────────────────────────────────────────────────────┐
│                    Infrastructure Layer                           │
│                                                                   │
│  ┌───────────────────────┐    ┌──────────────────────────┐       │
│  │  DiagnosticDescriptor  │    │      DiagnosticBag        │       │
│  │  (readonly record      │    │  + Report(descriptor,...) │       │
│  │   struct)              │    │  + Add(diagnostic) [kept] │       │
│  └───────────────────────┘    └──────────────────────────┘       │
│                                                                   │
│  ┌───────────────┐  ┌──────────────────┐  ┌─────────────────┐   │
│  │ DiagnosticId   │  │DiagnosticSeverity │  │  PipelineStage   │   │
│  │ (record struct)│  │   (enum)          │  │    (enum)        │   │
│  └───────────────┘  └──────────────────┘  └─────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼ used by
┌─────────────────────────────────────────────────────────────────┐
│                    Emission Sites                                 │
│                                                                   │
│  Analyzers (OwnershipAnalyzer, TypeCheckerAnalyzer, etc.)        │
│  Driver (CompilerDriver, ProjectFileParser, ClassicPipeline)     │
│  Backends (CSharpTranspilerBackend, CSharpCompilationDriver)     │
│  Parser (MistParser)                                             │
│  Lexer (MistLexer)                                               │
│  SemanticModelBuilder                                            │
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
Emission site calls:
  diagnostics.Report(OwnershipDiagnostics.UseAfterMove, location, path, variableName)

          │
          ▼

DiagnosticBag.Report:
  1. Reads descriptor.MessageTemplate → "Variable '{0}' has been moved..."
  2. Calls string.Format(template, args) → "Variable 'x' has been moved..."
  3. Constructs Diagnostic record:
       Id = descriptor.Id
       Severity = descriptor.DefaultSeverity
       Stage = descriptor.Stage
       Message = formatted string
       Location = supplied location
       CompilationUnitPath = supplied path
       AnalyzerName = supplied or null
  4. Appends to internal list

          │
          ▼

DiagnosticFormatter.Format (unchanged)
  → "file.mist(5,12): error MIST_OWN_USE_AFTER_MOVE: Variable 'x' has been moved..."
```

## Components and Interfaces

### DiagnosticDescriptor

**File:** `Source/Mist/Common/Diagnostics/DiagnosticDescriptor.cs`

```csharp
namespace Mist.Common.Diagnostics;

/// <summary>
/// Bundles all static metadata for a compiler diagnostic: its stable ID,
/// default severity, originating pipeline stage, and a positional message
/// template. Emission sites supply only the dynamic parts (location, format args).
/// </summary>
public readonly record struct DiagnosticDescriptor
{
    public DiagnosticId Id { get; }
    public DiagnosticSeverity DefaultSeverity { get; }
    public PipelineStage Stage { get; }
    public string MessageTemplate { get; }

    public DiagnosticDescriptor(
        DiagnosticId id,
        DiagnosticSeverity defaultSeverity,
        PipelineStage stage,
        string messageTemplate)
    {
        if(string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException(
                "DiagnosticId.Value must not be null, empty, or whitespace.", nameof(id));
        }

        if(string.IsNullOrWhiteSpace(messageTemplate))
        {
            throw new ArgumentException(
                "MessageTemplate must not be null, empty, or whitespace.", nameof(messageTemplate));
        }

        Id = id;
        DefaultSeverity = defaultSeverity;
        Stage = stage;
        MessageTemplate = messageTemplate;
    }
}
```

**Design decisions:**
- `readonly record struct` for zero-allocation static fields and value equality.
- Validation in the constructor catches misconfigurations at static initialization time (app startup) rather than at diagnostic emission time.
- No `Category` or `Title` fields — the Mist compiler doesn't use Roslyn's diagnostic infrastructure and doesn't need those concepts yet.

### DiagnosticBag Report Methods

**File:** `Source/Mist/Common/Diagnostics/DiagnosticBag.cs` (extended)

```csharp
/// <summary>
/// Reports a diagnostic using a descriptor. Formats the message template
/// with the supplied arguments and appends the constructed Diagnostic.
/// </summary>
public void Report(
    DiagnosticDescriptor descriptor,
    LinePositionSpan location,
    string compilationUnitPath,
    params ReadOnlySpan<object?> args)
{
    var message = args.Length > 0
        ? string.Format(descriptor.MessageTemplate, args.ToArray())
        : descriptor.MessageTemplate;

    diagnostics.Add(new Diagnostic(
        descriptor.Id,
        descriptor.DefaultSeverity,
        descriptor.Stage,
        message,
        location,
        compilationUnitPath,
        AnalyzerName: null));
}

/// <summary>
/// Reports a diagnostic using a descriptor with an explicit analyzer name.
/// Used by named analyzers (OwnershipAnalyzer, TypeChecker, etc.).
/// </summary>
public void Report(
    DiagnosticDescriptor descriptor,
    LinePositionSpan location,
    string compilationUnitPath,
    string? analyzerName,
    params ReadOnlySpan<object?> args)
{
    var message = args.Length > 0
        ? string.Format(descriptor.MessageTemplate, args.ToArray())
        : descriptor.MessageTemplate;

    diagnostics.Add(new Diagnostic(
        descriptor.Id,
        descriptor.DefaultSeverity,
        descriptor.Stage,
        message,
        location,
        compilationUnitPath,
        analyzerName));
}
```

**Design decisions:**
- `params ReadOnlySpan<object?>` avoids allocating a `params object[]` on every call. The `ToArray()` inside is only needed when args are present.
- Two overloads because `params` must be the last parameter.
- `FormatException` behavior (Req 3 AC 4) is satisfied by `string.Format`'s default behavior.

### DiagnosticBagExtensions (Node-Based Reporting)

**File:** `Source/Mist/Common/Diagnostics/DiagnosticBagExtensions.cs`

```csharp
using Mist.Common.Source;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Extension methods for reporting diagnostics with AST node location resolution.
/// Replaces the per-analyzer MakeDiagnostic helper pattern.
/// </summary>
public static class DiagnosticBagExtensions
{
    /// <summary>
    /// Reports a diagnostic using a descriptor, resolving location from an AST node.
    /// </summary>
    public static void ReportAtNode(
        this DiagnosticBag bag,
        DiagnosticDescriptor descriptor,
        SyntaxNode node,
        string? analyzerName,
        params ReadOnlySpan<object?> args)
    {
        var (path, location, _) = DiagnosticHelper.ResolveLocation(node);

        var message = args.Length > 0
            ? string.Format(descriptor.MessageTemplate, args.ToArray())
            : descriptor.MessageTemplate;

        bag.Add(new Diagnostic(
            descriptor.Id,
            descriptor.DefaultSeverity,
            descriptor.Stage,
            message,
            location,
            path,
            analyzerName));
    }
}
```

**Design decisions:**
- Extension method rather than modifying `DiagnosticBag` directly, because `DiagnosticBag` should not depend on `Storm.Runtime.SyntaxNode` (that's a parser-layer type).
- This eliminates the duplicated `MakeDiagnostic` helper that exists in 8+ analyzer/verifier files.
- Uses the existing `DiagnosticHelper.ResolveLocation` which handles `ParsedOrigin`, `SyntheticOrigin`, and `TestOrigin` chains.

### Descriptor Registry Classes

**Directory:** `Source/Mist/Common/Diagnostics/Descriptors/`

Each file is a `public static class` containing `public static readonly DiagnosticDescriptor` fields. The namespace is `Mist.Common.Diagnostics` (no sub-namespace) so emission sites don't need an extra `using`.

| File | Class | Contents |
|------|-------|----------|
| `OwnershipDiagnostics.cs` | `OwnershipDiagnostics` | All `MIST_OWN_*` descriptors |
| `TypeCheckDiagnostics.cs` | `TypeCheckDiagnostics` | All `MIST_TC_*`, `MIST_CONSTRAINT_*`, `MIST_PI_*` descriptors |
| `EntryPointDiagnostics.cs` | `EntryPointDiagnostics` | All `MIST_EP_*` descriptors |
| `DriverDiagnostics.cs` | `DriverDiagnostics` | All `MIST_PROJECT_*`, project-file, SDK, package descriptors |
| `BackendDiagnostics.cs` | `BackendDiagnostics` | All `MIST_BE_*`, `MIST_BUILD_*`, `MIST_CS_*`, `MIST_BACKEND_*`, `MIST_ASM_*` descriptors |
| `GenericsDiagnostics.cs` | `GenericsDiagnostics` | All `MIST_ARITY_*`, `MIST_NOT_GENERIC`, `MIST_MISSING_TYPE_ARGUMENTS`, etc. |
| `CommonDiagnostics.cs` | `CommonDiagnostics` | `MIST_STDLIB_*`, `MIST_NS_*`, `MIST_TYPE_AMBIGUOUS`, `MIST_INTEROP_*`, `MIST_FWREF_*`, `MIST_PKG_*`, `MIST_SDK_*` |
| `ParserDiagnostics.cs` | `ParserDiagnostics` | All `MIST_PARSE_*`, `MIST_REMOVED_*`, `MIST_ABSTRACT_PROPERTY_*` descriptors |
| `LexerDiagnostics.cs` | `LexerDiagnostics` | `MIST_LEX_ERROR` |
| `SemanticDiagnostics.cs` | `SemanticDiagnostics` | `MIST_SEMANTIC_*`, `MIST_UNDEFINED_ABSTRACT`, `MIST_NOT_AN_ABSTRACT`, `MIST_CIRCULAR_*` |

**Example:**

```csharp
namespace Mist.Common.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the ownership and reference system analyzer.
/// </summary>
public static class OwnershipDiagnostics
{
    /// <summary>Access to a variable after it has been moved.</summary>
    public static readonly DiagnosticDescriptor UseAfterMove = new(
        new DiagnosticId("MIST_OWN_USE_AFTER_MOVE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Variable '{0}' has been moved and cannot be accessed here");

    /// <summary>A ref escapes its declaring scope.</summary>
    public static readonly DiagnosticDescriptor RefEscape = new(
        new DiagnosticId("MIST_OWN_REF_ESCAPE"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Reference '{0}' escapes its declaring scope");

    /// <summary>Mutation attempted while frozen ref is outstanding.</summary>
    public static readonly DiagnosticDescriptor MutationBlocked = new(
        new DiagnosticId("MIST_OWN_MUTATION_BLOCKED"),
        DiagnosticSeverity.Error,
        PipelineStage.Analysis,
        "Cannot mutate '{0}' while a frozen ref is outstanding. "
        + "Mutation is blocked until all frozen refs are returned.");

    // ... remaining ownership descriptors
}
```

## Data Models

### DiagnosticDescriptor (New)

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `DiagnosticId` | Stable string identifier (e.g., `"MIST_OWN_USE_AFTER_MOVE"`) |
| `DefaultSeverity` | `DiagnosticSeverity` | Error, Warning, or Info |
| `Stage` | `PipelineStage` | Which compiler phase emits this diagnostic |
| `MessageTemplate` | `string` | Positional format string with `{0}`, `{1}`, etc. placeholders |

### Diagnostic (Unchanged)

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `DiagnosticId` | Copied from descriptor |
| `Severity` | `DiagnosticSeverity` | Copied from descriptor's `DefaultSeverity` |
| `Stage` | `PipelineStage` | Copied from descriptor |
| `Message` | `string` | Formatted message (template + args applied) |
| `Location` | `LinePositionSpan` | Source location |
| `CompilationUnitPath` | `string` | File path |
| `AnalyzerName` | `string?` | Optional analyzer attribution |

### DiagnosticId (Simplified)

After migration, retains only its `Value` property with no static fields:

```csharp
public readonly record struct DiagnosticId(string Value);
```

## Error Handling

### Constructor Validation

`DiagnosticDescriptor` throws `ArgumentException` on:
- Null, empty, or whitespace `DiagnosticId.Value`
- Null, empty, or whitespace `MessageTemplate`

These fire at static initialization time (type load), making misconfigured descriptors immediately visible during development.

### Format Argument Mismatch

When `string.Format(template, args)` is called with more placeholders than arguments, `string.Format` throws `FormatException`. This is the desired behavior per Requirement 3 AC 4 — it surfaces bugs at emission sites immediately rather than producing malformed messages silently.

### RoslynDiagnosticMapper Special Case

The `RoslynDiagnosticMapper` constructs `DiagnosticId` from runtime strings (Roslyn diagnostic codes like `CS0246`). This remains the one legitimate use of `new DiagnosticId(code)` outside the registry. It continues using `DiagnosticBag.Add()` directly since these IDs are not known at Mist-compiler compile time.

## Correctness Properties

### Property 1: Message Equivalence

For every migrated emission site, the final formatted message string produced by `string.Format(descriptor.MessageTemplate, args)` MUST be byte-for-byte identical to the message previously produced by the inline string interpolation or helper method, given the same dynamic values. This ensures existing diagnostic-assertion tests pass without modification.

**Validates: Requirements 6.4, 6.5**

### Property 2: ID Uniqueness

A reflection-based unit test enforces that no two `DiagnosticDescriptor` fields across all registry classes share the same `Id.Value` string. This prevents accidental ID collisions when adding new diagnostics.

**Validates: Requirements 2.11, 4.1, 4.2**

### Property 3: Severity Consistency

Each descriptor's `DefaultSeverity` matches the severity that was previously hardcoded at the emission site. The migration does not change any diagnostic's severity — that would be a separate, intentional change requiring user approval per the zero-warnings policy.

**Validates: Requirements 4.1, 4.2, 5.1, 5.2, 5.3**

## Migration Strategy

The migration is structured to be incremental and safe:

1. **Phase 1: Infrastructure** — Add `DiagnosticDescriptor`, the `Report` methods, and the `ReportAtNode` extension. All existing code continues to compile unchanged.

2. **Phase 2: Registry population** — Create the descriptor registry classes. Initially these coexist with the old `DiagnosticId` static fields.

3. **Phase 3: Emission site migration** — Migrate one area at a time. Each migration:
   - Replaces `MakeDiagnostic(id, message, node, model)` calls with `diagnostics.ReportAtNode(Descriptor, node, analyzerName, args...)`.
   - Replaces `new Diagnostic(new DiagnosticId("..."), ...)` with `diagnostics.Report(Descriptor, location, path, args...)`.
   - Removes the local `DiagnosticId` fields and `MakeDiagnostic` helper once all usages in that file are migrated.

4. **Phase 4: Cleanup** — Remove static fields from `DiagnosticId.cs`, delete dead helper methods, add the uniqueness test, add the steering file.

Each phase is independently compilable and testable.

## Modified Components

| Component | Change |
|-----------|--------|
| `DiagnosticBag.cs` | Add `Report` overloads |
| `DiagnosticId.cs` | Remove all static `DiagnosticId` fields (Phase 4) |
| `OwnershipAnalyzer.cs` | Remove local `DiagnosticId` fields + `MakeDiagnostic` + message helpers; use `ReportAtNode` |
| `TypeCheckerAnalyzer.cs` | Remove local `DiagnosticId` fields; use `ReportAtNode` |
| `EntryPointAnalyzer.cs` | Replace `new Diagnostic(...)` with `Report` calls |
| `DropAnalyzer.cs` | Remove local `DiagnosticId` fields + `MakeDiagnostic`; use `ReportAtNode` |
| `ReadonlyAnalyzer.cs` | Remove local `DiagnosticId` fields + `MakeDiagnostic`; use `ReportAtNode` |
| `FrameEscapeAnalyzer.cs` | Remove local `DiagnosticId` fields + `MakeDiagnostic`; use `ReportAtNode` |
| `MoveBoundaryResolver.cs` | Remove `MakeDiagnostic`; use `ReportAtNode` |
| `ThisTierInferencePass.cs` | Remove `MakeDiagnostic`; use `ReportAtNode` |
| `OwnershipInferencePass.cs` | Remove `MakeDiagnostic`; use `ReportAtNode` |
| `FieldTypeRestrictionVerifier.cs` | Remove `MakeDiagnostic`; use `ReportAtNode` |
| `CooperationContractVerifier.cs` | Remove `MakeDiagnostic`; use `ReportAtNode` |
| `ProjectFileParser.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `CompilerDriver.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `ClassicPipeline.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `MistParser.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `MistLexer.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `SemanticModelBuilder.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `AssemblyTypeProvider.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `CSharpCompilationDriver.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `CSharpTranspilerBackend.cs` | Replace inline `new DiagnosticId(...)` with `Report` calls |
| `RoslynDiagnosticMapper.cs` | **Unchanged** — continues using `new DiagnosticId(code)` for runtime-mapped Roslyn codes |

## New Components

| Component | Purpose |
|-----------|---------|
| `Source/Mist/Common/Diagnostics/DiagnosticDescriptor.cs` | The descriptor record struct |
| `Source/Mist/Common/Diagnostics/DiagnosticBagExtensions.cs` | `ReportAtNode` extension method |
| `Source/Mist/Common/Diagnostics/Descriptors/OwnershipDiagnostics.cs` | Ownership descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/TypeCheckDiagnostics.cs` | Type-checker descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/EntryPointDiagnostics.cs` | Entry-point descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/DriverDiagnostics.cs` | Driver/project descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/BackendDiagnostics.cs` | Backend descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/GenericsDiagnostics.cs` | Generics descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/CommonDiagnostics.cs` | Shared/cross-area descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/ParserDiagnostics.cs` | Parser descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/LexerDiagnostics.cs` | Lexer descriptor registry |
| `Source/Mist/Common/Diagnostics/Descriptors/SemanticDiagnostics.cs` | Semantic model descriptor registry |
| `Test/Mist.Tests/Diagnostics/DiagnosticDescriptorRegistryTests.cs` | Uniqueness enforcement test |
| `.kiro/steering/diagnostic-descriptors.md` | Steering file for future diagnostics |

## Testing Strategy

- **Uniqueness test**: Reflection-based test asserts no duplicate IDs across all registry classes.
- **Existing tests**: All current diagnostic-assertion tests pass without modification because the emitted `Diagnostic` records contain identical ID strings, severity, and message content.
- **Format correctness**: The `DiagnosticFormatterTests` and property tests continue to validate the formatter output format is unchanged.
- **Validation tests**: Unit tests for `DiagnosticDescriptor` constructor validation (null/empty ID, null/empty template throw `ArgumentException`).
- **Report method test**: Unit test that `DiagnosticBag.Report` produces a `Diagnostic` with the expected fields and formatted message.
