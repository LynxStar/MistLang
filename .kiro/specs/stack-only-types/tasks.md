# Implementation Plan: Stack-Only Types (`Span<T>`, `ReadOnlySpan<T>`, `stackalloc`)

## Overview

This plan implements stack-only types on top of the shipped `ref struct` mechanism, following the design's two firm precedents: the **`Array<T, Length>` precedent** (dedicated `SpanType` IType, length-erasing emission, `ValidateArrayIndexRefinement` reuse, inbound unknown-length projection) and the **`ref struct` precedent** (heap-escape reuse via `IsRefStructType`). The genuinely new component is the `FrameEscapeAnalyzer`.

Implementation language: **C#** for the compiler edits (the Mist compiler is a C# codebase), **Storm** for the grammar, and **Mist** for the stdlib declaration, sample, and book snippets — exactly as the design specifies. No pseudocode is used, so no implementation-language selection is needed.

Tasks build bottom-up: grammar/AST + `SpanType` + stdlib declaration first, then resolution, then type-checking (target/size/stampability/bounds/read-only/interop), then emission and projection, then the new frame-escape analysis, then docs and the sample. All 21 correctness properties are covered by **in-process** property tests (no process spawns). The only external process spawn anywhere is the single sample run in `StackOnlyTypesSampleTests` (R13); the zero-warning emission check (R6.7) runs in-process via Roslyn `CSharpCompilation.Emit` (0 `dotnet` spawns). The build MUST stay at zero warnings (`CS*`, `STORM*`, MSBuild/SDK) throughout.

## Tasks

- [x] 1. Grammar and AST: `stackalloc` token and expression rule
  - [x] 1.1 Add the `STACKALLOC` token and the `StackallocExpression` Primary rule
    - In `Source/Mist/Grammar/keywords.storm`, add `token STACKALLOC = "stackalloc";` grouped with the other expression keywords
    - In `Source/Mist/Grammar/Mist.storm`, add one `Primary` alternative beside `ArrayCreationExpression`, mirroring its shape: `| STACKALLOC type:ElementTypeRef LEFT_BRACKET size:Expression RIGHT_BRACKET as StackallocExpression`, reusing the existing `ElementTypeRef` rule so the node has exactly one element-type child and one size-expression child
    - Regenerate Storm output and confirm a `StackallocExpressionPrimaryNode` with `Type` (`ElementTypeRefNode`) and `Size` (`ExpressionNode`) accessors is produced; ensure zero `STORM*` warnings
    - _Requirements: 3.1, 3.2, 3.3, 3.6 (design: Grammar / Lexer)_

  - [x] 1.2 Write property test for stackalloc parsing structure and round-trip
    - **Property 2: Stackalloc parsing structure and round-trip**
    - **Validates: Requirements 3.2, 3.6**
    - In-process via `MistParser` + pretty-printer; assert exactly one `StackallocExpression` node with one element-type child and one size-expression child, and that pretty-print→re-parse preserves the structure; min 100 iterations, tagged `Feature: stack-only-types, Property 2`

  - [x] 1.3 Write property test for missing/empty stackalloc size recovery
    - **Property 3: Stackalloc missing/empty size is rejected and recovered**
    - **Validates: Requirements 3.4, 3.5**
    - In-process via `MistParser`; assert `stackalloc T` and `stackalloc T[]` produce no `StackallocExpression` node, emit `MIST_PARSE_STACKALLOC_SIZE`, and parsing continues; min 100 iterations

- [x] 2. Introduce the `SpanType` IType (mirror `ArrayType`)
  - [x] 2.1 Create `SpanType` modeled field-for-field on `ArrayType`
    - Add `Source/Mist/Semantics/Model/Types/SpanType.cs` implementing `IType` with `ElementType`, `IsReadOnly`, `KnownLength` (`long?`), `SymbolicLengthId` (`int?`), `Kind => TypeKind.Struct`, and a `DisplayName` producing `Span<T, N>` / `Span<T>` / `ReadOnlySpan<T, N>` / `ReadOnlySpan<T>`
    - Provide the same three-constructor shape as `ArrayType`: unknown-length `(elementType, isReadOnly)`, known-length `(elementType, knownLength, isReadOnly)`, and symbolic `(elementType, symbolicLengthId, isReadOnly)`
    - _Requirements: 1.1, 2.1, 4.4 (design: Data Models — `SpanType`)_

  - [x] 2.2 Write unit tests for `SpanType`
    - Test all three constructors, `DisplayName` for mutable/read-only and known/unknown/symbolic lengths, and `Kind == TypeKind.Struct`
    - _Requirements: 1.1, 2.1_

