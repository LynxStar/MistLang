# Implementation Plan: Diagnostic Descriptor System

## Overview

This plan migrates the Mist compiler's diagnostic infrastructure from scattered magic strings and manual `Diagnostic` record construction to a centralized descriptor-based system. The work is split into 4 phases: infrastructure, registry population, emission site migration, and cleanup/enforcement. Each phase compiles independently.

## Tasks

- [x] 1. Create `DiagnosticDescriptor` readonly record struct
  - Create file `Source/Mist/Common/Diagnostics/DiagnosticDescriptor.cs`
  - Implement `public readonly record struct DiagnosticDescriptor` with fields: `Id` (DiagnosticId), `DefaultSeverity` (DiagnosticSeverity), `Stage` (PipelineStage), `MessageTemplate` (string)
  - Add constructor validation: throw `ArgumentException` if `id.Value` is null/empty/whitespace or if `messageTemplate` is null/empty/whitespace
  - Verify the project builds with zero warnings
  - Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7

- [x] 2. Add `Report` methods to `DiagnosticBag`
  - Add `using Mist.Common.Source;` to `DiagnosticBag.cs`
  - Add `Report(DiagnosticDescriptor, LinePositionSpan, string compilationUnitPath, params ReadOnlySpan<object?> args)` overload that formats the template and appends a Diagnostic with `AnalyzerName: null`
  - Add `Report(DiagnosticDescriptor, LinePositionSpan, string compilationUnitPath, string? analyzerName, params ReadOnlySpan<object?> args)` overload for named analyzers
  - Keep existing `Add(Diagnostic)` method unchanged
  - Verify the project builds with zero warnings
  - Requirements: 3.1, 3.2, 3.3, 3.4, 3.5

- [x] 3. Create `DiagnosticBagExtensions` with `ReportAtNode`
  - Create file `Source/Mist/Common/Diagnostics/DiagnosticBagExtensions.cs`
  - Implement `public static class DiagnosticBagExtensions` with method `ReportAtNode(this DiagnosticBag, DiagnosticDescriptor, SyntaxNode, string? analyzerName, params ReadOnlySpan<object?> args)`
  - Use `DiagnosticHelper.ResolveLocation(node)` to obtain path and location
  - Format the message template with args and append via `bag.Add(new Diagnostic(...))`
  - Verify the project builds with zero warnings
  - Requirements: 3.1, 3.2, 3.3

- [x] 4. Create `OwnershipDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/OwnershipDiagnostics.cs`
  - Migrate all `MIST_OWN_*` DiagnosticId fields from `OwnershipAnalyzer.cs` into `public static readonly DiagnosticDescriptor` fields with their severity (Error), stage (Analysis), and message templates extracted from the existing message helper methods and inline interpolations
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.1, 2.8, 2.9, 2.10, 4.1, 4.2, 6.1, 6.2

- [x] 5. Create `TypeCheckDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/TypeCheckDiagnostics.cs`
  - Migrate all `MIST_TC_*`, `MIST_CONSTRAINT_*`, and `MIST_PI_*` DiagnosticId fields from `TypeCheckerAnalyzer.cs` into `public static readonly DiagnosticDescriptor` fields with severity, stage (Analysis), and message templates
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.2, 2.8, 2.9, 2.10, 4.1, 4.2

- [x] 6. Create `EntryPointDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/EntryPointDiagnostics.cs`
  - Migrate all `MIST_EP_*` DiagnosticId fields from `DiagnosticId.cs` into `public static readonly DiagnosticDescriptor` fields with severity (Error), stage (Analysis), and message templates extracted from `EntryPointAnalyzer.cs` emission sites
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.3, 2.8, 2.9, 2.10, 4.1, 6.1

