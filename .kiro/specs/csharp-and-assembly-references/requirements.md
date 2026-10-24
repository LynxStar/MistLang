# Requirements Document

## Introduction

A Mist project can reference other Mist projects, whose sources are composed into its compilation, and
NuGet packages, whose assemblies are read as metadata. It cannot reference a compiled assembly on disk or a
C# project, even though a NuGet package is already nothing but a list of assembly paths by the time the type
provider or Roslyn sees it. This feature adds those two kinds to the one `[references]` table, decides the
kind from the path, builds a referenced C# project before the Mist frontend runs, and keeps one CLR identity
for every Mist type that a referenced C# assembly was compiled against.

The identity problem is the part that is not plumbing. Mist-to-Mist references compose source, so a Mist
library's types live in the consuming program's primary output. A C# project that depends on that library
was compiled against the library's standalone assembly and records every one of those types as "this name in
assembly `<library>`". The runtime resolves types by assembly plus name and does not unify by name; a probe
on 2026-10-24 showed `FileNotFoundException` for the library assembly when it was absent, and two distinct
types with zero migrations applied when it was present. The fix specified here is the CLR's own mechanism
for a type that has moved: the compilation emits a facade assembly named after the composed library that
holds only `TypeForwardedTo` entries pointing at the primary output. The same probe verified the facade at
runtime and under Roslyn, including a signature that crosses the boundary.

The syntax, the bare-directory rule and the facade were decided on 2026-10-24. This spec layers on
`project-references`, `named-references` and `assembly-type-provider`; where it restates one of their rules
it does so only to extend it.

Out of scope, each a decision or task of its own: deploying native runtime assets from packages (`mist run`
of a program using SQLite still needs a staged native library); packaging a Mist library as a NuGet package;
MSBuild driving a Mist build (the hand-written bridge targets stay the documented way for a C# project to
depend on a Mist library and are not part of the compiler); and per-project emission of Mist-to-Mist
references, which stay source composition.

## Glossary

- **Reference_Entry**: one key-value pair in the `[references]` table of `mist.toml`, with a `path` field.
- **Reference_Kind**: one of `Mist_Project`, `CSharp_Project`, `Assembly`.
- **Reference_Classifier**: the pure decision that assigns a Reference_Kind and a resolved file path to a
  Reference_Entry from its `path` and, for a directory, the names of that directory's own files.
- **Composed_Project**: the root project or a `Mist_Project` reference, whose source files are compiled into
  this compilation's Primary_Output.
- **External_Assembly**: the file named by an `Assembly` reference, or the built output of a
  `CSharp_Project` reference.
- **Runtime_Closure**: for a `CSharp_Project`, the managed assemblies the .NET SDK placed in that project's
  output directory beside its output; for an `Assembly`, the file alone.
- **CSharp_Project_Builder**: the driver component that builds a referenced C# project with the .NET SDK and
  reads back its output path.
- **Facade**: an assembly whose name is a Composed_Project's name and whose only contents are type forwards
  to the Primary_Output.
- **Ambient_Type_Provider**: `AmbientTypeProviderV2`, created by `AmbientTypeProviderV2Factory` from a list
  of assembly paths read through a `MetadataLoadContext`, registering CLR types into the program model.
- **Backend**: `CSharpCompilationDriver`, which compiles the emitted C# with Roslyn and fills the
  Output_Directory.
- **Primary_Output**: `<project name>.dll` in the Output_Directory.
- **Output_Directory**: the project's `bin/`.
- **Build_Chain**: the ordered list of canonical `mist.toml` paths of the compilations currently in progress
  in this process tree, carried to child processes in the `MIST_BUILD_CHAIN` environment variable.

## Requirements

### Requirement 1: Reference kind from a direct path

**User Story:** As a Mist developer, I want to name a Mist manifest, a C# project file or a compiled
assembly in `[references]` by its path, so that one table declares every dependency and the file I name says
what kind it is.

#### Acceptance Criteria

1. WHEN a Reference_Entry's `path` names a file called `mist.toml`, THE Reference_Classifier SHALL assign
   `Mist_Project`, preserving today's behavior.
