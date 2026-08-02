# Requirements Document

## Introduction

The Assembly Type Provider replaces the manually coded ambient type system with automatic metadata reading from .NET assemblies. Instead of hand-writing C# type definitions in the `CSharpAmbientTypeResolver`, the compiler reads type metadata directly from .NET assembly DLLs (via `System.Reflection.MetadataLoadContext` or similar) and surfaces those types into the Mist semantic model. Users can reference NuGet packages in `mist.toml`, and all types from those packages become available in Mist source code. Assemblies bundled with `Microsoft.NET.Sdk` (and `Microsoft.CSharp`) are always available without explicit package references. Standard Library intrinsic types (e.g., `Standard.Array<T, Length>`) have bidirectional mappings to their CLR counterparts (e.g., `System.Array`), with appropriate type parameter adjustments on the boundary.

## Glossary

- **Assembly_Type_Provider**: The compiler component that reads .NET assembly metadata and produces Mist `IType` and `ISymbol` instances for types discovered in referenced assemblies.
- **Assembly_Reference**: A NuGet package or framework assembly whose types are made available to Mist source code through the Assembly_Type_Provider.
- **SDK_Assembly**: An assembly that ships as part of the `Microsoft.NET.Sdk` target framework (e.g., `System.Runtime.dll`, `System.Collections.dll`) and is always available without an explicit package reference in `mist.toml`.
- **NuGet_Package_Reference**: A `[packages]` table entry in `mist.toml` specifying a NuGet package name and version whose assemblies the Assembly_Type_Provider loads.
- **Intrinsic_Type**: A Standard Library type (e.g., `Standard.Array<T, Length>`, `Standard.Int32`) that has a known bidirectional mapping to a CLR type and is handled specially by the type provider.
- **CLR_Type_Mapping**: The rules that translate a CLR type's metadata (namespace, name, members, generics) into a Mist `IType` with appropriate Mist-idiomatic members.
- **Visibility_Filter**: The set of rules that determines which CLR type members are hidden from Mist because they use unsupported CLR features (e.g., `out` parameters, `ref` returns, pointer types).
- **Metadata_Load_Context**: The `System.Reflection.MetadataLoadContext` (or equivalent) used to inspect assembly metadata without loading assemblies into the runtime.
- **Backing_Type_Map**: The existing per-backend mapping from Standard Library types to their target-language representations (e.g., `Standard.Int32 → int`).
- **Mist_Type_Projection**: The Mist `IType` instance produced by the Assembly_Type_Provider for a given CLR type, with members filtered and signatures translated to Mist types.

## Requirements

### Requirement 1: NuGet Package References in mist.toml

**User Story:** As a Mist developer, I want to declare NuGet package dependencies in my `mist.toml` file, so that types from those packages are available in my Mist source code.

#### Acceptance Criteria

1. WHEN a `[packages]` table is present in `mist.toml`, THE Project_File_Parser SHALL parse each key-value entry as a package name (the TOML key) and a version string (the TOML string value).
2. THE Project_File_Parser SHALL validate that each package name matches the pattern `[a-zA-Z0-9\-_\.]+`, does not exceed 128 characters in length, and each version string is non-empty and non-whitespace-only.
3. IF a `[packages]` entry has an empty or whitespace-only name, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error.
4. IF a `[packages]` entry has an empty or whitespace-only version, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error.
5. WHEN the `[packages]` table is absent from `mist.toml`, THE Project_File_Parser SHALL treat the package list as empty (no error).
6. IF the `[packages]` field is present but is not a TOML table, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error.
7. IF a `[packages]` entry has a version value that is not a string type, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error.
8. IF a `[packages]` entry has a name that does not match the pattern `[a-zA-Z0-9\-_\.]+` or exceeds 128 characters, THEN THE Project_File_Parser SHALL emit a diagnostic with severity Error indicating the invalid package name.

### Requirement 2: SDK Assembly Auto-Availability

