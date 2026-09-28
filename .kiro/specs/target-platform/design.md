# Design Document: Target Platform

## Overview

The platform is a compile-time choice, made once and carried in `CompilerOptions`. The only consumer
today is the C# backend's decision to emit the Startup_Hook.

Four decisions shape it, each stated with its alternative:

1. **The platform is chosen when compiling, not detected when the program runs.** The alternative was
   an `OperatingSystem.IsWindows()` check inside every emitted program. The language's owner chose a
   compile-time target: compiling for a platform is a statement about where the program will run,
   defaulting to the machine that builds it, as a cross-compiler does.
2. **No runtime identifier is emitted.** The target only steers emission. Writing
   `<RuntimeIdentifier>` would make every build platform-specific and need runtime packs, for no
   behaviour this spec needs.
3. **The locale logic is real C#, compiled into the compiler and embedded into programs.** Emitted
   runtime support is otherwise written as text through `CSharpWriter` (`MistInvariant`, the task
   combinators). Text can only be tested by compiling and running a program (Tier 4). This logic has
   a precedence order and several name shapes, so it is written once as
   `Backends/CSharpTranspiler/Runtime/PosixLocale.cs`. That file is compiled into `Mist.Core`, where
   Tier 1 tests call it. It is also an embedded resource (`Mist.Runtime.PosixLocale.cs`), which the
   backend emits verbatim. Mist.Core compiles with implicit usings and emitted programs do not, so
   the emitted copy is prefixed with `using System;`, and the file may name nothing outside `System`
   and `System.Globalization`.
4. **The hook is a module initializer.** `[ModuleInitializer]` runs before any other code in the
   module: before `Main`, top-level statements, and every static initializer. Composed programs emit
   one CLR assembly, so one hook covers every project in the program.

## Components

- `Driver/Contracts/TargetPlatform.cs`:
  - `TargetPlatform` is the enum.
  - `TargetPlatforms.Host` is the Host_Platform, from `Of(isWindows, isMacOS)`.
  - `CompilerOptions.Platform` is null when no platform was named.
  - `CompilerOptions.EffectivePlatform` resolves the default in one place.
- `Mist.CLI/Args`:
  - `CliArgumentParser` parses `--platform`, and `CliArguments.Platform` carries it.
  - `Program.CompileProject` passes it to `CompilerOptions`.
- `Backends/CSharpTranspiler/Runtime/PosixLocale.cs`:
  - `Culture(environment)` is the decision.
  - `Apply()` sets the four culture properties.
- `Backends/CSharpTranspiler/Lowering/LocaleStartup.cs`:
  - `Source(options)` returns the hook file, or null.
  - `EmitFile` writes `__Locale.g.cs`, and `CSharpEmitterV2.Emit` calls it beside the other runtime files.

## Behaviour Measured Against

- **ICU:** `uprv_getPOSIXIDForCategory(LC_MESSAGES)` in `icu4c/source/common/putil.cpp` reads `LC_ALL`, then `LC_MESSAGES`, then `LANG`. `uprv_getDefaultLocaleID` cuts the value at `.` and `@`, and maps `C` and `POSIX` to `en_US_POSIX`.
- **.NET:** `DetectDefaultLocaleName` in `System.Globalization.Native/pal_locale.c` maps `en_US_POSIX` to the invariant culture, the empty name.

There are two deliberate differences from Linux:
- An empty variable is skipped, where ICU takes an empty `LC_ALL` as set.
- A name .NET does not predefine keeps the system culture, rather than making a custom culture.

## Not Covered

- **`mist test`:** a test project is a library and the test host owns the process, so the Locale_Variables do not reach a test's culture on Windows.
- **A `mist.toml` field** for the platform.
- **Anything else that depends on the platform.**

## Testing Strategy

- **Tier 1:**
  - `PosixLocaleTests` covers precedence, empty variables, name shapes, `C`/`POSIX`, unusable names, and the first set variable deciding.
  - `LocaleStartupTests` covers which programs carry the hook, and its shape.
  - `TargetPlatformTests` covers the Host_Platform mapping.
  - `CliPlatformArgumentTests` covers the option.
- **Tier 4:** `CultureInvariantEmissionTests` is unchanged, and proves the seam. It compiles for the Host_Platform and runs the program under `tr_TR.UTF-8` and `de_DE.UTF-8`, so on Windows it runs the hook, and elsewhere .NET reads the variables itself.
- **Not tested:** `Apply()` is never called by a lower tier, since it sets the culture of the whole test process.