2. WHEN a Reference_Entry's `path` names a file with the extension `.csproj`, THE Reference_Classifier SHALL
   assign `CSharp_Project` and record that file as the resolved path.
3. WHEN a Reference_Entry's `path` names a file with the extension `.dll`, THE Reference_Classifier SHALL
   assign `Assembly` and record that file as the resolved path.
4. IF a Reference_Entry's `path` names an existing file that is not `mist.toml` and has an extension other
   than `.csproj` or `.dll`, THEN THE Project_File_Parser SHALL report `MIST_REFERENCE_UNSUPPORTED_PATH`
   naming the key and the path. A path that names nothing on disk and has no recognized extension keeps
   today's treatment: `mist.toml` is appended and the resolver reports `MIST_PROJECT_REFERENCE_NOT_FOUND`.
5. IF a `.csproj` or `.dll` path does not exist, THEN THE Project_Reference_Resolver SHALL report
   `MIST_REFERENCE_FILE_NOT_FOUND` naming the key and the path.
6. THE Reference_Classifier SHALL decide from the path's final segment alone and SHALL NOT read the file.
7. THE `ProjectReference` record SHALL carry the assigned Reference_Kind so that every later component
   dispatches on it rather than re-deriving it, and SHALL mark a reference written as a bare directory, so
   that only such a reference is ever listed and classified by the resolver.

### Requirement 2: Reference kind from a bare directory

**User Story:** As a Mist developer, I want `path = "../App.Migrations"` to work when that directory holds
one obvious thing to reference, so that I do not spell out a file name the directory already makes clear.

#### Acceptance Criteria

1. WHEN a Reference_Entry's `path` names an existing directory, THE Reference_Classifier SHALL consider the
   directory's own files named `mist.toml`, ending in `.csproj`, or ending in `.dll` as candidates.
2. THE Reference_Classifier SHALL NOT consider files in subdirectories, so that a project's `bin/`, `obj/`
   and `out/` contents are never candidates.
3. WHEN exactly one candidate exists, THE Reference_Classifier SHALL assign that candidate's kind and record
   the candidate as the resolved path.
4. IF no candidate exists, THEN THE Project_Reference_Resolver SHALL report
   `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE`, whose message SHALL say the directory holds no `mist.toml`,
   `.csproj` or `.dll`.
5. IF two or more candidates exist, whether of one kind or of several, THEN THE Project_Reference_Resolver
   SHALL report `MIST_REFERENCE_AMBIGUOUS` at the referencing manifest, naming the key, listing every
   candidate, and saying to name the intended file in `path`.
6. IF the directory does not exist, THEN THE Project_Reference_Resolver SHALL report
   `MIST_PROJECT_REFERENCE_NOT_FOUND` as today.
7. THE Reference_Classifier SHALL compare file names ordinally and case-insensitively on extensions, so that
   `App.Migrations.CSPROJ` is a candidate.

### Requirement 3: The reference key names the produced assembly

**User Story:** As a Mist developer, I want the key of a reference to be the assembly name I will see in
`bin/`, so that typos and renames fail at build time for every kind alike.

#### Acceptance Criteria

1. WHEN the kind is `Mist_Project`, THE Project_Reference_Resolver SHALL require the key to equal the target
   manifest's `name`, as `named-references` already requires.
2. WHEN the kind is `Assembly`, THE Project_Reference_Resolver SHALL require the key to equal the file name
   without its extension.
3. WHEN the kind is `CSharp_Project`, THE Project_Reference_Resolver SHALL require the key to equal the file
   name, without extension, of the built output reported by the CSharp_Project_Builder, which is MSBuild's
   `AssemblyName` and need not equal the project file's name.
4. IF the key does not match, THEN THE Project_Reference_Resolver SHALL report
   `MIST_PROJECT_REFERENCE_NAME_MISMATCH`, whose message SHALL name the kind and the name the target
   produces.
5. THE key SHALL continue to satisfy the existing name pattern, which admits dots, so that `App.Data` and
   `App.Migrations` are valid keys.

### Requirement 4: Building a referenced C# project

**User Story:** As a Mist developer, I want a referenced C# project to be built as part of `mist build`, so
that my Mist code compiles against its current output and the build runs from a clean tree.

#### Acceptance Criteria

