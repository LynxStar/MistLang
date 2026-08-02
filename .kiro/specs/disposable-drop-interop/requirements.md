# Requirements Document

## Introduction

This feature enables seamless interop between .NET's `System.IDisposable` pattern and Mist's drop semantics. CLR types that implement `IDisposable` are automatically recognized as droppable in the Mist semantic model, allowing them to participate in `with` blocks (both declaration and expression forms) without requiring Mist-side wrappers or explicit `with` declarations. The emitter maps the synthetic drop to a `Dispose()` call rather than `Drop()`.

## Glossary

- **Semantic_Model**: The typed representation of a Mist program built by `SemanticModelBuilder`, containing type information, scopes, and symbol metadata.
- **Emitter**: The C# transpiler backend (`CSharpEmitter`) that generates C# source from the semantic model.
- **CLR_Type**: A type loaded from a .NET assembly via `MetadataLoadContext` and projected into the Mist type system by `AssemblyTypeProvider` and `TypeProjector`.
- **Synthetic_Disposable_Drop**: A compiler-internal flag indicating that a CLR type's droppability originates from implementing `System.IDisposable` rather than from an explicit Mist `Drop()` method declaration.
- **With_Block**: A Mist language construct (`with expr { ... }` or `with (var x = expr) { ... }`) that scopes a value and calls `Drop()` in a `finally` block at scope exit.
- **Drop_Emission_Site**: Any location in the emitter where a `.Drop()` call is generated for a droppable value (includes `EmitWithBlockDropCall`, `EmitDroppableLocalWithWrapping`, `EmitDiscardedTaskWithWrapping`, and `EmitRemainingFieldDropsIfManualDrop`).
- **SyntheticDropInferenceEngine**: The compiler pass that synthesizes `Drop()` for Mist types containing droppable fields.
- **TypeProjector**: The component in `AssemblyTypeProvider` that projects CLR reflection metadata into Mist `ClassType`/`StructType` instances.

## Requirements

### Requirement 1: Recognize IDisposable CLR Types as Droppable

**User Story:** As a Mist developer, I want CLR types that implement `System.IDisposable` to be automatically recognized as droppable, so that I can use them in `with` blocks without writing wrapper code.

#### Acceptance Criteria

1. WHEN the TypeProjector projects a CLR type that implements `System.IDisposable`, THE Semantic_Model SHALL set `HasDrop = true` on the resulting `ClassType` or `StructType`.
2. WHEN the TypeProjector projects a CLR type that implements `System.IDisposable`, THE Semantic_Model SHALL set `IsDisposableDrop = true` on the resulting type to differentiate IDisposable-sourced drops from explicit Mist `Drop()` methods.
3. WHEN the TypeProjector projects a CLR type that does NOT implement `System.IDisposable` and has no explicit `Drop()` method, THE Semantic_Model SHALL leave `HasDrop = false` on the resulting type.
4. WHEN the TypeProjector projects a CLR type that implements an interface which extends `System.IDisposable` (transitive implementation), THE Semantic_Model SHALL set `HasDrop = true` and `IsDisposableDrop = true` on the resulting type, identical to direct `IDisposable` implementation.
5. WHEN the TypeProjector projects a generic CLR type that implements `System.IDisposable`, THE Semantic_Model SHALL set `HasDrop = true` and `IsDisposableDrop = true` on the generic definition type.

### Requirement 2: Explicit Drop Takes Precedence

**User Story:** As a Mist developer, I want my explicit `Drop()` declarations to take precedence over IDisposable-based drops, so that I retain full control when wrapping CLR types.

#### Acceptance Criteria

1. WHEN a Mist type declares an explicit `Drop()` method AND the type also implements `System.IDisposable`, THE Semantic_Model SHALL set `HasDrop = true` and `IsDisposableDrop = false` on the type.
2. WHEN a CLR type has both a public parameterless method named `Drop` (with any return type) AND implements `System.IDisposable`, THE Semantic_Model SHALL set `HasDrop = true` and `IsDisposableDrop = false` on the projected type.
3. WHEN a type has `IsDisposableDrop = false` and `HasDrop = true`, THE Emitter SHALL emit `.Drop()` rather than `.Dispose()` at Drop_Emission_Sites.

### Requirement 3: Emit Dispose Instead of Drop for IDisposable Types

**User Story:** As a Mist developer, I want the compiler to emit `.Dispose()` for IDisposable CLR types used in `with` blocks, so that .NET resource cleanup happens correctly.

#### Acceptance Criteria

