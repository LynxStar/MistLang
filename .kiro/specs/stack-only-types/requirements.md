# Requirements Document

## Introduction

This feature expands Mist to support **stack-only types** on top of the existing `ref struct` mechanism. It adds three things:

1. First-class intrinsic `Standard.Span<T, Length>` and `Standard.ReadOnlySpan<T, Length>` types declared in the standard library as `intrinsic public ref struct`, with a dependent value type parameter `Length:ZeroOrPositiveInt` modeled exactly on the existing `Array<TValue, Length:ZeroOrPositiveInt>` intrinsic, backed by C# `System.Span<T>` / `System.ReadOnlySpan<T>`. Ergonomic aliases `type Span<T> = Span<T, unknown>;` and `type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;` provide the unknown-length form (the common case), exactly as `T[]` is shorthand for `Array<T, unknown>`.
2. A `stackalloc T[n]` expression that allocates a contiguous block on the stack and yields a `Span<T, Length>` (or `ReadOnlySpan<T, Length>` when target-typed into a read-only context). When the size is an integer literal `N`, the result is the known-length `Span<T, N>`; otherwise it is the unknown-length `Span<T>` (= `Span<T, unknown>`).
3. **Frame-escape analysis** ensuring a stackalloc-backed span cannot outlive the stack frame that allocated it.

This feature builds on the already-shipped `ref-structs` feature. `ref struct` is the one and only stack-only mechanism in Mist — this feature introduces no separate stack-only concept. `ref struct` already parses (`REF` is a modifier), sets `StructType.IsRefStruct`, emits C# `ref struct`, projects inbound from ambient assemblies (`clrType.IsByRefLike → IsRefStruct`), and enforces **heap-escape** restrictions: fields in non-ref-struct types (`MIST2002`), properties in non-ref-struct types (`MIST2003`), generic type arguments (`MIST2004`), union constituents (`MIST2005`), interface boxing (`MIST2007`), optional suffix (`RefStructCannotBeOptional`), and lambda capture (`MIST_TC_0123`). Those rules prevent a ref struct value from reaching the **heap**.

A stackalloc-backed span introduces a new hazard the heap-escape rules do not cover: the span points into the current stack frame, so it must not **escape its frame** by being returned, or stored where it would outlive the frame. This feature adds that analysis, modeled on C#'s ref-safety semantics.

The dependent-typed `Standard.Span<T, Length>` reconciles with the CLR `System.Span<T>` by following the existing **`Array` precedent**, not the `Task` precedent. The Mist Span is arity-2 (`Span<T, Length>`) while the CLR `System.Span<T>` is arity-1 — the same arity mismatch that `Array<T, Length>` ↔ `T[]` already handles. On emission the `Length` type argument is erased and `Span<T, Length>` lowers to `System.Span<T>` (analogous to `Array<T, Length>` → `T[]`); on inbound projection a CLR `System.Span\`1` projects to the unknown-length `Span<T, unknown>` (just as an inbound array projects to an unknown/symbolic-length array type). A value returned from a C# API as `System.Span<int>` (for example `int[].AsSpan()`) therefore unifies with the Mist `Span<int>` (= `Span<int, unknown>`) and is accepted without an explicit conversion.

Bounds checking on the span indexer **mirrors arrays exactly**. The indexer parameter is a `SpanBounds` refinement (`ZeroOrPositiveInt and < Length`) reusing the existing `Array` bounds machinery. Where `Length` is a known compile-time constant (e.g. `Span<int, 4>` from `stackalloc int[4]`), indexing is fully statically checked. Where `Length` is unknown (`Span<T, unknown>` — from the alias, `AsSpan()`, a runtime-sized `stackalloc int[n]`, or an explicit unknown-length annotation), only the lower bound `>= 0` is enforced statically and the upper bound is deferred to runtime (the CLR throws `IndexOutOfRangeException`), matching C# and current array behavior. This intentional runtime-fallback gap is tracked as a separate critical item in `project/TODO.md`; this spec deliberately mirrors current array behavior and does not close it.

As proof that the feature works end-to-end, a dedicated sample application exercises `stackalloc`, the `Span<T>` / `ReadOnlySpan<T>` API, slice aliasing, `foreach`, stampable struct element types, and ambient `System.Span` identity reconciliation.

## Glossary

