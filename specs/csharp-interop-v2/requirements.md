# Requirements Document

## Introduction

This feature closes a set of CLR/C# interop gaps discovered while building a real application against .NET libraries on top of the existing `assembly-type-provider` (which surfaces CLR types) and `clr-ref-interop` (ref/byref interop) work. The gaps fall into six areas:

1. **Package resolution feedback and wiring** — Today `[packages]` entries in `mist.toml` are parsed but the resolved package assemblies are never passed to the type provider, and the build emits no package-resolution stage or diagnostics. Packages fail silently.
2. **Cross-assembly inheritance** — Base-type relationships that span CLR assemblies (e.g., `NetworkStream : Stream`, `IPEndPoint : EndPoint`) are not resolved, so derived types are not assignable to their base types and inherited members appear missing.
3. **Per-overload visibility filtering** — When any overload of a method group uses an unsupported type (e.g., `Span<T>`), the whole method group is hidden instead of just the offending overload.
4. **`mist inspect <TypeName>` diagnostic command** — There is no way to see which members the type provider surfaces, which are hidden, and why.
5. **Framework references** — Only the base SDK shared framework auto-loads; there is no way to opt into additional shared frameworks (e.g., `Microsoft.AspNetCore.App`).
6. **`Span<T>` / ref struct mapping** — `Span<T>` and other ref structs do not map into Mist; the behaviour and limitation around ref structs must be specified.

The scope is the C# transpiler backend's interop pipeline (`PackageResolver`, `AssemblyTypeProvider`, `VisibilityFilter`, `TypeProjector`, `ProjectFileParser`, `CompilerDriver`) plus a new CLI inspection command. No Mist source-language syntax is added, so no TextMate grammar changes are required.

## Glossary

- **Compiler_Driver**: The component in `CompilerDriver.cs` that orchestrates the compilation pipeline and reports build stages through the Progress_Reporter.
- **Progress_Reporter**: The `ICompilationProgressReporter` implementation that prints build stage headers and detail lines to the console. The CLI chooses it by verbosity, so it never carries a diagnostic.
- **Resolution_Listener**: The `IResolutionDiagnosticListener` a caller gives the Compiler_Driver. It receives each diagnostic of the Package_Resolution_Stage during that stage. The CLI's listener writes them to stderr whatever the verbosity, and the build result keeps them.
- **Project_File_Parser**: The component in `ProjectFileParser.cs` that reads and validates `mist.toml` into a `ProjectConfiguration`.
- **Package_Resolver**: The component in `PackageResolver.cs` that resolves NuGet package references to assembly file paths, downloading and resolving transitive dependencies.
- **Framework_Reference_Resolver**: The component that resolves a shared-framework name (e.g., `Microsoft.AspNetCore.App`) to its reference assembly directory.
- **Assembly_Type_Provider**: The `IAmbientTypeResolver` implementation in `AssemblyTypeProvider.cs` that reads CLR assembly metadata and projects types into the Mist semantic model.
- **Visibility_Filter**: The component in `VisibilityFilter.cs` that decides which CLR types and members are hidden from Mist.
- **Type_Projector**: The component in `TypeProjector.cs` that converts CLR metadata into Mist `IType` and `ISymbol` instances.
- **Semantic_Model**: The Mist semantic model that performs name resolution, assignability checks, and overload resolution.
- **Inspect_Command**: A new `mist inspect <TypeName>` CLI command that reports the projected and hidden members of a CLR type.
- **Restore_Command**: A new `mist restore` CLI command that resolves the project's declared packages and framework references and prints the full resolver output (per-package and per-framework detail) on demand, without performing lexing, type checking, or emit.
- **Verbose_Flag**: A command-line option (`--verbose`) on the build and run commands that opts into detailed package and framework resolution output during a build.
- **Package_Resolution_Stage**: The build pipeline stage, ordered after Loading and before Lexing, during which the Package_Resolver and Framework_Reference_Resolver run. The stage always runs when packages or framework references are declared; whether its detail output is printed is governed separately by the Verbose_Flag.
- **Target_Framework**: The .NET target framework moniker the project compiles against, defaulting to `net10.0`.
- **Framework_Reference**: A shared .NET framework (e.g., `Microsoft.AspNetCore.App`, `Microsoft.WindowsDesktop.App`) whose reference assemblies are made available to the Assembly_Type_Provider, declared in a `[framework_references]` table in `mist.toml`.
- **Ref_Struct_Type**: A CLR type declared as `ref struct` (carries `System.Runtime.CompilerServices.IsByRefLikeAttribute`), including `System.Span<T>` and `System.ReadOnlySpan<T>`.
- **Overload_Group**: The set of CLR methods on a type that share the same name.

