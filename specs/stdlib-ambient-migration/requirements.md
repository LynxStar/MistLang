# Requirements Document

## Introduction

Today, `Console`, `Math`, and `Convert` are **ambient types** — synthesized on-demand by `CSharpAmbientTypeResolver` when user code has `using System;` and references them. This is a legacy mechanism from before the Mist Standard Library existed. Now that the stdlib infrastructure is in place (with `String`, `Array`, numeric primitives, etc. already declared as intrinsic types in `Source/Standard/Bootstrap/`), these three types should follow the same pattern: proper `.mist` declarations in the `Standard` namespace, marked `intrinsic`, with the C# backend mapping them to their CLR equivalents via `CSharpTypeMap`.

This migration moves Console, Math, and Convert from the ambient type system to the Standard Library, unifying how all "external" types are represented in the semantic model. The ambient type system itself remains in place for future use.

## Glossary

- **Ambient_Type**: A type injected per-backend by an `IAmbientTypeResolver`, representing an external runtime facility surfaced into Mist.
- **Stdlib_Console**: The `Console` type declared in `Source/Standard/Bootstrap/Console.mist` under the `Standard` namespace, marked with a type-level `intrinsic` modifier.
- **Stdlib_Math**: The `Math` type declared in `Source/Standard/Bootstrap/Math.mist` under the `Standard` namespace, marked with a type-level `intrinsic` modifier.
- **Stdlib_Convert**: The `Convert` type declared in `Source/Standard/Bootstrap/Convert.mist` under the `Standard` namespace, marked with a type-level `intrinsic` modifier.
- **CSharp_Type_Map**: The `CSharpTypeMap` class that maps fully-qualified stdlib type names to their C# source-code representations.
- **CSharp_Ambient_Type_Resolver**: The `CSharpAmbientTypeResolver` class that currently synthesizes Console, Math, and Convert as ambient types.
- **IAmbientTypeResolver**: The interface contract for ambient type resolution, defined in `Source/Mist/Semantics/Contracts/IAmbientTypeResolver.cs`.
- **Backend_Backed_Type**: A Standard_Library type whose storage layout and intrinsic operations are provided by the backend, identified by a type-level `intrinsic` marker.
- **Backing_Type_Map**: The per-backend mapping from each Backend_Backed_Type to its target-language backing type (e.g., `Standard.Console → System.Console`).
- **Standard_Namespace**: The namespace `Standard`, which contains all stdlib types.
- **Intrinsic_Marker**: The `intrinsic` modifier keyword applied to type and member declarations to mark them as backend-provided.

## Requirements

### Requirement 1: Console Stdlib Type Declaration

**User Story:** As a Mist developer, I want `Console` to be a proper stdlib type declared in Mist source, so that it follows the same pattern as all other stdlib types and is discoverable in the stdlib source tree.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Console` as an `intrinsic public class` in the `Standard` namespace, in a file at `Source/Standard/Bootstrap/Console.mist`.
2. THE Stdlib_Console SHALL declare an intrinsic static method `WriteLine` with a `String` parameter and `Void` return type.
3. THE Stdlib_Console SHALL declare an intrinsic static method `Write` with a `String` parameter and `Void` return type.
4. EACH method declared on Stdlib_Console SHALL carry a member-level Intrinsic_Marker.

### Requirement 2: Math Stdlib Type Declaration

**User Story:** As a Mist developer, I want `Math` to be a proper stdlib type declared in Mist source, so that mathematical operations are discoverable and follow the same pattern as other stdlib types.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Math` as an `intrinsic public class` in the `Standard` namespace, in a file at `Source/Standard/Bootstrap/Math.mist`.
2. THE Stdlib_Math SHALL declare an intrinsic static method `Abs` with an `Int32` parameter and `Int32` return type.
3. THE Stdlib_Math SHALL declare an intrinsic static method `Max` with two `Int32` parameters and `Int32` return type.
4. THE Stdlib_Math SHALL declare an intrinsic static method `Min` with two `Int32` parameters and `Int32` return type.
5. EACH method declared on Stdlib_Math SHALL carry a member-level Intrinsic_Marker.

### Requirement 3: Convert Stdlib Type Declaration