- **Span**: The Mist standard-library type `Standard.Span<T, Length>`, an `intrinsic public ref struct` representing a contiguous, mutable region of memory, with a dependent value type parameter `Length:ZeroOrPositiveInt` modeled on `Array<TValue, Length>`. Backed by C# `System.Span<T>` (the `Length` argument is erased on emission). The alias `type Span<T> = Span<T, unknown>;` denotes the unknown-length form.
- **ReadOnlySpan**: The Mist standard-library type `Standard.ReadOnlySpan<T, Length>`, an `intrinsic public ref struct` representing a contiguous, read-only region of memory, with a dependent `Length:ZeroOrPositiveInt`. Backed by C# `System.ReadOnlySpan<T>`. The alias `type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;` denotes the unknown-length form.
- **SpanBounds**: The refinement type `ZeroOrPositiveInt and < Length` used as the span indexer's parameter type, analogous to `Array`'s `ArrayBounds`. It is enforced by the existing `Array` bounds machinery: fully static when `Length` is a known compile-time constant; lower-bound-only with the upper bound deferred to runtime when `Length` is unknown.
- **Stackalloc_Expression**: A Mist expression of the form `stackalloc T[n]` that allocates `n` contiguous elements of type `T` on the current stack frame and produces a Span value over that memory. A literal `n = N` yields `Span<T, N>`; a non-literal `n` yields `Span<T, unknown>`.
- **Ref_Struct**: A value type declared with the `ref struct` modifier (existing feature). It can only live on the stack and carries `StructType.IsRefStruct = true`.
- **Stampable_Type**: A type for which `StructType.IsStampable` is true — a primitive struct, or a pure struct whose instance fields are transitively all primitives or other stampable structs. This is Mist's existing equivalent of C#'s `unmanaged` constraint, already used to gate bare `new T[size]` array creation.
- **Frame_Escape**: The situation where a value backed by stack-frame memory (a Stackalloc_Expression result) is made reachable after the allocating stack frame returns — by returning it, or storing it into a binding, field, or location whose lifetime exceeds the current frame.
- **Heap_Escape**: The situation, already prevented by the existing ref-struct restrictions, where a ref struct value reaches heap-allocated storage (fields/properties of non-ref-struct types, generic arguments, union/optional types, interface boxing, lambda capture).
- **Readonly_Context**: A target-typing position whose expected type is `ReadOnlySpan<T>` (e.g., a local declared `ReadOnlySpan<T>`, a parameter typed `ReadOnlySpan<T>`, or a return type `ReadOnlySpan<T>`).
- **Lexer**: The Storm-generated Mist lexer produced from the `.storm` grammar.
- **Parser**: The Mist compiler component that transforms a token stream into a syntax tree.
- **Type_Checker**: The Mist compiler analyzer (`TypeCheckerAnalyzer`) that validates type correctness of expressions and statements.
- **Escape_Analyzer**: The Mist compiler analysis responsible for Frame_Escape detection (a new or extended analyzer, reusing existing ref-struct machinery where applicable).
- **Assembly_Type_Provider**: The Mist compiler component that loads .NET assemblies and projects CLR types into Mist's semantic model.
- **Type_Projector**: The Mist compiler component (`TypeProjector`) that maps CLR types to Mist types during projection.
- **Emitter**: The Mist C# transpiler backend (`CSharpEmitter`).
- **Backing_Type_Map**: The `CSharpTypeMap.BackingTypes` dictionary — the single source of truth mapping canonical `Standard.*` type names to CLR type names, used for both outbound emission and (via `IntrinsicTypeMap.BuildReversedMap`) inbound projection identity.
- **Standard_Library**: The built-in Mist library at `Source/Standard/`.
- **Sample_Application**: A demonstration program at `samples/StackOnlyTypes/Program.mist`.
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.

## Requirements

### Requirement 1: Standard.Span&lt;T, Length&gt; Intrinsic Ref Struct Type

**User Story:** As a Mist developer, I want a first-class dependent-length `Span<T, Length>` type in the standard library, so that I can work with contiguous mutable memory regions with a stable Mist API that carries the same compile-time bounds protection as `Array`.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Standard.Span<T, Length>` as an `intrinsic public ref struct` in `Source/Standard/Bootstrap/Span.mist` with a dependent value type parameter `Length:ZeroOrPositiveInt`, modeled exactly on the existing `Array<TValue, Length:ZeroOrPositiveInt>` intrinsic.
2. THE Standard_Library SHALL declare the ergonomic alias `type Span<T> = Span<T, unknown>;` so that `Span<T>` denotes the unknown-length form, exactly as `T[]` is shorthand for `Array<T, unknown>`.
3. WHEN the Semantic_Model processes the `Standard.Span<T, Length>` declaration, THE Semantic_Model SHALL register the type with `StructType.IsRefStruct` set to true and as a backend-backed intrinsic (skipped from emission); IF `StructType.IsRefStruct` cannot be set to true during this registration, THEN the Semantic_Model SHALL fail semantic processing (or correct `IsRefStruct` to true) rather than register an inconsistent type.
4. THE `Standard.Span<T, Length>` type SHALL expose an intrinsic `Int32 Length` property that reports a non-negative element count equal to the span's creation length, in the range 0 to 2,147,483,647 inclusive.
5. THE `Standard.Span<T, Length>` type SHALL expose an intrinsic indexer whose parameter type is a `SpanBounds` refinement (`ZeroOrPositiveInt and < Length`), supporting both read access (reading the element value) and write access (assigning the element value), where index validation reuses the existing `Array` bounds machinery: WHERE `Length` is a known compile-time constant the index SHALL be fully statically bounds-checked (an out-of-range literal or unprovable non-constant index is a compile error); WHERE `Length` is unknown only the lower bound `>= 0` SHALL be enforced statically and the upper bound SHALL be deferred to runtime, matching the current behavior of `Array`.
6. THE `Standard.Span<T, Length>` type SHALL expose an intrinsic `Slice` operation that accepts a start index and a length and returns a `Span<T, Length2>` exhibiting observable aliasing with the original span (writes through one span are visible through the other), where `Length2` is the literal length argument when it is a compile-time constant and unknown otherwise.
7. THE `Standard.Span<T, Length>` type SHALL expose an intrinsic nested `Enumerator` and a `GetEnumerator` method such that a value of type `Span<T, Length>` is usable in a `foreach` statement, following the same enumeration mechanism as the `Array<T, Length>` intrinsic.
8. WHEN a `foreach` statement iterates a `Span<T, Length>` value, THE Emitter SHALL emit a C# `foreach` that visits each element of the backing `System.Span<T>` exactly once in ascending index order from 0 to `Length` minus 1.

### Requirement 2: Standard.ReadOnlySpan&lt;T, Length&gt; Intrinsic Ref Struct Type

**User Story:** As a Mist developer, I want a dependent-length `ReadOnlySpan<T, Length>` type in the standard library, so that I can expose contiguous memory for reading without permitting mutation.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Standard.ReadOnlySpan<T, Length>` as an `intrinsic public ref struct` in `Source/Standard/Bootstrap/Span.mist` with a dependent value type parameter `Length:ZeroOrPositiveInt`, modeled exactly on `Array<TValue, Length>`.
2. THE Standard_Library SHALL declare the ergonomic alias `type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;` so that `ReadOnlySpan<T>` denotes the unknown-length form.
3. WHEN the Semantic_Model processes the `Standard.ReadOnlySpan<T, Length>` declaration, THE Semantic_Model SHALL register the type with `StructType.IsRefStruct` set to true and as a backend-backed intrinsic (skipped from emission); IF `StructType.IsRefStruct` cannot be set to true during this processing, THEN the Semantic_Model SHALL fail semantic analysis rather than register an inconsistent type.
4. THE `Standard.ReadOnlySpan<T, Length>` type SHALL expose an intrinsic `Int32 Length` property whose value is the element count of the backing memory, in the range 0 to 2,147,483,647 inclusive.
5. THE `Standard.ReadOnlySpan<T, Length>` type SHALL expose an intrinsic indexer whose parameter type is a `SpanBounds` refinement (`ZeroOrPositiveInt and < Length`) supporting read access, with index validation reusing the existing `Array` bounds machinery (fully static when `Length` is a known compile-time constant; lower-bound-only with the upper bound deferred to runtime when `Length` is unknown).
6. THE `Standard.ReadOnlySpan<T, Length>` type SHALL expose an intrinsic `Slice` operation that accepts a start index and a length and returns a `ReadOnlySpan<T, Length2>` over the same backing memory, where `Length2` is the literal length argument when it is a compile-time constant and unknown otherwise.
7. THE `Standard.ReadOnlySpan<T, Length>` type SHALL expose an intrinsic nested `Enumerator` and a `GetEnumerator` method such that a value of type `ReadOnlySpan<T, Length>` is usable in a `foreach` statement, following the same enumeration mechanism as the `Standard.Span<T, Length>` intrinsic.
8. IF a Mist program performs an assignment whose target is the indexer of a value typed `ReadOnlySpan<T, Length>`, THEN THE Type_Checker SHALL reject the assignment and emit a diagnostic error indicating that a read-only span element cannot be assigned, identifying the offending indexer expression. This read-only rejection SHALL hold independently of length validation: an assignment through a `ReadOnlySpan` indexer SHALL be rejected even when the index exceeds the documented length range.