**User Story:** As a Mist developer, I want all types from the .NET SDK framework assemblies to be available without explicit package references, so that I can use standard .NET types like `System.Collections.Generic.List<T>` immediately.

#### Acceptance Criteria

1. THE Assembly_Type_Provider SHALL automatically load all `.dll` assemblies found in the target framework reference path for `Microsoft.NET.Sdk`, defaulting to `net10.0` when no target framework is specified in `mist.toml`.
2. THE Assembly_Type_Provider SHALL automatically load the `Microsoft.CSharp` assembly from the same target framework reference path.
3. WHEN a Mist source file references a type from an SDK assembly by its CLR namespace and name, THE Assembly_Type_Provider SHALL resolve the type without requiring an explicit `[packages]` entry.
4. THE Assembly_Type_Provider SHALL discover the SDK assembly reference path by locating the installed .NET SDK on the host machine using the `dotnet` CLI or well-known installation paths, and SHALL select the highest installed SDK version that supports the target framework.
5. IF the .NET SDK installation cannot be located or the target framework reference path does not exist, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error indicating that the SDK is required and which path was searched.
6. IF multiple .NET SDK versions are installed, THEN THE Assembly_Type_Provider SHALL select the latest installed SDK version that provides the target framework reference assemblies.

### Requirement 3: Assembly Metadata Reading

**User Story:** As a Mist developer, I want the compiler to read type information directly from .NET assembly DLLs, so that I get accurate type information without manual type definitions.

#### Acceptance Criteria

1. THE Assembly_Type_Provider SHALL use a metadata-only loading mechanism (not full assembly load) to read type information from referenced assemblies.
2. THE Assembly_Type_Provider SHALL read the following from each public type (including nested public types) in a loaded assembly: namespace, type name, generic type parameters, base type, implemented interfaces, and public members (methods, properties, fields, constructors, and events as defined in Requirement 5).
3. WHEN a Mist source file first references a CLR type by name, THE Assembly_Type_Provider SHALL resolve that type's metadata from the loaded assemblies at that point rather than eagerly loading all types from all assemblies at startup.
4. THE Assembly_Type_Provider SHALL cache resolved types so that subsequent references to the same CLR type (identified by assembly-qualified namespace, type name, and generic arity) return the same Mist `IType` instance.
5. IF an assembly file cannot be read or is corrupted, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error identifying the assembly path and continue compilation without the types from that assembly.

### Requirement 4: CLR Type to Mist Type Mapping

**User Story:** As a Mist developer, I want CLR types to appear as idiomatic Mist types, so that I can use .NET library types naturally in my Mist code.

#### Acceptance Criteria

1. WHEN a CLR class is projected, THE Assembly_Type_Provider SHALL produce a Mist `ClassType` with the CLR type's simple name (without generic arity suffix) and namespace.
2. WHEN a CLR struct (value type) is projected, THE Assembly_Type_Provider SHALL produce a Mist `StructType` with the CLR type's simple name (without generic arity suffix) and namespace.
3. WHEN a CLR interface is projected, THE Assembly_Type_Provider SHALL produce a Mist `InterfaceType` with the CLR type's simple name (without generic arity suffix) and namespace.
4. WHEN a CLR enum is projected, THE Assembly_Type_Provider SHALL produce a Mist `EnumType` with the enum's members as named constants, each carrying its declared integer value, and with the underlying type set to the CLR enum's underlying integral type mapped to the corresponding Mist Standard Library type.
5. WHEN a CLR generic type definition is projected, THE Assembly_Type_Provider SHALL produce a generic Mist type with the same number of type parameters, preserving the CLR type parameter names.
6. WHEN a CLR type has generic constraints that have a Mist equivalent (`class`, `struct`, `new()`, or a base type/interface constraint referencing a projected Mist type), THE Assembly_Type_Provider SHALL translate those constraints to the corresponding Mist generic constraints. IF a CLR generic constraint has no Mist equivalent, THEN THE Assembly_Type_Provider SHALL omit that constraint from the projected type (the Mist type parameter remains less constrained).
7. WHEN a CLR type inherits from a base class other than `System.Object`, THE Assembly_Type_Provider SHALL record the inheritance relationship in the projected Mist type by setting the base type to the projected Mist type for that CLR base class.
8. WHEN a CLR type implements interfaces, THE Assembly_Type_Provider SHALL record the interface implementations in the projected Mist type by including each implemented interface's projected Mist type in the type's interface list.