## Requirements

### Requirement 1: Package Resolution Stage and Verbose Output

**User Story:** As a Mist developer, I want builds to stay quiet by default and only show detailed package and framework resolution output when I ask for it, so that ordinary builds aren't cluttered while I can still opt into detail when diagnosing dependency problems.

#### Acceptance Criteria

1. WHEN `mist.toml` declares at least one `[packages]` entry or at least one `[framework_references]` entry, THE Compiler_Driver SHALL run the Package_Resolution_Stage after the Loading stage and before the Lexing stage, regardless of whether the Verbose_Flag is supplied.
2. WHERE no `[packages]` entries and no `[framework_references]` entries are declared in `mist.toml`, THE Compiler_Driver SHALL skip the Package_Resolution_Stage.
3. WHEN a build or run completes the Package_Resolution_Stage with no resolution failure AND the Verbose_Flag is not supplied, THE Progress_Reporter SHALL NOT emit the stage header, any per-package detail line, any per-framework detail line, any zero-assemblies note, the elapsed-time line, or the stage summary line.
4. WHEN the Verbose_Flag is supplied to a build or run, THE Progress_Reporter SHALL emit a stage header labeled `Stage 1.5 - Package Resolution`.
5. WHEN the Verbose_Flag is supplied and a package is restored successfully, THE Progress_Reporter SHALL emit exactly one detail line identifying the package name, resolved version, and the selected target framework moniker (e.g., `Restored EmbedIO 3.5.2 (net10.0)`).
6. WHEN the Verbose_Flag is supplied and a Framework_Reference is resolved successfully, THE Progress_Reporter SHALL emit exactly one detail line identifying the framework name and the resolved framework version (e.g., `Resolved Microsoft.AspNetCore.App 10.0.0`).
7. WHEN the Verbose_Flag is supplied and a `[packages]` entry resolves to zero compatible assemblies and produces no error diagnostic, THE Progress_Reporter SHALL emit a detail line stating that the package contributed no assemblies for the Target_Framework.
8. WHEN the Verbose_Flag is supplied and the Package_Resolution_Stage completes, THE Progress_Reporter SHALL emit a non-negative integer count of whole milliseconds elapsed for the stage and a stage summary line reporting the count of successful resolutions and the count of failed resolutions.
9. WHEN the Package_Resolution_Stage produces any error diagnostic (the `MIST_PKG_*` and `MIST_FWREF_*` diagnostics owned by Requirements 3 and 5), THE Compiler_Driver SHALL deliver that diagnostic to the Resolution_Listener, and the CLI SHALL surface it, regardless of whether the Verbose_Flag or `--quiet` is supplied.

### Requirement 2: Wiring Resolved Package Assemblies into the Type Provider

**User Story:** As a Mist developer, I want types from my declared NuGet packages to be usable in Mist source code, so that referencing a package in `mist.toml` actually makes its types available.

#### Acceptance Criteria

1. WHEN `mist.toml` declares one or more `[packages]` entries, THE Compiler_Driver SHALL pass the assembly paths produced by the Package_Resolver to the Assembly_Type_Provider in addition to the SDK reference assemblies, and SHALL include each distinct assembly path at most once when a package assembly and an SDK reference assembly denote the same assembly.
2. WHEN a Mist source file references a public type from a successfully resolved package by its CLR namespace and name, THE Assembly_Type_Provider SHALL make that type and its public members available for type checking without requiring any configuration beyond the `[packages]` entry, where a "successfully resolved package" is a `[packages]` entry for which the Package_Resolver contributed at least one assembly path and produced no error-severity diagnostic.
3. IF a Mist source file references a type by CLR namespace and name that is not present as a public type in any resolved package assembly or SDK reference assembly, THEN THE Assembly_Type_Provider SHALL produce an error-severity diagnostic identifying the unresolved namespace and type name and SHALL continue analyzing the remaining source rather than aborting.
4. WHEN a resolved package contributes transitive dependency assemblies, THE Compiler_Driver SHALL make those transitive assemblies available to the Assembly_Type_Provider so that public types referenced from the direct package's public API resolve.
5. THE Compiler_Driver SHALL use the Target_Framework from `mist.toml`, defaulting to `net10.0` when no Target_Framework is specified, when invoking the Package_Resolver, so that the same Target_Framework drives both SDK assembly discovery and package framework selection.
6. IF package resolution produces an error-severity diagnostic for a given package, THEN THE Compiler_Driver SHALL preserve that diagnostic in the build output and SHALL continue the build with the remaining successfully resolved assemblies rather than aborting before Lexing.

