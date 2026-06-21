# Mist Package Resolver Improvements

## Resolution (June 2025)

All four issues below were resolved by replacing the in-process NuGet client (`NuGet.Protocol` / `FindPackageByIdResource`) with a `dotnet restore` delegation approach. The new `PackageResolver`:

1. Generates a temporary `.csproj` with declared packages + framework references
2. Runs `dotnet restore` (inherits the SDK's certificate trust, TFM fallback, and runtime-package handling)
3. Parses `project.assets.json` to extract resolved assembly paths
4. Deduplicates against framework-provided assemblies before returning paths

This also removed the `NuGet.Protocol`, `NuGet.Packaging`, and `NuGet.Frameworks` package dependencies from `Mist.csproj` (only `NuGet.Versioning` remains for version string validation).

---

## Issue 1: NuGet Signature Verification Failures (MIST_PKG_003) — RESOLVED

**Symptom:** `mist restore` fails with `MIST_PKG_003: Connection or network failure while restoring package ... Unable to get repository signature information for source https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json` — even though the endpoint is reachable via curl and `dotnet restore` works fine on the same machine.

**Root cause:** Mist was using the NuGet client libraries directly with `NullLogger` and no certificate/trust configuration. The SDK's own NuGet client has preconfigured certificate bundles that the bare library usage didn't inherit.

**Fix:** Delegating to `dotnet restore` means the SDK handles all TLS/certificate/signature verification using its own trust store.

---

## Issue 2: Runtime-Provided Packages Reported as Errors (MIST_PKG_001) — RESOLVED

**Symptom:** Packages like `System.Diagnostics.DiagnosticSource 5.0.0` and `Microsoft.NETCore.Platforms 5.0.0` were reported as errors because they don't declare a `net10.0` target.

**Fix:** The SDK's restore writes `_._` placeholder entries in `project.assets.json` for runtime-provided packages. The new parser skips these placeholders and also identifies packages where all compile entries are `_._` as runtime-provided, adding them to the exclusion set.

---

## Issue 3: Duplicate Assembly Conflict with Shared Framework (MIST_SEMANTIC_FAILURE) — RESOLVED

**Symptom:** Adding `Microsoft.AspNetCore.Mvc.Testing 10.0.0` alongside `[framework_references] "Microsoft.AspNetCore.App" = true` caused duplicate assembly loading in MetadataLoadContext.

**Fix:** The new resolver passes framework references into the generated `.csproj` and builds a set of framework-provided assembly names from `project.assets.json`. Package assemblies whose names appear in this set are excluded from the returned paths, preventing duplicates from reaching MetadataLoadContext.

---

## Issue 4: No Fallback from Unavailable TFM to Compatible Lower Target — RESOLVED

**Symptom:** `xunit.v3 3.2.0` targets `net8.0` and `net472`. When resolving for `net10.0`, the old resolver could miss the `net8.0` fallback.

**Fix:** The SDK's restore correctly selects `net8.0` as the nearest compatible target for `net10.0` and records it in `project.assets.json`. The new parser reads whatever TFM the SDK selected, so fallback is handled automatically.

---

## Summary of Priority (historical)

| Issue | Severity | Status |
|-------|----------|--------|
| 1 - Signature verification | High | ✅ Resolved |
| 3 - Duplicate assembly | High | ✅ Resolved |
| 4 - TFM fallback | Medium | ✅ Resolved |
| 2 - Runtime-provided noise | Low | ✅ Resolved |