- [x] 3. Standard library declaration `Span.mist`
  - [x] 3.1 Author `Source/Standard/Bootstrap/Span.mist`
    - Declare `intrinsic public ref struct Span<T, Length:ZeroOrPositiveInt>` and `intrinsic public ref struct ReadOnlySpan<T, Length:ZeroOrPositiveInt>` modeled byte-for-byte on `Array.mist`, each with the `SpanBounds = ZeroOrPositiveInt and < Length` refinement, intrinsic `Int32 Length`, the `this[SpanBounds index]` indexer, `Slice` returning the `unknown`-length form, the nested `Enumerator` (`MoveNext`/`Current`) and `GetEnumerator`
    - Add the aliases `type Span<T> = Span<T, unknown>;` and `type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;`
    - Ensure the stdlib loads cleanly through `StdlibLoader`
    - _Requirements: 1.1, 1.2, 1.4, 1.5, 1.6, 1.7, 2.1, 2.2, 2.4, 2.5, 2.6, 2.7 (design: Standard library)_

  - [x] 3.2 Write smoke test for `Span.mist` registration
    - Assert `Span.mist` loads and both `StructType`s carry `IsRefStruct == true` and `IsBackendBacked == true` (backend-backed intrinsic, skipped from emission)
    - _Requirements: 1.1, 1.3, 2.1, 2.3_

- [x] 4. Resolve stackalloc and Span declarations in `SemanticModelBuilder`
  - [x] 4.1 Add `StackallocExpression` resolution and Span declaration recognition
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a `StackallocExpressionPrimaryNode` case mirroring `ArrayCreationExpressionPrimaryNode`: resolve the element type via `ResolveElementTypeRef`, resolve the size expression, extract a literal via `TryExtractIntegerLiteral`, read the read-only target context (threaded like `_emptyArrayTargetType`), and build a `SpanType` (`KnownLength = N` for a literal size, unknown otherwise; `IsReadOnly` from the read-only target)
    - Recognize the `Standard.Span` / `Standard.ReadOnlySpan` declarations from `Span.mist`: assert `IsRefStruct`/`IsBackendBacked` (fail semantic processing or correct to true if not set), and resolve the `Span<T>` / `ReadOnlySpan<T>` aliases to `SpanType(T, unknown)`
    - _Requirements: 1.2, 1.3, 2.2, 2.3, 4.1, 4.2, 4.3 (design: Parser/AST → SemanticModelBuilder)_

  - [x] 4.2 Write property test for alias resolution
    - **Property 1: Alias resolution**
    - **Validates: Requirements 1.2, 2.2**
    - In-process via `SemanticModelBuilder`; for any element `T` and kind `K`, assert `K<T>` resolves to a `SpanType` with element `T`, `IsReadOnly == (K == ReadOnlySpan)`, and unknown length

  - [x] 4.3 Write property test for length and kind inference
    - **Property 4: Length and kind inference**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
    - In-process via `SemanticModelBuilder` + `TypeCheckerAnalyzer`; assert `KnownLength == N` for literal `N`, unknown otherwise, and `IsReadOnly` true iff the target context is a `ReadOnlySpan` family target