### Requirement 3: Stackalloc Keyword and Expression Parsing

**User Story:** As a Mist developer, I want to write `stackalloc T[n]`, so that I can allocate a stack buffer with familiar C#-like syntax.

#### Acceptance Criteria

1. THE Lexer SHALL recognize `stackalloc` as a keyword, defined in `Source/Mist/Grammar/keywords.storm`.
2. THE Parser SHALL parse an expression of the form `stackalloc T[n]`, where `T` is an element type reference and `n` is an expression, into a dedicated Stackalloc_Expression syntax node containing exactly one element-type child and exactly one size-expression child, mirroring the existing `ArrayCreationExpression` rule (`NEW type LEFT_BRACKET size RIGHT_BRACKET`).
3. THE Parser SHALL support single-dimensional `stackalloc T[n]` only, where single-dimensional means exactly one size expression and no comma-separated dimensions, and SHALL NOT define multidimensional stackalloc syntax.
4. IF a `stackalloc` expression is written without a bracketed size (e.g., `stackalloc T`), THEN THE Parser SHALL produce no Stackalloc_Expression node, emit a diagnostic error indicating that a `stackalloc` expression requires an element type and a bracketed size, and continue parsing.
5. IF a `stackalloc T[]` expression is written with empty brackets (no size expression), THEN THE Parser SHALL handle it the same way as a `stackalloc` expression without a bracketed size (criterion 4): THE Parser SHALL produce no Stackalloc_Expression node, emit a diagnostic error indicating that a `stackalloc` expression requires a size, and continue parsing.
6. THE `stackalloc` keyword SHALL be usable as an expression in the following syntactic positions: a local variable initializer, a call argument, and the right-hand side of an assignment.

### Requirement 4: Stackalloc Type Assignment

**User Story:** As a Mist developer, I want `stackalloc T[n]` to produce a `Span<T>` by default and a `ReadOnlySpan<T>` when assigned into a read-only target, so that the resulting type matches my intent.

#### Acceptance Criteria

1. WHEN a Stackalloc_Expression `stackalloc T[N]` whose size is an integer literal `N` is type-checked in a context with no read-only target type, THE Type_Checker SHALL assign it the known-length type `Standard.Span<T, N>`.
2. WHEN a Stackalloc_Expression `stackalloc T[n]` whose size is a non-literal (runtime) expression is type-checked in a context with no read-only target type, THE Type_Checker SHALL assign it the unknown-length type `Standard.Span<T, unknown>` (i.e. `Span<T>`).
3. WHEN a Stackalloc_Expression `stackalloc T[n]` is type-checked in a Readonly_Context (the target type is a `Standard.ReadOnlySpan<T, Length>` family type), THE Type_Checker SHALL assign it the corresponding `Standard.ReadOnlySpan<T, Length>` type, applying the same literal/non-literal length inference as criteria 1 and 2.
4. WHEN a Stackalloc_Expression result of type `Standard.Span<T, N>` is the initializer of a local variable declared with the unknown-length type `Span<T>` (= `Span<T, unknown>`), THE Type_Checker SHALL accept the declaration as a permitted widening that erases the known length, exactly as `int[] x = new int[3]` erases the known length for arrays.
5. WHEN a Stackalloc_Expression result is the initializer of a local variable declared with an explicit `ReadOnlySpan<T>` (= `ReadOnlySpan<T, unknown>`) type, THE Type_Checker SHALL assign the expression the `Standard.ReadOnlySpan<T, Length>` type and accept the declaration.
6. THE Type_Checker SHALL accept a Stackalloc_Expression `stackalloc T[n]` only in the following target-type contexts: a `var` declaration (which infers `Standard.Span<T, Length>`), an explicit `Standard.Span<T, Length>` or `Standard.ReadOnlySpan<T, Length>` type, or the `Span<T>` / `ReadOnlySpan<T>` aliases (which map to `Span<T, unknown>` / `ReadOnlySpan<T, unknown>`). IF a Stackalloc_Expression `stackalloc T[n]` is used in any other context — including an incompatible context such as an operand of an arithmetic expression, or a target type that is not a `Standard.Span<T, Length>` family type nor a `Standard.ReadOnlySpan<T, Length>` family type (for example a target type of `int` or `Array<T, Length>`) — THEN THE Type_Checker SHALL reject the expression and emit a diagnostic error that identifies both the stackalloc result type and the expected target type and indicates the mismatch between them.
7. THE Type_Checker SHALL type-check the size expression `n` in `stackalloc T[n]` as an integer-typed expression, where an integer-typed expression is one whose type is `Standard.Int32` or an integer type implicitly convertible to `Standard.Int32`.
8. IF the size expression `n` in `stackalloc T[n]` is not an integer-typed expression as defined in criterion 7, THEN THE Type_Checker SHALL reject the expression and emit a diagnostic error that identifies the type of `n` and indicates that the stackalloc size must be an integer-typed expression.