### Requirement 5: CLR Member Projection

**User Story:** As a Mist developer, I want to call methods, access properties, and use constructors from .NET types, so that I can fully interact with .NET libraries.

#### Acceptance Criteria

1. WHEN a CLR type has public instance methods, THE Assembly_Type_Provider SHALL project each method as a Mist method symbol with parameter types and return type translated to Mist types.
2. WHEN a CLR type has public static methods, THE Assembly_Type_Provider SHALL project each static method as a Mist static method symbol with parameter types and return type translated to Mist types.
3. WHEN a CLR type has public properties (get/set), THE Assembly_Type_Provider SHALL project each property as a Mist property symbol. IF the CLR property has only a getter, THEN the projected property SHALL be read-only. IF the CLR property has both getter and setter, THEN the projected property SHALL be read-write. IF the CLR property has a setter whose accessibility is more restrictive than public (e.g., `protected set`, `internal set`), THEN the projected property SHALL be read-only (the non-public setter is hidden).
4. WHEN a CLR type has public fields, THE Assembly_Type_Provider SHALL project each field as a Mist field symbol. IF the CLR field is marked `readonly` or `const`, THEN the projected field SHALL be read-only.
5. WHEN a CLR type has public constructors, THE Assembly_Type_Provider SHALL project each constructor as a Mist constructor symbol with parameter types translated to Mist types.
6. WHEN a CLR method has optional parameters with default values, THE Assembly_Type_Provider SHALL project those parameters as optional in the Mist signature.
7. WHEN a CLR method is generic, THE Assembly_Type_Provider SHALL project the method's type parameters and constraints.
8. WHEN a CLR type has public events, THE Assembly_Type_Provider SHALL hide events from the Mist projection (events are not yet supported in Mist).
9. WHEN a CLR type has public indexers (parameterized properties named `Item` with `[IndexerName]` or default indexer convention), THE Assembly_Type_Provider SHALL project each indexer as a Mist indexer symbol with parameter types and return type translated to Mist types.

### Requirement 6: Visibility Filter for Unsupported CLR Features

**User Story:** As a Mist developer, I want CLR members that use unsupported features to be hidden rather than causing errors, so that I can still use the supported parts of a .NET type.

#### Acceptance Criteria

1. WHEN a CLR method or constructor has `out` parameters, THE Visibility_Filter SHALL exclude that method or constructor from the Mist projection.
2. WHEN a CLR method or constructor has `ref` parameters (excluding `in` parameters, which are ref-readonly and SHALL be treated as regular pass-by-value parameters), THE Visibility_Filter SHALL exclude that method or constructor from the Mist projection.
3. WHEN a CLR method or property has a `ref` return type, THE Visibility_Filter SHALL exclude that method or property from the Mist projection.
4. WHEN a CLR method, constructor, or property has pointer-type parameters or return type, THE Visibility_Filter SHALL exclude that member from the Mist projection.
5. WHEN a CLR type is a `ref struct` (including `Span<T>` and `ReadOnlySpan<T>`), THE Visibility_Filter SHALL exclude that type entirely from the Mist projection.
6. WHEN a CLR member's signature references a type that is itself excluded by the Visibility_Filter in any position (parameter types, return type, property type, or field type), THE Visibility_Filter SHALL exclude that member from the Mist projection.
7. THE Visibility_Filter SHALL only project members with `public` accessibility; `internal`, `protected`, `private`, `protected internal`, and `private protected` members SHALL be excluded.

