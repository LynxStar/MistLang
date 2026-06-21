# Design Document

## Overview

This feature adds a `[csproj_properties]` table to `mist.toml` that allows users to declare arbitrary MSBuild properties. These properties are emitted into a dedicated `<PropertyGroup>` in the generated `.csproj` file, enabling scenarios like User Secrets, custom assembly names, and source generator configuration.

The design follows the existing pattern established by `[packages]` and `[framework_references]`: parsed by `ProjectFileParser`, stored in `ProjectConfiguration`, threaded through `CompilerOptions`, and applied by `CompilerDriver` via the existing csproj-patching mechanism.

## Architecture

The feature touches four layers of the existing pipeline, all within the compiler's driver and backend stages:

1. **Parsing** — `ProjectFileParser` reads `[csproj_properties]` from the TOML model.
2. **Data Model** — `ProjectConfiguration` and `CompilerOptions` carry the parsed properties.
3. **CLI Wiring** — `Program.cs` maps `config.CsprojProperties` to the options record.
4. **Emission** — `CompilerDriver` patches the generated csproj with the custom PropertyGroup.

No changes to the `IBackend` contract, semantic model, or any analyzer are required. The feature is entirely within the driver/CLI layer and the post-emit csproj patching phase.

## Components and Interfaces

### ProjectFileParser (modified)

New step 14 added to the `Parse()` method:

- Reads `csproj_properties` from the `TomlTable` model.
- Validates each key (non-empty, character pattern, length ≤ 128).
- Validates each value (must be a TOML string, non-empty).
- Checks keys against a compiler-managed set for warning diagnostics.
- Produces an `ImmutableArray<CsprojProperty>`.

### CompilerDriver (modified)

New method `PatchCsprojWithProperties`:

```csharp
private static void PatchCsprojWithProperties(
    string outputDirectory,
    string projectName,
    ImmutableArray<CsprojProperty> csprojProperties)
```

- Reads the generated csproj file.
- Builds a `<PropertyGroup>` XML fragment with XML-escaped values.
- Inserts after the first `</PropertyGroup>` tag.
- Writes the modified content back.

### CLI Program.cs (modified)

- Passes `config.CsprojProperties` through to `CompilerOptions`.

## Data Models

### CsprojProperty (new)

```csharp
// Source/Mist/Driver/Contracts/CsprojProperty.cs
public sealed record CsprojProperty(string Key, string Value);
```

### ProjectConfiguration (extended)

```csharp
public sealed record ProjectConfiguration(
    string Name,
    string NamespaceRoot,
    ImmutableArray<ProjectReference> References,
    string? EntryPoint = null,
    ImmutableArray<string> GlobalUsings = default,
    ImmutableArray<NuGetPackageReference> Packages = default,
    ImmutableArray<FrameworkReference> FrameworkReferences = default,
    string TargetFramework = "net10.0",
    ImmutableArray<CsprojProperty> CsprojProperties = default);  // NEW
```

### CompilerOptions (extended)

```csharp
public sealed record CompilerOptions(
    // ... existing parameters ...
    string TargetFramework = "net10.0",
    bool VerboseResolution = false,
    ImmutableArray<CsprojProperty> CsprojProperties = default);  // NEW
```

### Diagnostic IDs

| ID | Severity | Condition |
|---|---|---|
| `MIST_CSPROJ_PROP_TABLE_EXPECTED` | Error | `csproj_properties` field present but not a TOML table |
| `MIST_CSPROJ_PROP_EMPTY_KEY` | Error | Property key is empty/whitespace after trimming |
| `MIST_CSPROJ_PROP_INVALID_KEY` | Error | Property key contains invalid characters |
| `MIST_CSPROJ_PROP_KEY_TOO_LONG` | Error | Property key exceeds 128 characters |
| `MIST_CSPROJ_PROP_INVALID_VALUE` | Error | Property value is not a string |
| `MIST_CSPROJ_PROP_EMPTY_VALUE` | Error | Property value is empty/whitespace |
| `MIST_CSPROJ_PROP_MANAGED_OVERRIDE` | Warning | Property key shadows a compiler-managed property |

## Correctness Properties

### Property 1: Preservation

All valid `[csproj_properties]` entries in `mist.toml` appear verbatim (after XML escaping) in the generated csproj.

**Validates: Requirements 1.1, 3.1, 3.2**

### Property 2: Order Determinism

Properties appear in the generated csproj in the same order they appear in `mist.toml`.

**Validates: Requirements 1.1, 3.5**

### Property 3: Isolation

The custom PropertyGroup does not interfere with the hardcoded PropertyGroup or existing ItemGroups.

**Validates: Requirements 3.4**

### Property 4: Non-fatal Validation

A single invalid property entry does not prevent other valid entries from being emitted.

**Validates: Requirements 2.5**

### Property 5: XML Validity

The generated csproj is always valid XML regardless of property values (ensured by XML escaping).

**Validates: Requirements 3.6**

## Error Handling

- **Table type mismatch** — If `csproj_properties` exists but is not a TOML table, an error diagnostic is emitted and the section is skipped entirely. Compilation continues without custom properties.
- **Individual entry errors** — Invalid keys or values produce per-entry error diagnostics. The entry is skipped, but subsequent entries continue to be processed.
- **Managed property warnings** — Shadowing a compiler-managed property produces a warning. The property is still emitted (user intent honored), but the warning alerts the user to potential conflicts.
- **Missing csproj file** — If `PatchCsprojWithProperties` cannot find the csproj file on disk (e.g., `EmitToDisk` is false), it no-ops gracefully — matching the existing behavior of `PatchCsprojWithReferences`.

## Testing Strategy

All tests are in-process (no process spawning):

1. **Parser unit tests** — Feed TOML strings through `ProjectFileParser.Parse()` and assert on the resulting `ProjectConfiguration.CsprojProperties` and diagnostics.
2. **Csproj emission tests** — Call `PatchCsprojWithProperties` with known inputs and verify the output XML string contains the expected PropertyGroup.
3. **Validation tests** — Verify each error/warning diagnostic is emitted for the appropriate invalid inputs.
4. **XML escaping tests** — Verify that values with `<`, `>`, `&`, `"`, `'` are properly escaped in the output.