### Requirement 5: Stackalloc Element Type Restriction

**User Story:** As a Mist developer, I want the compiler to reject `stackalloc` element types that cannot be safely placed in unmanaged stack memory, so that I get a clear Mist diagnostic rather than a downstream C# error.

#### Acceptance Criteria

1. WHEN type-checking a Stackalloc_Expression `stackalloc T[n]`, THE Type_Checker SHALL determine whether the element type `T` is a Stampable_Type using the existing stampability determination (`StructType.IsStampable`) that already gates bare `new T[size]` array creation, and SHALL accept the Stackalloc_Expression if and only if `T` is a Stampable_Type.
2. WHERE the element type `T` is a primitive struct (e.g., `int`, `bool`, `char`, `float`), THE Type_Checker SHALL accept the Stackalloc_Expression and assign it the type determined by Requirement 4.
3. WHERE the element type `T` is a pure struct whose instance fields are transitively all stampable, THE Type_Checker SHALL accept the Stackalloc_Expression and assign it the type determined by Requirement 4.
4. IF the element type `T` of a Stackalloc_Expression is a reference type, THEN THE Type_Checker SHALL reject the Stackalloc_Expression, SHALL NOT assign it a `Span<T>` or `ReadOnlySpan<T>` type, and SHALL emit a diagnostic error, anchored at the element type `T`, indicating that the element type is a reference type and cannot be stack-allocated.
5. IF the element type `T` of a Stackalloc_Expression is a struct with one or more non-stampable fields, THEN THE Type_Checker SHALL reject the Stackalloc_Expression, SHALL NOT assign it a `Span<T>` or `ReadOnlySpan<T>` type, and SHALL emit a diagnostic error, anchored at the element type `T`, indicating that the element type is not stampable.
6. IF the element type `T` of a Stackalloc_Expression is a ref struct, THEN THE Type_Checker SHALL reject the Stackalloc_Expression, SHALL NOT assign it a `Span<T>` or `ReadOnlySpan<T>` type, and SHALL emit a diagnostic error, anchored at the element type `T`, indicating that a ref struct cannot be used as a span element type.
7. IF the element type `T` of a Stackalloc_Expression is a generic type parameter that is not constrained to be a Stampable_Type, THEN THE Type_Checker SHALL reject the Stackalloc_Expression, SHALL NOT assign it a `Span<T>` or `ReadOnlySpan<T>` type, and SHALL emit a diagnostic error, anchored at the element type `T`, indicating that the element type cannot be determined to be stampable.

### Requirement 6: Stackalloc C# Code Generation

**User Story:** As a Mist developer, I want `stackalloc T[n]` to compile to correct C#, so that the program allocates on the stack and runs on the .NET runtime.

#### Acceptance Criteria

1. WHEN emitting a Stackalloc_Expression `stackalloc T[n]`, THE Emitter SHALL emit a C# `stackalloc T[n]` expression where the element type is emitted as the CLR type name resolved via the Backing_Type_Map (or the projected CLR name otherwise) and the size expression is emitted exactly once as its C# form.
2. WHEN a Stackalloc_Expression result is the initializer of a local variable of any `Span<T, Length>` family type (whether known-length `Span<T, N>` or unknown-length `Span<T>` = `Span<T, unknown>`), THE Emitter SHALL erase the `Length` type argument and emit a C# local of type `System.Span<T>` initialized to the `stackalloc` expression, analogous to `Array<T, Length>` erasing to `T[]`.
3. WHEN a Stackalloc_Expression result is target-typed to any `ReadOnlySpan<T, Length>` family type, THE Emitter SHALL erase the `Length` type argument and emit a C# local of type `System.ReadOnlySpan<T>` initialized to the `stackalloc` expression.
4. WHEN emitting an indexer read, `Length` access, or `Slice` call on a stackalloc-backed span, THE Emitter SHALL emit standard C# member access against the backing `System.Span<T>` / `System.ReadOnlySpan<T>` type.
5. WHEN emitting an indexer write on a stackalloc-backed span, THE Emitter SHALL emit standard C# element assignment against the backing `System.Span<T>` type, applicable to mutable `Span<T>` only.
6. WHEN emitting a `foreach` over a stackalloc-backed span, THE Emitter SHALL emit a C# `foreach` over the backing `System.Span<T>` / `System.ReadOnlySpan<T>` type.
7. THE generated C# SHALL compile with zero C# compiler warnings (CS-prefixed), consistent with the repository zero-warnings policy.