### Requirement 3: Package Resolution Error Diagnostics

**User Story:** As a Mist developer, I want explicit errors when a package cannot be resolved, so that I can distinguish a missing package, an unsupported framework, and a network failure.

#### Acceptance Criteria

1. IF a declared package version provides no assemblies compatible with the Target_Framework, THEN THE Package_Resolver SHALL emit exactly one diagnostic with severity Error, stable identifier `MIST_PKG_001`, identifying the package name, requested version, the Target_Framework, and the list of framework targets the package provides (e.g., `Package 'EmbedIO' version '3.5.2' does not support net10.0`).
2. IF a declared package name or version cannot be found on any configured NuGet feed, THEN THE Package_Resolver SHALL emit exactly one diagnostic with severity Error, stable identifier `MIST_PKG_002`, identifying the package name, requested version, and every configured NuGet feed that was searched.
3. IF a connection or network failure occurs while restoring a package, THEN THE Package_Resolver SHALL emit exactly one diagnostic with severity Error, stable identifier `MIST_PKG_003`, identifying the package name, requested version, and indicating that the failure category is a connection or network failure.
4. IF restoring a package does not complete within 100 seconds, THEN THE Package_Resolver SHALL emit exactly one diagnostic with severity Error, stable identifier `MIST_PKG_003`, identifying the package name, requested version, and indicating that the failure category is a restore timeout.
5. WHEN the Package_Resolver emits any error diagnostic, THE Compiler_Driver SHALL deliver that error to the Resolution_Listener during the Package_Resolution_Stage before any subsequent stage begins, and the CLI SHALL surface it then and not again when it prints the build's remaining diagnostics.
6. IF more than one declared package fails to resolve, THEN THE Package_Resolver SHALL continue attempting to resolve the remaining declared packages and SHALL emit one independent diagnostic per failing package.

### Requirement 4: Framework References in mist.toml

**User Story:** As a Mist developer, I want to declare shared-framework dependencies in `mist.toml`, so that I can unlock additional .NET shared frameworks such as ASP.NET Core.

#### Acceptance Criteria

1. WHEN a `[framework_references]` table is present in `mist.toml`, THE Project_File_Parser SHALL parse each entry as a framework name taken from the TOML key (trimmed of leading and trailing whitespace, 1 to 256 characters after trimming) and an enabled flag taken from the TOML boolean value.
2. WHEN a `[framework_references]` entry has the boolean value `true`, THE Project_File_Parser SHALL include that framework name exactly once in the parsed `ProjectConfiguration` framework reference list.
3. WHEN a `[framework_references]` entry has the boolean value `false`, THE Project_File_Parser SHALL exclude that framework name from the parsed framework reference list.
4. WHERE the `[framework_references]` table is absent from `mist.toml`, THE Project_File_Parser SHALL produce an empty framework reference list and SHALL NOT emit any diagnostic.
5. IF the `framework_references` field is present but is not a TOML table, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error indicating that `[framework_references]` must be a table, and SHALL produce an empty framework reference list.
6. IF a `[framework_references]` entry has a value that is not a TOML boolean, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error identifying the framework name and indicating that a boolean value is expected, and SHALL exclude that entry from the framework reference list.
7. IF a `[framework_references]` entry has a framework name that is empty or contains only whitespace after trimming, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error and SHALL exclude that entry from the framework reference list.
8. IF a `[framework_references]` entry has a framework name that exceeds 256 characters after trimming, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error identifying the offending entry and SHALL exclude that entry from the framework reference list.

### Requirement 5: Framework Reference Resolution and Type Availability

**User Story:** As a Mist developer, I want types from an enabled framework reference to be usable in Mist source code, so that I can use ASP.NET Core and other shared-framework types.

