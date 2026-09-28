# Implementation Plan: Target Platform

## Overview

The contract first, so every caller of the compiler shares one default; then the CLI option; then
the locale logic and the backend's decision to emit it; then the proof.

## Tasks

- [x] 1. Contract
  - [x] 1.1 Add `TargetPlatform` and `TargetPlatforms.Host` in `Driver/Contracts/TargetPlatform.cs`
    - _Requirements: 1.3_
  - [x] 1.2 Add `CompilerOptions.Platform` (null when none is named) and `EffectivePlatform`
    - _Requirements: 1.3_

- [x] 2. CLI
  - [x] 2.1 Parse `--platform` in `CliArgumentParser` into `CliArguments.Platform`, and refuse unknown or missing values
    - _Requirements: 1.1, 1.2_
  - [x] 2.2 Pass it to `CompilerOptions` in `Program.CompileProject`, and list it in the usage and help text
    - _Requirements: 1.5, 1.6_

- [x] 3. The starting culture
  - [x] 3.1 Write `Backends/CSharpTranspiler/Runtime/PosixLocale.cs`, and embed it as `Mist.Runtime.PosixLocale.cs`
    - _Requirements: 2.2, 2.3, 2.4_
  - [x] 3.2 Add `Lowering/LocaleStartup`, which emits `__Locale.g.cs` with a `[ModuleInitializer]` for Windows executables only, and call it from `CSharpEmitterV2.Emit`
    - _Requirements: 1.4, 2.1, 2.5_

- [x] 4. Proof
  - [x] 4.1 Tier 1:
    - `PosixLocaleTests`: 18 cases.
    - `LocaleStartupTests`: 5 cases.
    - `TargetPlatformTests`: 3 cases.
    - `CliPlatformArgumentTests`: 8 cases.
  - [x] 4.2 Tier 4: `CultureInvariantEmissionTests`, unchanged, passes on Windows.

## Verification (2026-09-28)

- **Before the change:** `CultureInvariantEmissionTests` failed on Windows. Its program printed `en-US` where `tr-TR` was expected, in ledgers `20260927-165112` and `20260927-165445`. Both ran on a branch of `666c596c` whose one change, SDK reference caching, the test does not depend on.
- **After it:** the test passes (`20260928-001919`).
- **The full ledger `20260928-002617` passed 10,786 of 10,786:**
  - `Mist.CLI.Tests` 97/97;
  - `Storm.SourceGenerator.Tests` 819/819;
  - `Mist.Tests` 9,870/9,870, including both Tier 6 orchestrators.
- **Build and format:** `dotnet build Mist.slnx --no-incremental` has 0 warnings and 0 errors. `dotnet format --verify-no-changes` reports only the IDE0047 already at `Test/Mist.Tests/Backend/ClrSpellingTests.cs:30` on `main`.
- **Ten one-line mutants**, each built and run through the ledger, from `20260928-002150` to `20260928-002346`, failed the cases below:

  | Mutant | Cases failed |
  |---|---|
  | `LANG` read first | 3 |
  | Empty variables counted | 1 |
  | Codeset not stripped | 9, including the Tier 4 test |
  | `C`/`POSIX` not invariant | 3 |
  | Custom cultures admitted | 1 |
  | An unusable first variable skipped | 1 |
  | The hook for every platform | 2 |
  | The hook for libraries | 1 |
  | The emitter's call removed | 1, the Tier 4 test alone |
  | The option read case-sensitively | 2 |

  Every file was restored byte-identical.

**Through the rebuilt CLI**, on a probe program printing `CultureInfo.CurrentCulture.Name` on a Windows machine whose culture is `en-US`:

| Run | Printed | Hook file |
|---|---|---|
| Default platform, `LANG=tr_TR.UTF-8` | `tr-TR` | written |
| `--platform linux`, same `LANG` | `en-US` | none |
| `--platform windows`, no variables | `en-US` | written |
| `LC_ALL=de_DE.UTF-8` over `LANG=sv_SE.UTF-8` | `de-DE` | written |
| `LANG=C.UTF-8` | the invariant culture's empty name | written |

- `--platform freebsd` was refused with the Requirement 1.2 message.
- The emitted project had no `RuntimeIdentifier`.