### Requirement 7: Span Backend Type Mapping and Identity Reconciliation

**User Story:** As a Mist developer, I want Mist's `Standard.Span<T, Length>` and the CLR `System.Span<T>` to interoperate as the same type, so that values flowing across the C# boundary (e.g., `int[].AsSpan()`) unify with the Mist Span type without conversions — handled the same way `Array<T, Length>` ↔ `T[]` already works despite the arity mismatch.

#### Acceptance Criteria

1. WHEN emitting a Mist type reference to any `Standard.Span<T, Length>` family type, THE Emitter SHALL erase the `Length` type argument and emit `System.Span<T>`, following the same length-erasure path that lowers `Array<T, Length>` to `T[]` (NOT the single-`BackingTypes`-entry ConstructedType path used for `Standard.Task`).
2. WHEN emitting a Mist type reference to any `Standard.ReadOnlySpan<T, Length>` family type, THE Emitter SHALL erase the `Length` type argument and emit `System.ReadOnlySpan<T>`.
3. WHEN the Assembly_Type_Provider projects a CLR member whose signature references `System.Span<T>` (CLR `System.Span\`1`) or `System.ReadOnlySpan<T>` (CLR `System.ReadOnlySpan\`1`), THE Type_Projector SHALL project it to the Mist type `Standard.Span<T, unknown>` / `Standard.ReadOnlySpan<T, unknown>`, the same way an inbound CLR array projects to an unknown/symbolic-length array type, and SHALL enforce that the projected Mist type's `Length` type argument is exactly `unknown` (not merely the correct span family type).
4. WHEN a C# API returns `System.Span<T>` (for example `int[].AsSpan()`) and the result is assigned to a Mist binding declared `Span<T>` (= `Span<T, unknown>`), THE Type_Checker SHALL treat both as the same logical type, reconciling the Mist element type with the CLR element type, and accept the assignment without an explicit conversion.
5. WHEN a Mist value of a `Span<T, Length>` family type is passed to a C# API parameter typed `System.Span<T>`, THE Type_Checker SHALL require successful reconciliation of the Mist element type with the CLR element type before accepting the argument, and on successful reconciliation SHALL accept the argument (the `Length` is erased at the boundary) without an explicit conversion; IF reconciliation of the element types fails, THEN THE Type_Checker SHALL reject the argument.
6. THE Type_Checker SHALL treat the Mist `ReadOnlySpan<T, Length>` family type and the CLR `System.ReadOnlySpan<T>` as the same logical type in BOTH directions, reconciling the Mist element type with the CLR element type and accepting without an explicit conversion in each: (a) the assignment direction — WHEN a C# API returns `System.ReadOnlySpan<T>` (for example `string.AsSpan()` returning `ReadOnlySpan<char>`) and the result is assigned to a Mist binding declared `ReadOnlySpan<T>`; and (b) the argument-passing direction — WHEN a Mist value of a `ReadOnlySpan<T, Length>` family type is passed to a C# API parameter typed `System.ReadOnlySpan<T>`.
7. IF the span kind differs (mutable `Span` versus read-only `ReadOnlySpan`) or the Mist and CLR element types cannot be reconciled, THEN THE Type_Checker SHALL reject the operation with a mismatch diagnostic, SHALL NOT treat the types as compatible, and SHALL leave the binding or call unmodified; specifically, when element types cannot be reconciled in the assignment direction (assigning a CLR span result to a Mist span binding) THE Type_Checker SHALL reject the assignment, and when they cannot be reconciled in the argument-passing direction THE Type_Checker SHALL reject the argument.

### Requirement 8: Frame-Escape and Lifetime Safety Analysis

**User Story:** As a Mist developer, I want the compiler to prevent a stackalloc-backed span from outliving its stack frame, so that I cannot create a dangling reference into freed stack memory.

#### Acceptance Criteria

1. THE Escape_Analyzer SHALL track, for each Span / ReadOnlySpan value, whether the value is backed by a Stackalloc_Expression in the current stack frame, propagating that backing through frame-local bindings, `Slice` results, and aliasing expressions that the analysis determines carry the stackalloc backing.
2. WHEN a value backed by a Stackalloc_Expression in the current frame is returned from a method (directly, or through a binding or expression that the analysis determines carries the stackalloc backing), THE Escape_Analyzer SHALL emit a Frame_Escape diagnostic at error severity, located at the offending return expression, whose message identifies the offending span value and states that a stackalloc-backed span cannot be returned, and compilation SHALL fail with no emitted C# for the affected compilation.
3. WHEN a value backed by a Stackalloc_Expression in the current frame is assigned or stored into a binding, parameter, field, or location whose lifetime exceeds the current frame, THE Escape_Analyzer SHALL emit a Frame_Escape diagnostic at error severity, located at the offending assignment expression, whose message identifies the offending span value and states that a stackalloc-backed span cannot escape its stack frame, and compilation SHALL fail with no emitted C# for the affected compilation.
4. THE Escape_Analyzer SHALL model Frame_Escape on C#'s ref-safety semantics, such that a Mist program rejected for frame escape corresponds to a program C# would reject for the equivalent `stackalloc`/`Span<T>` usage, and a Mist program accepted corresponds to one C# accepts. WHERE the Escape_Analyzer's modeling of C# ref-safety is incomplete or imperfect, THE Escape_Analyzer SHALL prioritize safety over strict C# parity by failing compilation when it detects a potential frame escape (no false-accept); the analysis is permitted to be stricter than C# but SHALL NEVER be looser.
5. WHERE a Stackalloc_Expression result is stored into a field of another ref struct, THE Escape_Analyzer SHALL propagate the stackalloc backing to the containing ref struct value and apply criteria 2 and 3 to that containing value, matching C#'s ref-safety treatment of ref struct fields.
6. THE Escape_Analyzer SHALL accept, without emitting any Frame_Escape diagnostic, a stackalloc-backed span that is used as a local variable within the allocating frame, read from, written through its indexer, sliced into another span used within the same frame, iterated with `foreach`, or passed as an argument to a method whose parameter accepts the span where the span does not escape beyond the duration of that call.
7. THE Frame_Escape diagnostics SHALL be assigned new diagnostic identifiers distinct from the existing Heap_Escape diagnostics (`MIST2002`, `MIST2003`, `MIST2004`, `MIST2005`, `MIST2007`, `RefStructCannotBeOptional`, `MIST_TC_0123`), following the established ref-struct `MIST20xx` convention (specific identifiers to be finalized in design), and SHALL reuse the existing ref-struct analysis machinery where applicable.