1. WHEN the Emitter encounters a Drop_Emission_Site for a type with `IsDisposableDrop = true`, THE Emitter SHALL emit a call to `.Dispose()` instead of `.Drop()`.
2. WHEN the Emitter encounters a Drop_Emission_Site for a type with `IsDisposableDrop = false` (or no flag), THE Emitter SHALL emit `.Drop()` as it does today.
3. WHEN the Emitter emits `.Dispose()` for an IDisposable type in a `with` block, THE Emitter SHALL place the `.Dispose()` call inside the `finally` block, consistent with existing Drop emission structure.
4. WHEN the Emitter emits chained drop expressions and the outermost type has `IsDisposableDrop = true`, THE Emitter SHALL use `.Dispose()` for that outer call while preserving the chaining logic for any inner droppable return type.

### Requirement 4: Drop Analyzer Treats IDisposable CLR Types as Droppable

**User Story:** As a Mist developer, I want the drop analyzer to enforce drop-semantics rules on IDisposable CLR types, so that flow analysis and diagnostics work consistently.

#### Acceptance Criteria

1. WHEN the Drop_Analyzer evaluates a variable of an IDisposable CLR type inside a `with` block, THE Drop_Analyzer SHALL treat the variable as droppable for purposes of flow analysis.
2. WHEN the `TypeHasDrop` helper evaluates a type with `HasDrop = true` set via IDisposable recognition, THE helper SHALL return `true`.
3. WHEN the Drop_Analyzer encounters an IDisposable CLR type used outside a `with` block in a context that requires droppability (e.g., linear or relevant discipline), THE Drop_Analyzer SHALL emit the same diagnostic it would for any non-droppable type used in that context.

### Requirement 5: No Synthetic Drop Inference Cascading for IDisposable Fields

**User Story:** As a Mist developer, I want the synthetic drop inference engine to NOT automatically synthesize drops based on IDisposable fields in Mist types, so that I maintain explicit control over my own type's cleanup semantics.

#### Acceptance Criteria

1. WHEN the SyntheticDropInferenceEngine evaluates fields of a Mist-declared type, THE SyntheticDropInferenceEngine SHALL NOT consider a field's type as droppable solely because that field's type has `IsDisposableDrop = true`.
2. WHEN a Mist-declared type has an IDisposable CLR field but no explicit `Drop()` method, THE SyntheticDropInferenceEngine SHALL NOT synthesize a `Drop()` method on the containing Mist type.
3. WHEN a Mist-declared type has both an IDisposable CLR field AND a field with an explicit Mist `Drop()` method, THE SyntheticDropInferenceEngine SHALL synthesize `Drop()` only for the explicitly-droppable field (not the IDisposable field).

### Requirement 6: No IAsyncDisposable Support

**User Story:** As a Mist developer, I understand that only `IDisposable` is supported for automatic drop interop in this iteration.

#### Acceptance Criteria

1. WHEN the TypeProjector projects a CLR type that implements `System.IAsyncDisposable` but NOT `System.IDisposable`, THE Semantic_Model SHALL NOT set `HasDrop = true` based on `IAsyncDisposable` alone.
2. WHEN the TypeProjector projects a CLR type that implements BOTH `System.IAsyncDisposable` and `System.IDisposable`, THE Semantic_Model SHALL recognize the type as droppable based on `IDisposable` only (setting `IsDisposableDrop = true`).

### Requirement 7: Mist Types Implementing IDisposable Are Unaffected

**User Story:** As a Mist developer, I want to manually implement `IDisposable` on my Mist types without the compiler interfering with synthetic drop behavior.

#### Acceptance Criteria

1. WHEN a Mist type implements `System.IDisposable` by declaring a `Dispose()` method, THE Semantic_Model SHALL NOT automatically set `HasDrop = true` based on the `IDisposable` implementation.
2. WHEN a Mist type implements `System.IDisposable` AND declares an explicit `Drop()` method, THE Semantic_Model SHALL set `HasDrop = true` based on the explicit `Drop()` method, and `IsDisposableDrop = false`.
3. WHEN a Mist type implements `System.IDisposable` without declaring `Drop()`, THE Semantic_Model SHALL leave `HasDrop = false` (the type is not automatically droppable in `with` blocks unless explicitly opted in via `Drop()`).

