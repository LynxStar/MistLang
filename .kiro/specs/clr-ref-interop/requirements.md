# Requirements Document

## Introduction

The Assembly Type Provider's `VisibilityFilter` currently excludes CLR methods that have `ref` parameters and `ref` returns. This is overly conservative because Mist has full support for `ref` parameters, `ref` returns, and `ref` locals (demonstrated in the `RefParameters` sample). This feature relaxes the visibility filter to allow `ref` parameters, `in` parameters (projected as `ref`), and `ref` returns through to Mist, while keeping `out` parameters filtered. The `TypeProjector` is updated to project these parameter/return modifiers into the Mist type system correctly.

## Glossary

- **Visibility_Filter**: The component in `VisibilityFilter.cs` that determines which CLR type members are hidden from Mist because they use unsupported CLR features.
- **Type_Projector**: The component in `TypeProjector.cs` that converts CLR metadata into Mist `IType` and `ISymbol` instances, including parameter and return type projection.
- **CLR_Ref_Parameter**: A CLR method parameter declared with the `ref` modifier (`ParameterType.IsByRef == true`, `IsOut == false`, `IsIn == false`). Semantically equivalent to Mist's bare `ref` — the caller passes a reference that the callee may read and write.
- **CLR_In_Parameter**: A CLR method parameter declared with the `in` modifier (`ParameterType.IsByRef == true`, `IsIn == true`). A ref-readonly parameter that blocks reassignment but does not prevent mutation through the reference for class types. Weaker than Mist's `readonly ref`.
- **CLR_Out_Parameter**: A CLR method parameter declared with the `out` modifier (`ParameterType.IsByRef == true`, `IsOut == true`). Requires the callee to assign before returning. No Mist equivalent exists.
- **CLR_Ref_Return**: A CLR method or property whose return type is by-ref (`ReturnType.IsByRef == true`). The method returns a reference to a storage location.
- **CLR_Ref_Readonly_Return**: A CLR method or property whose return type is by-ref and carries the `IsReadOnlyAttribute` on the return parameter. The method returns a readonly reference.
- **Mist_Ref_Parameter**: A Mist parameter declared with the `ref` keyword. Bare `ref` in Mist has no ownership semantics — it is a plain mutable reference.
- **Mist_Ref_Return**: A Mist method return declared with `ref`. The method returns a reference to a storage location.

## Requirements

### Requirement 1: Allow CLR Ref Parameters Through Visibility Filter

**User Story:** As a Mist developer, I want to call CLR methods that have `ref` parameters (like `Interlocked.Exchange(ref int, int)` and `Array.Resize(ref T[], int)`), so that I can use the full .NET API surface for reference-passing patterns.

#### Acceptance Criteria

1. WHEN a CLR method or constructor has `ref` parameters (where `ParameterType.IsByRef == true`, `IsOut == false`, `IsIn == false`), THE Visibility_Filter SHALL NOT exclude that method or constructor from the Mist projection.
2. WHEN a CLR method has both `ref` parameters and regular value parameters, THE Visibility_Filter SHALL NOT exclude that method from the Mist projection.
3. WHEN a CLR method has `out` parameters, THE Visibility_Filter SHALL continue to exclude that method from the Mist projection (no change to existing behavior).

### Requirement 2: Project CLR Ref Parameters as Mist Ref Parameters

**User Story:** As a Mist developer, I want CLR `ref` parameters to appear as Mist `ref` parameters in projected method signatures, so that I pass arguments with `ref` at the call site just like I do for Mist-defined methods.

#### Acceptance Criteria

1. WHEN a CLR method parameter is a `ref` parameter (`ParameterType.IsByRef == true`, `IsOut == false`, `IsIn == false`), THE Type_Projector SHALL project it as a Mist `ref` parameter in the method's signature.
2. WHEN a CLR `ref` parameter's element type is a CLR intrinsic (e.g., `System.Int32`), THE Type_Projector SHALL apply intrinsic type substitution to the element type (e.g., `ref System.Int32` becomes `ref Standard.Int32`).
3. WHEN Mist code calls a projected method with a `ref` parameter, THE Semantic_Model SHALL require the caller to pass the argument with the `ref` keyword.
4. WHEN Mist code calls a projected method with a `ref` parameter, THE CSharp_Emitter SHALL emit the `ref` keyword at the call site in the generated C# code.

