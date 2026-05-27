# Design Document

## Overview

This design migrates the `mist.toml` project reference format from `[[references]]` (TOML array-of-tables) to `[references]` (TOML table with named keys). The change touches the data model, parser, resolver, tests, sample files, and book documentation.

The scope is narrow: only the `ProjectReference` record, `ProjectFileParser`, `ProjectReferenceResolver`, and their tests are modified. The rest of the compiler pipeline (semantic model, backends, analysis) consumes `ResolvedProjectReference` which is unchanged.

## Architecture

The change is localized to the Driver layer of the compiler. The parser (`ProjectFileParser`) reads the new TOML format and produces `ProjectReference` records with a `Name` field. The resolver (`ProjectReferenceResolver`) validates that names match target projects. All downstream consumers (`CompilerDriver`, semantic model, backends) are unaffected because they consume `ResolvedProjectReference` which is unchanged.

## Components and Interfaces

### Data Flow

```
mist.toml (new format)
    │
    ▼
ProjectFileParser.Parse()
    │  reads [references] table
    │  validates keys + values
    │  produces ProjectReference(Name, Path)
    ▼
ProjectConfiguration.References: ImmutableArray<ProjectReference>
    │
    ▼
ProjectReferenceResolver.Resolve()
    │  DFS traversal (unchanged)
    │  NEW: name-mismatch check after parsing each target
    ▼
ImmutableArray<ResolvedProjectReference>  (unchanged record)
    │
    ▼
CompilerDriver (unchanged consumer)
```

### Components Modified

| Component | File | Change |
|-----------|------|--------|
| `ProjectReference` | `Source/Mist/Driver/Contracts/ProjectReference.cs` | Add `Name` parameter (positional, before `Path`) |
| `ProjectFileParser` | `Source/Mist/Driver/ProjectFileParser.cs` | Replace `TomlTableArray` iteration with `TomlTable` iteration |
| `ProjectReferenceResolver` | `Source/Mist/Driver/ProjectReferenceResolver.cs` | Add name-mismatch validation; pass name into DFS |
| `DiagnosticId` | `Source/Mist/Common/Diagnostics/DiagnosticId.cs` | Add 3 new constants |
| `ProjectConfigurationRoundTripPropertyTests` | `Test/Mist.Tests/Driver/ProjectConfigurationRoundTripPropertyTests.cs` | Update serializer + generator to include `Name` |
| `ProjectReferenceResolverTests` | `Test/Mist.Tests/Driver/ProjectReferenceResolverTests.cs` | Update `RefTo` helper, add name-mismatch test |
| `ProjectReferencePropertyTests` | `Test/Mist.Tests/Driver/ProjectReferencePropertyTests.cs` | Update TOML generation in all properties to new format |
| `samples/AllFeatures/mist.toml` | `samples/AllFeatures/mist.toml` | Migrate to `[references]` syntax |
| Book chapter | `book/src/tooling/project-configuration.md` | Rewrite references section |

### Interface Changes

**ProjectReference record** — new constructor signature:

```csharp
public sealed record ProjectReference(string Name, string Path);
```

**ProjectReferenceResolver.Dfs** — new private method signature:

```csharp
private static void Dfs(
    string referenceName,
    string canonicalTomlPath,
    List<string> chain,
    HashSet<string> visited,
    HashSet<string> inProgress,
    List<ResolvedProjectReference> result,
    DiagnosticBag diagnostics)
```

**Public API (`Resolve`)** — signature unchanged, but now each `ProjectReference` in the input carries a `Name` field.

## Data Models

### ProjectReference (modified)

```csharp
/// <param name="Name">
/// The declared reference name (TOML key). Must match the pattern
/// ^[a-zA-Z0-9\-_\.]+$ and not exceed 128 characters.
/// Validated by ProjectFileParser; compared against target project's
/// name by ProjectReferenceResolver.
/// </param>
/// <param name="Path">
/// Normalized absolute path to the referenced project's mist.toml file.
/// </param>
public sealed record ProjectReference(string Name, string Path);
```

### New DiagnosticId Constants