### Requirement 8: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating IDisposable-to-Drop interop, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DisposableDropInterop/Program.mist`.
2. The sample SHALL demonstrate all usage patterns defined in Requirements 1–7:
   - Using an IDisposable CLR type (e.g., `System.IO.MemoryStream`) in a `with` block form, proving that `.Dispose()` is emitted in the `finally` block.
   - Using an IDisposable CLR type in a `with` declaration form (no braces), proving scope-exit disposal.
   - Using a Mist type with an explicit `Drop()` method in a `with` block, proving that explicit `Drop()` takes precedence over any IDisposable implementation.
   - Showing that a Mist type implementing `IDisposable` but without an explicit `Drop()` method does NOT auto-drop in `with` blocks.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `DisposableDropInterop.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
using Standard;
using System;

namespace DisposableDropInterop;

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== DisposableDropInterop Demo ===");

        // PROOF: IDisposable CLR type works in with-block form
        // MemoryStream implements IDisposable, so the compiler should
        // recognize it as droppable and emit .Dispose() in finally
        Console.WriteLine("--- 1. IDisposable with block ---");
        with (var ms = new IO.MemoryStream())
        {
            ms.WriteByte(42);
            Console.WriteLine($"  wrote byte, length={ms.Length}");
        }
        Console.WriteLine("  disposed (no exception = success)");

        // PROOF: IDisposable CLR type works in with-declaration form
        // The drop/dispose fires at end of enclosing scope
        Console.WriteLine("--- 2. IDisposable with declaration ---");
        DemoWithDeclaration();
        Console.WriteLine("  disposed (no exception = success)");

        // PROOF: Mist type with explicit Drop() still uses Drop(), not Dispose()
        Console.WriteLine("--- 3. Explicit Drop takes precedence ---");
        with (var r = new MistResource("hello"))
        {
            Console.WriteLine($"  resource value={r.Value}");
        }

        // PROOF: Mist type implementing IDisposable without Drop() is NOT auto-droppable
        // This compiles but does NOT call Dispose automatically — must be manual
        Console.WriteLine("--- 4. Mist IDisposable without Drop ---");
        var manual = new ManualDisposable();
        manual.DoWork();
        manual.Dispose();
        Console.WriteLine("  manually disposed");

        Console.WriteLine("=== Done ===");
    }

    public static void DemoWithDeclaration()
    {
        with var ms = new IO.MemoryStream();
        ms.WriteByte(99);
        Console.WriteLine($"  wrote byte, length={ms.Length}");
    }
}

// A Mist type with explicit Drop — proves explicit Drop takes precedence
public class MistResource
{
    private string value;

    public MistResource(string v)
    {
        value = v;
    }

    public string Value => value;

    public void Drop()
    {
        Console.WriteLine("  MistResource.Drop() called");
    }
}

// A Mist type that implements IDisposable but does NOT declare Drop()
// This should NOT be auto-droppable in with blocks
public class ManualDisposable : IDisposable
{
    public void DoWork()
    {
        Console.WriteLine("  ManualDisposable.DoWork()");
    }

    public void Dispose()
    {
        Console.WriteLine("  ManualDisposable.Dispose() called");
    }
}
```

**Expected output:**
```
=== DisposableDropInterop Demo ===
--- 1. IDisposable with block ---
  wrote byte, length=1
  disposed (no exception = success)
--- 2. IDisposable with declaration ---
  wrote byte, length=1
  disposed (no exception = success)
--- 3. Explicit Drop takes precedence ---
  resource value=hello
  MistResource.Drop() called
--- 4. Mist IDisposable without Drop ---
  ManualDisposable.DoWork()
  ManualDisposable.Dispose() called
  manually disposed
=== Done ===
```

### Requirement 9: Book Documentation Update

**User Story:** As a Mist developer, I want the language book's drop semantics chapter to document IDisposable interop, so that I know how .NET types work with `with` blocks.

#### Acceptance Criteria

1. THE book's drop semantics chapter (`book/src/advanced/drop-semantics.md`) SHALL include a new section titled "IDisposable Interop" after the existing "Interaction with Ownership" section, covering how CLR types implementing `IDisposable` are automatically droppable in `with` blocks.
2. THE section SHALL explain that `with` blocks emit `.Dispose()` for IDisposable CLR types instead of `.Drop()`, with a compiled snippet demonstrating a CLR IDisposable type (e.g., `IO.MemoryStream`) in a `with` block alongside the emitted C# showing the `try/finally` with `.Dispose()`.
3. THE section SHALL explain precedence: when a Mist type declares an explicit `Drop()` method, that method is called instead of `.Dispose()`, regardless of whether the type also implements `IDisposable`. A compiled snippet SHALL demonstrate a type with both `Drop()` and `IDisposable`, showing that `Drop()` is emitted.
4. THE section SHALL explain that Mist types implementing `IDisposable` are NOT automatically droppable — only CLR types imported from .NET assemblies receive this treatment. Mist types must declare an explicit `Drop()` method to be usable in `with` blocks.
5. All code examples SHALL be compiled snippets following the Book Snippet Policy (`.mist` files in `book/snippets/advanced/drop-semantics/` with corresponding `.cs` files generated by the SnippetEmitter tool).
6. THE book's SUMMARY.md SHALL NOT require a new entry because the content is added as a section within the existing "Drop Semantics" chapter page.