#### Acceptance Criteria

1. WHEN `mist.toml` enables a Framework_Reference, THE Framework_Reference_Resolver SHALL, during the Package_Resolution_Stage, locate the reference assembly directory for that framework at the installed shared-framework pack path for the Target_Framework, treating resolution as successful only when the pack directory exists and contains at least one reference assembly.
2. WHEN a Framework_Reference is resolved, THE Compiler_Driver SHALL make all reference assemblies from that framework available to the Assembly_Type_Provider alongside the SDK reference assemblies and the `[packages]` assemblies.
3. WHEN a Mist source file references a public type from an enabled Framework_Reference by its CLR namespace and name, THE Assembly_Type_Provider SHALL resolve that type without requiring a `[packages]` entry for it.
4. IF an enabled Framework_Reference is not installed on the host machine for the Target_Framework, THEN THE Framework_Reference_Resolver SHALL emit a diagnostic with severity Error, stable identifier `MIST_FWREF_001`, identifying the framework name, the Target_Framework, and the pack path that was searched.
5. IF an enabled framework name does not correspond to a known shared framework, THEN THE Framework_Reference_Resolver SHALL emit a diagnostic with severity Error, stable identifier `MIST_FWREF_002`, identifying the framework name.
6. WHEN multiple versions of a Framework_Reference pack are installed, THE Framework_Reference_Resolver SHALL select, among the installed packs whose major version matches the Target_Framework's major version, the pack with the greatest version determined by comparing major, then minor, then patch as integers, where "supports the Target_Framework" means the pack's major version matches the Target_Framework's major version.
7. WHEN more than one Framework_Reference is enabled, THE Framework_Reference_Resolver SHALL resolve each enabled framework independently and SHALL emit a separate diagnostic per failing framework.
8. IF a Framework_Reference fails to resolve, THEN THE Compiler_Driver SHALL continue the build with the remaining resolved assemblies rather than aborting before Lexing.

### Requirement 6: Cross-Assembly Base Type and Inheritance Resolution

**User Story:** As a Mist developer, I want CLR inheritance relationships that span assemblies to be resolved, so that I can pass a derived type (like `NetworkStream`) where a base type (like `Stream`) is expected.

#### Acceptance Criteria

1. WHEN the Type_Projector projects a CLR type whose base type is defined in a different assembly than the derived type, THE Type_Projector SHALL resolve the base type through the Assembly_Type_Provider and set it as the projected Mist type's base type.
2. WHEN the Type_Projector projects a CLR type whose implemented interfaces are defined in a different assembly than the type, THE Type_Projector SHALL resolve each interface through the Assembly_Type_Provider and include each resolved interface exactly once in the projected type's interface list.
3. WHEN a CLR type has a multi-level inheritance chain that spans two or more assemblies across two or more levels, up to and including the root base type (e.g., `NetworkStream : Stream`, `Stream : MarshalByRefObject`), THE Assembly_Type_Provider SHALL project the full chain so that the derived Mist type is assignable to each ancestor type.
4. WHEN Mist code assigns a value of a projected derived type to a variable, parameter, or return position typed as a projected base type or implemented interface in that derived type's inheritance chain, THE Semantic_Model SHALL accept the assignment without producing a type error.
5. WHEN Mist code casts a value between a projected derived type and a projected base type in the same inheritance chain in either direction (derived-to-ancestor or ancestor-to-derived), THE Semantic_Model SHALL accept the cast.
6. IF a CLR base type or implemented interface cannot be resolved from any assembly available to the Assembly_Type_Provider during projection, THEN THE Type_Projector SHALL omit only that unresolved relationship, SHALL record a diagnostic indication identifying which relationship was omitted and the unresolved type name, and SHALL continue projecting the derived type and its directly declared members with its remaining resolved relationships preserved.

### Requirement 7: Inherited Member Visibility