### Requirement 7: Intrinsic Type Bidirectional Mapping

**User Story:** As a Mist developer, I want Standard Library intrinsic types to interoperate seamlessly with their CLR counterparts, so that when a .NET method returns a `System.String` I see it as `Standard.String` in Mist, and vice versa.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider encounters a CLR type that has a corresponding Standard Library intrinsic (e.g., `System.String` → `Standard.String`, `System.Int32` → `Standard.Int32`), THE Assembly_Type_Provider SHALL substitute the Standard Library type in the projected signature instead of creating a new Mist type.
2. WHEN a CLR method signature contains `System.Array` (or `T[]`), THE Assembly_Type_Provider SHALL map it to `Standard.Array<T, unknown>` in the Mist projection (using `unknown` for the length parameter since the length is not statically known from the CLR signature).
3. WHEN a Mist expression of type `Standard.Array<T, Length>` is passed to a CLR method expecting `T[]`, THE CSharp_Emitter SHALL emit the array value directly (the dependent length parameter is erased at the CLR level).
4. THE Assembly_Type_Provider SHALL derive its CLR-to-Mist type substitutions by reversing the existing Backing_Type_Map (the same map that powers emission from Mist types to C# types). There SHALL be a single source of truth for the intrinsic type relationships; the Assembly_Type_Provider SHALL NOT maintain a separate mapping table. This applies to all positions where a CLR type appears in a projected signature — parameters, return types, generic type arguments, field types, and property types.
5. WHEN a CLR generic type is constructed with a primitive type argument (e.g., `List<int>`), THE Assembly_Type_Provider SHALL recursively substitute the corresponding Standard Library type in each type argument position (e.g., `List<Standard.Int32>`). This recursive substitution SHALL apply at all nesting levels (e.g., `Dictionary<string, List<int>>` becomes `Dictionary<Standard.String, List<Standard.Int32>>`).
6. WHEN a CLR type does not appear in the reversed Backing_Type_Map, THE Assembly_Type_Provider SHALL project it as a normal Mist type (no substitution) using the rules in Requirement 4.

### Requirement 8: CLR Namespace Resolution

**User Story:** As a Mist developer, I want to use `using` directives to import CLR namespaces and reference types by their simple names, so that I can work with .NET types using the same namespace conventions as C#.

#### Acceptance Criteria

1. WHEN a Mist source file contains a `using` directive for a CLR namespace (e.g., `using System.Collections.Generic`), THE Semantic_Model SHALL make all public types from that namespace available for unqualified name resolution within that file.
2. WHEN a type name is referenced without qualification, THE Semantic_Model SHALL resolve the name by searching Mist-declared types first; IF no match is found among Mist-declared types, THEN THE Semantic_Model SHALL search imported CLR namespaces (via the Assembly_Type_Provider).
3. WHEN a type is referenced with full qualification (e.g., `System.Collections.Generic.List<T>`), THE Assembly_Type_Provider SHALL resolve the type directly by namespace and name without requiring a `using` directive for that namespace.
4. IF a type name is ambiguous between multiple imported CLR namespaces and no Mist-declared type with that name exists, THEN THE Semantic_Model SHALL emit a diagnostic with severity Error indicating the ambiguity and listing the conflicting namespaces.
5. IF a `using` directive references a namespace that does not exist in any loaded assembly, THEN THE Semantic_Model SHALL emit a diagnostic with severity Error identifying the unresolved namespace.

### Requirement 9: NuGet Package Resolution

**User Story:** As a Mist developer, I want the compiler to automatically download and resolve NuGet packages declared in `mist.toml`, so that I can use third-party libraries without manual assembly management.

#### Acceptance Criteria

1. WHEN `mist.toml` contains a `[packages]` entry that is not already present in the local NuGet cache, THE Package_Resolver SHALL download and extract the package from configured NuGet feeds (defaulting to nuget.org) within 60 seconds per package.
2. IF a referenced package cannot be found on any configured NuGet feed (e.g., the package name or version does not exist), THEN THE Package_Resolver SHALL emit a diagnostic with severity Error identifying the package name, requested version, and feed URL that was searched.
3. THE Package_Resolver SHALL resolve the full transitive dependency graph of referenced packages and make their assemblies available to the Assembly_Type_Provider.
4. IF two or more packages in the transitive dependency graph depend on different versions of the same package, THEN THE Package_Resolver SHALL select the highest compatible version (NuGet nearest-version resolution semantics).
5. WHEN a package targets multiple frameworks, THE Package_Resolver SHALL select the assembly from the nearest compatible target framework moniker at or below net10.0 (e.g., preferring net10.0, then net9.0, then net8.0, then netstandard2.1, then netstandard2.0).
6. IF a package has no compatible framework target, THEN THE Package_Resolver SHALL emit a diagnostic with severity Error identifying the package name, version, and the list of framework targets the package provides.
7. IF a network error or timeout occurs during package restore, THEN THE Package_Resolver SHALL emit a diagnostic with severity Error identifying the package name, version, and the nature of the failure (e.g., timeout, connection refused).
8. THE Package_Resolver SHALL treat the version string in each `[packages]` entry as an exact version (e.g., `1.2.3`); version ranges are not supported.

### Requirement 10: CLR Generic Type Handling

**User Story:** As a Mist developer, I want to use generic .NET types like `List<T>`, `Dictionary<TKey, TValue>`, and `Task<T>` with full type parameter support, so that I get the same type safety as in C#.

#### Acceptance Criteria

1. WHEN a CLR generic type definition is projected, THE Assembly_Type_Provider SHALL create a Mist generic type with type parameters matching the CLR definition's arity and names.
2. WHEN a CLR generic constraint specifies `class` (reference type), THE Assembly_Type_Provider SHALL translate it to a Mist `where T : class` constraint.
3. WHEN a CLR generic constraint specifies `struct` (value type), THE Assembly_Type_Provider SHALL translate it to a Mist `where T : struct` constraint.
4. WHEN a CLR generic constraint specifies `new()` (parameterless constructor), THE Assembly_Type_Provider SHALL translate it to a Mist `where T : new()` constraint.
5. WHEN a CLR generic constraint specifies a base type or interface, THE Assembly_Type_Provider SHALL translate it to a Mist type constraint referencing the projected Mist type for that base/interface.
6. IF Mist code supplies a type argument that violates a projected generic constraint (e.g., a class type where `struct` is required, or a type missing a required interface), THEN THE Semantic_Model SHALL emit a diagnostic with severity Error identifying the violated constraint.
7. WHEN a CLR member signature contains a constructed generic type as a parameter or return type (e.g., `Task<List<int>>`), THE Assembly_Type_Provider SHALL recursively resolve each type argument, applying intrinsic type substitution at every nesting level.
8. WHEN Mist code constructs a generic CLR type (e.g., `new List<Standard.Int32>()`), THE CSharp_Emitter SHALL emit the construction with the backing C# type arguments (e.g., `new List<int>()`).
9. WHEN Mist code invokes a generic CLR method with explicit type arguments (e.g., `list.ConvertAll<Standard.String>(converter)`), THE CSharp_Emitter SHALL emit the method call with the backing C# type arguments (e.g., `list.ConvertAll<string>(converter)`).

### Requirement 11: Method Overload Projection

**User Story:** As a Mist developer, I want to see and call overloaded methods from .NET types, so that I can use the full API surface of .NET libraries.

#### Acceptance Criteria

1. WHEN a CLR type has multiple public methods with the same name but different parameter lists, THE Assembly_Type_Provider SHALL project all visible overloads as separate Mist method symbols.
2. WHEN a CLR method overload is excluded by the Visibility_Filter (e.g., has `out` parameters), THE Assembly_Type_Provider SHALL still project the remaining visible overloads.
3. WHEN Mist code calls an overloaded method, THE Semantic_Model SHALL select the correct overload based on argument types (overload resolution).

### Requirement 12: Operator and Conversion Projection

**User Story:** As a Mist developer, I want arithmetic and comparison operators on .NET types to work naturally in Mist expressions, so that I can use operators like `+`, `-`, `==` on types that define them.

#### Acceptance Criteria

1. WHEN a CLR type defines `op_Addition`, `op_Subtraction`, `op_Multiply`, `op_Division`, or `op_Modulus`, THE Assembly_Type_Provider SHALL project those as Mist binary operator symbols.
2. WHEN a CLR type defines `op_Equality`, `op_Inequality`, `op_LessThan`, `op_GreaterThan`, `op_LessThanOrEqual`, or `op_GreaterThanOrEqual`, THE Assembly_Type_Provider SHALL project those as Mist comparison operator symbols.
3. WHEN a CLR type defines `op_Implicit` or `op_Explicit` conversion operators, THE Assembly_Type_Provider SHALL project those as Mist implicit or explicit cast operators.
4. WHEN a CLR type defines `op_UnaryNegation` or `op_UnaryPlus`, THE Assembly_Type_Provider SHALL project those as Mist unary operator symbols.

### Requirement 13: Inheritance and Interface Projection

**User Story:** As a Mist developer, I want .NET type hierarchies to be represented in Mist, so that I can use polymorphism and interface-based programming with .NET types.

#### Acceptance Criteria

1. WHEN a CLR class inherits from a non-`object` base class, THE Assembly_Type_Provider SHALL project the inheritance relationship so that the Mist type is assignable to its base type in variable assignments, parameter passing, and return positions. The base type's public members SHALL be accessible on the derived Mist type as if declared on it.
2. WHEN a CLR type implements interfaces, THE Assembly_Type_Provider SHALL project those interface implementations so that the Mist type is assignable to each interface type in variable assignments, parameter passing, and return positions.
3. WHEN a CLR method is declared `virtual` or `abstract`, THE Assembly_Type_Provider SHALL project it as a regular method (Mist does not currently support virtual dispatch declaration, but the method is callable).
4. IF Mist code attempts to instantiate a CLR type that is `abstract` using `new`, THEN THE Semantic_Model SHALL emit a diagnostic with severity Error indicating that abstract types cannot be directly instantiated.
5. IF Mist code attempts to inherit from a CLR type that is `sealed`, THEN THE Semantic_Model SHALL emit a diagnostic with severity Error indicating that sealed types cannot be extended.
6. WHEN a CLR class has a multi-level inheritance chain (e.g., C inherits B inherits A), THE Assembly_Type_Provider SHALL project the full chain so that the Mist type is assignable to any ancestor type in the hierarchy.

### Requirement 14: Nullable and Reference Type Handling

**User Story:** As a Mist developer, I want CLR nullable and reference types to be represented safely in Mist, so that I am forced to handle the possibility of null values from .NET APIs before using them.

#### Acceptance Criteria

1. WHEN a CLR method parameter or return type is `Nullable<T>` (e.g., `int?`), THE Assembly_Type_Provider SHALL project the type as the union `T | void` where `T` is the corresponding Mist Standard Library type.
2. WHEN a CLR method parameter or return type is a reference type (class, interface, string, array, delegate), THE Assembly_Type_Provider SHALL project the type as `T | void` regardless of any C# nullable reference type annotations (`NullableAttribute`, `NullableContextAttribute`) present in the assembly metadata (the conservative projection ensures null safety even for assemblies compiled with NRT enabled).
3. WHEN the CSharp_Emitter emits a call to a CLR method expecting `Nullable<T>`, and the Mist argument has been narrowed from `T | void` to `T`, THE CSharp_Emitter SHALL emit the value as `Nullable<T>` constructed from the narrowed value; WHEN the argument has been narrowed to `void`, THE CSharp_Emitter SHALL emit `default(Nullable<T>)`.
4. WHEN the CSharp_Emitter emits a call to a CLR method expecting a reference type parameter, and the Mist argument has been narrowed from `T | void` to `T`, THE CSharp_Emitter SHALL emit the value as `T` directly; WHEN the argument has been narrowed to `void`, THE CSharp_Emitter SHALL emit `null`.
5. WHEN a CLR method has a non-nullable value type parameter (e.g., `int`, `bool`), THE Assembly_Type_Provider SHALL project it as the plain Mist type without the `| void` union (value types cannot be null unless wrapped in `Nullable<T>`).
6. THE Assembly_Type_Provider SHALL track whether a projected union type originated from `Nullable<T>` or from a reference type, so that the CSharp_Emitter can produce the correct CLR-level signature on emission.

### Requirement 15: Diagnostic Reporting

**User Story:** As a Mist developer, I want clear error messages when assembly loading or type resolution fails, so that I can diagnose and fix configuration issues.

#### Acceptance Criteria

1. IF a referenced NuGet package cannot be found on any configured feed, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error identifying the package name and requested version.
2. IF an assembly file is missing or unreadable, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error identifying the assembly path, and SHALL continue compilation without the types from that assembly.
3. IF a type referenced in Mist source cannot be found in any loaded assembly or the Standard Library, THEN THE Semantic_Model SHALL emit the existing "undefined type" diagnostic with the source location of the reference.
4. IF the .NET SDK installation cannot be located, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error indicating that the SDK is required for the C# backend, and SHALL halt assembly loading (no SDK assemblies or NuGet package assemblies will be available).
5. IF a NuGet package has no compatible framework target for the project's configured target framework, THEN THE Assembly_Type_Provider SHALL emit a diagnostic with severity Error identifying the package name and the expected target framework.

### Requirement 16: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating assembly type provider interop, so that I can see how to consume .NET types from Mist and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AssemblyTypeProvider/Program.mist`.
2. The sample SHALL demonstrate the following usage patterns: importing CLR namespaces, constructing CLR types, calling instance and static methods, accessing properties, accessing indexers, using generic CLR types, handling nullable return values via pattern matching, passing Mist intrinsic types to CLR methods, and array interop with `Standard.Array<T, Length>`.
3. Each demonstrated permutation SHALL print labeled output to stdout in `Key=Value` format so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `AssemblyTypeProvider.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler, and its stdout output SHALL match the expected output specified in the Proof Code section exactly (same lines, same order, same values).

#### Proof Code

```mist
using Standard;
using System.Collections.Generic;

namespace AssemblyTypeProvider;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== AssemblyTypeProvider Sample ===");

        // PROOF: SDK assembly types are available without explicit package reference
        // PROOF: Generic CLR type (List<T>) resolves with Mist type argument
        var list = new List<int>();

        // PROOF: Instance method call on CLR type works
        list.Add(10);
        list.Add(20);
        list.Add(30);

        // PROOF: Property access on CLR type works
        Console.WriteLine($"Count={list.Count}");

        // PROOF: Indexer access on CLR type works
        Console.WriteLine($"Item0={list[0]}");

        // PROOF: CLR reference type return is T | void, requires narrowing
        // PROOF: Static method on CLR type works
        var dict = new Dictionary<string, int>();
        dict.Add("hello", 42);

        // PROOF: Out parameter interop — TryGetValue is now callable (out projected as ref)
        int val = 0;
        if (dict.TryGetValue("hello", ref val))
        {
            Console.WriteLine($"Found={val}");
        }

        // PROOF: String methods from System.String are available via Standard.String
        var greeting = "Hello, World";
        Console.WriteLine($"Length={greeting.Length}");
        Console.WriteLine($"Upper={greeting.ToUpper()}");

        // PROOF: System.Math static methods are callable
        var abs = Math.Abs(-5);
        Console.WriteLine($"Abs={abs}");

        // PROOF: Array interop — Standard.Array<T, Length> passes to CLR methods
        var arr = new int[3];
        arr[0] = 100;
        arr[1] = 200;
        arr[2] = 300;
        Console.WriteLine($"ArrLen={arr.Length}");
    }

    public static void Main()
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== AssemblyTypeProvider Sample ===
Count=3
Item0=10
Found=42
Length=12
Upper=HELLO, WORLD
Abs=5
ArrLen=3
```

### Requirement 17: Book Documentation

**User Story:** As a Mist developer reading the language book, I want a chapter explaining how to use .NET types from Mist, so that I understand the interop model, how to add NuGet packages, and how to handle nullable values from CLR APIs.

#### Acceptance Criteria

1. A new chapter SHALL be added to the book at `book/src/advanced/dotnet-interop.md` covering: how the Assembly Type Provider works, how to add NuGet packages in `mist.toml`, which SDK assemblies are available by default, how CLR types map to Mist types, how reference types project as `T | void` unions, and how to narrow before use.
2. The chapter SHALL be listed in `book/src/SUMMARY.md` under the "Advanced Features" section.
3. The chapter SHALL include at least two compilable snippet files in `book/snippets/advanced/dotnet-interop/` demonstrating: (a) importing and using a CLR collection type, and (b) handling a nullable return value via pattern matching. Each snippet SHALL be a complete, standalone Mist program (with `using` directives, a class, and a `Main` method) that compiles and runs without errors through the Mist CLI.
4. Each snippet SHALL have a corresponding `.cs` file generated by the SnippetEmitter tool.
5. The chapter SHALL explain which CLR features are hidden by the Visibility Filter (out params, ref params, ref structs) and why.
6. The chapter SHALL reference all Mist code examples using the `{{#tabs}}` / `{{#include}}` pattern (referencing files in `book/snippets/advanced/dotnet-interop/`) rather than inline Mist code blocks.

### Requirement 18: IEnumerable/IEnumerator Interop

**User Story:** As a Mist developer, I want to use `foreach` on .NET collection types like `List<T>` and `Dictionary<TKey, TValue>`, so that I can iterate over CLR collections using the same syntax as Mist-native enumerables.

#### Acceptance Criteria

1. WHEN a CLR type implements `System.Collections.Generic.IEnumerable<T>`, THE Assembly_Type_Provider SHALL project that type as implementing `Standard.IEnumerable<TElement>` (where `TElement` maps to the corresponding Mist type). The non-generic `System.Collections.IEnumerable` interface SHALL be ignored (not projected).
2. WHEN a CLR type implements `System.Collections.Generic.IEnumerator<T>`, THE Assembly_Type_Provider SHALL project that type as implementing `Standard.IEnumerator<TElement>`.
3. THE Assembly_Type_Provider SHALL synthesize the associated type slot `Enumerator` on projected `IEnumerable<TElement>` implementations by inspecting the CLR type's public `GetEnumerator()` method return type. IF the return type is a concrete type (e.g., `List<T>.Enumerator`), THEN THE Assembly_Type_Provider SHALL use that concrete type as the `Enumerator` slot. IF the return type is the interface `IEnumerator<T>` itself, THEN THE Assembly_Type_Provider SHALL use the projected `Standard.IEnumerator<TElement>` as the `Enumerator` slot.
4. WHEN Mist code uses `foreach` on a CLR collection type, THE Semantic_Model SHALL resolve the `IEnumerable<TElement>` implementation and infer the element type using the same logic as for Mist-native enumerables.
5. THE CSharp_Emitter SHALL emit `foreach` over CLR collection types as a plain C# `foreach` statement (the CLR runtime handles the enumerator protocol natively), rather than emitting explicit `GetEnumerator()`/`MoveNext()`/`Current` calls.
6. WHEN a CLR type implements multiple constructed `IEnumerable<T>` interfaces (e.g., both `IEnumerable<int>` and `IEnumerable<string>`), THE Assembly_Type_Provider SHALL project all implementations, and THE Semantic_Model SHALL require the `foreach` variable to have an explicit type annotation to disambiguate the element type.