### Requirement 9: Continued Consumption of Arbitrary C# Ref Structs

**User Story:** As a Mist developer, I want existing consumption of ambient C# ref structs to keep working after this feature lands, so that the Span work does not regress general ref-struct interop.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider encounters a CLR ref struct type other than `System.Span<T>` / `System.ReadOnlySpan<T>` (a type with `IsByRefLike` that the inbound projection does not recognize as a `Standard.*` intrinsic), THE Type_Projector SHALL project it as a Mist struct with `StructType.IsRefStruct` set to true that does NOT reconcile to any `Standard.*` intrinsic identity.
2. WHEN a C# API returns `System.Span<T>` or `System.ReadOnlySpan<T>` (for example `int[].AsSpan()`, or `string.AsSpan()` returning `ReadOnlySpan<char>`), THE Type_Checker SHALL unify the result with the Mist `Standard.Span<T>` / `Standard.ReadOnlySpan<T>` type as the same `IType` identity and accept it without an explicit conversion, per Requirement 7.
3. IF a ref struct value (of `Standard.Span<T>`, `Standard.ReadOnlySpan<T>`, or any other ref struct type) reaches a Heap_Escape position, THEN THE compiler SHALL emit the existing diagnostic identified for that position: a field in a non-ref-struct type (`MIST2002`), a property in a non-ref-struct type (`MIST2003`), a generic type argument (`MIST2004`), a union constituent (`MIST2005`), interface boxing (`MIST2007`), an optional suffix (`RefStructCannotBeOptional`), or lambda capture (`MIST_TC_0123`), and compilation SHALL halt — the program is rejected and no output is produced.
4. WHEN a non-stackalloc-backed ref struct value is used within its own stack frame, THE Escape_Analyzer SHALL NOT emit a Frame_Escape diagnostic for that value.

### Requirement 10: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the `stackalloc` keyword to be syntax-highlighted correctly, so that I get the same visual cues as other expression keywords.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL recognize `stackalloc` as a keyword using whole-word/word-boundary matching and assign it the identical scope name the grammar already assigns to the `new` array-creation keyword (`keyword.other.mist`).
2. WHEN a `stackalloc T[n]` expression is encountered, THE TextMate_Grammar SHALL highlight the element type `T` following `stackalloc` with the identical type-name scope (`entity.name.type.mist`) used for the element type in an equivalent `new T[n]` expression.
3. WHEN the `stackalloc` rule is added, THE TextMate_Grammar SHALL leave the tokenization of all tokens unchanged versus pre-stackalloc behavior, except the `stackalloc` keyword token and its element-type token.
4. A syntax highlighting test file that is a valid `.mist` file SHALL exist under `Editor/mist-syntax-highlighting/tests/` containing at least one `stackalloc T[n]` expression, and the grammar SHALL tokenize `stackalloc` to the scope named in criterion 1 rather than to a default or unscoped token, following the format of the existing `.mist` test files in that directory.

### Requirement 11: Book Documentation

**User Story:** As a Mist developer, I want the language book to document stack-only types, `Span<T>`, `ReadOnlySpan<T>`, and `stackalloc`, so that I can learn the syntax, semantics, and safety rules from the official reference.

#### Acceptance Criteria

1. THE book SHALL include exactly one section covering `Span<T>`, `ReadOnlySpan<T>`, and the `stackalloc T[n]` expression with compiled code examples, placed in exactly one of the two locations: the Object Model "Ref Structs" subsection or the .NET interop / advanced features area.
2. WHERE the section is placed in the .NET interop / advanced features area, THE section SHALL be cross-referenced from the existing ref struct material.
3. THE book section prose SHALL cover each of the following topics: the `stackalloc T[n]` syntax, that the result is a `Span<T, Length>` (or `ReadOnlySpan<T, Length>` in a read-only context) where a literal size yields a known `Length` and a runtime size yields `unknown`, the `Span<T>` = `Span<T, unknown>` alias, the stampable-element-type restriction, the `Length` / indexer / `Slice` / `foreach` API, that indexing a known-length span is statically bounds-checked while an unknown-length span defers the upper bound to runtime (mirroring arrays), and the frame-escape rule that a stackalloc-backed span cannot be returned or stored where it outlives its frame.
4. THE book section SHALL include at least one compiled example for the `stackalloc T[n]` expression and at least one compiled example for the `Span<T>` / `ReadOnlySpan<T>` API usage.
5. All code examples SHALL be compiled snippets following the Book Snippet Policy: each `.mist` snippet under `book/snippets/` SHALL be a complete compilable program with a corresponding `.cs` file generated by the SnippetEmitter tool with zero errors and zero warnings.
6. THE book's `SUMMARY.md` SHALL contain a concrete linked entry referencing the new or updated section in the appropriate location.

### Requirement 12: CSHARP_FEATURES.md Update