**User Story:** As a Mist developer, I want members inherited from a CLR base class to be callable on a derived type, so that I can use methods like `Read` and `Write` that `NetworkStream` inherits from `Stream`.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider projects a CLR type that inherits from a base class, THE Assembly_Type_Provider SHALL make the base class's visible public members (methods, properties, fields, and events, both instance and static) accessible on the derived Mist type as if declared directly on it.
2. WHEN the Visibility_Filter hides a base class member, THE Assembly_Type_Provider SHALL hide only that member on the derived type and SHALL keep the remaining inherited members accessible.
3. WHEN a derived CLR type overrides or shadows a base class member, THE Assembly_Type_Provider SHALL project only the most-derived visible signature for that member and SHALL NOT duplicate shadowed base definitions.
4. IF the most-derived definition of an inherited member is hidden by the Visibility_Filter but a definition on an ancestor type is visible, THEN THE Assembly_Type_Provider SHALL project the nearest visible ancestor signature for that member.
5. WHEN Mist code invokes a member inherited from a base class on an instance of a projected derived type, THE Semantic_Model SHALL resolve the member.
6. WHEN a projected inherited member is resolved, THE Type_Projector SHALL provide its parameter and return types with intrinsic type substitution applied.
7. IF Mist code invokes an inherited member that is hidden by the Visibility_Filter on a projected derived type, THEN THE Semantic_Model SHALL produce a member-not-found error.
8. WHEN a member is inherited through two or more levels of the inheritance chain, THE Assembly_Type_Provider SHALL make the most-derived visible definition of that member accessible on the most-derived projected type.

### Requirement 8: Per-Overload Visibility Filtering

**User Story:** As a Mist developer, I want overloads that use unsupported types to be hidden individually, so that supported overloads in the same method group remain callable.

#### Acceptance Criteria

1. WHEN an Overload_Group contains an overload whose signature uses a type excluded by the Visibility_Filter (for example a pointer type) in any parameter position or the return position, THE Visibility_Filter SHALL exclude that overload.
2. WHEN an Overload_Group contains overloads whose signatures use only supported types, THE Visibility_Filter SHALL project those overloads.
3. WHEN `System.Text.Encoding` is projected, THE Assembly_Type_Provider SHALL project the `GetBytes(string)` overload returning a byte array; its `Span<T>`/`ReadOnlySpan<T>` overloads are also projected (ref structs are projected, not hidden — Requirement 9).
4. WHEN `System.Net.Sockets.Socket` is projected, THE Assembly_Type_Provider SHALL project the `Send(byte[])` overload; its `Span<T>`/`ReadOnlySpan<T>` overloads are also projected.
5. WHEN `System.Console` is projected, THE Assembly_Type_Provider SHALL project the parameterless `ReadLine()` overload returning a nullable string, even though some other members of `Console` use unsupported types (for example pointer-based members).
6. WHEN every overload in an Overload_Group is excluded by the Visibility_Filter (for example a method group in which every overload has a pointer parameter), THE Assembly_Type_Provider SHALL project no symbol for that method name.
7. WHEN Mist code calls an Overload_Group that has both projected and excluded overloads, THE Semantic_Model SHALL perform overload resolution only over the projected overloads, and the excluded overloads SHALL NOT be resolution candidates.
8. IF Mist code calls an Overload_Group whose argument list matches only an excluded overload (for example an overload with a pointer parameter), THEN THE Semantic_Model SHALL produce an error-severity diagnostic indicating no matching overload, SHALL NOT resolve to the excluded overload, and sibling projected overloads SHALL remain callable.

### Requirement 9: Ref Struct and Span Type Handling

**User Story:** As a Mist developer, I want `Span<T>`, `ReadOnlySpan<T>`, and other ref structs to be usable in Mist source code, so that I can call the many CLR APIs that expose them and write span-based code.

#### Acceptance Criteria

1. WHEN the Type_Projector encounters a Ref_Struct_Type (any type whose CLR metadata carries `IsByRefLikeAttribute`, including `System.Span<T>` and `System.ReadOnlySpan<T>`), THE Type_Projector SHALL project it as a Mist stack-only struct with `StructType.IsRefStruct` set to `true`.
2. THE Visibility_Filter SHALL NOT exclude a Ref_Struct_Type on the basis that it is a ref struct; a Ref_Struct_Type is excluded only if it is non-public.
3. WHEN a CLR member's signature references a Ref_Struct_Type in any signature position (parameter type, return type, generic type argument, or field type, including positions nested within generic type arguments), THE Visibility_Filter SHALL NOT hide that member on the basis of the Ref_Struct_Type reference.
4. WHEN a Mist source file uses a projected Ref_Struct_Type, THE Semantic_Model SHALL support declaring it as a local variable, constructing it, indexing it, calling its members (e.g., `Slice`, `Length`), and iterating it with `foreach`, the same as it did before this feature.
5. WHEN the Assembly_Type_Provider resolves a Ref_Struct_Type by its CLR namespace and name (e.g., `ResolveType("System", "Span", 1)`), THE Assembly_Type_Provider SHALL return the projected stack-only struct rather than `null`.
6. WHERE a Mist program uses a projected Ref_Struct_Type in a way that violates CLR ref-struct lifetime rules (for example storing it in a heap location, boxing it, capturing it in a closure, or using it as a generic type argument), THE Mist compiler does NOT itself perform ref-struct escape or lifetime analysis for projected CLR ref structs; such misuse is reported by the downstream C# compiler when the transpiled C# is compiled, not by Mist's own checks. Full first-party ref-struct safety (stackalloc plus escape analysis) is out of scope for this feature and is tracked as separate future work.


