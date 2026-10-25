# Requirements Document

## Introduction

The Mist compiler currently scatters diagnostic creation across dozens of emission sites. Each site manually constructs a `Diagnostic` record with inline `DiagnosticId` strings, hardcoded severity, and interpolated messages. This makes it difficult to maintain consistent messaging, enumerate all diagnostics, or change severity without touching every call site.

This feature introduces a `DiagnosticDescriptor` type that bundles an ID, default severity, pipeline stage, and message template into a single declaration. A centralized registry organizes all descriptors by compiler area. A new reporting API on `DiagnosticBag` lets emission sites report diagnostics by referencing a descriptor and supplying only the location and message format arguments — eliminating manual `Diagnostic` record construction at call sites.

## Glossary

- **Diagnostic_Descriptor**: A record that bundles a `DiagnosticId`, default `DiagnosticSeverity`, `PipelineStage`, and a message template string into a single immutable declaration.
- **Descriptor_Registry**: A set of static classes (one per compiler area) that expose all `DiagnosticDescriptor` instances as `public static readonly` fields.
- **Message_Template**: A format string using positional placeholders (`{0}`, `{1}`, etc.) that the reporting API fills with caller-supplied arguments.
- **DiagnosticBag**: The existing append-only diagnostic collection that accumulates `Diagnostic` records during compilation.
- **Emission_Site**: Any location in the compiler source code that produces a diagnostic (analyzers, driver, parser, backends).
- **Pipeline_Stage**: The enum identifying which compiler phase originated a diagnostic (Lexing, Parsing, Analysis, Backend, Driver, etc.).

## Requirements

### Requirement 1: DiagnosticDescriptor Type

**User Story:** As a compiler maintainer, I want a single type that captures all static metadata about a diagnostic, so that emission sites only supply the dynamic parts (location, format arguments).

#### Acceptance Criteria

1. THE Diagnostic_Descriptor SHALL be a `public readonly record struct` in the `Mist.Common.Diagnostics` namespace.
2. THE Diagnostic_Descriptor SHALL contain a `DiagnosticId` field named `Id`.
3. THE Diagnostic_Descriptor SHALL contain a `DiagnosticSeverity` field named `DefaultSeverity`.
4. THE Diagnostic_Descriptor SHALL contain a `PipelineStage` field named `Stage`.
5. THE Diagnostic_Descriptor SHALL contain a `string` field named `MessageTemplate` holding a positional format string (e.g., `"Variable '{0}' has been moved"`).
6. IF a Diagnostic_Descriptor is constructed with a null, empty, or whitespace-only MessageTemplate, THEN the constructor SHALL throw an `ArgumentException`.
7. IF a Diagnostic_Descriptor is constructed with a DiagnosticId whose Value is null, empty, or whitespace-only, THEN the constructor SHALL throw an `ArgumentException`.

### Requirement 2: Descriptor Registry Organization

**User Story:** As a compiler maintainer, I want all diagnostic descriptors organized by compiler area in discoverable static classes, so that I can find and reference any diagnostic without searching the entire codebase.

#### Acceptance Criteria

1. THE Descriptor_Registry SHALL provide a static class `OwnershipDiagnostics` containing all ownership-related descriptors.
2. THE Descriptor_Registry SHALL provide a static class `TypeCheckDiagnostics` containing all type-checking-related descriptors.
3. THE Descriptor_Registry SHALL provide a static class `EntryPointDiagnostics` containing all entry-point-related descriptors.
4. THE Descriptor_Registry SHALL provide a static class `DriverDiagnostics` containing all driver and project-file-related descriptors.
5. THE Descriptor_Registry SHALL provide a static class `BackendDiagnostics` containing all backend-emission-related descriptors.
6. THE Descriptor_Registry SHALL provide a static class `GenericsDiagnostics` containing all generics-related descriptors.
7. THE Descriptor_Registry SHALL provide a static class `CommonDiagnostics` containing diagnostics shared across multiple areas (stdlib, SDK, namespace resolution, etc.).
8. EACH descriptor field in the Descriptor_Registry SHALL be declared as `public static readonly DiagnosticDescriptor`.
9. EACH descriptor field in the Descriptor_Registry SHALL have an XML doc summary describing the condition that triggers the diagnostic.
10. EACH `DiagnosticDescriptor` instance SHALL contain at minimum a `DiagnosticId`, a `DiagnosticSeverity`, a `PipelineStage`, and a `MessageTemplate` string with zero or more format placeholders.
11. THE Descriptor_Registry SHALL enforce that no two descriptor fields across all registry classes share the same `DiagnosticId` value.
12. IF a diagnostic applies to more than one compiler area, THEN THE Descriptor_Registry SHALL place that descriptor in `CommonDiagnostics` rather than duplicating it across multiple area classes.

### Requirement 3: Reporting API on DiagnosticBag

**User Story:** As a compiler developer, I want a concise reporting method on `DiagnosticBag`, so that I can emit a diagnostic by referencing a descriptor and supplying only location and format arguments.

#### Acceptance Criteria

1. THE DiagnosticBag SHALL expose a `Report` method that accepts a `DiagnosticDescriptor`, a `LinePositionSpan` location, a `string` compilationUnitPath, and a `params ReadOnlySpan<object?>` of format arguments.
2. WHEN `Report` is called, THE DiagnosticBag SHALL construct a `Diagnostic` record using the descriptor's Id, DefaultSeverity, and Stage, with the message produced by formatting the descriptor's MessageTemplate with the supplied arguments, with `AnalyzerName` set to `null`, and SHALL append that Diagnostic to its internal collection.
3. THE DiagnosticBag SHALL expose an overload of `Report` that additionally accepts a `string? analyzerName` parameter, passing it as the `AnalyzerName` value of the constructed Diagnostic, for diagnostics emitted by named analyzers.
4. IF the format arguments count does not match the number of placeholders in the MessageTemplate, THEN THE DiagnosticBag SHALL throw a `FormatException`.
5. THE existing `Add(Diagnostic)` method on DiagnosticBag SHALL remain available for backward compatibility during incremental migration.

