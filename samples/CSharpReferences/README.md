# CSharpReferences

A Mist program that references a C# project, where the C# project's public method takes a type
declared in a Mist library. The call crosses the language boundary in both directions: Mist
constructs a `Widget`, C# reads it, and the result comes back as a string.

This sample is not in `AllFeaturesV2`, because building it runs MSBuild. One Tier 5 test,
`CSharpReferences_RunsFromACleanTree` in `Test/Mist.Tests/Integration/`, runs it from a clean tree.

## Projects and dependencies

| Project | Folder | Kind | Depends on |
| --- | --- | --- | --- |
| `Lib` | `Lib/` | Mist library: `Lib.Widget` with a `Name` property | nothing |
| `Bridge` | `Bridge/` | C# class library, no packages: `Bridge.Gateway.Describe(Widget)`, plus fake native files | `Lib`, through the bridge targets |
| `App` | `App/` | Mist executable | `Lib` and `Bridge`, both in its `mist.toml` |

`App/mist.toml` names both:

```toml
[references]
"Lib" = { path = "../Lib" }
"Bridge" = { path = "../Bridge" }
```

`Lib` is a directory holding a `mist.toml`, so it is a Mist reference and its source is composed
into `App.dll`. `Bridge` is a directory holding exactly one `.csproj`, so it is a C# project
reference: the compiler builds it with `dotnet build` before it compiles any Mist, references its
output and copies its output folder into `bin/`. Each key equals the assembly the target produces.

## Why there is a facade

`Bridge.dll` was compiled against `Lib.dll`, so its signature says "`Widget` in assembly `Lib`".
`App` does not reference `Lib.dll`; it compiles `Lib`'s source into `App.dll`, where `Widget`
lives in assembly `App`. Left alone, the runtime would treat the two as different types, or fail to
find `Lib` at all. When a referenced C# assembly was compiled against a Mist library that the program
composes, the compiler writes `bin/Lib.dll` as a facade that forwards every `Lib` type to `App.dll`,
so `Bridge` and `App` agree on what a `Widget` is. The facade holds only type forwards.

## The fake native files

`Bridge/native/<rid>/` holds one text file per common runtime identifier: `osx-arm64`, `osx-x64`,
`linux-x64`, `linux-arm64`, `win-x64` and `win-arm64`. Each is a single ASCII line naming its
identifier and is never loaded. `Bridge.csproj` lists them as `None` items with
`CopyToOutputDirectory="PreserveNewest"` and a `Link` of `runtimes/<rid>/native/<file>`, so
`Bridge`'s build output gets the same `runtimes/<rid>/native/` layout a NuGet package's native
assets have, without any package or network.

They exercise the compiler's deployment of native assets: it picks the target runtime identifier
(the host's, or `--rid`), selects the matching `runtimes/<rid>/native/` folder in a referenced C#
project's output along the SDK's fallback chain, and copies its files flat into `App/bin`. The file
name is shared by the two architectures of one operating system (`libbridge-native.dylib` for
both macOS identifiers), so the line inside is what tells them apart. The Tier 5 test asserts that
`App/bin` holds exactly one file named for the host's operating system, directly in `bin/` and
carrying the host's identifier, and that no other operating system's file and no second copy under
a `runtimes/` folder reached `App/bin`.

## How the bridge works

`Bridge/Bridge.csproj` sets `MistProject` to `../Lib` and `MistAssemblyName` to `Lib`, then imports
`Bridge/MistProject.targets`. Its one target runs before `ResolveAssemblyReferences`, executes
`mist build ../Lib --backend csharp --quiet` and adds `Lib/bin/Lib.dll` as a copy-local `Reference`,
so every build of `Bridge` rebuilds `Lib` first.

The command defaults to `mist` on `PATH`. To build `Lib` with another compiler, set the
`MistCommand` property or environment variable to the command to run in its place; the Tier 5 test
sets it to the compiler under test.

## Run it

From this directory, with `mist` on `PATH`:

```sh
mist run App
```

Expected output:

```
bridge:widget-1
```

Do not build `Bridge` by hand. The compiler builds it as part of compiling `App`, reads its output
path back from MSBuild, and passes `MIST_BUILD_CHAIN` to the build so that a bridge leading back to
a project already being compiled is refused instead of recursing. A hand build skips that, and its
nested `mist build` uses whichever `mist` is first on `PATH`.