**User Story:** As a Mist developer, I want `Convert` to be a proper stdlib type declared in Mist source, so that type conversion operations are discoverable and follow the same pattern as other stdlib types.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Convert` as an `intrinsic public class` in the `Standard` namespace, in a file at `Source/Standard/Bootstrap/Convert.mist`.
2. THE Stdlib_Convert SHALL declare an intrinsic static method `ToInt32` with a `String` parameter and `Int32` return type.
3. EACH method declared on Stdlib_Convert SHALL carry a member-level Intrinsic_Marker.

### Requirement 4: CSharpTypeMap Entries

**User Story:** As a compiler developer, I want the C# backend to know how to emit these three new stdlib types as their CLR equivalents, so that generated C# code references `System.Console`, `System.Math`, and `System.Convert` correctly.

#### Acceptance Criteria

1. THE CSharp_Type_Map SHALL contain an entry mapping `Standard.Console` to `System.Console`.
2. THE CSharp_Type_Map SHALL contain an entry mapping `Standard.Math` to `System.Math`.
3. THE CSharp_Type_Map SHALL contain an entry mapping `Standard.Convert` to `System.Convert`.
4. WHEN the CSharp_Emitter encounters a reference to Stdlib_Console, Stdlib_Math, or Stdlib_Convert, THE emitter SHALL emit the fully-qualified CLR type name from the Backing_Type_Map (e.g., `System.Console.WriteLine(...)` for a call to `Console.WriteLine`).

### Requirement 5: Ambient Type Resolver Cleanup

**User Story:** As a compiler developer, I want the ambient type resolver to no longer register Console, Math, or Convert, so that these types are resolved exclusively through the stdlib.

#### Acceptance Criteria

1. THE CSharp_Ambient_Type_Resolver SHALL remove the registrations for Console, Math, and Convert from the `System` namespace.
2. THE CSharp_Ambient_Type_Resolver SHALL report `HasNamespace("System")` as `false` (since no types remain registered in that namespace).
3. THE `IAmbientTypeResolver` interface and `CSharpAmbientTypeResolver` class SHALL remain in the codebase for future use.
4. THE `using System;` directive SHALL no longer resolve Console, Math, or Convert. Programs that previously used `using System;` to access these types SHALL use `using Standard;` instead.

### Requirement 6: Sample Migration

**User Story:** As a Mist developer, I want all sample programs to use `using Standard;` instead of `using System;` for accessing Console, Math, and Convert, so that samples reflect the current stdlib-based approach.

#### Acceptance Criteria

1. ALL sample `.mist` files that currently use `using System;` to access Console, Math, or Convert SHALL be updated to use `using Standard;` instead.
2. THE `samples/AmbientTypes/` directory SHALL be deleted, as it is obsolete once these types move to stdlib.
3. THE `samples/AllFeatures/Program.mist` SHALL remove the call to `AmbientTypes.Program.RunDemo()`.
4. THE `samples/AllFeatures/Program.mist` SHALL use `using Standard;` instead of `using System;`.
5. THE `samples/Classes/Point.mist` SHALL use `using Standard;` instead of `using System;` (it references `Math.Abs`).
6. THE `samples/Primitives/StringUtils.mist` SHALL use `using Standard;` instead of `using System;` (it references `Convert.ToInt32`) and SHALL replace `Convert.ToString(doubled)` with string interpolation (`$"{doubled}"`).
7. ALL other sample files that use `using System;` solely for Console access SHALL be updated to `using Standard;`.
8. ALL updated samples SHALL compile and run without errors using the Mist compiler after migration.

### Requirement 7: Test Migration

**User Story:** As a compiler developer, I want the test suite to reflect the new stdlib-based architecture for Console, Math, and Convert, so that tests validate the correct behavior.

#### Acceptance Criteria

1. THE `AmbientTypePropertyTests` test class SHALL be removed, as the ambient type registrations it validates (Console, Math, Convert) no longer exist.
2. THE `CSharpAmbientTypeResolverStdlibTests` test class SHALL be removed, as the registrations it validates no longer exist.
3. THE test suite SHALL include tests verifying that Console, Math, and Convert resolve correctly as stdlib types when `using Standard;` is in scope.
4. THE test suite SHALL include a test verifying that `using System;` no longer resolves Console, Math, or Convert.
5. THE test suite SHALL include tests verifying that `CSharpTypeMap` correctly maps `Standard.Console`, `Standard.Math`, and `Standard.Convert` to their CLR equivalents.

### Requirement 8: Backward Compatibility

**User Story:** As a Mist developer, I want the migration to preserve all existing program behavior, so that programs using Console, Math, and Convert continue to work identically after switching from `using System;` to `using Standard;`.

#### Acceptance Criteria

1. WHEN a program uses `using Standard;` and references `Console.WriteLine(string)`, THE compiled output SHALL behave identically to the previous `using System;` behavior.
2. WHEN a program uses `using Standard;` and references `Math.Abs(int)`, `Math.Max(int, int)`, or `Math.Min(int, int)`, THE compiled output SHALL behave identically to the previous `using System;` behavior.
3. WHEN a program uses `using Standard;` and references `Convert.ToInt32(string)`, THE compiled output SHALL behave identically to the previous `using System;` behavior.
4. THE Stdlib_Console, Stdlib_Math, and Stdlib_Convert types SHALL declare the subset of members actually used by sample programs, matching the parameter types and return types of the corresponding former ambient type definitions in `CSharpAmbientTypeResolver`.
5. THE semantic model SHALL represent Stdlib_Console, Stdlib_Math, and Stdlib_Convert with `SymbolOrigin.Source` (since they are now declared in `.mist` source files), not `SymbolOrigin.Backend`.