### Requirement 4: Migration of DiagnosticId Static Fields

**User Story:** As a compiler maintainer, I want the existing `DiagnosticId` static fields on `DiagnosticId` struct and analyzer classes migrated to the descriptor registry, so that all diagnostics are in one place with their severity and message template.

#### Acceptance Criteria

1. WHEN migrating existing `DiagnosticId` fields from `DiagnosticId.cs`, THE Descriptor_Registry SHALL preserve the exact same string ID values (e.g., a field with value `"MIST_ARITY_MISMATCH"` becomes a DiagnosticDescriptor whose Id has Value `"MIST_ARITY_MISMATCH"`).
2. WHEN migrating existing `DiagnosticId` fields from analyzer classes (OwnershipAnalyzer, TypeCheckerAnalyzer, EntryPointAnalyzer, and any other analyzers declaring local DiagnosticId fields), THE Descriptor_Registry SHALL preserve the exact same string ID values.
3. WHEN migration is complete, THE `DiagnosticId` struct SHALL retain its `public readonly record struct DiagnosticId(string Value)` definition but SHALL contain zero `public static readonly DiagnosticId` field declarations.
4. WHEN migration is complete, analyzer classes SHALL obtain DiagnosticId values exclusively via Descriptor_Registry fields (e.g., `OwnershipDiagnostics.UseAfterMove.Id`) instead of declaring local `static readonly DiagnosticId` fields.
5. WHEN migration is complete, THE Descriptor_Registry SHALL contain one DiagnosticDescriptor field for every `DiagnosticId` static field that previously existed in `DiagnosticId.cs` and the analyzer classes listed in criterion 2, with no field omitted.

### Requirement 5: Migration of Inline Magic Strings

**User Story:** As a compiler maintainer, I want inline `new DiagnosticId("...")` expressions replaced with descriptor references, so that no diagnostic ID exists only as a string literal at a call site.

#### Acceptance Criteria

1. WHEN an emission site in `ProjectFileParser.cs` uses `new DiagnosticId("...")` with a string literal argument, THE migration SHALL replace the inline construction with a reference to the corresponding Descriptor_Registry field and a `Report` call.
2. WHEN an emission site in `CompilerDriver.cs` uses `new DiagnosticId("...")` with a string literal argument, THE migration SHALL replace the inline construction with a reference to the corresponding Descriptor_Registry field and a `Report` call.
3. WHEN an emission site in any file under `Source/Mist/` uses `new DiagnosticId("...")` with a string literal argument, THE migration SHALL replace the inline construction with a reference to the corresponding Descriptor_Registry field and a `Report` call.
4. AFTER migration, THE codebase SHALL contain zero occurrences of `new DiagnosticId(` outside of (a) the `DiagnosticDescriptor` field initializers within the Descriptor_Registry classes, and (b) the `DiagnosticId` struct definition itself.

### Requirement 6: Message Template Migration

**User Story:** As a compiler maintainer, I want existing string-interpolated diagnostic messages converted to message templates, so that message content is defined alongside the descriptor rather than scattered at emission sites.

#### Acceptance Criteria

1. WHEN an existing emission site uses string interpolation to build a message, THE migration SHALL extract the static portions into a MessageTemplate using indexed placeholders (`{0}`, `{1}`, ...) ordered by appearance and pass the corresponding dynamic values as format arguments to `Report`.
2. WHEN an existing emission site uses a static message helper method (e.g., `OwnershipAnalyzer.MutableHiddenMessage`), THE migration SHALL convert the helper's format string into the descriptor's MessageTemplate with indexed placeholders, update all call sites of that helper to pass the dynamic values directly to `Report`, and remove the helper method.
3. THE migrated emission sites SHALL pass only the dynamic values (variable names, type names, file paths) as format arguments to `Report`, with no string concatenation or interpolation at the call site.
4. WHEN a migrated emission site emits a diagnostic, THE system SHALL produce a final formatted message identical to the message produced by the original string-interpolation or helper-method implementation for the same dynamic values.
5. AFTER migration, THE project SHALL compile with zero errors and all existing diagnostic-assertion tests SHALL continue to pass without modification to expected message content.

### Requirement 7: Steering File for New Diagnostics

**User Story:** As a compiler developer, I want a steering file that enforces the descriptor pattern for new diagnostics, so that future diagnostics follow the established convention.

#### Acceptance Criteria

1. THE Steering_File SHALL be located at `.kiro/steering/diagnostic-descriptors.md`.
2. THE Steering_File SHALL instruct that new diagnostics be added as `DiagnosticDescriptor` fields in the registry class that corresponds to the diagnostic's pipeline stage or feature area (e.g., generics diagnostics grouped together, entry point diagnostics grouped together).
3. THE Steering_File SHALL instruct that emission sites use `DiagnosticBag.Report(descriptor, ...)` instead of constructing `Diagnostic` records directly.
4. THE Steering_File SHALL instruct that no new `new DiagnosticId("...")` expressions be introduced outside of `DiagnosticDescriptor` field initializers within registry classes.
5. THE Steering_File SHALL instruct that message templates use positional placeholders (`{0}`, `{1}`) rather than string interpolation at emission sites.
6. IF a new diagnostic does not fit an existing grouping in any registry class, THEN THE Steering_File SHALL instruct the developer to create a new descriptively-named static class containing the `DiagnosticDescriptor` field.
