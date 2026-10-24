# Implementation Plan: Native Runtime Assets

## Overview

Pick a target runtime identifier, select each package's `runtimeTargets` along the SDK's fallback order,
copy the selected assets and a referenced C# project's matching `runtimes/` files flat into `bin/`. Every
task leaves the solution building with zero warnings and `dotnet format --verify-no-changes` clean. Tiers
follow the root `AGENTS.md` "Tests" table.

## Tasks

- [x] 1. Identifier and fallback order (driver)
  - [x] 1.1 `RuntimeAsset`, `RuntimeAssetKind` contracts; `PipelineInput.RuntimeIdentifiers` and `RuntimeAssets`
    - _Requirements: 1.7, 2.5_
  - [x] 1.2 `TargetRuntime.Select` and Tier 1 `TargetRuntimeTests`
    - _Requirements: 1.1, 1.2, 1.3, 6.1_
  - [x] 1.3 `RidGraph`: parse, `FallbackOrder`, built-in table; Tier 1 `RidGraphTests`
    - _Requirements: 1.5, 1.6, 6.2_
  - [x] 1.4 `--rid` in `CliArgumentParser`/`CliArguments`; `CompilerOptions.RuntimeIdentifier`; `MIST_RID_UNKNOWN`
    - _Requirements: 1.3, 1.4, 5.3_

- [x] 2. Package runtime assets (driver)
  - [x] 2.1 `PackageResolver`: read `runtimeTargets` and `runtimeIdentifierGraphPath`; `PackageResolutionResult.RuntimeAssets`
    - _Requirements: 2.1, 2.5_
  - [x] 2.2 `RuntimeAssetSelection.Select` and Tier 1 `RuntimeAssetSelectionTests`
    - _Requirements: 2.2, 2.3, 2.4, 6.3_
  - [x] 2.3 Tier 1 `PackageAssetsParsingTests` over a synthetic assets document
    - _Requirements: 6.8_
  - [x] 2.4 `CompilerDriver`: compute identifiers before resolution; fill the pipeline input; Stage 1 detail
    - _Requirements: 1.4, 1.7, 5.1_

- [x] 3. Deployment (backend)
  - [x] 3.1 `ClosureAssetSelection` and Tier 1 `ClosureAssetSelectionTests`
    - _Requirements: 4.1, 4.2, 4.3, 6.4_
  - [x] 3.2 Copy rules in `CSharpCompilationDriver`: runtime assets, replacement of `lib/` copies, first wins, refresh, closure assets, deployed count; Tier 1 `RuntimeAssetDeploymentTests` over a stub file source
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 5.2, 6.5_

- [x] 4. Samples, test and book
  - [x] 4.1 `samples/CSharpReferences/Bridge`: fake native file per common identifier as content under `runtimes/<rid>/native/`; Tier 5 test asserts the host's file is deployed and the others are not; README
    - _Requirements: 6.6_
  - [x] 4.2 `samples/EfCoreMigrations`: delete `run.sh`; README documents `mist run App`
    - _Requirements: 6.7_
  - [x] 4.3 Book: a paragraph on runtime assets and `--rid` in the Project Configuration chapter or the CLI chapter