**User Story:** As a Mist developer reviewing the feature coverage matrix, I want `CSHARP_FEATURES.md` to reflect that `stackalloc` and the Mist Span types are now supported, so that the document accurately tracks compiler capabilities.

#### Acceptance Criteria

1. THE `CSHARP_FEATURES.md` document SHALL change the existing single `stackalloc` row in the "Unsafe / Low-Level" table (currently `| stackalloc | ⛔ | Out of scope |`) so that its Status column is `✅` and its Notes column states that `stackalloc T[n]` produces a `Span<T>` (or `ReadOnlySpan<T>` in a read-only context) with frame-escape safety enforced, without introducing a duplicate `stackalloc` row in that table.
2. THE `CSHARP_FEATURES.md` document SHALL annotate the existing `Span<T> / Memory<T>` row in the "Unsafe / Low-Level" table to note the `Standard.Span<T>` / `Standard.ReadOnlySpan<T>` intrinsic types and `stackalloc` support, leaving that row's Feature and Status columns unchanged.
3. WHEN the `stackalloc` and `Span<T> / Memory<T>` rows are updated, THE `CSHARP_FEATURES.md` document SHALL retain all other rows of the "Unsafe / Low-Level" table (`unsafe` blocks, `Pointer types (int*)`, `fixed` statement, `sizeof`, `Function pointers`, `ref struct`) unchanged in their Feature, Status, and Notes columns.
4. THE `CSHARP_FEATURES.md` document SHALL update the feature-count summary table's "Unsafe / Low-Level" row so that the counts reflect `stackalloc` moving from out-of-scope to supported (the supported count increases by one and the out-of-scope count decreases by one relative to the current `0 | 0 | 8`).

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating stack-only types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. THE Sample_Application SHALL exist at `samples/StackOnlyTypes/Program.mist` with a corresponding `samples/StackOnlyTypes/mist.toml` project file whose `name` field is exactly the string `"StackOnlyTypes"`.
2. THE Sample_Application SHALL contain the proof code block below verbatim, where verbatim means byte-for-byte identical to the proof code block with no added, removed, reordered, or reworded lines.
3. WHEN the Sample_Application is compiled and run by the Mist compiler, THE produced stdout SHALL match the expected output below line-by-line exactly, where line-by-line exactly means the same line count, the same characters per line, and the same line order, with no extra or missing lines.
4. THE Sample_Application SHALL print, for each demonstrated pattern, a section header whose first three characters are `---` followed by result values to stdout, matching the format shown in the expected output.
5. THE Sample_Application SHALL include a `public static void RunDemo()` method that exercises all demonstrated patterns.
6. `samples/AllFeatures/Program.mist` SHALL call `StackOnlyTypes.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a reference entry `StackOnlyTypes = { path = "../StackOnlyTypes" }` to include the feature in the full-suite sample run.
7. WHEN the Sample_Application is compiled by the Mist compiler, THE compilation SHALL produce zero errors and zero warnings.
8. WHEN the Sample_Application is run by the Mist compiler, THE process SHALL terminate with exit code 0.

#### Proof Code

```mist
using Standard;

namespace StackOnlyTypes;

// PROOF: a pure struct with only primitive fields is stampable,
// so it is a legal stackalloc element type
public struct Point
{
    public int x;
    public int y;

    public Point(int a, int b)
    {
        this.x = a;
        this.y = b;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== StackOnlyTypes Sample ===");

        // PROOF: stackalloc int[n] parses, type-checks as Span<int>, and emits C# stackalloc
        // PROOF: Span<int> indexer write and read both work
        // PROOF: Span<int>.Length property resolves
        Console.WriteLine("--- stackalloc Span ---");
        Span<int> buffer = stackalloc int[4];
        buffer[0] = 10;
        buffer[1] = 20;
        buffer[2] = 30;
        buffer[3] = 40;
        Console.WriteLine($"buffer.Length={buffer.Length}");
        Console.WriteLine($"buffer[0]={buffer[0]}");
        Console.WriteLine($"buffer[3]={buffer[3]}");

        // PROOF: Span<int> is foreachable via the intrinsic Enumerator/GetEnumerator
        int total = 0;
        foreach (int v in buffer)
        {
            total = total + v;
        }
        Console.WriteLine($"total={total}");

        // PROOF: Span<T>.Slice returns a Span<T> over the SAME backing memory (aliasing)
        // PROOF: a write through the slice is visible through the original span
        Console.WriteLine("--- Slice Aliasing ---");
        var mid = buffer.Slice(1, 2);
        Console.WriteLine($"mid.Length={mid.Length}");
        Console.WriteLine($"mid[0]={mid[0]}");
        Console.WriteLine($"mid[1]={mid[1]}");
        mid[0] = 99;
        Console.WriteLine($"buffer[1]={buffer[1]}");

        // PROOF: stackalloc target-typed into a ReadOnlySpan<int> binding yields ReadOnlySpan<int>
        // PROOF: ReadOnlySpan<int>.Length and indexer read resolve; memory is zero-initialized
        Console.WriteLine("--- ReadOnlySpan ---");
        ReadOnlySpan<int> ro = stackalloc int[3];
        Console.WriteLine($"ro.Length={ro.Length}");
        Console.WriteLine($"ro[0]={ro[0]}");

        // PROOF: a stampable user struct (Point) is a legal stackalloc element type
        // PROOF: Span<Point> indexer write/read and field access on elements work
        Console.WriteLine("--- stackalloc Struct Element ---");
        Span<Point> points = stackalloc Point[2];
        points[0] = new Point(1, 2);
        points[1] = new Point(3, 4);
        Console.WriteLine($"points[0].x={points[0].x}");
        Console.WriteLine($"points[1].y={points[1].y}");

        // PROOF: System.Span<int> returned from int[].AsSpan() unifies with Standard.Span<int>
        // PROOF: the Array-precedent length erasure (Span<T,Length> -> System.Span<T>) holds for Span
        Console.WriteLine("--- Ambient AsSpan Identity ---");
        int[] arr = new int[3];
        arr[0] = 7;
        arr[1] = 8;
        arr[2] = 9;
        Span<int> view = arr.AsSpan();
        Console.WriteLine($"view.Length={view.Length}");
        Console.WriteLine($"view[2]={view[2]}");

        // PROOF: a stackalloc-backed span passed as a method argument is allowed
        // (used only for the duration of the call, does not escape the frame)
        Console.WriteLine("--- Span Argument ---");
        Console.WriteLine($"SumSpan={SumSpan(buffer)}");

        Console.WriteLine("=== StackOnlyTypes Done ===");
    }