- [x] 5. Length-erasing emission in `CSharpTypeMap`
  - [x] 5.1 Add the `SpanType` length-erasure branch
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs`, beside the `ArrayType a => $"{...}[]"` branch, add `SpanType s when s.IsReadOnly => "System.ReadOnlySpan<...>"` and `SpanType s => "System.Span<...>"`, resolving the element via `GetTypeName`; add a `SpanType s => TypeContainsUnknown(s.ElementType)` arm to `TypeContainsUnknown` (the erased `Length` is irrelevant to emission)
    - This MUST be the Array length-erasure path, NOT the `Task`/`IntrinsicTypeMap` ConstructedType path
    - _Requirements: 6.2, 6.3, 7.1, 7.2 (design: CSharpEmitter — CSharpTypeMap)_

  - [x] 5.2 Write property test for length-erasing emission
    - **Property 13: Length-erasing emission**
    - **Validates: Requirements 6.2, 6.3, 7.1, 7.2**
    - In-process; call `CSharpTypeMap.GetTypeName` on `SpanType`s (all length states) and assert `System.Span<C>` (mutable) / `System.ReadOnlySpan<C>` (read-only) where `C` is the Backing_Type_Map CLR name; assert the C# string only (no compilation)

- [x] 6. `stackalloc` expression emission in `CSharpEmitter`
  - [x] 6.1 Add the `StackallocExpressionPrimaryNode` emission case
    - In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, beside the `ArrayCreationExpressionPrimaryNode` case, emit `stackalloc {EmitElementTypeRef(Type)}[{EmitExpression(Size)}]` with the size emitted exactly once and the element type resolved via the Backing_Type_Map / projected CLR name
    - Ensure indexer read/write, `Length`, `Slice`, and `foreach` over a span lower to standard C# member access / element assignment / `foreach` against the backing `System.Span<T>` / `System.ReadOnlySpan<T>`
    - _Requirements: 6.1, 6.4, 6.5, 6.6 (design: CSharpEmitter)_

  - [x] 6.2 Write property test for stackalloc expression emission
    - **Property 14: Stackalloc expression emission**
    - **Validates: Requirements 6.1**
    - In-process; assert emitting `stackalloc T[n]` produces `stackalloc C[E]` where `C` is the CLR name of `T` and `E` is the emitted size appearing exactly once

- [x] 7. Shared bounds helper and span index dispatch
  - [x] 7.1 Extract `ValidateLengthIndexRefinement` and add the `SpanType` dispatch branch
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, extract the existing 3-case array-bounds logic into `ValidateLengthIndexRefinement(indexSuffix, long? knownLength, int? symbolicLengthId, model, diagnostics)` verbatim, parameterized on length
    - Make `ValidateArrayIndexRefinement` a one-line adapter delegating to the new helper; add a sibling branch at the index-dispatch site so `targetType is SpanType spanTarget` routes through `ValidateLengthIndexRefinement(..., spanTarget.KnownLength, spanTarget.SymbolicLengthId, ...)`
    - This is reuse of the existing array machinery, not a second bounds checker
    - _Requirements: 1.5, 2.5, 14.6 (design: TypeCheckerAnalyzer — Bounds-machinery reuse)_

  - [x] 7.2 Write property test for index refinement parity with arrays
    - **Property 11: Index refinement parity with arrays (incl. unknown-length runtime fallback)**
    - **Validates: Requirements 1.5, 2.5, 14.6**
    - In-process, **model-based** against `ArrayType`: for identical length state and index, assert the span static-bounds outcome equals `ValidateArrayIndexRefinement`'s outcome — full static check when length is a known constant, lower-bound-only with no upper-bound diagnostic when unknown

- [x] 8. Stackalloc type-checking: target, size, stampability, widening, read-only, slice
  - [x] 8.1 Implement stackalloc target-type and size-type checks
    - In `TypeCheckerAnalyzer.cs`, verify the stackalloc result is consumed only in `var` / `Span<T,Length>` / `ReadOnlySpan<T,Length>` (or alias) context, else emit `MIST_TC_0150` naming both the result type and expected type; verify the size is integer-typed via `IsIntLike`, else emit `MIST_TC_0151` naming the size type
    - _Requirements: 4.6, 4.7, 4.8, 14.4 (design: TypeCheckerAnalyzer; Stackalloc Type-Inference Rules)_

  - [x] 8.2 Write property test for stackalloc target-type restriction
    - **Property 6: Stackalloc target-type restriction**
    - **Validates: Requirements 4.6, 14.4**
    - In-process; assert accepted iff `var` / Span family / ReadOnlySpan family target, else rejected with `MIST_TC_0150` whose message names both types

  - [x] 8.3 Write property test for stackalloc size integer-typedness
    - **Property 7: Stackalloc size must be integer-typed**
    - **Validates: Requirements 4.7, 4.8**
    - In-process; assert accepted (w.r.t. size) iff size type is `Int32` or implicitly convertible to `Int32`, else `MIST_TC_0151` naming the size type

  - [x] 8.4 Implement the element-type stampability gate with specific reasons
    - In `TypeCheckerAnalyzer.cs`, reuse `StructType.IsStampable` (+ `IsPrimitiveStructByName` fallback) — the same gate `new T[size]` uses — anchored at the element-type node; on rejection assign no span type and emit exactly one reason: `MIST_TC_0152` (reference type), `MIST_TC_0153` (non-stampable struct), `MIST_TC_0154` (ref struct), `MIST_TC_0155` (unconstrained type parameter)
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 14.3 (design: TypeCheckerAnalyzer — element-type stampability gate)_

  - [x] 8.5 Write property test for the stampability gate (array parity)
    - **Property 8: Element-type stampability gate (array parity)**
    - **Validates: Requirements 5.1, 5.2, 5.3**
    - In-process, **model-based** against bare `new T[n]`: assert `stackalloc T[n]` is accepted iff `T` is `Stampable_Type` iff `new T[n]` is accepted

  - [x] 8.6 Write property test for non-stampable rejection reason
    - **Property 9: Non-stampable element rejection carries the correct reason**
    - **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 14.3**
    - In-process; for each non-stampable category assert the exact diagnostic ID (`MIST_TC_0152`/`0153`/`0154`/`0155`), no span type assigned, anchored at the element type

  - [x] 8.7 Implement known→unknown widening, read-only indexer rejection, and slice inference
    - In `TypeCheckerAnalyzer.cs`, accept `SpanType(T, known)` initializer assigned to a `Span<T>`/`ReadOnlySpan<T>` (unknown) binding as a permitted length-erasing widening (kind must match), reusing the array assignability path; reject assignment whose target is the indexer of a read-only `SpanType` with `MIST_TC_0156`, independent of length validation; infer `Slice(start, len)` result as a `SpanType` of the same kind with `KnownLength = len` for a literal `len`, unknown otherwise
    - _Requirements: 2.8, 4.4, 4.5, 1.6, 2.6 (design: TypeCheckerAnalyzer; Length inference and widening)_

  - [x] 8.8 Write property test for known-to-unknown widening
    - **Property 5: Known-to-unknown widening**
    - **Validates: Requirements 4.4, 4.5**
    - In-process; assert initializing a `K<T>` (unknown) binding with a matching-kind `stackalloc T[N]` is accepted with no diagnostic, mirroring `int[] x = new int[N]`

  - [x] 8.9 Write property test for read-only indexer assignment rejection
    - **Property 10: Read-only indexer assignment is always rejected**
    - **Validates: Requirements 2.8**
    - In-process; for any index (in or out of range), assert assignment through a `ReadOnlySpan` indexer is rejected with `MIST_TC_0156` independently of length validation

  - [x] 8.10 Write property test for slice length inference
    - **Property 12: Slice length inference**
    - **Validates: Requirements 1.6, 2.6**
    - In-process; assert `Slice(start, len)` yields a same-kind `SpanType` with `KnownLength == len` for literal `len`, unknown otherwise

- [x] 9. Span ↔ CLR interop reconciliation in `TypeCheckerAnalyzer`
  - [x] 9.1 Implement bidirectional span identity reconciliation and mismatch rejection
    - In `TypeCheckerAnalyzer.cs`, treat a `SpanType` and an inbound-projected `System.Span`/`System.ReadOnlySpan` as the same logical type in both directions (assignment of a CLR span result to a Mist span binding, and passing a Mist span as a `System.Span<T>`/`System.ReadOnlySpan<T>` argument), reconciling element types with no explicit conversion; if the span kind differs or element types cannot be reconciled, reject with a mismatch diagnostic and leave the binding/call unmodified
    - _Requirements: 7.4, 7.5, 7.6, 7.7, 9.2 (design: Interop Reconciliation Rules)_

  - [x] 9.2 Write property test for cross-boundary span identity (bidirectional)
    - **Property 16: Cross-boundary span identity (bidirectional)**
    - **Validates: Requirements 7.4, 7.5, 7.6, 9.2**
    - In-process; assert a CLR `System.K<T>` is assignable to a Mist `K<T>` binding and a Mist `K<T, Length>` value is accepted as a `System.K<T>` argument, without conversion, whenever element types reconcile

  - [x] 9.3 Write property test for span mismatch rejection
    - **Property 17: Span mismatch rejection**
    - **Validates: Requirements 7.7**
    - In-process; assert differing span kind or non-reconcilable element types are rejected with a mismatch diagnostic and the binding/call left unmodified

- [x] 10. Inbound CLR span projection in `TypeProjector`
  - [x] 10.1 Project `System.Span\`1` / `System.ReadOnlySpan\`1` to unknown-length `SpanType`
    - In `Source/Mist/Backends/CSharpTranspiler/TypeProjector.cs`, in `ProjectTypeReference`, recognize constructed CLR `System.Span\`1` / `System.ReadOnlySpan\`1` and project to `SpanType(projectedElement, isReadOnly, length: unknown)`, mirroring the inbound-array arm; enforce `Length == unknown`; retain the existing `ProjectStruct` `IsByRefLike → IsRefStruct` path for all other CLR ref structs; keep `SynthesizeDuckTypedIEnumerable`'s `Span<T>` special-case and the arity-2-vs-arity-1 handling (no fabricated `Length`)
    - _Requirements: 7.3, 9.1 (design: TypeProjector / AssemblyTypeProvider)_

  - [x] 10.2 Write property test for inbound CLR span projection
    - **Property 15: Inbound CLR span projection**
    - **Validates: Requirements 7.3**
    - In-process; drive `TypeProjector` with reflection `Type` objects for `System.Span\`1[E]` / `System.ReadOnlySpan\`1[E]` and assert the projected `SpanType(project(E), isReadOnly)` has unknown length

  - [x] 10.3 Write property test for arbitrary CLR ref structs
    - **Property 18: Arbitrary CLR ref structs keep projecting as ref structs**
    - **Validates: Requirements 9.1**
    - In-process; assert any `IsByRefLike` CLR type that is not `System.Span`/`System.ReadOnlySpan` projects to a `StructType{IsRefStruct=true}` with no `Standard.*` identity

