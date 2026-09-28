# Requirements Document

## Introduction

A Mist program is compiled for a platform: the operating system it is meant to run on. The platform
is chosen when compiling, and defaults to the machine doing the compiling. The first thing that
depends on it is the culture a program starts under.

On Linux and macOS, .NET takes a program's starting culture from the POSIX locale variables, through
ICU. On Windows it takes it from the user's regional settings and ignores those variables. So
`LANG=tr_TR.UTF-8` runs a C# program in Turkish on Linux and in the user's own culture on Windows.
Mist is not C#: a Mist program honours the variables on every platform it is compiled for.

This was measured on 2026-09-27. `CultureInvariantEmissionTests` (purity, Requirement 13) failed on a
Windows machine: its program ran with `LANG` and `LC_ALL` set to `tr_TR.UTF-8` and reported `en-US`,
the Windows user's culture.

## Glossary

- **Target_Platform**: `windows`, `linux` or `macos`.
- **Host_Platform**: the platform of the machine running the compiler. A host that is neither Windows
  nor macOS is `linux`.
- **Locale_Variables**: `LC_ALL`, `LC_MESSAGES` and `LANG`, in that order of precedence.
- **Startup_Hook**: code the C# backend emits into a program that runs before any of the program's own
  code, static initializers included.

### Requirement 1: Choosing the Target Platform

**User Story:** As a Mist developer, I want to name the platform a program is compiled for, so that
platform-dependent behaviour follows the machine the program will run on, not the one that built it.

#### Acceptance Criteria

1. THE CLI SHALL accept `--platform <windows|linux|macos>`, case-insensitively.
2. IF `--platform` names another value, THEN THE Argument_Parser SHALL report
   `Unknown platform: '<value>'. Valid platforms: windows, linux, macos`. IF it has no value, THEN THE
   Argument_Parser SHALL report `Missing value for --platform flag.`
3. WHEN no platform is named, THE Compiler SHALL compile for the Host_Platform. `CompilerOptions`
   SHALL carry the named platform, or none, and SHALL expose the platform compiled for as
   `EffectivePlatform`, so every caller of the compiler, not only the CLI, gets the same default.
4. THE C# backend SHALL NOT write the Target_Platform into the emitted project as a runtime
   identifier. A compiled program stays framework-dependent: it is compiled for a platform, not
   restricted to one.
5. THE classic pipeline SHALL accept the option and ignore it.
6. THE CLI's usage and help text SHALL list the option.

### Requirement 2: The Starting Culture on Windows

**User Story:** As a Mist developer, I want a program to start under the culture the Locale_Variables
name on Windows as it does on Linux and macOS, so that a program behaves the same under the same
environment wherever it runs.

#### Acceptance Criteria

1. WHEN the experimental pipeline compiles an executable for `windows`, THE C# backend SHALL emit a
   Startup_Hook.
2. THE Startup_Hook SHALL read the first Locale_Variable that is set and not empty. The first such
   variable decides, even when its value names no culture: the next is not consulted.
3. THE Startup_Hook SHALL name a culture from that value as ICU does. It drops the codeset after `.`
   and the modifier after `@`. `C` and `POSIX` are the invariant culture. Otherwise `_` becomes `-`,
   and the name must be a culture .NET predefines.
4. WHEN a culture results, THE Startup_Hook SHALL make it the current culture, the current UI culture,
   and the default of both for every thread. WHEN none results, because no Locale_Variable is set or
   the one that decides names no predefined culture, the system culture SHALL stand.
5. THE C# backend SHALL NOT emit the Startup_Hook into a program compiled for another platform, where
   .NET reads the Locale_Variables itself, nor into a library, which must not change the culture of
   the process that loads it.
6. THE Compiler SHALL still read no culture itself (purity, Requirement 13.3). The Startup_Hook runs in
   the compiled program, not in the compiler.