    // PROOF: a Span<int> parameter is accepted; reading it inside the callee is allowed
    public static int SumSpan(Span<int> s)
    {
        int sum = 0;
        foreach (int v in s)
        {
            sum = sum + v;
        }
        return sum;
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== StackOnlyTypes Sample ===
--- stackalloc Span ---
buffer.Length=4
buffer[0]=10
buffer[3]=40
total=100
--- Slice Aliasing ---
mid.Length=2
mid[0]=20
mid[1]=30
buffer[1]=99
--- ReadOnlySpan ---
ro.Length=3
ro[0]=0
--- stackalloc Struct Element ---
points[0].x=1
points[1].y=4
--- Ambient AsSpan Identity ---
view.Length=3
view[2]=9
--- Span Argument ---
SumSpan=179
=== StackOnlyTypes Done ===
```

> Note on `SumSpan`: after the slice write `mid[0] = 99`, `buffer` holds `[10, 99, 30, 40]`, so `SumSpan(buffer) = 10 + 99 + 30 + 40 = 179`.

### Requirement 14: Diagnostic Quality

**User Story:** As a Mist developer, I want clear diagnostics for stack-only-type errors that follow existing conventions, so that I can quickly understand and fix mistakes.

#### Acceptance Criteria

1. THE diagnostics introduced by this feature SHALL use identifiers following the established Mist conventions (`MISTxxxx` for semantic-model diagnostics, `MIST_TC_xxxx` for type-checker diagnostics, `MIST_OWN_xxxx` for ownership diagnostics), consistent with the existing ref-struct diagnostic identifiers.
2. IF a Frame_Escape condition is detected, THEN THE Escape_Analyzer SHALL report a diagnostic at error severity that identifies the offending span value by its source location (file, line, and column of the offending construct) and states that a stackalloc-backed span cannot escape its stack frame, and the program SHALL be rejected (compilation does not produce output).
3. IF the element type of a Stackalloc_Expression violates the stampable-element-type restriction, THEN THE Type_Checker SHALL report a diagnostic at error severity that identifies the element type by name and states exactly one specific rejection reason from the set {reference type, non-stampable struct, ref struct}.
4. IF a Stackalloc_Expression result is used where the expected type is neither `Span<T>` nor `ReadOnlySpan<T>`, THEN THE Type_Checker SHALL report a diagnostic at error severity that identifies both the stackalloc result type and the expected type by name.
5. THE diagnostics introduced by this feature SHALL each be emitted with a non-empty source location covering the offending construct, so that two independent testers observing the same diagnostic agree on its identifier, severity, and reported span.
6. THE unknown-length span indexer behavior (where the upper bound is deferred to runtime, mirroring arrays) SHALL NEVER be reported as a compile-time diagnostic, even when a statically-detectable unsafe pattern is present; the new compile-time diagnostics introduced by this feature are limited to the stampable-element-type restriction (Requirement 5), frame escape (Requirement 8), stackalloc target-type mismatch (Requirement 4), and read-only-indexer assignment (Requirement 2).

## Notes for Design and Tasks Phases (Non-Functional Constraints)

These constraints are recorded here so the design and tasks phases respect repository steering. They are not themselves acceptance criteria but MUST shape the design and task plan.

- **Zero-warnings policy**: All new and generated code MUST build with zero warnings (`CS*`, `STORM*`, MSBuild/SDK). Any unavoidable warning MUST be raised with the user before proceeding.
- **Test process-spawning approval**: Any proposed test that spawns an external process (e.g., `dotnet run`, `Process.Start`) MUST be brought to the user for explicit approval during design/task planning, with the spawn count, estimated time cost, and a batching/in-process alternative presented. Process spawns MUST be batched (compile once / run once for many scenarios).
- **Test UTF-8 encoding**: Any test that spawns a compiled Mist program and reads stdout MUST configure UTF-8 encoding on both reader and writer sides per the UTF-8 encoding policy.
- **Sample app integrity**: Existing sample apps MUST continue to compile and run end-to-end after this feature lands; the new `StackOnlyTypes` sample MUST integrate into the `AllFeatures` orchestrator without breaking other samples.
- **Reuse over reinvention**: The frame-escape analysis MUST reuse or extend the existing ref-struct analysis machinery and the existing stampability determination rather than introducing parallel mechanisms.
- **Array bounds runtime-fallback gap (out of scope here)**: A separate CRITICAL item is logged in `project/TODO.md` — the array (and therefore Span) indexer upper bound is deferred to runtime for unknown-length types, so an out-of-range index can compile and fault at runtime. This spec INTENTIONALLY mirrors current array behavior (chosen by the user) and does NOT close that gap. The design and tasks phases MUST reuse the existing `Array` bounds machinery (`SpanBounds` via `ValidateArrayIndexRefinement`) and MUST NOT diverge from array semantics; closing the runtime-fallback gap is tracked separately.