- [x] 11. Route span values through ref-struct heap-escape recognition
  - [x] 11.1 Extend `IsRefStructType` and route inline ref-struct match sites
    - Extend the central `IsRefStructType(IType)` predicate to return `true` for `SpanType`, and route the inline `StructType { IsRefStruct: true }` match sites through it: `MIST2002` field, `MIST2003` property, `MIST2005` union (in `SemanticModelBuilder` and `TypeCheckerAnalyzer`), `RefStructCannotBeOptional`, `MIST2004` generic arg, `MIST2007` interface, and `MIST_TC_0123` capture — reusing existing IDs unchanged
    - _Requirements: 9.3 (design: Data Models — heap-escape reuse; Diagnostic identifiers)_

  - [x] 11.2 Write property test for span heap-escape parity
    - **Property 19: Spans obey heap-escape rules like any ref struct**
    - **Validates: Requirements 9.3**
    - In-process, model-based against a known ref struct; for each heap-escape position assert the same existing diagnostic (`MIST2002`/`2003`/`2004`/`2005`/`2007`/`RefStructCannotBeOptional`/`MIST_TC_0123`) fires for span values and compilation halts

- [x] 12. Checkpoint — Ensure all tests pass
  - Ensure all tests pass and the build is at zero warnings, ask the user if questions arise.