1. WHEN the reference graph contains a `CSharp_Project` reference, THE CSharp_Project_Builder SHALL run
   `dotnet build` on it before Stage 1 loads any source, with the `Debug` configuration, the Mist project's
   `target_framework` as the framework, quiet verbosity, and no logo.
2. THE CSharp_Project_Builder SHALL obtain the output assembly path from MSBuild's `TargetPath` property for
   that configuration and framework, not by assuming a `bin/Debug/<tfm>/` layout.
3. THE CSharp_Project_Builder SHALL record the directory containing the output as the reference's
   Runtime_Closure, taken as every `.dll` in that directory other than the output itself.
4. IF the build exits non-zero, THEN THE CSharp_Project_Builder SHALL report
   `MIST_CSHARP_PROJECT_BUILD_FAILED` naming the key and the project path, with MSBuild's error lines in the
   message, and the compilation SHALL stop before Stage 1.
5. THE CSharp_Project_Builder SHALL build each referenced C# project once per compilation, in the order the
   manifests declare them, and SHALL run a referenced Mist project's own `CSharp_Project` references before
   the root project's.
6. THE compiler SHALL set `MIST_BUILD_CHAIN` in the environment of every `dotnet build` it starts to the
   Build_Chain extended with the canonical `mist.toml` path being compiled, so that a bridge target inside
   the C# project may start a nested `mist build` of a Mist library.
7. IF a compilation's canonical `mist.toml` path is already present in the Build_Chain it inherits, THEN THE
   compiler SHALL report `MIST_REFERENCE_CYCLE` naming the chain and SHALL exit without building, so that a
   C# project that bridges back to the project referencing it fails once instead of recursing.
8. THE CSharp_Project_Builder SHALL bound the build with a timeout, as `DotnetRestoreProcess` bounds a
   restore, and SHALL report a timeout as `MIST_CSHARP_PROJECT_BUILD_FAILED`.

### Requirement 5: Assembly references

**User Story:** As a Mist developer, I want to reference a compiled assembly by path, so that a library I
have on disk is as usable as one from NuGet.

#### Acceptance Criteria

1. WHEN the kind is `Assembly`, THE compiler SHALL treat the named file as an External_Assembly for the
   frontend, for Roslyn and for deployment.
2. THE compiler SHALL NOT infer references to other files beside an `Assembly` reference; a dependency of
   that assembly that the program needs is declared as its own reference or package.
3. THE compiler SHALL NOT validate an `Assembly` reference's target framework itself; a mismatch surfaces
   through the Ambient_Type_Provider's or Roslyn's own diagnostics.

### Requirement 6: External assemblies in the frontend

**User Story:** As a Mist developer, I want types from a referenced C# project or assembly to be usable in
Mist code exactly as package types are, and I want a C# type that mentions one of my own Mist types to mean
my type.

#### Acceptance Criteria

1. THE compiler SHALL pass every External_Assembly path to the Ambient_Type_Provider together with the
   package and framework assembly paths, through the same resolved-paths list the pipeline already carries.
2. THE compiler SHALL add every Runtime_Closure directory to the paths the Ambient_Type_Provider's resolver
   searches, so that an External_Assembly's own dependencies resolve during metadata reading, and SHALL NOT
   register types from those dependencies unless they are also package, framework or External_Assembly
   members.
3. WHEN a member signature in an External_Assembly names a type whose full name is declared in a
   Composed_Project, THE frontend SHALL bind that type to the declared type. With criterion 4 in force the
   standalone copy is never loaded, so the Ambient_Type_Provider's resolver SHALL fall back, for a CLR name
   that matches no loaded type, to a type declared in Mist source with that full name, and only then treat
   the name as unresolved. The 2026-10-24 probe covered a class, an array, `List<T>`, `IEnumerable<T>`,
   `IReadOnlyList<T>`, `Func<T, int>` and a generic method over the composed type.
4. WHEN an External_Assembly or a Runtime_Closure member has the simple name of a Composed_Project, THE
   Ambient_Type_Provider SHALL NOT register any of its types, so that a stale standalone copy of a composed
   library never shadows or duplicates the declared types.
5. THE Ambient_Type_Provider SHALL register nested types of an External_Assembly's public types as it does
   for package assemblies today.