```csharp
public static readonly DiagnosticId ProjectReferenceInvalidFormat =
    new("MIST_PROJECT_REFERENCE_INVALID_FORMAT");

public static readonly DiagnosticId ProjectReferenceInvalidName =
    new("MIST_PROJECT_REFERENCE_INVALID_NAME");

public static readonly DiagnosticId ProjectReferenceNameMismatch =
    new("MIST_PROJECT_REFERENCE_NAME_MISMATCH");
```

### TOML Format (new)

```toml
name = "AllFeatures"

[references]
Primitives = { path = "../Primitives" }
Classes = { path = "../Classes" }
```

Equivalent expanded form:

```toml
[references.Primitives]
path = "../Primitives"

[references.Classes]
path = "../Classes"
```

Both forms produce identical `TomlTable` structures via Tomlyn and are handled identically by the parser.

## Detailed Design

### 1. ProjectFileParser — Reference Parsing (Step 9)

Replace the existing `TomlTableArray` logic with `TomlTable` iteration:

```csharp
// 9. Read [references] table (optional)
var references = ImmutableArray.CreateBuilder<ProjectReference>();

if (model.TryGetValue("references", out var refsObj))
{
    if (refsObj is not TomlTable refsTable)
    {
        diagnostics.Add(new Diagnostic(
            DiagnosticId.ProjectReferenceInvalidFormat,
            DiagnosticSeverity.Error,
            PipelineStage.Driver,
            $"The 'references' field in '{filePath}' must be a table ([references]).",
            Location: default,
            CompilationUnitPath: filePath,
            AnalyzerName: null));
        return null;
    }

    foreach (var (refName, refValue) in refsTable)
    {
        // 9a. Validate reference key name (empty → pattern → length)
        if (string.IsNullOrWhiteSpace(refName))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceInvalidName, ...));
            return null;
        }

        if (!ValidNamePattern().IsMatch(refName))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceInvalidName, ...));
            return null;
        }

        if (refName.Length > MaxNameLength)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceInvalidName, ...));
            return null;
        }

        // 9b. Validate reference value is a table
        if (refValue is not TomlTable refEntry)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceInvalidFormat, ...));
            return null;
        }

        // 9c. Extract and validate 'path' field
        if (!refEntry.TryGetValue("path", out var pathObj) || pathObj is not string rawPath)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceMissingPath, ...));
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.ProjectReferenceEmptyPath, ...));
            return null;
        }

        // 9d. Normalize path (unchanged logic)
        string normalizedPath = /* same as before */;

        references.Add(new ProjectReference(refName, normalizedPath));
    }
}
```

### 2. ProjectReferenceResolver — Name Mismatch Check

After successfully parsing the target project's config inside `Dfs`:

```csharp
// Name-mismatch validation (Requirement 4)
if (config.Name != referenceName)
{
    diagnostics.Add(new Diagnostic(
        DiagnosticId.ProjectReferenceNameMismatch,
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        $"Reference key '{referenceName}' does not match the target project's name '{config.Name}' in '{canonicalTomlPath}'.",
        Location: default,
        CompilationUnitPath: canonicalTomlPath,
        AnalyzerName: null));

    // Skip this reference and its transitive deps — do NOT recurse
    inProgress.Remove(canonicalTomlPath);
    chain.RemoveAt(chain.Count - 1);
    return;
}
```

The recursive call passes the name from transitive references:

```csharp
foreach (var transRef in config.References)
{
    Dfs(transRef.Name, transRef.Path, chain, visited, inProgress, result, diagnostics);
}
```

### 3. Round-Trip Property Test Serializer

```csharp
private static string SerializeToToml(ProjectConfiguration config)
{
    var sb = new StringBuilder();
    sb.AppendLine($"name = \"{EscapeTomlString(config.Name)}\"");
    sb.AppendLine($"namespace_root = \"{EscapeTomlString(config.NamespaceRoot)}\"");

    if (!config.GlobalUsings.IsDefaultOrEmpty)
    {
        sb.AppendLine();
        sb.AppendLine("[global_usings]");
        var nsEntries = string.Join(", ",
            config.GlobalUsings.Select(ns => $"\"{EscapeTomlString(ns)}\""));
        sb.AppendLine($"namespaces = [{nsEntries}]");
    }

    if (!config.References.IsDefaultOrEmpty)
    {
        sb.AppendLine();
        sb.AppendLine("[references]");
        foreach (var reference in config.References)
        {
            sb.AppendLine(
                $"{reference.Name} = {{ path = \"{EscapeTomlString(reference.Path)}\" }}");
        }
    }

    return sb.ToString();
}
```

