# Requirements Document

## Introduction

This feature closes the gap between Mist's structural `func<...>` type family and C#'s nominal delegate type system. Today, when the `TypeProjector` encounters a named CLR delegate (e.g., `RequestDelegate`, `Predicate<T>`, or `Func<T, TResult>`), it projects it into the Mist `Standard.Func<...>` type, erasing the original delegate's identity. The `CSharpTypeMap` then emits `System.Func<...>` or `System.Action<...>` — never the original delegate type name. This creates two failures:

1. **No implicit conversion from `func<...>` to a named CLR delegate type** — APIs that accept a specific named delegate (e.g., `RequestDelegate`) cannot receive a Mist `func<...>` or lambda because the emitted C# uses `System.Func<...>` where the target expects `RequestDelegate`.

2. **No path to `System.Delegate`** — CLR delegates inherit `MulticastDelegate → Delegate`, but Mist does not model this hierarchy. APIs accepting the base `System.Delegate` type (e.g., ASP.NET Core's `MapGet(string, Delegate)`) cannot receive any Mist function value.

This feature introduces:
- A `DelegateType` wrapper that preserves the original CLR delegate identity through projection and emission
- Implicit conversion from `func<...>` (and lambdas) to a signature-compatible named delegate type at call sites
- Modeling of the `System.Delegate` base type so that any delegate-typed or func-typed value can be passed where `Delegate` is expected
- Emission of proper delegate constructor invocations (e.g., `new RequestDelegate(lambda)`) when the target expects a named delegate

The scope is the type projection pipeline (`TypeProjector`, `AssemblyTypeProvider`), type checking (`SemanticModel` assignability and overload resolution), and C# emission (`CSharpTypeMap`, `CSharpEmitter`). No new Mist source-language syntax is introduced.

## Glossary

- **Delegate_Type**: A Mist type representation that wraps a projected `func<...>` type while preserving the original CLR delegate's fully qualified name, namespace, and generic type parameters. Carries a reference to the equivalent `func<...>` signature.
- **Func_Type**: The existing `Standard.Func<TParams..., TResult>` structural type family representing callable values in Mist.
- **Named_Delegate**: A CLR type that inherits from `System.MulticastDelegate` and has a specific name (e.g., `RequestDelegate`, `Predicate<T>`, `Comparison<T>`, `EventHandler`). Distinguished from the generic `System.Func<...>` / `System.Action<...>` families.
- **Base_Delegate_Type**: The Mist type representing `System.Delegate`, the root base type of all CLR delegates.
- **Type_Projector**: The component in `TypeProjector.cs` that converts CLR metadata into Mist `IType` instances.
- **Assembly_Type_Provider**: The `IAmbientTypeResolver` implementation in `AssemblyTypeProvider.cs` that reads CLR assembly metadata and projects types into the Mist semantic model.
- **Semantic_Model**: The Mist semantic model that performs name resolution, assignability checks, and overload resolution.
- **Type_Checker**: The `TypeCheckerAnalyzer` component that validates type correctness.
- **Emitter**: The `CSharpEmitter` backend component that generates C# source code.
- **Type_Map**: The `CSharpTypeMap` component that maps Mist types to their C# source representations.
- **Visibility_Filter**: The component in `VisibilityFilter.cs` that decides which CLR types and members are hidden from Mist.
- **Signature_Compatible**: Two delegate or function types are signature-compatible when they have the same number of parameters and each corresponding parameter type is assignable, and the return type is assignable.
- **Compiler**: The Mist compiler pipeline, encompassing lexing, parsing, semantic analysis, type checking, and code generation.

## Requirements

### Requirement 1: Named Delegate Type Preservation During Projection

**User Story:** As a Mist developer, I want CLR named delegates to retain their identity when projected into Mist's type system, so that the emitter can produce the correct C# delegate type name when calling APIs that require a specific delegate type.

#### Acceptance Criteria

1. WHEN the Type_Projector encounters a Named_Delegate (a CLR type whose base type is `System.MulticastDelegate` and whose fully qualified name is not `System.Func`, `System.Action`, or any generic arity of `System.Func` or `System.Action`), THE Type_Projector SHALL project it as a Delegate_Type that preserves the delegate's fully qualified CLR name, namespace, and generic type parameters alongside the equivalent `func<...>` signature derived from the delegate's `Invoke` method.
2. WHEN the Type_Projector encounters `System.Func<T1, ..., TN, TResult>`, `System.Action<T1, ..., TN>`, or the non-generic `System.Action`, THE Type_Projector SHALL project them directly as `Standard.Func<...>` types without a Delegate_Type wrapper.
3. WHEN a CLR method parameter or return type references a Named_Delegate, THE Assembly_Type_Provider SHALL use the Delegate_Type in the projected method signature rather than collapsing it to the bare `func<...>` type.
4. WHEN a Delegate_Type is used as a type annotation in Mist code (e.g., a variable declared with type `RequestDelegate`), THE Semantic_Model SHALL resolve the type to the projected Delegate_Type and SHALL treat a value of that Delegate_Type as assignable to the equivalent `func<...>` type.
5. WHEN the Type_Projector projects a generic Named_Delegate (e.g., `Predicate<T>`, `Comparison<T>`), THE Delegate_Type SHALL carry the open generic type parameters and, when constructed with concrete type arguments (e.g., `Predicate<int>`), SHALL produce a constructed Delegate_Type that preserves the fully qualified CLR name with substituted type arguments and carries the correspondingly constructed `func<...>` signature.
6. THE Delegate_Type SHALL expose the equivalent `func<...>` signature so that the Semantic_Model can compare it structurally with other function types, where structural comparison means two types are compatible if their parameter types and return type are pairwise identical.
7. WHEN Mist code passes a lambda expression or function reference to a parameter typed as a Delegate_Type, THE Semantic_Model SHALL accept the conversion provided the lambda's parameter types and return type are compatible with the Delegate_Type's `func<...>` signature.
8. WHEN the C# emitter produces code for a call site that passes a value to a Named_Delegate-typed parameter, THE emitter SHALL emit the delegate's fully qualified CLR type name (e.g., `System.Predicate<int>`, `Microsoft.AspNetCore.Http.RequestDelegate`) rather than a generic `Func<...>` or `Action<...>` type name.

### Requirement 2: Implicit Conversion from func to Named Delegate

**User Story:** As a Mist developer, I want to pass a lambda or `func<...>` value where a named CLR delegate type is expected, so that I can call APIs like `list.Sort(comparison)` or `app.Use(middleware)` without manual wrapping.

#### Acceptance Criteria

1. WHEN Mist code passes a lambda expression as an argument to a parameter whose type is a Delegate_Type, THE Type_Checker SHALL accept the argument if the lambda's inferred signature is Signature_Compatible with the Delegate_Type's equivalent `func<...>` signature.
2. WHEN Mist code passes a `func<...>`-typed value as an argument to a parameter whose type is a Delegate_Type, THE Type_Checker SHALL accept the argument if the `func<...>` type is Signature_Compatible with the Delegate_Type's equivalent `func<...>` signature.
3. WHEN Mist code assigns a lambda expression or `func<...>` value to a variable whose declared type is a Delegate_Type, THE Type_Checker SHALL accept the assignment if the signatures are Signature_Compatible.
4. WHEN Mist code returns a lambda expression or `func<...>` value from a method whose return type is a Delegate_Type, THE Type_Checker SHALL accept the return if the signatures are Signature_Compatible.
5. IF a lambda or `func<...>` value has a signature that is not Signature_Compatible with the target Delegate_Type (differing parameter count or incompatible parameter/return types), THEN THE Type_Checker SHALL produce an error-severity diagnostic indicating the signature mismatch, identifying the expected delegate signature and the actual function signature.
6. WHEN overload resolution considers a method overload whose parameter is a Delegate_Type, THE Semantic_Model SHALL include a `func<...>`-typed argument or lambda as a valid candidate for that parameter if the signatures are Signature_Compatible.
7. WHEN Mist code uses a method group reference (a method name used as a value) as an argument to a parameter whose type is a Delegate_Type, THE Type_Checker SHALL accept the argument if the method's signature is Signature_Compatible with the Delegate_Type's equivalent `func<...>` signature.
8. WHEN a lambda expression with implicit parameter types appears in a position whose target type is a Delegate_Type, THE Type_Checker SHALL infer the lambda's parameter types and return type from the Delegate_Type's equivalent `func<...>` signature (target-type inference), applying the same inference rules used when the target type is a `func<...>`.
9. IF a method group reference contains multiple overloads and exactly one overload is Signature_Compatible with the target Delegate_Type, THEN THE Type_Checker SHALL select that overload for the conversion. IF zero or more than one overload is Signature_Compatible, THEN THE Type_Checker SHALL produce an error-severity diagnostic indicating the ambiguity or incompatibility.

### Requirement 3: System.Delegate Base Type Modeling

**User Story:** As a Mist developer, I want to pass any delegate-typed or func-typed value where `System.Delegate` is expected, so that I can use APIs like ASP.NET Core's `MapGet(string, Delegate)` that accept the base delegate type.

#### Acceptance Criteria

1. THE Type_Projector SHALL project `System.Delegate` as the Base_Delegate_Type in Mist's type system. THE Type_Projector SHALL also project `System.MulticastDelegate` as an intermediate type between Named_Delegates and Base_Delegate_Type, but Mist code SHALL NOT be required to reference `System.MulticastDelegate` directly — it is an implementation detail of the hierarchy.
2. WHEN Mist code passes a Delegate_Type value as an argument to a parameter typed as Base_Delegate_Type, THE Type_Checker SHALL accept the argument (named delegates are assignable to the base Delegate type).
3. WHEN Mist code passes a `func<...>` value or lambda expression as an argument to a parameter typed as Base_Delegate_Type, THE Type_Checker SHALL accept the argument (all function values are convertible to the base Delegate type).
4. WHEN Mist code passes a method group reference as an argument to a parameter typed as Base_Delegate_Type, THE Type_Checker SHALL accept the argument.
5. WHEN overload resolution considers a method overload whose parameter is Base_Delegate_Type, THE Semantic_Model SHALL include any `func<...>`-typed value, Delegate_Type value, lambda expression, or method group as a valid candidate for that parameter.
6. THE Semantic_Model SHALL NOT permit invocation on a value typed as Base_Delegate_Type (the base Delegate type is not directly callable without a cast to a concrete delegate or func type).
7. IF Mist code attempts to invoke a value typed as Base_Delegate_Type, THEN THE Type_Checker SHALL produce an error-severity diagnostic indicating that `System.Delegate` is not directly invocable and suggesting a cast to a specific delegate or func type.
8. WHEN Mist code accesses members defined on `System.Delegate` (e.g., `GetType()`, `DynamicInvoke(...)`, `Method`, `Target`), THE Semantic_Model SHALL resolve those member accesses against the projected members of `System.Delegate`.

### Requirement 4: C# Emission of Named Delegate Types

**User Story:** As a compiler developer, I want the C# emitter to produce the correct CLR delegate type name in the generated C# code, so that the downstream C# compiler accepts the code without delegate-type mismatches.

#### Acceptance Criteria

1. WHEN the Type_Map encounters a Delegate_Type, THE Type_Map SHALL emit the delegate's fully qualified CLR name (e.g., `Microsoft.AspNetCore.Http.RequestDelegate`, `System.Predicate<int>`) rather than the equivalent `System.Func<...>` or `System.Action<...>`.
2. WHEN the Type_Map encounters a generic Delegate_Type with concrete type arguments (e.g., `Predicate<int>`), THE Type_Map SHALL emit the fully qualified name with the C# type arguments applied.
3. WHEN the Emitter encounters an implicit conversion from a lambda or `func<...>` to a Delegate_Type at a call site, THE Emitter SHALL emit a `new DelegateName(lambda)` constructor invocation wrapping the lambda in the target delegate type (e.g., `new RequestDelegate(ctx => ...)`) to satisfy C#'s type system.
4. WHEN the Emitter encounters an implicit conversion from a lambda or `func<...>` to Base_Delegate_Type, THE Emitter SHALL emit the lambda as-is without a delegate wrapper, relying on C#'s own implicit conversion from lambda to `Delegate` (available in C# 11+).
5. WHEN the Type_Map encounters Base_Delegate_Type, THE Type_Map SHALL emit `System.Delegate`.
6. WHEN a Delegate_Type appears in a generic type argument position (e.g., `List<RequestDelegate>`), THE Type_Map SHALL emit the fully qualified delegate name in that position.
7. WHEN the Emitter encounters an implicit conversion from a method group to a Delegate_Type at a call site, THE Emitter SHALL emit a `new DelegateName(methodGroup)` constructor invocation wrapping the method group in the target delegate type.
8. WHEN the Emitter encounters an implicit conversion from a Delegate_Type value to a `func<...>`-typed target (e.g., assignment to a `func<...>` variable), THE Emitter SHALL emit the value as-is without unwrapping, relying on C#'s implicit delegate-to-compatible-delegate conversion.

### Requirement 5: Delegate Visibility and Filtering

**User Story:** As a Mist developer, I want CLR delegate types to be visible and usable in Mist unless they reference unsupported types, so that the delegate projection works consistently with the existing visibility rules.

#### Acceptance Criteria

1. THE Visibility_Filter SHALL NOT hide a Named_Delegate on the basis that it is a delegate type; Named_Delegates are visible by default.
2. IF a Named_Delegate's `Invoke` method signature references a type that is excluded by the Visibility_Filter (e.g., a pointer type) in any parameter position or the return position, or if the `Invoke` method has a `ref readonly` return type, THEN THE Visibility_Filter SHALL hide that delegate type.
3. WHEN a CLR method's parameter type or return type is a visible Named_Delegate, THE Assembly_Type_Provider SHALL project that parameter or return type using the Delegate_Type.
4. WHEN a CLR method's parameter type or return type is a hidden Named_Delegate, THE Visibility_Filter SHALL hide the overload containing that type reference, consistent with existing per-overload filtering rules.
5. THE Visibility_Filter SHALL NOT hide `System.Delegate` or `System.MulticastDelegate` as types (they serve as the delegate hierarchy root).
6. WHEN a CLR method's parameter type or return type is `System.Delegate` or `System.MulticastDelegate`, THE Visibility_Filter SHALL NOT hide that overload on the basis of the delegate base type reference.

### Requirement 6: Assignability Between Delegate Types

**User Story:** As a Mist developer, I want clear rules about when one delegate type can be assigned to another, so that the type system is predictable when working with multiple delegate types that share a signature.

#### Acceptance Criteria

1. WHEN two distinct Named_Delegates have Signature_Compatible `func<...>` equivalents (e.g., `RequestDelegate` and a hypothetical `MiddlewareDelegate` both being `func<HttpContext, Task>`), THE Type_Checker SHALL NOT implicitly convert between them — named delegates are nominally distinct from each other.
2. WHEN Mist code assigns a `func<...>` value to a variable typed as a Delegate_Type, THE Type_Checker SHALL accept the assignment if the signatures are Signature_Compatible (func-to-named-delegate conversion).
3. WHEN Mist code assigns a Delegate_Type value to a variable typed as `func<...>`, THE Type_Checker SHALL accept the assignment if the Delegate_Type's equivalent `func<...>` signature is Signature_Compatible with the target `func<...>` type (named-delegate-to-func conversion).
4. WHEN Mist code assigns any Delegate_Type value to a variable typed as Base_Delegate_Type, THE Type_Checker SHALL accept the assignment.
5. WHEN Mist code assigns a `func<...>` value to a variable typed as Base_Delegate_Type, THE Type_Checker SHALL accept the assignment.
6. IF Mist code assigns a Delegate_Type value to a variable typed as a different Delegate_Type (both named, both distinct), THEN THE Type_Checker SHALL produce an error-severity diagnostic identifying the source delegate type name and the target delegate type name and indicating that the two named delegate types are not assignment-compatible even if their signatures match.
7. IF Mist code assigns a Base_Delegate_Type value to a variable typed as a specific Delegate_Type or typed as `func<...>`, THEN THE Type_Checker SHALL produce an error-severity diagnostic indicating that `System.Delegate` cannot be implicitly narrowed to a specific delegate or function type.

### Requirement 7: Overload Resolution with Delegate Parameters

**User Story:** As a Mist developer, I want overload resolution to correctly select the best overload when multiple overloads accept different delegate types, so that method calls are unambiguous.

#### Acceptance Criteria

1. WHEN a method has an overload accepting a specific Delegate_Type and another overload accepting `func<...>` with the same signature, THE Semantic_Model SHALL prefer the Delegate_Type overload when the argument is already typed as that Delegate_Type, SHALL prefer the `func<...>` overload when the argument is a bare `func<...>` value, and SHALL prefer the Delegate_Type overload when the argument is a lambda that is Signature_Compatible with the Delegate_Type.
2. WHEN a method has an overload accepting a specific Delegate_Type and another overload accepting Base_Delegate_Type, THE Semantic_Model SHALL prefer the more specific Delegate_Type overload when the argument is a lambda or `func<...>` that is Signature_Compatible with the Delegate_Type.
3. WHEN a method has a single overload accepting a Delegate_Type parameter and the call site provides a Signature_Compatible lambda, THE Semantic_Model SHALL resolve to that overload without ambiguity.
4. WHEN a method has overloads accepting different Delegate_Types (e.g., one accepting `RequestDelegate` and another accepting `Predicate<HttpContext>`), THE Semantic_Model SHALL resolve based on the argument's type: a Delegate_Type-typed argument resolves to the matching overload; a `func<...>`-typed argument resolves to the overload whose Delegate_Type is Signature_Compatible; a lambda that is Signature_Compatible with exactly one overload resolves to that overload.
5. IF a lambda is Signature_Compatible with multiple Delegate_Type overloads and no other disambiguation is available, THEN THE Semantic_Model SHALL produce an error-severity diagnostic indicating the ambiguity, identifying the conflicting delegate type names.
6. WHEN a method group is used as an argument and the method group has a single method whose signature is Signature_Compatible with exactly one Delegate_Type overload, THE Semantic_Model SHALL resolve to that overload following the same rules as lambdas.

### Requirement 8: Invocation of Delegate-Typed Values

**User Story:** As a Mist developer, I want to invoke a value typed as a Named_Delegate using the same call syntax as `func<...>` values, so that delegates and functions are uniformly callable.

#### Acceptance Criteria

1. WHEN a variable's type is a Delegate_Type and an invocation suffix (parenthesized argument list) follows it, THE Type_Checker SHALL type-check the invocation against the Delegate_Type's equivalent `func<...>` signature (parameter types and return type), and the resulting expression's type SHALL be the return type of that signature.
2. WHEN a Delegate_Type-typed value is invoked with correct arguments, THE Emitter SHALL emit a standard C# delegate invocation (`variable(args)`).
3. IF a Delegate_Type-typed value is invoked with wrong argument count, THEN THE Type_Checker SHALL produce a diagnostic error reporting the expected parameter count versus the actual argument count. IF a Delegate_Type-typed value is invoked with incompatible argument types, THEN THE Type_Checker SHALL produce a diagnostic error identifying the parameter position and the type mismatch.
4. WHEN a Delegate_Type-typed value is used in a position expecting its equivalent `func<...>` type (e.g., passed as an argument to a parameter typed as the matching `func<...>`, or assigned to a `func<...>` variable), THE Type_Checker SHALL accept it without explicit conversion.

### Requirement 9: Book Documentation

**User Story:** As a Mist developer, I want the language book to document delegate-func conversion and `System.Delegate` interop, so that I can learn how to work with CLR APIs that use named delegates.

#### Acceptance Criteria

1. THE book SHALL include a section in the .NET Interop chapter at `book/src/advanced/dotnet-interop.md` covering delegate-func conversion with at least 2 compiled code snippets stored under `book/snippets/advanced/dotnet-interop/`.
2. THE book section SHALL explain that named CLR delegates are visible as distinct types in Mist while being implicitly convertible from signature-compatible `func<...>` values and lambdas.
3. THE book section SHALL explain the relationship between `func<...>`, named delegates, and `System.Delegate`, stating explicitly which conversions are implicit (func-to-named-delegate, func-to-System.Delegate, named-delegate-to-func, any-delegate-to-System.Delegate) and which are not allowed (between two distinct named delegates even when signatures match).
4. THE book section SHALL include an ASP.NET Core example demonstrating `app.MapGet("/", () => "Hello!")` to show the `Delegate` base type conversion in practice.
5. THE book section SHALL document that named delegates are nominally distinct from each other even when their signatures are identical, and that attempting to assign one named delegate to a differently-named delegate produces a type error.
6. THE book section SHALL document that a value typed as a Delegate_Type is directly invocable using standard call syntax, just like `func<...>` values.
7. All code examples SHALL be compiled snippets following the Book Snippet Policy.
8. THE book's `SUMMARY.md` already references the .NET Interop chapter; no new entry SHALL be added for this section since it is content within the existing chapter.

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating delegate-func conversion, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DelegateFuncConversion/Program.mist` with a corresponding `samples/DelegateFuncConversion/mist.toml` project file whose `name` field is set to `"DelegateFuncConversion"`.
2. The sample's `mist.toml` SHALL declare a `[framework_references]` entry whose key is `Microsoft.AspNetCore.App` with the boolean value `true`.
3. The sample SHALL demonstrate the following conversion patterns defined in the preceding requirements:
   a. Passing a lambda to a parameter expecting a Named_Delegate (`Predicate<int>` via `List<int>.FindAll`)
   b. Passing a method group to a parameter expecting a Named_Delegate (`Predicate<int>` via `List<int>.FindAll` with `IsPositive`)
   c. Passing a lambda to a parameter expecting a Named_Delegate (`Comparison<int>` via `List<int>.Sort` with `CompareDescending`)
   d. Assigning a `func<int, bool>` value to a `Predicate<int>`-typed variable
   e. Assigning a `Predicate<int>`-typed value to a `func<int, bool>`-typed variable
   f. Assigning a `func<...>` value to a `System.Delegate`-typed variable
   g. Invoking a `Predicate<int>`-typed value directly with call syntax
   h. Passing a lambda to a parameter expecting `System.Delegate` (ASP.NET Core `MapGet` pattern)
4. Each demonstrated pattern SHALL print exactly one labeled output line to stdout so correctness is observable, where each line follows the format `key=value` with no leading or trailing whitespace.
5. The sample SHALL include a `public static void RunDemo()` method that exercises all demonstrated patterns.
6. The `DelegateFuncConversion` sample SHALL NOT be wired into `samples/AllFeatures` (it is excluded from the full-suite baseline run because it requires the `Microsoft.AspNetCore.App` framework reference).
7. A dedicated integration test SHALL compile and run the `samples/DelegateFuncConversion` project end-to-end — resolving the `Microsoft.AspNetCore.App` framework reference — and SHALL assert the process exits with code 0 and that stdout matches the Proof Code expected output byte-for-byte. This test MAY spawn a process to run the compiled sample (an approved exception to the Test Process Spawning Policy for this single sample) and SHALL configure UTF-8 stdout/stderr encoding per the Test UTF-8 Encoding Policy.
8. The sample SHALL use Allman braces, placing every opening brace on its own line, and SHALL prefer `var` for local declarations except where an explicit type is required to demonstrate assignability.
9. WHEN the sample is compiled and run using the Mist compiler, THE compilation SHALL produce zero error diagnostics, the process SHALL exit with code 0, and the stdout output SHALL match the expected output in the Proof Code section byte-for-byte (same lines, same order, same values).

#### Proof Code

```mist
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace DelegateFuncConversion;

public class Program
{
    // PROOF: A method whose signature matches Predicate<int> — used for method group conversion
    public static bool IsPositive(int x)
    {
        return x > 0;
    }

    // PROOF: A method whose signature matches Comparison<int>
    public static int CompareDescending(int a, int b)
    {
        return b - a;
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== DelegateFuncConversion Sample ===");

        // PROOF: Lambda → Named_Delegate (Predicate<int>)
        // The List<int>.FindAll method expects Predicate<int>, not func<int, bool>.
        // This proves implicit conversion from lambda to a named delegate type.
        var numbers = new List<int> { -2, -1, 0, 1, 2, 3 };
        var positives = numbers.FindAll(x => x > 0);
        Console.WriteLine($"positives.Count={positives?.Count}");

        // PROOF: Method group → Named_Delegate (Predicate<int>)
        // Passing a method group where Predicate<int> is expected.
        var alsoPositives = numbers.FindAll(IsPositive);
        Console.WriteLine($"alsoPositives.Count={alsoPositives?.Count}");

        // PROOF: Lambda → Named_Delegate (Comparison<int>)
        // List<T>.Sort(Comparison<T>) expects a named delegate, not func<int, int, int>.
        var sortable = new List<int> { 3, 1, 4, 1, 5 };
        sortable.Sort(CompareDescending);
        Console.WriteLine($"sortedFirst={sortable[0]}");

        // PROOF: func<...> value → Delegate_Type variable assignment
        // A func<int, bool> value can be assigned to a Predicate<int>-typed variable.
        func<int, bool> isEven = x => x % 2 == 0;
        System.Predicate<int> predicate = isEven;
        var evens = numbers.FindAll(predicate);
        Console.WriteLine($"evens.Count={evens?.Count}");

        // PROOF: Delegate_Type value → func<...> variable assignment
        // A Predicate<int>-typed value can be assigned back to a func<int, bool> variable.
        func<int, bool> funcFromDelegate = predicate;
        Console.WriteLine($"funcFromDelegate(4)={funcFromDelegate(4)}");

        // PROOF: Any func/delegate → System.Delegate base type
        // A lambda can be assigned to a System.Delegate-typed variable.
        System.Delegate baseDelegate = isEven;
        Console.WriteLine($"baseDelegateType={baseDelegate.GetType().Name}");

        // PROOF: Invoking a Delegate_Type-typed value
        // A Predicate<int>-typed variable is directly invocable.
        Console.WriteLine($"predicate(6)={predicate(6)}");
        Console.WriteLine($"predicate(7)={predicate(7)}");

        // PROOF: Lambda → System.Delegate parameter (ASP.NET Core MapGet pattern)
        // WebApplication.CreateBuilder().Build().MapGet("/", Delegate) accepts System.Delegate.
        // This proves that a lambda returning string is convertible to System.Delegate.
        var builder = WebApplication.CreateBuilder();
        var app = builder?.Build();
        app?.MapGet("/hello", () => "Hello from Mist!");
        Console.WriteLine($"mapGetRegistered=true");

        Console.WriteLine("=== DelegateFuncConversion Done ===");
    }

    public static void Main()
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== DelegateFuncConversion Sample ===
positives.Count=3
alsoPositives.Count=3
sortedFirst=5
evens.Count=3
funcFromDelegate(4)=True
baseDelegateType=Func`2
predicate(6)=True
predicate(7)=False
mapGetRegistered=true
=== DelegateFuncConversion Done ===
```

### Requirement 11: Distributed Steering File Updates

**User Story:** As a Mist developer relying on the bundled steering files for language-aware assistance, I want the distributed .NET Interop steering to reflect delegate-func conversion behavior, so that Kiro does not give guidance that contradicts how the compiler actually works.

#### Acceptance Criteria

1. THE distributed steering file at `distribute/steering/mist-interop.md` SHALL be updated to document that named CLR delegates (those not in the `System.Func` or `System.Action` families) are projected as distinct Delegate_Type values preserving their CLR identity, while remaining implicitly convertible from Signature_Compatible `func<...>` values, lambdas, and method groups, and that Delegate_Type values are implicitly convertible back to their equivalent `func<...>` type and are invocable using the same call syntax as `func<...>` values.
2. THE distributed steering file SHALL document that `System.Func<...>` and `System.Action<...>` continue to project directly as `func<...>` types without a Delegate_Type wrapper, and that `System.Delegate` is modeled as the Base_Delegate_Type to which any delegate-typed or `func<...>`-typed value is assignable, enabling patterns like `app.MapGet("/", () => "Hello!")`.
3. THE distributed steering file SHALL document that two distinct named delegates are NOT implicitly convertible to each other even when their signatures match (delegates are nominally distinct from each other).
4. THE distributed steering file SHALL update or remove any existing content that states or implies named CLR delegates are collapsed to bare `func<...>` types, so that no section of the file contradicts the Delegate_Type projection behavior.
5. THE distributed steering file SHALL retain its `MIST-MANAGED-STEERING` marker comment and its front matter so that `mist steering` continues to manage it as a bundled file.
6. WHERE another distributed steering file (e.g., `distribute/steering/mist-language.md`) contains a statement that contradicts the delegate-func conversion behavior documented by this requirement, THAT file SHALL be updated for consistency.