### Requirement 10: Type Inspection Command

**User Story:** As a Mist developer, I want a `mist inspect <TypeName>` command, so that I can quickly see which members of a CLR type are usable, which are hidden, and why.

#### Acceptance Criteria

1. WHEN a developer runs `mist inspect <TypeName>` from a directory containing a Mist project file, THE Inspect_Command SHALL resolve `<TypeName>` through the Assembly_Type_Provider using the same SDK, package, and framework reference assemblies the build would use.
2. WHEN the type is resolved, THE Inspect_Command SHALL print the type's fully qualified name and a type kind that is exactly one from the set {class, struct, interface, enum}.
3. WHEN the type is resolved, THE Inspect_Command SHALL list each visible property in ascending ordinal order by name with its name and projected Mist type, marked as visible.
4. WHEN the type is resolved, THE Inspect_Command SHALL list each visible method in ascending ordinal order by name with its name, projected Mist parameter types in declaration order, and projected Mist return type, marked as visible.
5. WHEN the type has hidden members, THE Inspect_Command SHALL list each hidden member in ascending ordinal order by name with its name and a reason describing why it is hidden.
6. WHEN the resolved type has no visible members and no hidden members, THE Inspect_Command SHALL print the type header and indicate that no members are listed.
7. WHEN a member is inherited from a base type, THE Inspect_Command SHALL include it in the listing and indicate the declaring base type by its fully qualified name.
8. IF `<TypeName>` cannot be resolved from any loaded assembly, THEN THE Inspect_Command SHALL print a message identifying the unresolved type name and SHALL exit with a non-zero exit code.
9. IF `<TypeName>` is omitted, THEN THE Inspect_Command SHALL print usage guidance and SHALL exit with a non-zero exit code.
10. IF the command is run outside a directory containing a Mist project file, THEN THE Inspect_Command SHALL print guidance and SHALL exit with a non-zero exit code.

### Requirement 11: Book Documentation

**User Story:** As a Mist developer, I want the language book to document the v2 interop capabilities, so that I can learn how package resolution, framework references, cross-assembly inheritance, overload filtering, and `mist inspect` work.

#### Acceptance Criteria

1. THE book SHALL update the .NET Interop chapter at `book/src/advanced/dotnet-interop.md` to document declaring NuGet packages, that builds are quiet by default, that the `--verbose` flag shows detailed `Stage 1.5 - Package Resolution` output during a build, and that the `mist restore` command dumps the full resolver output on demand, and SHALL document each of the `MIST_PKG_001`, `MIST_PKG_002`, and `MIST_PKG_003` diagnostics with its triggering condition and meaning, with each diagnostic identifier appearing at least once in the chapter.
2. THE book chapter SHALL document the `[framework_references]` section of `mist.toml`, including a `Microsoft.AspNetCore.App` example, and SHALL document each of the `MIST_FWREF_001` and `MIST_FWREF_002` diagnostics with its triggering condition and meaning, with each diagnostic identifier appearing at least once in the chapter.
3. THE book chapter SHALL document that a projected derived type is assignable to its projected base type across assemblies.
4. THE book chapter SHALL document that members inherited across assemblies are callable on the projected derived type.
5. THE book chapter SHALL update the "What Gets Hidden" material to explain that when an Overload_Group contains overloads using unsupported types, only those offending overloads are hidden while the remaining overloads stay callable, and that `Span<T>` and other ref structs remain unmapped.
6. THE book chapter SHALL document the `mist inspect <TypeName>` command with an example showing both the command invocation and a representative sample of its output.
7. Every code example in the chapter SHALL be a referenced compiled snippet following the Book Snippet Policy.
8. THE book's `SUMMARY.md` SHALL reference the .NET Interop chapter.