- [x] 7. Create `DriverDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/DriverDiagnostics.cs`
  - Migrate all `MIST_PROJECT_*` DiagnosticId fields from `DiagnosticId.cs` and inline magic strings from `ProjectFileParser.cs` and `CompilerDriver.cs` into `public static readonly DiagnosticDescriptor` fields with appropriate severity, stage (Driver), and message templates
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.4, 2.8, 2.9, 2.10, 4.1, 5.1, 5.2

- [x] 8. Create `BackendDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/BackendDiagnostics.cs`
  - Migrate all `MIST_BE_*`, `MIST_BUILD_*`, `MIST_CS_*`, `MIST_BACKEND_*`, `MIST_ASM_*` DiagnosticId values (from `DiagnosticId.cs` and inline magic strings in backend files) into `public static readonly DiagnosticDescriptor` fields with severity, stage (Backend), and message templates
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.5, 2.8, 2.9, 2.10, 4.1, 5.1

- [x] 9. Create `GenericsDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/GenericsDiagnostics.cs`
  - Migrate all generics-related DiagnosticId fields (`MIST_ARITY_MISMATCH`, `MIST_NOT_GENERIC`, `MIST_MISSING_TYPE_ARGUMENTS`, `MIST_DUPLICATE_TYPE_PARAMETER`, `MIST_TYPE_PARAMETER_SHADOWS_TYPE`, `MIST_METHOD_ARITY_MISMATCH`, `MIST_UNDEFINED_TYPE_ARGUMENT`) from `DiagnosticId.cs` into `public static readonly DiagnosticDescriptor` fields with severity, stage, and message templates
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.6, 2.8, 2.9, 2.10, 4.1

- [x] 10. Create `CommonDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/CommonDiagnostics.cs`
  - Migrate cross-area DiagnosticId fields (`MIST_STDLIB_*`, `MIST_NS_*`, `MIST_TYPE_AMBIGUOUS`, `MIST_INTEROP_*`, `MIST_FWREF_*`, `MIST_PKG_*`, `MIST_SDK_*`, `MIST_TARGET_NEW_*`, default keyword diagnostics, void type diagnostics, associated type diagnostics) from `DiagnosticId.cs` into `public static readonly DiagnosticDescriptor` fields
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.7, 2.8, 2.9, 2.10, 2.12, 4.1

- [x] 11. Create `ParserDiagnostics` registry
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/ParserDiagnostics.cs`
  - Migrate all parser inline magic strings (`MIST_PARSE_*`, `MIST_REMOVED_*`, `MIST_ABSTRACT_PROPERTY_*`) from `MistParser.cs` into `public static readonly DiagnosticDescriptor` fields with severity (Error), stage (Parsing), and message templates
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.8, 2.9, 2.10, 5.3

- [x] 12. Create `LexerDiagnostics` and `SemanticDiagnostics` registries
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/LexerDiagnostics.cs` with `MIST_LEX_ERROR` descriptor
  - Create file `Source/Mist/Common/Diagnostics/Descriptors/SemanticDiagnostics.cs` with `MIST_SEMANTIC_*`, `MIST_UNDEFINED_ABSTRACT`, `MIST_NOT_AN_ABSTRACT`, `MIST_CIRCULAR_*` descriptors extracted from `SemanticModelBuilder.cs` and `ClassicPipeline.cs` inline magic strings
  - Preserve exact ID string values
  - Add XML doc summaries to each field
  - Verify the project builds with zero warnings
  - Requirements: 2.8, 2.9, 2.10, 5.3

- [x] 13. Migrate `EntryPointAnalyzer.cs` emission sites
  - Replace all `new Diagnostic(DiagnosticId.*, ...)` calls with `diagnostics.Report(EntryPointDiagnostics.*, location, path, AnalyzerId, args...)` calls
  - Remove any local DiagnosticId field references that are now in the registry
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.4, 6.1, 6.3