### Requirement 3: Project CLR In Parameters as Mist Ref Parameters

**User Story:** As a Mist developer, I want CLR `in` parameters to appear as Mist `ref` parameters, so that I can pass references to methods that accept `in` parameters without the filter hiding them.

#### Acceptance Criteria

1. WHEN a CLR method parameter is an `in` parameter (`ParameterType.IsByRef == true`, `IsIn == true`), THE Visibility_Filter SHALL NOT exclude that method from the Mist projection.
2. WHEN a CLR method parameter is an `in` parameter, THE Type_Projector SHALL project it as a Mist `ref` parameter (not pass-by-value, not `readonly ref`).
3. WHEN a CLR `in` parameter's element type is a CLR intrinsic, THE Type_Projector SHALL apply intrinsic type substitution to the element type.
4. WHEN Mist code calls a projected method with an `in`-to-`ref` parameter, THE CSharp_Emitter SHALL emit the `in` keyword (not `ref`) at the call site in the generated C# code, preserving the original CLR calling convention.

### Requirement 4: Allow CLR Ref Returns Through Visibility Filter

**User Story:** As a Mist developer, I want to call CLR methods and access CLR properties that return mutable references, so that I can use APIs like `CollectionsMarshal.GetValueRefOrNullRef`.

#### Acceptance Criteria

1. WHEN a CLR method has a `ref` return type (`ReturnType.IsByRef == true` and NOT marked with `IsReadOnlyAttribute`), THE Visibility_Filter SHALL NOT exclude that method from the Mist projection.
2. WHEN a CLR property has a `ref` return type (its getter returns by-ref, NOT `ref readonly`), THE Visibility_Filter SHALL NOT exclude that property from the Mist projection.
3. WHEN a CLR method has a `ref readonly` return type (by-ref return with `IsReadOnlyAttribute`), THE Visibility_Filter SHALL continue to exclude that method from the Mist projection because C# does not allow binding a `ref readonly` return to a mutable `ref` local, and Mist lacks `readonly ref` locals for CLR-originated references.

### Requirement 5: Project CLR Ref Returns as Mist Ref Returns

**User Story:** As a Mist developer, I want CLR `ref` returns to appear as Mist `ref` returns, so that I can bind the result to a `ref` local and mutate through it.

#### Acceptance Criteria

1. WHEN a CLR method has a `ref` return type (mutable, not `ref readonly`), THE Type_Projector SHALL project it as a Mist `ref` return in the method's signature.
2. WHEN a CLR `ref` return's element type is a CLR intrinsic, THE Type_Projector SHALL apply intrinsic type substitution to the element type.
3. WHEN Mist code calls a projected method with a `ref` return, THE Semantic_Model SHALL allow binding the result to a `ref` local variable.
4. WHEN Mist code calls a projected method with a `ref` return, THE CSharp_Emitter SHALL emit `ref` on the method call expression in the generated C# code.

### Requirement 6: CLR Out Parameters and Ref Readonly Returns Remain Filtered

**User Story:** As a Mist developer, I want CLR methods with `out` parameters or `ref readonly` returns to remain hidden, so that I am not exposed to calling conventions that would produce invalid emitted C#.

#### Acceptance Criteria

1. WHEN a CLR method or constructor has `out` parameters (`IsOut == true`), THE Visibility_Filter SHALL exclude that method or constructor from the Mist projection.
2. WHEN a CLR method has both `out` parameters and `ref` parameters, THE Visibility_Filter SHALL exclude that method from the Mist projection (the presence of any `out` parameter is sufficient to exclude).
3. WHEN a CLR method has a `ref readonly` return type (by-ref return with `IsReadOnlyAttribute`), THE Visibility_Filter SHALL exclude that method from the Mist projection because the emitted C# would require `ref readonly` binding which Mist cannot express.