- [x] 13. Frame-escape analysis (`FrameEscapeAnalyzer`)
  - [x] 13.1 Implement the `FrameEscapeAnalyzer` pass and register it
    - Add the new flow-sensitive `FrameEscapeAnalyzer` hosted with the `OwnershipAnalyzer` machinery, reusing its per-method-body walk; track the `StackBacked`/`NotStackBacked` lattice originating at `StackallocExpressionPrimaryNode` and propagating through frame-local bindings, `Slice` results, aliasing expressions, and storage into a containing ref struct field (conservative `StackBacked` when safety cannot be proven)
    - Reject return of a `StackBacked` value with `MIST2008` and an escaping store (field/property of a heap object, static/captured binding, `out`/`ref` outliving the call, heap-collection element, or containing-ref-struct propagation) with `MIST2009`, each at the offending expression's source location, failing compilation with no emitted C# for the affected compilation; accept purely in-frame uses and all `NotStackBacked` values
    - Register the pass in the analyzer list consumed by `AnalyzerRunner`, ordered after the type checker so span types are resolved
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 9.4, 14.2 (design: Frame-Escape Analysis Algorithm)_

  - [x] 13.2 Write property test for frame-escape soundness — reject on escape
    - **Property 20: Frame-escape soundness — reject on escape**
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.7, 14.2**
    - In-process via `FrameEscapeAnalyzer` over generated method bodies; generators enumerate escape shapes (return, field store, static store, `out` param, container ref struct); assert `MIST2008` (return) / `MIST2009` (store) at the offending location with no C# emitted

  - [x] 13.3 Write property test for frame-escape soundness — accept on safe use
    - **Property 21: Frame-escape soundness — accept on safe use**
    - **Validates: Requirements 8.4, 8.6, 9.4**
    - In-process; generators enumerate safe shapes (local/read/write/slice/foreach/call-arg) and `NotStackBacked` sources (parameter, `AsSpan()`, inbound projection); assert no `MIST2008`/`MIST2009` is emitted