### 4. Sample Migration

`samples/AllFeatures/mist.toml` converts each `[[references]]` entry to a named key. The key is the `name` field from the target project's `mist.toml` (which matches the directory name for all existing samples).

## Error Handling

### Parser Errors (ProjectFileParser)

| Condition | Diagnostic ID | Behavior |
|-----------|--------------|----------|
| `references` field is not a TOML table | `ProjectReferenceInvalidFormat` | Return null |
| Reference value is not a table | `ProjectReferenceInvalidFormat` | Return null |
| Reference key empty/whitespace | `ProjectReferenceInvalidName` | Return null |
| Reference key invalid characters | `ProjectReferenceInvalidName` | Return null |
| Reference key exceeds 128 chars | `ProjectReferenceInvalidName` | Return null |
| Missing `path` field or not a string | `ProjectReferenceMissingPath` | Return null |
| Empty/whitespace `path` value | `ProjectReferenceEmptyPath` | Return null |

All parser errors are fatal — exactly one diagnostic is added, then `null` is returned. Validation short-circuits on the first failing reference entry.

### Resolver Errors (ProjectReferenceResolver)

| Condition | Diagnostic ID | Behavior |
|-----------|--------------|----------|
| Reference key ≠ target project name | `ProjectReferenceNameMismatch` | Skip reference + transitive deps, continue siblings |
| Path not found | `ProjectReferenceNotFound` | Skip, continue (unchanged) |
| Circular reference | `ProjectCircularReference` | Skip, continue (unchanged) |

The resolver is non-fatal per-reference: it reports the error and continues resolving remaining siblings. This matches existing behavior for missing/circular references.

## Correctness Properties

### Property 1: Round-trip (updated)

**Validates: Requirements 1.1, 1.2, 1.3, 5.1, 5.2, 7.1, 7.2, 7.3, 7.4**

For any valid `ProjectConfiguration` with distinct reference names, serializing to TOML and parsing back produces a `ProjectConfiguration` where each reference has equal `Name` and `Path` fields.

### Property 2: Name-mismatch detection

**Validates: Requirements 4.1, 4.2, 4.3, 4.4**

For any reference where the key differs from the target project's `name` field, the resolver reports exactly one `ProjectReferenceNameMismatch` diagnostic and excludes the reference from the result.

### Property 3: Existing properties preserved

**Validates: Requirements 1.5, 6.1, 6.2, 6.3**

All existing properties (cycle detection, deduplication, topological ordering, transitive visibility) continue to hold with the new format. The DFS logic is unchanged — only the entry point and name validation are added.

## Testing Strategy

### Unit Tests (ProjectReferenceResolverTests)

- Add test: name-mismatch produces `ProjectReferenceNameMismatch` diagnostic
- Add test: name-mismatch skips transitive deps
- Update all existing tests to use new `RefTo(name, dir)` helper and `[references]` TOML format

### Property-Based Tests (ProjectReferencePropertyTests)

- Update TOML generation in P4, P5, P6, P7, P8, P9 to emit `[references]` format
- All existing properties must continue to pass

### Property-Based Tests (ProjectConfigurationRoundTripPropertyTests)

- Generator produces `ProjectReference(name, path)` with distinct names
- Serializer emits `[references]` table format
- Round-trip assertion compares both `Name` and `Path`

### Integration

- `samples/AllFeatures/mist.toml` migration is verified by the existing AllFeatures sample build (which runs as part of CI)

## Migration Impact

This is a **breaking change** to the `mist.toml` format. Only `samples/AllFeatures/mist.toml` uses references in this repository. No external consumers exist (pre-release language).

## Alternatives Considered

### Keep `[[references]]` and add optional `name` field

**Rejected:** Doesn't provide TOML-level duplicate detection. The name field would be optional, defeating the purpose.

### Use path basename as implicit name

**Rejected:** Fragile — renaming a directory silently changes the inferred name. Explicit is better for dependency declarations.