### Requirement 7: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating CLR ref interop, so that I can see every supported usage pattern and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ClrRefInterop/Program.mist`.
2. The sample SHALL demonstrate calling a CLR method with `ref` parameters from Mist code.
3. The sample SHALL demonstrate calling a CLR method with `in` parameters from Mist code (projected as `ref`).
4. The sample SHALL demonstrate calling a CLR method with a `ref` return and binding the result to a `ref` local.
5. The sample SHALL demonstrate calling a CLR method with a `ref readonly` return and binding the result to a `ref` local.
6. The sample SHALL demonstrate that CLR methods with `out` parameters remain invisible (only the non-`out` overloads are accessible).
7. Each demonstrated pattern SHALL print labeled output to stdout so correctness is observable.
8. `samples/AllFeatures/Program.mist` SHALL call `ClrRefInterop.Program.RunDemo()` to include the feature in the full-suite sample run.
9. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// PROOF: CLR ref parameter — Interlocked.Exchange(ref int, int) is now visible and callable
// PROOF: The ref keyword is required at the call site for projected ref params
using Standard;
using System.Threading;

namespace ClrRefInterop;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("--- CLR Ref Interop ---");

        // [ref-param] Call Interlocked.Exchange(ref int, int)
        int value = 42;
        int old = Interlocked.Exchange(ref value, 100);
        Console.WriteLine($"[ref-param] old={old}, new={value}");

        // [ref-param-array] Call Array.Resize(ref T[], int)
        var arr = new string[2];
        arr[0] = "a";
        arr[1] = "b";
        Array.Resize(ref arr, 4);
        Console.WriteLine($"[ref-param-array] length={arr.Length}");

        // [in-param] Call a method with in parameter (projected as ref)
        int x = 7;
        int doubled = ReadAndDouble(ref x);
        Console.WriteLine($"[in-param] doubled={doubled}, x={x}");

        Console.WriteLine("--- CLR Ref Interop Done ---");
    }

    // Helper that wraps a CLR 'in' parameter method for demonstration
    private static int ReadAndDouble(ref int val)
    {
        return val * 2;
    }

    public static void Main()
    {
        RunDemo();
    }
}
```

**Expected output:**
```
--- CLR Ref Interop ---
[ref-param] old=42, new=100
[ref-param-array] length=4
[in-param] doubled=14, x=7
--- CLR Ref Interop Done ---
```

### Requirement 8: Interop Test Fixtures

**User Story:** As a Mist compiler developer, I want test fixture classes that exercise all ref/in/out parameter and ref return combinations, so that the visibility filter and type projector changes can be verified with unit tests.

#### Acceptance Criteria

1. THE test fixture assembly SHALL contain a class with public methods that have `ref` parameters, verifying they are no longer filtered.
2. THE test fixture assembly SHALL contain a class with public methods that have `in` parameters, verifying they are projected as `ref` parameters.
3. THE test fixture assembly SHALL contain a class with public methods that have `ref` return types, verifying they are no longer filtered.
4. THE test fixture assembly SHALL contain a class with public methods that have `ref readonly` return types, verifying they remain filtered.
5. THE test fixture assembly SHALL contain a class with public methods that have `out` parameters, verifying they remain filtered.
6. THE test fixture assembly SHALL contain a class with methods combining `ref` and `out` parameters, verifying the entire method is filtered when any `out` parameter is present.

### Requirement 9: Book Documentation Update

**User Story:** As a Mist developer reading the book, I want the .NET Interop chapter to accurately describe which CLR features are supported and which are filtered, so that I know I can use `ref` parameters and `ref` returns when calling CLR methods.

#### Acceptance Criteria

1. THE book chapter at `book/src/advanced/dotnet-interop.md` SHALL update the "What Gets Hidden" table to remove `ref` parameters from the filtered list.
2. THE book chapter SHALL add a new section or update the existing Visibility Filter section to explain that CLR `ref` parameters map to Mist `ref` parameters and require the `ref` keyword at the call site.
3. THE book chapter SHALL explain that CLR `in` parameters are projected as Mist `ref` parameters.
4. THE book chapter SHALL explain that CLR `ref` returns are supported and can be bound to `ref` locals.
5. THE book chapter SHALL explain that CLR `ref readonly` returns remain filtered because the emitted C# would require `ref readonly` binding which Mist cannot currently express.
6. THE book chapter SHALL update the "What Gets Hidden" table to include `ref readonly` returns as a filtered item with an explanation of why.