- [x] 14. In-process emission compile verification (zero CS warnings)
  - [x] 14.1 Write the in-process Roslyn emission compile test
    - `StackOnlyTypesEmissionCompileTests`: emit N representative span programs (stackalloc Span/ReadOnlySpan, indexer read/write, `Length`, `Slice`, `foreach`, struct element, `AsSpan` identity), concatenate into ONE compilation, and compile **in-process via Roslyn `CSharpCompilation.Emit`** against the same reference set the driver uses — **0 `dotnet` spawns**; assert zero errors and zero `CS`-prefixed warnings (R6.7 / zero-warnings policy)
    - _Requirements: 6.7_

- [x] 15. TextMate grammar highlighting
  - [x] 15.1 Add `stackalloc` highlighting and a syntax test file
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, recognize `stackalloc` with word-boundary matching under the same scope the grammar gives the `new` array-creation keyword (`keyword.other.mist`), and highlight the element type after `stackalloc` with the same `entity.name.type.mist` scope used for `new T[n]`, leaving all other tokenization unchanged
    - Add a valid `.mist` test file under `Editor/mist-syntax-highlighting/tests/` containing at least one `stackalloc T[n]` expression, following the existing test-file format
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 15.2 Write tokenization assertion test for the grammar
    - Assert `stackalloc` tokenizes to `keyword.other.mist` and the element type to `entity.name.type.mist`, not to a default/unscoped token; no Mist-compiler process
    - _Requirements: 10.1, 10.2, 10.4_

- [x] 16. Update `CSHARP_FEATURES.md`
  - [x] 16.1 Mark `stackalloc` supported and annotate the Span row
    - In `CSHARP_FEATURES.md`, change the single `stackalloc` row in the "Unsafe / Low-Level" table to Status `✅` with Notes stating `stackalloc T[n]` produces a `Span<T>` (or `ReadOnlySpan<T>` in a read-only context) with frame-escape safety, without duplicating the row; annotate the `Span<T> / Memory<T>` row to note the `Standard.Span<T>` / `Standard.ReadOnlySpan<T>` intrinsics and `stackalloc` support (Feature/Status columns unchanged); keep all other rows unchanged; update the feature-count summary so the "Unsafe / Low-Level" counts reflect `stackalloc` moving from out-of-scope to supported
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

- [x] 17. Book documentation and compiled snippets
  - [x] 17.1 Add the book section and compiled snippets
    - Add exactly one book section under `book/src/` covering `Span<T>`, `ReadOnlySpan<T>`, and `stackalloc T[n]` (in the Object Model "Ref Structs" subsection or the .NET interop / advanced features area; cross-reference from the ref-struct material if placed in interop), covering: the `stackalloc T[n]` syntax, the literal→known / runtime→unknown `Length`, the `Span<T>` alias, the stampable-element restriction, the `Length`/indexer/`Slice`/`foreach` API, known-length static bounds vs unknown-length runtime upper-bound (array parity), and the frame-escape rule
    - Add at least one compiled `stackalloc` snippet and one `Span<T>`/`ReadOnlySpan<T>` API snippet under `book/snippets/` per the Book Snippet Policy (`.mist` + SnippetEmitter-generated `.cs`, zero errors/warnings); add a linked `SUMMARY.md` entry
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