### Requirement 12: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating v2 interop, so that I can see every supported pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist with its entry point located at `samples/CSharpInteropV2/Program.mist`.
2. The sample project's `mist.toml` SHALL declare a `[packages]` entry for `Newtonsoft.Json` pinned to exactly version `13.0.3` and a `[framework_references]` entry whose key is `Microsoft.AspNetCore.App` with the boolean value `true`.
3. The sample SHALL include at least one statement demonstrating each of the following patterns:
   a. using a public type from the resolved `Newtonsoft.Json` package;
   b. using a public type from the enabled `Microsoft.AspNetCore.App` Framework_Reference;
   c. assigning a projected derived type to a projected base type across assemblies;
   d. calling a member inherited from a base type across assemblies;
   e. calling a supported overload of a method group (e.g. `Encoding.GetBytes(string)`) where the same method group also exposes `Span<T>`/`ReadOnlySpan<T>` overloads that are now also projected (ref structs are projected, not hidden — Requirement 9).
4. WHEN a demonstrated pattern executes, THE sample SHALL print one line to stdout in `Key=Value` form, where `Key` is a non-empty label and `Value` is the observed runtime result of that pattern.
5. The `CSharpInteropV2` sample SHALL NOT be wired into `samples/AllFeatures` (it is excluded from the full-suite baseline run because it requires a network NuGet restore and an installed shared framework).
6. A dedicated integration test SHALL compile and run the `samples/CSharpInteropV2` project end-to-end — performing a real NuGet restore of `Newtonsoft.Json` and resolving the `Microsoft.AspNetCore.App` framework reference — and SHALL assert the process exits with code 0 and that stdout matches the Proof Code expected output byte-for-byte. This test MAY spawn a process to run the compiled sample (an approved exception to the Test Process Spawning Policy for this single sample) and SHALL configure UTF-8 stdout/stderr encoding per the Test UTF-8 Encoding Policy.
7. The sample SHALL use Allman braces, placing every opening brace on its own line, and SHALL prefer `var` for local declarations except where an explicit base type is required to demonstrate upcast assignability.
8. WHEN the sample is compiled and run using the Mist compiler, THE compilation SHALL produce zero error diagnostics, the process SHALL exit with code 0, and the stdout output SHALL match the expected output in the Proof Code section byte-for-byte (same lines, same order, same values).

#### Proof Code

```mist
using System.IO;
using System.Text;

namespace CSharpInteropV2;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("--- C# Interop V2 ---");

        // PROOF: NuGet package resolution is wired into the type provider.
        // PROOF: JsonConvert.SerializeObject(int[]) resolves from Newtonsoft.Json,
        //        which is only available if the [packages] entry was restored AND
        //        its assemblies were passed to the AssemblyTypeProvider.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(new int[] { 1, 2, 3 });
        Console.WriteLine($"json={json}");

        // PROOF: Framework reference resolution makes Microsoft.AspNetCore.App types
        //        available. QueryString lives in Microsoft.AspNetCore.Http.Abstractions,
        //        which only loads when [framework_references] enables the shared framework.
        var qs = new Microsoft.AspNetCore.Http.QueryString("?id=42");
        Console.WriteLine($"query={qs.Value}");
        Console.WriteLine($"hasValue={qs.HasValue}");

        // PROOF: Cross-assembly inheritance — MemoryStream is assignable to Stream.
        // PROOF: Inherited members (Length, CanRead) declared on Stream are callable
        //        on the value through the base-typed variable.
        var memory = new MemoryStream();
        memory.WriteByte(72);
        memory.WriteByte(105);
        Stream stream = memory;
        Console.WriteLine($"streamLength={stream.Length}");
        Console.WriteLine($"canRead={stream.CanRead}");

        // PROOF: Method-group overload resolution — Encoding.GetBytes(string) is callable.
        //        GetBytes also exposes Span<T>/ReadOnlySpan<T> overloads, which under
        //        Option A are also projected (ref structs are projected, not hidden); the
        //        string overload still resolves correctly alongside them.
        var encoded = Encoding.UTF8.GetBytes("Hi");
        Console.WriteLine($"encodedLength={encoded.Length}");

        // PROOF: The supported decode overload GetString(byte[]) is also preserved.
        var decoded = Encoding.UTF8.GetString(encoded);
        Console.WriteLine($"decoded={decoded}");

        Console.WriteLine("--- C# Interop V2 Done ---");
    }

    public static void Main()
    {
        RunDemo();
    }
}
```

