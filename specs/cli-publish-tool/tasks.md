# Implementation Plan: CLI Publish Tool

## Overview

Package the Mist compiler CLI as a self-contained, single-file executable named `mist` and provide a `publish.sh` script for one-command install. The implementation has three parts: configuring the csproj for publishing, creating the publish script, and adding unit tests for the csproj configuration. No changes to compiler logic or argument parsing are needed.

## Tasks

- [x] 1. Configure Mist.CLI.csproj for single-file self-contained publishing
  - [x] 1.1 Add publish-related MSBuild properties to the existing PropertyGroup
    - Add `<AssemblyName>mist</AssemblyName>` so the published executable is named `mist`
    - Add `<PublishSingleFile>true</PublishSingleFile>` to bundle all assemblies into one file
    - Add `<SelfContained>true</SelfContained>` to include the .NET runtime
    - Add `<PublishTrimmed>true</PublishTrimmed>` to remove unused framework code
    - These properties only affect `dotnet publish` output; `dotnet build` and `dotnet run` behavior is unchanged
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

  - [x] 1.2 Write unit tests for csproj configuration
    - Add a test class in `Test/Mist.CLI.Tests/` that loads `Source/Mist.CLI/Mist.CLI.csproj` as XML
    - Assert `AssemblyName` is `mist`
    - Assert `PublishSingleFile` is `true`
    - Assert `SelfContained` is `true`
    - Assert `PublishTrimmed` is `true`
    - Follow existing xUnit conventions in the project (namespace `Mist.CLI.Tests`, `[Fact]` attributes)
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Checkpoint - Verify csproj changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Create the publish.sh script
  - [x] 3.1 Create `publish.sh` at the repository root with shebang and strict mode
    - Start with `#!/usr/bin/env bash` and `set -euo pipefail`
    - Make the file executable (chmod +x)
    - _Requirements: 2.1, 2.2_

  - [x] 3.2 Implement prerequisite check for dotnet CLI
    - Use `command -v dotnet` to verify the .NET SDK is available
    - If not found, print `"Error: .NET SDK is required but 'dotnet' was not found on PATH."` to stderr and exit 1
    - _Requirements: 6.2_

  - [x] 3.3 Implement platform RID auto-detection
    - Use `uname -s` for OS detection: `Darwin` → `osx`, `Linux` → `linux`
    - Use `uname -m` for architecture: `arm64` → `arm64`, `x86_64` → `x64`, `aarch64` → `arm64`
    - Combine into RID format (e.g., `osx-arm64`, `linux-x64`)
    - Print status message showing detected platform
    - For unsupported OS/architecture combinations, print error to stderr and exit 1
    - _Requirements: 2.4, 6.1_

  - [x] 3.4 Implement dotnet publish invocation
    - Run `dotnet publish Source/Mist.CLI/Mist.CLI.csproj -c Release -r <RID>`
    - Print status message before publishing
    - If `dotnet publish` fails, print `"Error: dotnet publish failed."` to stderr and exit 1
    - _Requirements: 2.3, 2.7, 6.1_

  - [x] 3.5 Implement binary installation to ~/.mist/bin
    - Create `~/.mist/bin` directory if it doesn't exist using `mkdir -p`
    - Copy the published binary from `Source/Mist.CLI/bin/Release/net10.0/<RID>/publish/mist` to `~/.mist/bin/mist`
    - This overwrites any existing binary for idempotent installs
    - Print status message before copying
    - If copy fails, print error to stderr and exit 1
    - _Requirements: 2.5, 2.6, 5.1, 6.1, 6.3_

  - [x] 3.6 Implement PATH configuration with shell profile detection
    - Check if `$HOME/.mist/bin` is already in `$PATH`
    - If already present, print message that PATH is already configured and skip modification
    - If not present, detect shell from `$SHELL`: zsh → `~/.zshrc`, bash → `~/.bashrc` (fall back to `~/.bash_profile` on macOS if `~/.bashrc` doesn't exist), other → `~/.profile`
    - Append `export PATH="$HOME/.mist/bin:$PATH"` to the detected profile file
    - Ensure repeated runs do not add duplicate entries (check before appending)
    - Print instructions to restart terminal or source the profile
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 5.2_

  - [x] 3.7 Implement completion summary output
    - Print the install path (`~/.mist/bin/mist`)
    - Print confirmation message and usage instructions
    - _Requirements: 5.3, 6.4_

- [x] 4. Checkpoint - Verify publish script
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Final verification and wiring
  - [x] 5.1 Verify dotnet build and dotnet run still work after csproj changes
    - Run `dotnet build Source/Mist.CLI/Mist.CLI.csproj` to confirm the project still builds
    - Run `dotnet run --project Source/Mist.CLI/Mist.CLI.csproj` to confirm the CLI still works via dotnet run
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 6. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- No property-based tests are included — this feature is configuration and shell scripting with no pure functions to test
- The published binary runs the same `Program.Main` entry point, so behavioral equivalence (Requirement 4) is guaranteed by the .NET publish pipeline
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
