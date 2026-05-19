# Installation

This chapter walks you through installing the Mist compiler on your machine. By the end, you will have a working `mist` command that can compile and run Mist programs.

## Prerequisites

Mist is currently implemented in C#, targets .NET 10, and requires the .NET SDK to build from source. It also has a single backend, a C# transpiler which has the same requirements to use. Before you begin, ensure you have:

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

### Option A: Build and Run via `dotnet run`

The simplest way to use Mist during development. Build the CLI project and invoke it through `dotnet run`:

1. Clone the Mist repository:

   ```sh
   git clone https://github.com/mist-lang/mist.git
   cd mist
   ```

2. Build the CLI project:

   ```sh
   dotnet build Source/Mist.CLI/Mist.CLI.csproj -c Release
   ```

3. Run the compiler using `dotnet run`, passing arguments after `--`:

   ```sh
   dotnet run --project Source/Mist.CLI -- build ./my-project
   dotnet run --project Source/Mist.CLI -- run ./my-project
   ```

This approach requires the .NET SDK on every invocation but needs no installation or PATH changes.

### Option B: Install via Publish Script

For a permanent installation that puts `mist` on your PATH, use the publish script in the repository root. It auto-detects your platform, builds a self-contained binary, installs it to `~/.mist/bin/`, and configures your shell PATH.

**macOS / Linux:**

```sh
./publish.sh
```

**Windows (PowerShell):**

```powershell
./publish.ps1
```

After the script completes, restart your terminal (or source your shell profile as instructed) and the `mist` command is available globally:

```sh
mist build ./my-project
mist run ./my-project
```

## Verifying the Installation

Verify the compiler works by compiling a minimal project. Create a directory with a `mist.toml` and a source file:

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

Run the compiler (use whichever approach matches your installation):

```sh
# Option A (dotnet run):
dotnet run --project /path/to/mist/Source/Mist.CLI -- run ./hello

# Option B (installed binary):
mist run ./hello
```

Expected output:

```
Mist is installed!
```

If you see the output above, everything is working. The `build` command compiles without running, placing C# output in `out/` and the executable in `bin/`.

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