### Requirement 7: One identity through facades

**User Story:** As a Mist developer, I want a C# assembly compiled against my Mist library's standalone
output to work when that library is composed into my program, so that the C# project and the Mist program
agree on what each type is.

#### Acceptance Criteria

1. THE Backend SHALL read the assembly-reference table of every External_Assembly and collect the referenced
   simple names.
2. WHEN a collected name equals a Composed_Project's name, THE Backend SHALL emit a Facade with that name
   into the Output_Directory.
3. THE Facade SHALL forward every public top-level type declared in a compilation unit whose origin is that
   Composed_Project, and every public top-level type of the standard library and of the generated support
   files, each by one exported-type forwarder row targeting the Primary_Output. Every nested type of a
   forwarded type, whatever its accessibility, SHALL get its own row pointing at its encloser's row: the
   runtime does not follow an encloser's forward for a nested type, and a probe on 2026-10-24 failed with
   `MissingMethodException` when the rows were absent. The forwarded names are taken from the types Roslyn
   declares for the compilation, so they are exactly the emitted names, including generic arity and the
   specializations in the support files.
4. THE Backend SHALL pass each Facade to Roslyn as a metadata reference when compiling the Primary_Output, so
   that a signature crossing the boundary unifies instead of reporting two types of one name.
5. THE Backend SHALL exclude from Roslyn's references, and from deployment, any External_Assembly or
   Runtime_Closure member whose simple name equals a Composed_Project's name, whether or not a Facade is
   emitted for that name.
6. IF a collected name equals the root project's name, THEN THE Backend SHALL report `MIST_REFERENCE_CYCLE`
   naming the External_Assembly, since no Facade can forward into the output it would be part of.
7. THE Facade SHALL carry the assembly version the Primary_Output carries, and its contents SHALL be
   deterministic for a given program.
8. WHEN no External_Assembly references a Composed_Project's name, THE Backend SHALL emit no Facade for it.

### Requirement 8: Deployment

**User Story:** As a Mist developer, I want `mist run` and `dotnet bin/<app>.dll` to find every assembly
the program needs, so that a referenced C# project's dependencies do not have to be staged by hand.

#### Acceptance Criteria

1. THE Backend SHALL copy every External_Assembly and every Runtime_Closure member into the Output_Directory,
   except files excluded by Requirement 7.5 and files whose simple name the compiler's own package and
   framework resolution already supplies.
2. IF two files among the compiler's own resolution, the External_Assemblies and their Runtime_Closures
   share a simple name and differ in assembly version, THEN THE Backend SHALL report
   `MIST_REFERENCE_ASSEMBLY_CONFLICT` naming both paths and versions, so that no two parts of the program
   load different builds of one library. A file with no assembly definition never conflicts, and the first
   file of a name is the one deployed; a copy already in the Output_Directory is refreshed when its source
   is newer.
3. THE Backend SHALL write each Facade beside the Primary_Output.
4. WHEN a `CSharp_Project` reference's `runtimeconfig.json` names a shared framework the Mist project's
   `framework_references` does not, THE Backend SHALL report `MIST_REFERENCE_FRAMEWORK_MISSING` naming the
   framework, so that the runtime configuration the Backend writes is sufficient.
5. THE Backend SHALL leave native runtime assets to the separate task that owns them; this feature neither
   copies nor reports them.

### Requirement 9: CLI commands

**User Story:** As a Mist developer, I want `restore`, `build`, `run` and `inspect` to understand the new
reference kinds, so that the commands behave consistently.

#### Acceptance Criteria

1. WHEN `mist build` or `mist run` compiles a project, THE CLI SHALL perform Requirements 4 through 8 as part
   of the build.
2. WHEN `mist restore` runs, THE CLI SHALL run `dotnet restore` on every `CSharp_Project` reference and
   SHALL report each reference's kind, without building anything.
3. WHEN `mist inspect <TypeName>` runs, THE CLI SHALL build `CSharp_Project` references first so that their
   types can be inspected.
4. THE progress output for Stage 1 SHALL state how many C# projects were built and how many external
   assemblies were referenced.

### Requirement 10: Diagnostics