- [x] 18. Sample application and AllFeatures integration
  - [x] 18.1 Create the `StackOnlyTypes` sample with the proof code verbatim
    - Create `samples/StackOnlyTypes/Program.mist` containing the proof code from requirements §13 **verbatim** (byte-for-byte identical — no added, removed, reordered, or reworded lines), and `samples/StackOnlyTypes/mist.toml` with `name = "StackOnlyTypes"`; the sample exercises stackalloc Span/ReadOnlySpan, indexer write/read, `Length`, `foreach`, `Slice` aliasing, stampable `Point` struct elements, `AsSpan` identity, and a span argument, and includes `public static void RunDemo()`
    - _Requirements: 13.1, 13.2, 13.4, 13.5, 13.7, 13.8 (sample-proof-code policy)_

  - [x] 18.2 Wire `StackOnlyTypes` into the AllFeatures orchestrator
    - In `samples/AllFeatures/Program.mist`, call `StackOnlyTypes.Program.RunDemo()`; in `samples/AllFeatures/mist.toml`, add `StackOnlyTypes = { path = "../StackOnlyTypes" }`, without breaking other samples
    - _Requirements: 13.6_

  - [x] 18.3 Write the sample compile-and-run test (single process spawn)
    - `StackOnlyTypesSampleTests`: compile the sample **in-process via `CompilerDriver.Compile()`**, then spawn `dotnet <dll>` **exactly once**, with `StandardOutputEncoding`/`StandardErrorEncoding` = `Encoding.UTF8` on the reader side (per UTF-8 steering); assert exit code 0 and stdout matching the expected output in requirements §13 **line-by-line exactly** (same line count, characters per line, and order). No per-iteration spawns
    - _Requirements: 13.2, 13.3, 13.7, 13.8_

  - [x] 18.4 Extend `AllFeaturesIntegrationTests` for `StackOnlyTypes`
    - Confirm `AllFeatures` still builds and runs with `StackOnlyTypes` wired in, reusing the existing AllFeatures harness (no new per-scenario spawn)
    - _Requirements: 13.6_

- [x] 19. Final checkpoint — Ensure all tests pass and the build is clean
  - Ensure all tests pass and the full build reports zero warnings (`CS*`, `STORM*`, MSBuild/SDK), ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test/verification sub-tasks and can be skipped for a faster MVP; core implementation tasks are never marked optional. The model MUST NOT implement `*` sub-tasks and MUST implement non-`*` sub-tasks.
- All 21 correctness properties are covered by **in-process** property tests (zero process spawns), using the repository's existing FsCheck-based harness with a single shared stateless `CompilerDriver`, minimum 100 iterations each, tagged `Feature: stack-only-types, Property {n}`. Parity properties (P8, P11, P19) are model-based against `ArrayType` / a known ref struct.
- Process-spawning posture (user-approved): the zero-warning emission check (task 14.1) is an **in-process Roslyn `CSharpCompilation.Emit`** test — **0 `dotnet` spawns**; the sample test (task 18.3) compiles in-process and spawns `dotnet <dll>` **exactly once** with UTF-8 on both sides. No per-iteration spawning anywhere.
- The proof code in task 18.1 is **immutable** during implementation: if it does not compile, fix the compiler — not the proof code. The expected output must match exactly; if a proof block cannot be made to work, stop and report the gap.
- Zero-warnings policy: every task MUST keep the build at zero warnings. If a change introduces an unavoidable warning (e.g., a Storm-generated nullability warning on the new node), stop and raise it with the user before proceeding rather than suppressing it silently.
- The design deliberately mirrors current `Array` bounds behavior for unknown-length spans (lower-bound-only static check, upper bound deferred to the CLR at runtime) and does NOT close that runtime-fallback gap (tracked separately in `project/TODO.md`).

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1", "3.1", "15.1", "16.1"] },
    { "id": 1, "tasks": ["1.2", "1.3", "2.2", "3.2", "4.1", "5.1", "6.1", "10.1", "7.1", "15.2"] },
    { "id": 2, "tasks": ["4.2", "4.3", "5.2", "6.2", "10.2", "10.3", "7.2", "8.1"] },
    { "id": 3, "tasks": ["8.2", "8.3", "8.4"] },
    { "id": 4, "tasks": ["8.5", "8.6", "8.7"] },
    { "id": 5, "tasks": ["8.8", "8.9", "8.10", "9.1"] },
    { "id": 6, "tasks": ["9.2", "9.3", "11.1"] },
    { "id": 7, "tasks": ["11.2", "13.1"] },
    { "id": 8, "tasks": ["13.2", "13.3", "14.1", "17.1", "18.1"] },
    { "id": 9, "tasks": ["18.2"] },
    { "id": 10, "tasks": ["18.3", "18.4"] }
  ]
}
```