**Expected output:**
```
--- C# Interop V2 ---
json=[1,2,3]
query=?id=42
hasValue=True
streamLength=2
canRead=True
encodedLength=2
decoded=Hi
--- C# Interop V2 Done ---
```

### Requirement 13: Dependency Restore Command

**User Story:** As a Mist developer, I want a dedicated `mist restore` command, so that I can dump the dependency resolver's full output on demand without running a complete build.

#### Acceptance Criteria

1. WHEN a developer runs the Restore_Command from a directory containing a Mist project file, THE Restore_Command SHALL resolve all declared `[packages]` and `[framework_references]` using the same Target_Framework the build would use (defaulting to `net10.0` when none is specified).
2. WHEN the Restore_Command resolves a package successfully, THE Restore_Command SHALL print exactly one detail line identifying the package name, resolved version, and the selected target framework moniker, unconditionally and without requiring the Verbose_Flag.
3. WHEN the Restore_Command resolves a Framework_Reference successfully, THE Restore_Command SHALL print exactly one detail line identifying the framework name and the resolved framework version, unconditionally and without requiring the Verbose_Flag.
4. WHEN a `[packages]` entry resolves to zero compatible assemblies and produces no error diagnostic, THE Restore_Command SHALL print a detail line stating that the package contributed no assemblies for the Target_Framework.
5. WHEN the Restore_Command completes, THE Restore_Command SHALL print a summary line reporting the count of successful resolutions and the count of failed resolutions.
6. THE Restore_Command SHALL NOT perform lexing, type checking, or emit.
7. WHEN every declared package and Framework_Reference resolves successfully, THE Restore_Command SHALL exit with exit code 0.
8. IF any declared package or Framework_Reference fails to resolve, THEN THE Restore_Command SHALL emit the same `MIST_PKG_*` and `MIST_FWREF_*` error diagnostics owned by Requirements 3 and 5, one independent diagnostic per failing dependency, and SHALL exit with a non-zero exit code.
9. IF the Restore_Command is run outside a directory containing a Mist project file, THEN THE Restore_Command SHALL print usage guidance and SHALL exit with a non-zero exit code.

### Requirement 14: Distributed Steering File Updates

**User Story:** As a Mist developer relying on the bundled steering files for language-aware assistance, I want the distributed `.NET Interop` steering to reflect the v2 interop behavior, so that Kiro does not give me guidance that contradicts how the compiler actually works.

#### Acceptance Criteria

1. THE distributed steering file at `distribute/steering/mist-interop.md` SHALL be updated to document the `[framework_references]` table in `mist.toml`, including a `Microsoft.AspNetCore.App` example.
2. THE distributed steering file SHALL be updated to remove or correct the statement that `ref struct` types such as `Span<T>` and `ReadOnlySpan<T>` are projected as stack-only structs, and SHALL instead state that ref structs are not projected and that members using them are hidden per the per-overload filtering behavior.
3. THE distributed steering file SHALL document that when a method group contains overloads using unsupported types, only those offending overloads are hidden while the remaining overloads stay callable.
4. THE distributed steering file SHALL document that CLR inheritance relationships spanning assemblies are resolved, so that a projected derived type is assignable to its projected base type across assemblies and inherited members are callable.
5. THE distributed steering file SHALL document the `mist inspect <TypeName>` command and the `mist restore` command, and SHALL note that builds are quiet by default while `--verbose` shows detailed package and framework resolution output.
6. THE distributed steering file SHALL retain its `MIST-MANAGED-STEERING` marker comment and its front matter so that `mist steering` continues to manage it as a bundled file.
7. WHERE another distributed steering file (e.g., `distribute/steering/mist-language.md` or `distribute/steering/mist-advanced-features.md`) contains a statement that the v2 interop behavior makes inaccurate, THAT file SHALL be updated for consistency, and any companion-file index that lists the interop steering file SHALL remain accurate.