- [x] 14. Migrate `OwnershipAnalyzer.cs` emission sites
  - Replace all `MakeDiagnostic(id, message, node, model)` calls with `diagnostics.ReportAtNode(OwnershipDiagnostics.*, node, "Ownership", args...)`
  - Remove the local `static readonly DiagnosticId` fields (UseAfterMove, RefEscape, etc.)
  - Remove the private `MakeDiagnostic` helper method
  - Remove the static message helper methods (MutableHiddenMessage, RequiresOwnedMessage, etc.) whose content is now in the descriptor templates
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.2, 4.4, 6.1, 6.2, 6.3

- [x] 15. Migrate `MoveBoundaryResolver.cs`, `ThisTierInferencePass.cs`, `OwnershipInferencePass.cs`, `FieldTypeRestrictionVerifier.cs`, `CooperationContractVerifier.cs` emission sites
  - Replace all `MakeDiagnostic(...)` calls with `diagnostics.ReportAtNode(OwnershipDiagnostics.*, node, "Ownership", args...)`
  - Remove the private `MakeDiagnostic` helper methods from each file
  - Update references from `OwnershipAnalyzer.*` IDs to `OwnershipDiagnostics.*` descriptor references
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.4, 6.1, 6.3

- [x] 16. Migrate `TypeCheckerAnalyzer.cs` emission sites
  - Replace all diagnostic emission calls with `diagnostics.ReportAtNode(TypeCheckDiagnostics.*, node, "TypeChecker", args...)` or `diagnostics.Report(TypeCheckDiagnostics.*, ...)` as appropriate
  - Remove the local `static readonly DiagnosticId` fields
  - Remove any local `MakeDiagnostic` helper if present
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.2, 4.4, 6.1, 6.3

- [x] 17. Migrate `DropAnalyzer.cs`, `ReadonlyAnalyzer.cs`, `FrameEscapeAnalyzer.cs` emission sites
  - Replace all `MakeDiagnostic(...)` calls with `diagnostics.ReportAtNode(...)` using the appropriate registry class
  - Remove local `static readonly DiagnosticId` fields from each analyzer
  - Remove the private `MakeDiagnostic` helper methods from each file
  - Create additional registry entries in the appropriate class if any IDs from these analyzers are not yet in a registry
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.2, 4.4, 6.1, 6.3

- [x] 18. Migrate `MistParser.cs` and `MistLexer.cs` emission sites
  - Replace all `new Diagnostic(new DiagnosticId("MIST_PARSE_*"), ...)` calls with `diagnostics.Report(ParserDiagnostics.*, location, path, args...)`
  - Replace `new Diagnostic(new DiagnosticId("MIST_LEX_ERROR"), ...)` with `diagnostics.Report(LexerDiagnostics.LexError, location, path, args...)`
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 5.3, 6.1, 6.3

- [x] 19. Migrate `SemanticModelBuilder.cs` emission sites
  - Replace all `new Diagnostic(new DiagnosticId("MIST_SEMANTIC_*"), ...)` and other inline magic string constructions with `diagnostics.Report(SemanticDiagnostics.*, ...)` calls
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 5.3, 6.1, 6.3

- [x] 20. Migrate `ProjectFileParser.cs` and `CompilerDriver.cs` emission sites
  - Replace all `new Diagnostic(new DiagnosticId("MIST_PROJECT_*"), ...)` calls in ProjectFileParser with `diagnostics.Report(DriverDiagnostics.*, ...)` calls
  - Replace inline magic strings in CompilerDriver with `diagnostics.Report(DriverDiagnostics.*, ...)` calls
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 5.1, 5.2, 6.1, 6.3

- [x] 21. Migrate backend emission sites (`CSharpTranspilerBackend.cs`, `CSharpCompilationDriver.cs`, `AssemblyTypeProvider.cs`)
  - Replace `new Diagnostic(new DiagnosticId("MIST_BUILD_*"|"MIST_CS_*"|"MIST_BACKEND_*"|"MIST_ASM_*"), ...)` calls with `diagnostics.Report(BackendDiagnostics.*, ...)` calls
  - Leave `RoslynDiagnosticMapper.cs` unchanged (runtime-mapped IDs)
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 5.1, 5.3, 6.1, 6.3

