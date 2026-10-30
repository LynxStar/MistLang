# Implementation Plan: Csproj Property Passthrough

## Overview

Implement the `[csproj_properties]` table in `mist.toml` to allow arbitrary MSBuild properties to be passed through to the generated csproj file. The implementation follows the established pattern of `[packages]` and `[framework_references]`.

## Tasks

- [x] 1. Create the `CsprojProperty` record at `Source/Mist/Driver/Contracts/CsprojProperty.cs` with `Key` and `Value` parameters and XML doc comments matching the style of `NuGetPackageReference.cs`.
- [x] 2. Extend `ProjectConfiguration` record with `ImmutableArray<CsprojProperty> CsprojProperties = default` parameter and update its XML doc comment.
- [x] 3. Extend `CompilerOptions` record with `ImmutableArray<CsprojProperty> CsprojProperties = default` parameter and update its XML doc comment.
- [x] 4. Add `[csproj_properties]` parsing in `ProjectFileParser.Parse()` as step 14: read the TOML key, validate it is a table, iterate entries validating keys (empty, pattern, length) and values (string type, non-empty), emit error diagnostics for invalid entries while continuing to process remaining entries, and build the `ImmutableArray<CsprojProperty>`.
- [x] 5. Add compiler-managed property override warning: define a case-insensitive `HashSet<string>` of managed names (`OutputType`, `TargetFramework`, `ImplicitUsings`, `Nullable`, `Deterministic`, `EnableNETAnalyzers`), check each key during parsing, and emit `MIST0098` warning while still including the property.
- [x] 6. Thread `CsprojProperties` through the CLI by updating the `CompilerOptions` construction in `Source/Mist.CLI/Program.cs` to pass `CsprojProperties: config.CsprojProperties`.
- [x] 7. Add `PatchCsprojWithProperties` method in `CompilerDriver.cs` that builds a `<PropertyGroup>` XML fragment with XML-escaped values (using `SecurityElement.Escape()`), inserts it after the first `</PropertyGroup>` tag, preserves declaration order, and no-ops when the collection is empty. Call it from the same location as `PatchCsprojWithReferences`.
- [x] 8. Build the full solution (`dotnet build Mist.slnx`) and verify zero warnings are introduced.

## Task Dependency Graph

```json
{
  "waves": [
    {"tasks": [1]},
    {"tasks": [2]},
    {"tasks": [3]},
    {"tasks": [4]},
    {"tasks": [5]},
    {"tasks": [6]},
    {"tasks": [7]},
    {"tasks": [8]}
  ]
}
```

Tasks are sequential: each extends the data model or pipeline established by the previous task.

## Notes

- All diagnostic IDs follow the `MIST_CSPROJ_PROP_* (historical scheme)` prefix convention.
- XML escaping uses `System.Security.SecurityElement.Escape()` which handles `<`, `>`, `&`, `"`, and `'`.
- The insertion point targets `</PropertyGroup>` (first occurrence) rather than `</Project>` to place custom properties before ItemGroups.
- No process-spawning tests are needed; all validation is in-process via parser and string manipulation.
