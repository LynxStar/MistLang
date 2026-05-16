# Installation

This chapter walks you through installing the Mist compiler on your machine. By the end, you will have a working `mist` command that can compile and run Mist programs.

## Prerequisites

Mist targets .NET 10 and requires the .NET SDK to build from source. Before you begin, ensure you have:

- **.NET SDK 10.0 or later** — Download it from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download). The SDK includes the runtime, so no separate runtime install is needed.

You can verify your installed SDK version by running:

```sh
dotnet --version
```

The output should show `10.0.100` or higher. Any `10.x` version satisfies the requirement.

## Supported Platforms

Mist runs anywhere .NET 10 runs. The compiler is tested on:

| Operating System | Architecture |
|------------------|--------------|
| Windows 10/11    | x64, ARM64   |
| macOS 13+        | x64, ARM64   |
| Linux (Ubuntu, Fedora, Debian) | x64, ARM64 |

If your platform supports the .NET 10 SDK, it supports Mist.

## Installation Steps

### Option A: Build from Source

1. Clone the Mist repository:

   ```sh
   git clone https://github.com/mist-lang/mist.git
   cd mist
   ```

2. Build the CLI project:

   ```sh
   dotnet build Source/Mist.Cli/Mist.CLI.csproj -c Release
   ```

3. The compiled binary is located at:

   ```
   Source/Mist.Cli/bin/Release/net10.0/mist
   ```

4. Add the binary directory to your `PATH`, or copy the binary to a directory already on your `PATH`:

   ```sh
   export PATH="$PATH:$(pwd)/Source/Mist.Cli/bin/Release/net10.0"
   ```

### Option B: Publish a Self-Contained Binary

For a single-file deployment that does not require the .NET SDK on the target machine:

1. Publish the CLI as a self-contained, trimmed binary:

   ```sh
   dotnet publish Source/Mist.Cli/Mist.CLI.csproj -c Release
   ```

2. The published binary is located at:

   ```
   Source/Mist.Cli/bin/Release/net10.0/publish/mist
   ```

3. Copy the `mist` binary (and the `Standard/` directory next to it) to your preferred install location and add it to your `PATH`.

## Verifying the Installation

Once `mist` is on your `PATH`, verify it works by compiling a minimal project. Create a directory with a `mist.toml` and a source file:

```sh
mkdir hello && cd hello
```

Create `mist.toml`:

```toml
name = "Hello"
```

Create `Program.mist`:

```mist
{{#include ../../snippets/getting-started/installation-verify.mist}}
```

Run the compiler:

```sh
mist build
```

Expected output:

```
[Stage 0] Cleaning ... done (Xms)
[Stage 1] Parsing ... done (Xms)
[Stage 2] Semantic Model ... done (Xms)
[Stage 3] Analysis ... done (Xms)
[Stage 4] Code Generation ... done (Xms)

Build succeeded — 0 errors, 0 warnings (Xms)
```

If you see `Build succeeded`, the installation is working correctly. The compiled C# output is in the `out/` directory and the executable artifact is in `bin/`.

You can also compile and run in one step:

```sh
mist run
```

Expected output:

```
Mist is installed!
```

## Troubleshooting

### .NET SDK Not Installed

If the .NET SDK is not installed, running `dotnet build` or `dotnet publish` will fail with:

```
'dotnet' is not recognized as an internal or external command
```

or on Unix-like systems:

```
command not found: dotnet
```

**Fix:** Install the .NET 10 SDK from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) and ensure the `dotnet` command is on your `PATH`.

### .NET SDK Version Too Old

If you have an older .NET SDK installed (e.g., .NET 8 or .NET 9), building will fail with an error similar to:

```
error NETSDK1045: The current .NET SDK does not support targeting .NET 10.0.
```

**Fix:** Update to .NET SDK 10.0 or later. You can have multiple SDK versions installed side-by-side — the project will automatically use the correct one.

### Verifying Your SDK Version

To check which SDK versions are installed:

```sh
dotnet --list-sdks
```

Look for an entry starting with `10.0`. If none is present, install the .NET 10 SDK.