**User Story:** As a Mist developer, I want every failure in this feature to have one identifier and a
message that names what to change.

#### Acceptance Criteria

1. THE compiler SHALL define these descriptors, all with severity Error, at the Driver stage unless noted:

   | Identifier | Reported when |
   | --- | --- |
   | `MIST_REFERENCE_UNSUPPORTED_PATH` | A direct path names an existing file that is not `mist.toml`, a `.csproj` or a `.dll` (1.4). |
   | `MIST_REFERENCE_FILE_NOT_FOUND` | A named `.csproj` or `.dll` does not exist (1.5). |
   | `MIST_REFERENCE_AMBIGUOUS` | A directory holds two or more candidates (2.5). |
   | `MIST_CSHARP_PROJECT_BUILD_FAILED` | `dotnet build` of a referenced project fails or times out (4.4, 4.8). |
   | `MIST_REFERENCE_CYCLE` | The Build_Chain already holds this manifest, or an External_Assembly references the root project (4.7, 7.6). |
   | `MIST_REFERENCE_ASSEMBLY_CONFLICT` | Backend stage: one simple name, two versions (8.2). |
   | `MIST_REFERENCE_FRAMEWORK_MISSING` | Backend stage: a C# project's shared framework is not declared (8.4). |

2. THE compiler SHALL reuse `MIST_PROJECT_REFERENCE_NO_PROJECT_FILE`, `MIST_PROJECT_REFERENCE_NOT_FOUND` and
   `MIST_PROJECT_REFERENCE_NAME_MISMATCH` with the messages Requirements 2.4 and 3.4 describe.
3. THE descriptors SHALL live in `DriverDiagnostics` and `BackendDiagnostics` beside the existing
   project-reference and C# descriptors.

### Requirement 11: Verification

**User Story:** As a compiler maintainer, I want the feature proven at the tiers the testing philosophy
assigns, with one system test that runs the whole path from a clean tree.

#### Acceptance Criteria

1. THE Reference_Classifier's decision SHALL be proven at Tier 1 over directory listings built in memory:
   each single-candidate kind, no candidate, two of one kind, one each of two kinds, a subdirectory that
   must not count, and extension casing.
2. The facade's type selection SHALL be proven at Tier 1 over candidates built directly: a public type of the
   composed project is forwarded, an internal one is not, a type of another composed project is not,
   standard-library and support types are, and a nested type of a forwarded type is forwarded under its
   encloser.
3. The assembly-reference scan and the version-conflict rule SHALL be proven at Tier 1 over metadata images
   synthesized with `System.Reflection.Metadata`, never with Roslyn.
4. The cycle rule over the Build_Chain SHALL be proven at Tier 1.
5. Requirement 6.3 and 6.4 SHALL be proven at Tier 2 over a stub ambient provider.
6. The driver's handling of a populated `AssemblyPaths` SHALL be proven at Tier 4 with a prebuilt fixture
   assembly and no MSBuild.
7. THE repository SHALL hold a sample under `samples/CSharpReferences` with a Mist library, a C# project
   that depends on it through the bridge targets and declares no packages, and a Mist executable whose
   manifest references both; one Tier 5 test SHALL run it through the prebuilt CLI from a clean tree and
   assert its output. `AllFeaturesV2` cannot hold it because the sample runs MSBuild.
8. `samples/EfCoreMigrations` SHALL be updated so that `App` references `App.Migrations` and its `Main`
   calls `Migrate()`, as a documented demonstration rather than a suite test, since it needs NuGet, the
   `dotnet-ef` tool and a staged native library.

### Requirement 12: Non-goals

**User Story:** As a compiler maintainer, I want the boundaries of this feature stated, so that adjacent
work is filed on its own.

#### Acceptance Criteria

1. THE feature SHALL NOT change Mist-to-Mist references from source composition to assembly references.
2. THE feature SHALL NOT make the compiler emit a `.csproj` that MSBuild can build in place of the compiler;
   the bridge targets remain a documented sample artifact.
3. THE feature SHALL NOT deploy native runtime assets.
4. THE feature SHALL NOT remove the public `__CreateDefault` members or the per-assembly standard-library
   copies that a C# consumer of a Mist library sees; that cleanup is separate.