- [x] 22. Migrate `ClassicPipeline.cs` emission sites
  - Replace `new Diagnostic(new DiagnosticId("MIST_SEMANTIC_FAILURE"), ...)` with `diagnostics.Report(SemanticDiagnostics.SemanticFailure, ...)` call
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 5.3, 6.1, 6.3

- [x] 23. Remove static DiagnosticId fields from `DiagnosticId.cs`
  - Remove all `public static readonly DiagnosticId` field declarations from the `DiagnosticId` struct
  - Leave only the `public readonly record struct DiagnosticId(string Value);` definition
  - Fix any remaining compile errors from code still referencing `DiagnosticId.*` fields (update to use the appropriate registry descriptor's `.Id` property if needed)
  - Verify the project builds with zero warnings and existing tests pass
  - Requirements: 4.3, 4.5

- [ ] 24. Add ID uniqueness enforcement test
  - Create file `Test/Mist.Tests/Diagnostics/DiagnosticDescriptorRegistryTests.cs`
  - Implement a reflection-based test that discovers all static classes in `Mist.Common.Diagnostics` namespace with `DiagnosticDescriptor` fields, collects all `Id.Value` strings, and asserts no duplicates
  - Run the test and confirm it passes
  - Requirements: 2.11

- [ ] 25. Verify zero inline `new DiagnosticId(` outside registry
  - Run a grep/search across the `Source/Mist/` directory for `new DiagnosticId(` occurrences
  - Confirm all occurrences are either (a) in Descriptor registry class initializers or (b) in `RoslynDiagnosticMapper.cs`
  - Fix any remaining occurrences found outside those locations
  - Requirements: 5.4

- [ ] 26. Create steering file for diagnostic descriptor convention
  - Create file `.kiro/steering/diagnostic-descriptors.md`
  - Document the rule: new diagnostics must be added as `DiagnosticDescriptor` fields in the appropriate registry class
  - Document: emission sites must use `DiagnosticBag.Report(descriptor, ...)` or `ReportAtNode(descriptor, ...)` — not construct `Diagnostic` records directly
  - Document: no new `new DiagnosticId("...")` outside of registry descriptor initializers
  - Document: message templates use positional placeholders (`{0}`, `{1}`) — no string interpolation at emission sites
  - Document: if a new diagnostic doesn't fit an existing registry class, create a new descriptively-named static class
  - Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6

- [ ] 27. Run `dotnet format` and final build verification
  - Run `dotnet format --verify-no-changes` on the solution
  - Fix any formatting violations
  - Run the full test suite and confirm all tests pass
  - Confirm zero warnings in the build output
  - Requirements: 6.5

## Task Dependency Graph

```json
{
  "waves": [
    {"tasks": [1]},
    {"tasks": [2]},
    {"tasks": [3]},
    {"tasks": [4, 5, 6, 7, 8, 9, 10, 11, 12]},
    {"tasks": [13, 14, 15, 16, 17, 18, 19, 20, 21, 22]},
    {"tasks": [23]},
    {"tasks": [24, 25]},
    {"tasks": [26]},
    {"tasks": [27]}
  ]
}
```

## Notes

- `RoslynDiagnosticMapper.cs` is intentionally excluded from migration — it creates `DiagnosticId` from runtime Roslyn diagnostic codes that are not known at Mist-compiler compile time.
- Test files (`Test/`) that construct `new DiagnosticId("...")` for test fixtures are outside the scope of this migration. The requirement targets `Source/Mist/` only.
- Phase 2 registry tasks can be executed in parallel — they are independent of each other.
- Phase 3 migration tasks have soft dependencies on their corresponding Phase 2 registry task but can also be combined (create registry + migrate in one pass per area).
