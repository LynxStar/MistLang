# Implementation Plan: Variadics (Type-Parameter Packs and Value-Level params)

## Overview

Mist compiles through a **shared front end** and then one of **two pipelines**, and that split is
the organizing fact of this plan:

- **Classic pipeline** (`Source/Mist/Driver/ClassicPipeline.cs`) is the **default** and the **only**
  pipeline that type-checks and emits. Stage 4 builds the legacy semantic model
  (`Mist.Semantics.ISemanticModel`) via `SemanticModelBuilder`; Stage 5 runs `TypeCheckerAnalyzer`
  against that model; Stage 6 emits C# from it through the `CSharpTranspiler`. This is the path that
  compiles and runs the runnable sample and the `Func`/`Action` stdlib bootstrap.
- **ProgramModel pipeline** (`Source/Mist/Driver/ExperimentalPipeline.cs`) is the migration target.
  It builds `IProgramModel`, but its analyze and emit stages are unfinished stubs (no output).

The variadics work built so far -- the pack model, classification, `Type[]`-valued type identity,
the `PackBinder` binding rule, and the type-position capability ladder -- lives entirely in
`Source/Mist/ProgramModel/` and is reachable only through the **non-emitting** ProgramModel pipeline.
The original plan never included a task for the missing foundation: **pack representation in the
legacy `Mist.Semantics` model and `SemanticModelBuilder`**. Without it, the type-checker and backend
work could never land on the emitting (classic) path.

Per the **Option A** decision, this plan now:

1. **Freezes the ProgramModel track** at what is built (resolution level). It does not emit; its
   deeper body-level checks await the v2 pipeline's analyze/emit stages. That completed work is
   preserved in **Part 1** with its status intact.
2. **Completes the full feature through the classic pipeline** -- the missing semantic-model pack
   representation, then `TypeCheckerAnalyzer`, then the `CSharpTranspiler`, then the `Func`/`Action`
   bootstrap, the runnable sample, the locked negative proof, the book chapter, and the
   `CSHARP_FEATURES.md` update. That is the entirety of **Part 2**, and it is what makes Requirement
   18 (runnable sample) and Requirement 15 (`Func`/`Action` collapse) actually compile and run.

The keystone reduction (a `params TArgs` pack is a dependent type parameter over comptime `Type[]`)
still holds on both sides: the classic semantic model extends the same dependent-type machinery
(`DependentTypeParameterType` / `DependentValueArgument`) that the ProgramModel track used.

### Testing approach (applies to every test task below)

- All tests run entirely **in-process** and spawn **zero** external processes. There is no
  `dotnet run`, no `dotnet exec`, and no `Process.Start` anywhere in the variadics test suite, and no
  test runs a compiled DLL.
- Property-based tests use the existing FsCheck setup with a minimum of 100 iterations per property.
  Each generates its scenarios up front and evaluates them against structured results obtained
  in-process, batching where a program must be compiled and run (compile once, run once, compare per
  scenario) per the workspace test-process policy.
- **Track-specific harness.** The classic track's tests use the shared, stateless `CompilerDriver`
  fixture from task 1.3, which wires `SemanticModelBuilder` + `TypeCheckerAnalyzer` +
  `CSharpTranspilerBackend` -- the correct harness for the emitting pipeline. The already-written
  ProgramModel-track property tests (Part 1) remain as the ProgramModel-track validation and exercise
  the ProgramModel construction stages directly. The classic track carries its **own** validation of
  every correctness property that concerns the model, type-checker, or backend (Properties 2-21 as
  applicable), validated through the classic pipeline. **Property 1 (print/parse round-trip) is
  shared-surface and is already covered by task 2.2**, so it is not re-validated per track.
- The runnable sample's stdout is exercised transitively by the pre-existing AllFeatures baseline
  regression test once `Variadics` is wired in (task 22.2). This feature adds no new
  process-spawning runner test.

---

## Part 1 - Completed Work (preserved)

The tasks in this part are **done** and MUST remain checked. They are relabeled to name their track;
their completion status is unchanged. Part 1 splits into the **shared foundation** (surface syntax and
diagnostics that feed both pipelines) and the **ProgramModel track**, which is now **frozen at the
resolution level**.

> **ProgramModel track is FROZEN.** The pack model, classification, `Type[]`-valued type identity, the
> `PackBinder` binding rule, and the type-position capability ladder are complete in
> `Source/Mist/ProgramModel/`, but that pipeline does not emit. Its deeper body-level checks (spread
> forwarding, tuple decomposition, `is`-narrowing, zip, whole-pack constraints applied inside bodies)
> are intentionally deferred to the v2 pipeline's analyze/emit stages. The feature is carried to
> emission by the classic pipeline in Part 2. No further ProgramModel-track work is scheduled here.

### Shared foundation (feeds both pipelines) - COMPLETE

- [x] 1. Shared foundation: diagnostics, grammar surface, editor highlighting, and in-process test harness
  - [x] 1.1 Add the `MIST_VAR_*` diagnostic descriptors
    - Create `Source/Mist/Common/Diagnostics/Descriptors/VariadicsDiagnostics.cs` defining
      `MIST_VAR_0001` through `MIST_VAR_0011` with the stage and message from the design's Error
      Handling table (`MIST_VAR_0001` Program model; `0002`-`0006`, `0009`, `0011` Type checker;
      `0007`, `0008` Program model; `0010` Backend)
    - Follow the existing `DiagnosticDescriptor` pattern used by `GenericsDiagnostics.cs`
    - _Requirements: 1.8, 3.5, 4.6, 6.3, 6.4, 6.6, 7.9, 8.7, 10.3, 10.5, 11.3, 12.5, 12.8, 13.5, 16.2, 16.4_

  - [x] 1.2 Extend the Storm grammar for pack declarations, spreads, and value-level params
    - In `Source/Mist/Grammar/Mist.storm` add a pack alternative to the `TypeParameter` rule that
      accepts an optional `params` keyword and a `Type[]` / `Type[N]` domain annotation (the
      `params TArgs`, `Type[]:TArgs`, and `Type[N]:TArgs` spellings)
    - Accept a postfix spread suffix `...` on an identifier operand in type-argument lists, argument
      lists, tuple-type element lists, base-type lists, and member-declaration type positions; do
      not accept `...` as a value-parameter declaration marker
    - Accept a leading `params` modifier on a value parameter whose declared type is a pack type
      parameter (`params TArgs args`) or an array type (`params string[] parts`)
    - Keep the flat type-argument list (`Foo<int, string>`) parsing through the existing
      `TypeArgumentList` rule; parse more than one `params` without a parse error (defer the
      at-most-one check to the program model), and regenerate the Storm parser
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [x] 1.3 Add the shared in-process test fixture and FsCheck generators
    - Add a shared, stateless `CompilerDriver` fixture (`MistLexer` + `MistParser` +
      `SemanticModelBuilder` + `TypeCheckerAnalyzer` + `CSharpTranspilerBackend`) reused across
      iterations, plus a stdlib-loading helper (see `Helpers/StdlibTestHelper.cs`). This fixture wires
      the **classic** (emitting) pipeline and is the harness for every classic-track test in Part 2
    - Add FsCheck generators for variadic surface forms: pack declarations in the `params` and
      `Type[]:` / `Type[N]:` spellings, value packs, value-level `params T[]`, plain `(TArgs...)`
      tuple parameters, spreads, and flat/bracketed argument lists
    - All compilation is in-process; zero process spawns
    - _Requirements: 2.6_

  - [x] 1.4 Write unit tests for grammar parsing of variadic surface forms
    - Parse each surface form in-process and assert the produced AST node kinds and modifiers
    - Cover the `params`/`Type[]:`/`Type[N]:` spellings, spread suffixes, value-level `params`, and
      the more-than-one-`params` case (parses without a parse error)
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [x] 1.5 Update the VS Code TextMate syntax highlighting grammar (originally task 19.1)
    - In `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`, highlight the `params`
      modifier as `storage.modifier.mist`, the spread suffix `...` with an operator/punctuation scope
      distinct from the identifier, and the `Type[]:` / `Type[N]:` domain annotation so the pack name
      is scoped `entity.name.type.parameter.mist`
    - Shared editor surface; independent of either compiler pipeline
    - _Requirements: 17.1, 17.2, 17.3_

- [x] 2. Pretty-printer round-trip for variadic surface forms (shared surface)
  - [x] 2.1 Implement pretty-printing for all variadic surface forms
    - In `Source/Mist/Parsing/MistPrettyPrinter.cs` emit `params TArgs`, `Type[]:TArgs`,
      `Type[N]:TArgs`, the spread operand followed by `...`, value-side `params TArgs name` and
      `params T[] name`, and preserve the flat-versus-bracketed choice for each argument list without
      normalizing one form into the other
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 2.2 Write property test for print/parse round-trip
    - **Property 1: Print/parse round-trip**
    - Generate valid ASTs containing pack declarations (both spellings), value packs, value-level
      `params`, plain `(TArgs...)` params, spreads, and flat/bracketed argument lists; pretty-print,
      re-parse in-process, and assert structural equality (node kinds, nesting, identifiers, element
      types, fixed-size domain lengths, modifiers, and the flat-versus-bracketed choice), ignoring
      trivia
    - Shared-surface coverage: Property 1 is not re-validated per track
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6**

  - [x] 2.3 Write unit tests for pretty-printer variadic forms
    - Assert exact printed text for each spelling, including fixed-size `Type[N]:` and both value
      `params` targets
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 3. Checkpoint - shared surface complete
  - Ensure all tests pass, ask the user if questions arise.

### ProgramModel track (FROZEN at resolution level) - COMPLETE

- [x] 4. Program model (ProgramModel track): pack type parameters, arity, and type identity
  - [x] 4.1 Represent a pack type parameter in the program model
    - In `Source/Mist/ProgramModel/Model/` and `Construction/TypeParameterPopulator.cs`, model a
      pack `TypeParameter` whose `Domain` is comptime `Type[]` (or `Type[N]`), recording that it is a
      pack and whether it is the list's single unbracketed `params` slot
    - Treat `params TArgs` as sugar for `Type[]:TArgs` and classify it as the Unbracketed_Pack; treat
      a lone `Type[]:` pack as an Unbracketed_Pack (inverse sugar); classify additional `Type[]:`
      packs as Bracketed_Pack
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 4.2 Enforce one Unbracketed_Pack per type-parameter list and compute type-side arity
    - Emit `MIST_VAR_0001` when a type-parameter list declares more than one Unbracketed_Pack
    - Compute arity as the count of declared slots, counting each `Type[]:` slot as exactly one
      regardless of the number of types it later absorbs
    - _Requirements: 3.4, 3.5, 3.6_

  - [x] 4.3 Implement constructed variadic type identity
    - In `Construction/TypeInterner.cs` (and the constructed-type model), identify a constructed
      variadic type by the pair (family, the comptime `Type[]` bound to the pack slot); make identity
      order-sensitive and arity-sensitive via elementwise `Type[]` equality reusing the existing
      dependent-type equality
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 4.4 Write property test for type identity (ProgramModel track)
    - **Property 2: Type identity reflects the pack value**
    - Generate a family and two comptime `Type[]` values; assert `Foo<v1>` and `Foo<v2>` are the same
      constructed type iff `v1` and `v2` are equal `Type[]` values (same length, elementwise-equal),
      comparing constructed-type identity in-process
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

  - [x] 4.5 Write unit tests for pack classification and arity-in-slots
    - Cover `params`/`Type[]:` classification, inverse sugar, the arity-5 example, and the
      `MIST_VAR_0001` duplicate-`params` case
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

- [x] 5. Program model (ProgramModel track): value side, spread-generated members, and declaration-kind coverage
  - [x] 5.1 Represent the value pack parameter and value-side arity
    - In `Construction/ParameterBuilder.cs`, give `params TArgs args` the tuple type `(TArgs...)`
      (equivalently `ValueTuple<TArgs>`), represent it as exactly one parameter slot marked with the
      Flattened_Call_Convention, and count method arity in slots (a value pack is one slot)
    - Represent a plain `(TArgs...) args` parameter (no `params`) as an ordinary single-tuple
      parameter
    - _Requirements: 7.1, 7.2, 7.7_

  - [x] 5.2 Enforce one value-side `params` per value-parameter list
    - Emit `MIST_VAR_0001` when a value-parameter list declares more than one value-side `params`
      parameter; require additional packs to be written as explicit `(TArgs...)` tuple parameters
    - _Requirements: 7.8, 7.9_

  - [x] 5.3 Represent the homogeneous value-level `params T[]`
    - Represent `params T[] xs` as a Value_Level_Params of array type `T[]` marked with the
      Flattened_Call_Convention, distinguished from a type-parameter-list `params` by position, and
      counted toward the one-`params`-per-list rule
    - _Requirements: 13.1, 13.4, 13.5_

  - [x] 5.4 Implement spread-generated members
    - In `Construction/MemberTypeBuilder.cs`, expand `public TArgs... Item;` into the aggregate stem
      member of type `(TArgs...)` plus 1-based projections `Item1..ItemN`, taking the declaration's
      accessibility and member kind (field or property); handle empty and length-1 packs and
      correlated spreads of the same pack
    - Reserve the stem namespace and emit `MIST_VAR_0007` on stem/name collision (or prefixing) and
      `MIST_VAR_0008` on an empty stem
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_

  - [x] 5.5 Accept packs on methods and every declaration kind
    - Accept a pack type-parameter list on `class`, `struct`, `abstract` type, type alias, and
      newtype declarations and on method type-parameter lists (touching the respective builders:
      `TypeBuilder`, `AbstractTypeBuilder`, `TypeAliasBuilder`, `NewtypeBuilder`, `MethodBuilder`),
      applying the same pack rules uniformly and emitting `MIST_VAR_0001` for a method list with more
      than one Unbracketed_Pack; a newtype clones the (possibly variadic) source type's members
    - _Requirements: 12.1, 12.4, 12.5, 22.1, 22.3_

  - [x] 5.6 Write property test for arity-in-slots (ProgramModel track)
    - **Property 8: Arity is slot count**
    - Generate type-parameter and value-parameter lists; assert declared arity equals the number of
      declared slots, counting each pack (type-array `params`/`Type[]:` or value pack) as one slot
    - **Validates: Requirements 3.6, 7.2**

  - [x] 5.7 Write property test for one sugared pack per list (ProgramModel track)
    - **Property 9: One sugared pack per list**
    - Generate type-parameter, method type-parameter, and value-parameter lists with one versus two+
      sugared `params` packs; assert exactly one is accepted and two or more raise `MIST_VAR_0001`,
      counting value packs and homogeneous `params T[]` alike
    - **Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5**

  - [x] 5.8 Write property test for generated-member count (ProgramModel track)
    - **Property 6: Generated-member count equals pack length**
    - Generate a member spread with stem S over a pack of length N; assert the aggregate `S : (TArgs...)`
      plus exactly N projections S1..SN with the correct element types and accessibility/kind; empty
      pack yields the empty aggregate and zero projections; two spreads of the same pack align at N
    - **Validates: Requirements 10.1, 10.6, 10.7, 10.8**

  - [x] 5.9 Write unit tests for value-side representation and spread-member edge cases
    - Cover value pack single-slot arity, plain tuple parameter, empty stem (`MIST_VAR_0008`), stem
      collision (`MIST_VAR_0007`), and the length-1 projection case
    - _Requirements: 7.1, 7.7, 10.4, 10.5, 10.7_

- [x] 6. Checkpoint - ProgramModel model complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Type resolution (ProgramModel track): the shared binding rule (PackBinder)
  - [x] 7.1 Implement the flat-to-bracketed binding rule for type and value lists
    - In `Construction/PackBinder.cs`, bind fixed scalar and Bracketed_Pack slots inward from both
      ends and assign the contiguous middle to the single Unbracketed_Pack (leading, middle, or
      trailing), allowing an empty pack; treat the flat and bracketed forms as the same type
    - Emit `MIST_VAR_0005` when a flat argument list has too few arguments to fill the fixed and
      bracketed slots; apply the same rule to value-parameter binding (collect the middle into
      `(TArgs...)`)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.4, 7.6_

  - [x] 7.2 Write property test for binding determinism and totality (ProgramModel track)
    - **Property 4: Binding determinism and totality**
    - Generate slot lists with at most one Unbracketed_Pack and argument lists; assert a unique
      assignment (ends bind inward, pack takes the middle) when length is at least fixed+bracketed,
      and `MIST_VAR_0005` below that minimum
    - **Validates: Requirements 4, 3**

  - [x] 7.3 Write property test for flat/bracketed equivalence (ProgramModel track)
    - **Property 3: Flat/bracketed equivalence**
    - Generate a slot list with one Unbracketed_Pack and an argument list; assert the flat form and
      its bracketed canonical form bind to identical slot assignments and denote the same type
    - **Validates: Requirements 3, 4**

  - [x] 7.4 Write unit tests for binding edge cases
    - Cover the arity-5 middle-and-bracketed construction, the `Between<TFirst, params TMid, TLast>`
      example, and the too-few-arguments `MIST_VAR_0005` case
    - _Requirements: 4.5, 4.6_

- [x] 8. Type resolution (ProgramModel track): the type-position capability ladder
  - [x] 8.1 Implement pack position semantics and the capability ladder (ProgramModel resolver)
    - In the ProgramModel resolver, treat a pack identifier in expression position as the comptime
      `Type[]` value (with `.Length`, iteration, spread available regardless of proven length); emit
      `MIST_VAR_0002` in bare type position; emit `MIST_VAR_0003` for indexing an unknown-length
      pack; allow `Type[N]` indexing in range and emit `MIST_VAR_0004` out of range; restore in-range
      indexing after guard narrowing to `Type[N]`
    - This is the type-position ladder only. The value-side and body-level semantics are deferred with
      the rest of the ProgramModel track; the classic pipeline carries them in Part 2 (task 13)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

---

## Part 2 - Classic Pipeline Completion Plan (remaining)

Every task below is **new/not-done** and targets the **classic (emitting) pipeline**. The classic
semantic-model pack representation (tasks 9-10) is the missing foundation that the original plan
lacked; it mirrors the completed ProgramModel model work (tasks 4-5) but over
`Mist.Semantics.ISemanticModel`. The type checker (tasks 12-17) then consumes that model, and the
backend (tasks 19-20) emits from it, making the runnable sample and the `Func`/`Action` collapse
compile and run. Checkpoints sit at the semantic-model, type-checker, and backend boundaries, plus a
final checkpoint.

- [x] 9. Classic semantic model: pack type parameters, arity, and type identity
  - [x] 9.1 Represent a pack type parameter in the legacy semantic model
    - In `Source/Mist/Semantics/Model/Types/TypeParameterType.cs` and `DependentTypeParameterType.cs`
      (plus `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`), model a semantic pack type
      parameter whose value domain is comptime `Type[]` (or `Type[N]`), flagged as a pack and, where
      applicable, marked as the list's single unbracketed `params` slot; keep ordinal, name, and
      constraints otherwise unchanged, reusing the existing dependent-type-parameter machinery
      (`Types/DependentValueArgument.cs`)
    - Treat `params TArgs` as sugar for `Type[]:TArgs` and classify it as the Unbracketed_Pack; treat
      a lone `Type[]:` pack as an Unbracketed_Pack (inverse sugar); classify additional `Type[]:`
      packs as Bracketed_Pack. Mirrors the completed ProgramModel task 4.1 over `ISemanticModel`
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 9.2 Enforce one Unbracketed_Pack per type-parameter list and compute type-side arity (semantic model)
    - In `SemanticModelBuilder.cs`, emit `MIST_VAR_0001` when a type-parameter list declares more than
      one Unbracketed_Pack; compute arity as the count of declared slots, counting each `Type[]:` slot
      as exactly one regardless of the number of types it later absorbs
    - _Requirements: 3.4, 3.5, 3.6_

  - [x] 9.3 Implement constructed variadic type identity in the semantic type system
    - In `Source/Mist/Semantics/Model/Types/ConstructedType.cs`, identify a constructed variadic type
      by the pair (family, the comptime `Type[]` bound to the pack slot); make identity order- and
      arity-sensitive via elementwise `Type[]` equality, reusing the dependent-type equality already
      used for `DependentValueArgument`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x]* 9.4 Write property test for semantic-model type identity (classic pipeline)
    - **Property 2: Type identity reflects the pack value**
    - Via the shared classic `CompilerDriver` / `SemanticModelBuilder`, generate a family and two
      comptime `Type[]` values; assert `Foo<v1>` and `Foo<v2>` are the same constructed type iff `v1`
      and `v2` are equal `Type[]` values (same length, elementwise-equal), comparing legacy
      constructed-type identity in-process
    - **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

  - [x]* 9.5 Write unit tests for semantic pack classification and arity-in-slots
    - Cover `params`/`Type[]:` classification, inverse sugar, the arity-5 example, and the
      `MIST_VAR_0001` duplicate-`params` case over the legacy semantic model
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_

- [x] 10. Classic semantic model: value side, spread-generated members, and declaration-kind coverage
  - [x] 10.1 Represent the value pack parameter and value-side arity (semantic model)
    - In `Source/Mist/Semantics/Model/ParameterSignature.cs` and `SemanticModelBuilder.cs`, give
      `params TArgs args` the tuple type `(TArgs...)` (`ValueTuple<TArgs>`, via `Types/TupleType.cs`),
      represent it as exactly one parameter slot carrying the Flattened_Call_Convention (see
      `Model/ClrCallingConvention.cs`), and count method arity in slots; represent a plain
      `(TArgs...) args` parameter (no `params`) as an ordinary single-tuple parameter
    - _Requirements: 7.1, 7.2, 7.7_

  - [x] 10.2 Enforce one value-side `params` per value-parameter list (semantic model)
    - In `SemanticModelBuilder.cs`, emit `MIST_VAR_0001` when a value-parameter list declares more
      than one value-side `params` parameter; require additional packs to be written as explicit
      `(TArgs...)` tuple parameters
    - _Requirements: 7.8, 7.9_

  - [x] 10.3 Represent the homogeneous value-level `params T[]` (semantic model)
    - Represent `params T[] xs` as a Value_Level_Params of array type `T[]` (via `Types/ArrayType.cs`)
      marked with the Flattened_Call_Convention, distinguished from a type-parameter-list `params` by
      position, and counted toward the one-`params`-per-list rule
    - _Requirements: 13.1, 13.4, 13.5_

  - [x] 10.4 Implement spread-generated members (semantic model)
    - In `SemanticModelBuilder.cs`, expand `public TArgs... Item;` into the aggregate stem member of
      type `(TArgs...)` plus 1-based projections `Item1..ItemN`, taking the declaration's
      accessibility and member kind (field or property); handle empty and length-1 packs and
      correlated spreads of the same pack
    - Reserve the stem namespace and emit `MIST_VAR_0007` on stem/name collision (or prefixing) and
      `MIST_VAR_0008` on an empty stem
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_

  - [x] 10.5 Accept packs on methods and every declaration kind (semantic model)
    - In `SemanticModelBuilder.cs`, accept a pack type-parameter list on `class`, `struct`, `abstract`
      type, type alias, and newtype declarations and on method type-parameter lists, applying the same
      pack rules uniformly and emitting `MIST_VAR_0001` for a method list with more than one
      Unbracketed_Pack; a newtype clones the (possibly variadic) source type's members
    - _Requirements: 12.1, 12.4, 12.5, 22.1, 22.3_

  - [x]* 10.6 Write property test for arity-in-slots (classic pipeline)
    - **Property 8: Arity is slot count**
    - Via the classic `CompilerDriver`, generate type-parameter and value-parameter lists; assert
      declared arity equals the number of declared slots, counting each pack (type-array
      `params`/`Type[]:` or value pack) as one slot
    - **Validates: Requirements 3.6, 7.2**

  - [x]* 10.7 Write property test for one sugared pack per list (classic pipeline)
    - **Property 9: One sugared pack per list**
    - Via the classic `CompilerDriver`, generate type-parameter, method type-parameter, and
      value-parameter lists with one versus two+ sugared `params` packs; assert exactly one is
      accepted and two or more raise `MIST_VAR_0001`, counting value packs and homogeneous
      `params T[]` alike
    - **Validates: Requirements 3.4, 3.5, 7.8, 7.9, 12.4, 12.5, 13.5**

  - [x]* 10.8 Write property test for generated-member count (classic pipeline)
    - **Property 6: Generated-member count equals pack length**
    - Via the classic `CompilerDriver`, generate a member spread with stem S over a pack of length N;
      assert the aggregate `S : (TArgs...)` plus exactly N projections S1..SN with the correct element
      types and accessibility/kind; empty pack yields the empty aggregate and zero projections; two
      spreads of the same pack align at N
    - **Validates: Requirements 10.1, 10.6, 10.7, 10.8**

  - [x]* 10.9 Write unit tests for value-side representation and spread-member edge cases (semantic model)
    - Cover value pack single-slot arity, plain tuple parameter, empty stem (`MIST_VAR_0008`), stem
      collision (`MIST_VAR_0007`), and the length-1 projection case over the legacy model
    - _Requirements: 7.1, 7.7, 10.4, 10.5, 10.7_

- [x] 11. Checkpoint - classic semantic-model boundary
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Classic type checker: the shared binding rule
  - [x] 12.1 Implement the flat-to-bracketed binding rule over `ISemanticModel`
    - In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, bind fixed scalar and Bracketed_Pack
      slots inward from both ends and assign the contiguous middle to the single Unbracketed_Pack
      (leading, middle, or trailing), allowing an empty pack; treat the flat and bracketed forms as
      the same type
    - Emit `MIST_VAR_0005` when a flat argument list has too few arguments to fill the fixed and
      bracketed slots; apply the same rule to value-parameter binding (collect the middle into
      `(TArgs...)`)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 7.4, 7.6_

  - [x]* 12.2 Write property test for binding determinism and totality (classic pipeline)
    - **Property 4: Binding determinism and totality**
    - Generate slot lists with at most one Unbracketed_Pack and argument lists; assert a unique
      assignment (ends bind inward, pack takes the middle) when length is at least fixed+bracketed,
      and `MIST_VAR_0005` below that minimum
    - **Validates: Requirements 4, 3**

  - [x]* 12.3 Write property test for flat/bracketed equivalence (classic pipeline)
    - **Property 3: Flat/bracketed equivalence**
    - Generate a slot list with one Unbracketed_Pack and an argument list; assert the flat form and
      its bracketed canonical form bind to identical slot assignments and denote the same type
    - **Validates: Requirements 3, 4**

  - [x]* 12.4 Write unit tests for binding edge cases (classic pipeline)
    - Cover the arity-5 middle-and-bracketed construction, the `Between<TFirst, params TMid, TLast>`
      example, and the too-few-arguments `MIST_VAR_0005` case
    - _Requirements: 4.5, 4.6_

- [x] 13. Classic type checker: position semantics, capability ladder, and spread positions
  - [x] 13.1 Implement pack position semantics and the capability ladder over `ISemanticModel`
    - In `TypeCheckerAnalyzer.cs`, treat a pack identifier in expression position as the comptime
      `Type[]` value (with `.Length`, iteration, spread available regardless of proven length); emit
      `MIST_VAR_0002` in bare type position; emit `MIST_VAR_0003` for indexing an unknown-length pack;
      allow `Type[N]` indexing in range and emit `MIST_VAR_0004` out of range; restore in-range
      indexing after guard narrowing to `Type[N]`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [x] 13.2 Implement type-side spread expansion in every designated position
    - Expand a `Type[]` spread into one entry per element (empty -> zero) in type-argument lists,
      field/member type positions, base-type lists (including implemented abstract types), and
      tuple-type element lists (including tuple return types); reject a value-parameter position as a
      spread position
    - _Requirements: 6.8, 8.1, 9.1, 9.2, 9.3, 9.4, 9.5_

  - [x]* 13.3 Write property test for position-semantics soundness (classic pipeline)
    - **Property 10: Position-semantics soundness**
    - Generate pack identifiers in each position; assert expression-position value, `MIST_VAR_0002`
      bare-type, `MIST_VAR_0003` unknown-length index, `Type[N]` index accepted iff in range else
      `MIST_VAR_0004`, and narrowing restores in-range indexing
    - **Validates: Requirements 6.1, 6.3, 6.4, 6.5, 6.6, 6.7**

  - [x]* 13.4 Write property test for spread-accepting positions (classic pipeline)
    - **Property 17: Spread accepted in every designated position**
    - Generate `Type[]` spreads in type-argument, field/member, base-type, and tuple-type positions;
      assert each contributes exactly one entry per element with no position-attributable diagnostic,
      and that no value-parameter position is a spread position
    - **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**

  - [x]* 13.5 Write unit tests for the capability ladder (classic pipeline)
    - Cover bare-type rejection, unproven-index rejection, out-of-bounds fixed-size index, and
      guard-narrowing recovery
    - _Requirements: 6.3, 6.4, 6.6, 6.7_

- [x] 14. Classic type checker: value-side spread, tuple decomposition, and zip
  - [x] 14.1 Implement value-side spread as general tuple decomposition
    - In `TypeCheckerAnalyzer.cs`, decompose any tuple value `x...` into argument slots (empty ->
      zero), well-typed iff the ordered element types are positionally assignable to the target slots,
      else `MIST_VAR_0011`; treat `args...` as the pack-originated case; allow re-spread of
      spread-generated members (`Item...`)
    - _Requirements: 7.5, 8.2, 8.3, 8.4, 10.9_

  - [x] 14.2 Implement zip parallel expansion
    - Expand two operands element by element into N paired positions only when proven equal length
      (same origin or otherwise established); emit `MIST_VAR_0006` otherwise
    - _Requirements: 8.5, 8.6, 8.7_

  - [x]* 14.3 Write property test for spread width (classic pipeline)
    - **Property 5: Spread width equals source length**
    - Generate a `Type[]` of length N and a tuple of N elements; assert spreading yields exactly N
      ordered type slots / argument slots and an empty operand yields zero
    - **Validates: Requirements 6.8, 8**

  - [x]* 14.4 Write property test for tuple-decomposition well-typedness (classic pipeline)
    - **Property 11: Tuple-decomposition well-typedness**
    - Generate a tuple value and a target slot list; assert `x...` is well-typed iff element types are
      positionally assignable, else `MIST_VAR_0011`, for every tuple whether or not pack-originated
    - **Validates: Requirements 7.5, 8.4**

  - [x]* 14.5 Write property test for zip length proof (classic pipeline)
    - **Property 12: Zip requires provably-equal length**
    - Generate two spread operands; assert a zip is accepted iff proven equal length (yielding N
      paired positions) and otherwise raises `MIST_VAR_0006`
    - **Validates: Requirements 8.5, 8.6, 8.7**

- [ ] 15. Checkpoint - classic type-checker core boundary
  - Ensure all tests pass, ask the user if questions arise.

- [x] 16. Classic type checker: constraints, generic-body forwarding, and narrowing
  - [x] 16.1 Implement whole-pack constraints elementwise
    - In `TypeCheckerAnalyzer.cs` (with `Model/Types/TypeParameterConstraints.cs`), require every
      element type to satisfy a `where TArgs : C` bound, permit constrained members on each element
      and each `args[i]`, emit `MIST_VAR_0009` per failing element (treated as an error type so
      analysis continues), and treat an empty pack as vacuously satisfying the constraint
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

  - [x] 16.2 Implement generic-body open-generic checking and provable forwarding
    - Check a variadic body once for every instantiation the signature permits; accept `args...`
      forwarding only into a destination provably `(TArgs...)`-compatible for every `TArgs` (another
      `params TArgs` / `(TArgs...)` parameter or a `Func<TArgs..., R>` value); emit `MIST_VAR_0011`
      when forwarding a symbolic pack into a fixed-arity destination
    - _Requirements: 12.2, 12.3, 12.6, 12.7, 12.8_

  - [x] 16.3 Implement value-pack narrowing that recovers forwarding
    - Narrow `args` to a concrete tuple type `T` inside an `args is T x` branch so that spreading
      `x...` into a matching destination is well-typed there; emit `MIST_VAR_0011` for a wrong-shape
      forward within such a branch; support different shapes in different branches
    - _Requirements: 12.9, 6.7_

  - [x]* 16.4 Write property test for whole-pack constraint enforcement (classic pipeline)
    - **Property 16: Whole-pack constraint enforcement**
    - Generate a pack with constraint C and a non-empty binding; assert well-typed iff every element
      satisfies C (constrained members usable on each element and `args[i]`), else `MIST_VAR_0009` per
      failing element
    - **Validates: Requirements 11.1, 11.2, 11.3**

  - [x]* 16.5 Write property test for generic destination enforcement (classic pipeline)
    - **Property 19: Spread respects destination typing (generic enforcement)**
    - Assert a symbolic `args...` is accepted iff the destination provably accepts `(TArgs...)` for
      every `TArgs`, and rejected with `MIST_VAR_0011` for a fixed-arity destination (for example
      `Sum3(int, int, int)`) even though it would succeed for one instantiation
    - **Validates: Requirements 8.4, 12.6**

  - [x]* 16.6 Write property test for narrowing recovery (classic pipeline)
    - **Property 20: Narrowing recovers provable forwarding**
    - Assert that inside `args is T x` the pack narrows to `T` so `x...` into a matching destination is
      well-typed, that different-shape branches enable their own forwarding, and that a mismatched
      forward inside a branch is still `MIST_VAR_0011`
    - **Validates: Requirements 6.7, 12.9**

- [x] 17. Classic type checker: inference, value-level params, Func invocation, and variadic abstract types
  - [x] 17.1 Extend positional unification for pack inference
    - In `Source/Mist/Analysis/Builtin/TypeArgumentInferenceEngine.cs` (with `InferenceContext.cs` /
      `InferenceResult.cs`), infer a sole pack as the ordered static types of the value arguments;
      solve fixed scalar type parameters and the pack together via the binding rule; bind from
      explicit bracketed type arguments when supplied; infer the empty pack for zero arguments
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

  - [x] 17.2 Implement value-level `params T[]` collection semantics
    - In `TypeCheckerAnalyzer.cs` / `OverloadResolver.cs`, collect trailing arguments assignable to
      `T` in source order into a single `T[]` and bind it; bind a non-null length-0 `T[]` for zero
      trailing arguments; reject a non-assignable trailing argument as an ordinary type error and
      leave the parameter unbound
    - _Requirements: 13.2, 13.3, 13.7_

  - [x] 17.3 Generalize `Func` invocation to a single rule
    - In `TypeCheckerAnalyzer.cs`, replace per-arity `Func` invocation recognition with one rule:
      invoking a constructed `Func<params TArgs, TResult>` is well-typed with result type `TResult`
      iff the argument list has the same length as `TArgs` and each argument is assignable to the
      corresponding element; reject length or assignability mismatches; apply uniformly at every arity
      including the empty pack
    - _Requirements: 15.3, 15.4, 15.5, 15.6, 15.7, 15.8_

  - [x] 17.4 Implement variadic abstract type implementation checking
    - Permit implementing a variadic abstract type either at a concrete arity (variadic member slots
      monomorphize to that arity) or generically by spreading the implementer's own pack into the
      base-type list, reusing the base-type-list spread
    - _Requirements: 22.2_

  - [x]* 17.5 Write property test for inference agreement (classic pipeline)
    - **Property 7: Inference agrees with the explicit form**
    - Generate a variadic method and value arguments; assert inferring the pack (with any fixed type
      parameters) yields the same instantiation as the explicit bracketed form
    - **Validates: Requirements 14.1, 14.2, 14.3, 14.4**

  - [x]* 17.6 Write property test for value-level params collection (classic pipeline)
    - **Property 18: Homogeneous value-level params collection**
    - Generate invocations of a `params T[]` method; assert trailing arguments assignable to `T` are
      collected left-to-right into a single `T[]` (empty -> non-null length-0), and a non-assignable
      trailing argument is rejected with the parameter left unbound
    - **Validates: Requirements 13.2, 13.3, 13.7**

  - [x]* 17.7 Write property test for Func invocation generalization (classic pipeline)
    - **Property 15: Func invocation generalization**
    - Generate a constructed `Func<params TArgs, TResult>` and an argument list; assert the invocation
      is well-typed with result `TResult` iff length matches `TArgs` and each argument is assignable,
      at every arity including the empty pack
    - **Validates: Requirements 15.6, 15.7, 15.8**

  - [x]* 17.8 Write property test for pack-bearing declaration kinds (classic pipeline)
    - **Property 21: Packs are accepted on every declaration kind**
    - Generate pack type-parameter lists on `class`, `struct`, `abstract` type, type alias, newtype,
      and method lists; assert each is accepted under the same pack rules, and that a variadic
      abstract type can be declared and implemented at a concrete arity and generically
      (base-type-list spread). Cover the variadic newtype here (it is not part of the runnable sample)
    - **Validates: Requirements 22.1, 22.2**

- [x] 18. Checkpoint - classic type-checker complete
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 19. Classic backend: monomorphization, Func/Action + ValueTuple mapping, and the arity ceiling
  - [x] 19.1 Implement per-arity monomorphization and CLR mapping
    - In `Source/Mist/Backends/CSharpTranspiler/` (`CSharpEmitter.cs`, `CSharpTypeMap.cs`,
      `IntrinsicTypeMap.cs`), expand spreads to concrete width per used arity; map
      `Func<params TArgs, TResult>` to the matching `System.Func<...>` (empty pack ->
      `System.Func<TResult>`); map a value pack `(TArgs...)` to `System.ValueTuple`, nesting when over
      7 elements; map spread-generated members to `Item1..ItemN`
    - _Requirements: 15.1, 16.3, 16.5_

  - [ ] 19.2 Emit value-level `params T[]` as a C# params array
    - Emit a Value_Level_Params parameter as a C# `params T[]` array parameter so the callee receives
      a real array
    - _Requirements: 13.6_

  - [ ] 19.3 Implement the backend arity-ceiling diagnostic
    - Emit `MIST_VAR_0010` when a construction exceeds target CLR capacity (`System.Func`/
      `System.Action` over 16 inputs, or a non-nestable `System.ValueTuple` over 7), identifying the
      construction and exceeded capacity; impose no front-end limit and emit the C# shape when the
      construction fits
    - _Requirements: 16.1, 16.2, 16.4, 16.5_

  - [ ]* 19.4 Write property test for unbounded front end and backend ceiling (classic pipeline)
    - **Property 14: Unbounded front end, backend ceiling**
    - Generate packs of any element count; assert grammar/semantic-model/type-checker never reject on
      length and only the backend emits `MIST_VAR_0010` (Func/Action over 16, non-nestable ValueTuple
      over 7), while a nestable ValueTuple over 7 is represented by nesting
    - **Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5**

  - [ ]* 19.5 Write property test for empty-pack coherence (classic pipeline)
    - **Property 13: Empty-pack coherence**
    - Assert an empty pack yields an arity-0 construction, zero spread slots, zero generated members,
      the zero-argument callable `Func<TResult>`, and vacuous whole-pack-constraint satisfaction (no
      `MIST_VAR_0009`), verified end-to-end through the classic pipeline
    - **Validates: Requirements 4.3, 6.8, 10.6, 11.4, 15.4**

  - [ ]* 19.6 Write unit tests for the arity ceiling and ValueTuple nesting (classic pipeline)
    - Cover a 17-input `Func` (`MIST_VAR_0010`), an 8-element value pack nested into `ValueTuple`, and
      a boundary 16-input `Func` that emits cleanly
    - _Requirements: 16.2, 16.3_

- [ ] 20. Func / Action bootstrap rewrite (classic pipeline)
  - [ ] 20.1 Rewrite `Func.mist` to a single variadic declaration
    - Replace the 17 hand-written classes in `Source/Standard/Bootstrap/Func.mist` with one
      `intrinsic public class Func<params TArgs, TResult> { }` (trailing slot is the return type; the
      pack absorbs the parameter types)
    - _Requirements: 15.1_

  - [ ] 20.2 Rewrite `Action.mist` to a single variadic alias
    - Replace the 17 hand-written aliases in `Source/Standard/Bootstrap/Action.mist` with one
      `type Action<params TArgs> = Func<TArgs..., void>;`
    - _Requirements: 15.2_

  - [ ] 20.3 Write in-process integration test for Func/Action across arities (classic pipeline)
    - Compile in-process via the shared classic `CompilerDriver` and assert `Func<int, string>` binds
      `TArgs = [int]` / `TResult = string`, `Func<string>` binds `TArgs = []`, `Action<int, string>`
      resolves to `Func<int, string, void>`, and invocation type-checks and emits at each arity
      including the empty pack (no process spawn)
    - _Requirements: 15.3, 15.4, 15.5, 15.6, 15.7, 15.8_

- [ ] 21. Checkpoint - classic backend and bootstrap boundary
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 22. Sample application (classic pipeline)
  - [ ] 22.1 Create the Variadics sample project
    - Add `samples/Variadics/Program.mist` containing the Requirement 18 Proof Code verbatim
      (ASCII-only, Mist array-literal syntax, Allman braces) and `samples/Variadics/mist.toml` with
      `name = "Variadics"`
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.7_

  - [ ] 22.2 Wire Variadics into the AllFeatures full-suite sample
    - Add a `Variadics = { path = "../Variadics" }` reference entry to `samples/AllFeatures/mist.toml`
      and a `Variadics.Program.RunDemo();` call in `samples/AllFeatures/Program.mist`, following the
      existing AllFeatures integration convention; update `samples/__baselines__/AllFeatures.out` with
      the Variadics section so the pre-existing AllFeatures baseline regression test continues to pass
    - Do not add a new process-spawning runner test; the sample's runtime output is exercised
      transitively by the existing AllFeatures baseline test
    - _Requirements: 18.5_

  - [ ] 22.3 Verify the sample compiles cleanly in-process through the classic pipeline
    - Using the shared classic `CompilerDriver`, compile `samples/Variadics` in-process and assert
      zero errors and zero warnings (per the zero-warnings policy); do not spawn a process to run it
    - _Requirements: 18.6_

- [ ] 23. Negative proof - spread destination enforcement (locked gate, classic pipeline)
  - [ ] 23.1 Add the classic type-checker rejection test for the Requirement 21 Negative Proof Code
    - Add a test fixture containing the Requirement 21 Negative Proof Code verbatim (the
      `ForwardSumBad<params TArgs>` / `Sum3(args...)` case); compile in-process via the shared classic
      `CompilerDriver` and assert compilation fails with `MIST_VAR_0011` reported on `Sum3(args...)`;
      the test fails if the fixture compiles successfully
    - Keep this fixture out of the runnable sample at `samples/Variadics/Program.mist`
    - _Requirements: 21.1, 21.2, 21.3_

- [ ] 24. Book documentation
  - [ ] 24.1 Add the variadics book chapter
    - Add a variadics chapter under `book/src/type-system/` covering the `params`/`Type[]:` spellings
      and the pack-as-dependent-type keystone, the binding rule (leading/middle/trailing), order- and
      arity-sensitive identity, position semantics and the capability ladder, the value side
      (`(TArgs...)`, `args...` forwarding), spread-generated members, whole-pack constraints,
      value-level `params`, call-site inference, and the `Func`/`Action` rewrite
    - Use compiled snippets under `book/snippets/type-system/variadics/` (create `.mist` and paired
      `.cs`, generate the `.cs` via the SnippetEmitter tool per the Book Snippet Policy) and reference
      the chapter from `book/src/SUMMARY.md`
    - _Requirements: 19.1, 19.2, 19.3, 19.4_

- [ ] 25. Feature coverage documentation update
  - [ ] 25.1 Update `CSHARP_FEATURES.md`
    - Change the `params arrays` row from unimplemented to implemented with a note describing
      homogeneous value-level `params T[]` support, and update the `Summary` table counts
    - _Requirements: 20.1, 20.2_

- [ ] 26. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- **Dual-pipeline reality.** The classic pipeline is the default and the only one that type-checks and
  emits; the ProgramModel pipeline does not emit yet. Part 1 preserves the completed shared surface
  and the ProgramModel-track resolution work; Part 2 completes the feature through the classic
  pipeline.
- **ProgramModel track is frozen at resolution level.** It does not emit; its deeper body-level checks
  (spread forwarding, tuple decomposition, `is`-narrowing, zip, whole-pack constraints inside bodies)
  await the v2 pipeline's analyze/emit stages. Tasks 4-8 stay `[x]`; no further ProgramModel-track
  work is scheduled. Task 8.1 (the type-position capability ladder in the ProgramModel resolver) is
  complete.
- **Renumbering.** Completed tasks in Part 1 keep their original numbers so their status and history
  stay traceable; the one exception is the shared TextMate task, relabeled from `19.1` to `1.5` and
  grouped with the other shared-surface foundations (its `[x]` status is unchanged). All remaining
  classic-pipeline work is renumbered as a fresh, coherent sequence in Part 2 (tasks 9-26); the
  original ProgramModel-oriented task numbers for not-done work are superseded by these.
- **Two validations of the model/checker/backend properties.** The already-written ProgramModel-track
  property tests (4.4, 5.6, 5.7, 5.8, 7.2, 7.3) remain the ProgramModel-track validation. The classic
  track carries its own validation of the same properties through the classic pipeline (9.4, 10.6,
  10.7, 10.8, 12.2, 12.3) plus the properties that were never reached on the ProgramModel side
  (Properties 5, 7, 10-21). Every Correctness Property 1-21 is covered by a dedicated property-test
  sub-task; **Property 1 (round-trip) is shared-surface and covered once by task 2.2.**
- **Testing policy.** All tests run in-process with zero external process spawns; property tests use
  FsCheck with a minimum of 100 iterations and batch scenarios (compile once, run once) where a
  program must be compiled and run. The classic track's tests use the shared `CompilerDriver` fixture
  (task 1.3), which wires `SemanticModelBuilder` + `TypeCheckerAnalyzer` + `CSharpTranspilerBackend`.
- **Optional sub-tasks.** Sub-tasks postfixed with `*` are optional property/unit test tasks and can
  be skipped for a faster path. Core implementation tasks, the runnable sample (22.1), the AllFeatures
  wiring (22.2), the sample clean-compile check (22.3), the `Func`/`Action` integration check (20.3),
  and the locked negative proof (23.1) are never optional. Top-level tasks are never optional.
- **Checkpoints** sit at the classic pipeline boundaries: semantic model (11), type-checker core (15),
  type-checker complete (18), backend + bootstrap (21), and a final checkpoint (26).
- The variadic newtype (Requirement 22.3) is covered by the classic semantic model (task 10.5) and
  Property 21 (task 17.8); it is intentionally not placed in the runnable sample.
- Diagnostics `MIST_VAR_0001` through `MIST_VAR_0011` follow the design's Error Handling table.

## Task Dependency Graph

The graph schedules only the **remaining classic-pipeline work** (Part 2). Part 1 is complete and is
not scheduled here. Because the classic pipeline centers on two hot single files --
`SemanticModelBuilder.cs` (construction) and `TypeCheckerAnalyzer.cs` (checking) -- tasks that edit
either are placed in distinct waves, which makes the model (waves 0-6) and type-checker (waves 7-18)
phases largely sequential; property/unit tests, the backend, and the finalization tasks parallelize
where their files are independent. Checkpoints and top-level parent tasks are not included; only leaf
sub-tasks (including optional `*` tests) appear.

```json
{
  "waves": [
    { "id": 0, "tasks": ["9.1"] },
    { "id": 1, "tasks": ["9.2", "9.3"] },
    { "id": 2, "tasks": ["10.1", "9.4", "9.5"] },
    { "id": 3, "tasks": ["10.2", "10.6"] },
    { "id": 4, "tasks": ["10.3"] },
    { "id": 5, "tasks": ["10.4"] },
    { "id": 6, "tasks": ["10.5", "10.8", "10.9"] },
    { "id": 7, "tasks": ["12.1", "10.7"] },
    { "id": 8, "tasks": ["12.2", "12.3", "12.4", "13.1"] },
    { "id": 9, "tasks": ["13.2", "13.3", "13.5"] },
    { "id": 10, "tasks": ["14.1", "13.4"] },
    { "id": 11, "tasks": ["14.2", "14.3", "14.4"] },
    { "id": 12, "tasks": ["16.1", "14.5"] },
    { "id": 13, "tasks": ["16.2", "16.4"] },
    { "id": 14, "tasks": ["16.3", "16.5"] },
    { "id": 15, "tasks": ["17.1", "16.6", "23.1"] },
    { "id": 16, "tasks": ["17.2", "17.5"] },
    { "id": 17, "tasks": ["17.3", "17.6"] },
    { "id": 18, "tasks": ["17.4", "17.7"] },
    { "id": 19, "tasks": ["19.1", "17.8"] },
    { "id": 20, "tasks": ["19.2"] },
    { "id": 21, "tasks": ["19.3", "20.1"] },
    { "id": 22, "tasks": ["19.4", "19.5", "19.6", "20.2"] },
    { "id": 23, "tasks": ["20.3"] },
    { "id": 24, "tasks": ["22.1"] },
    { "id": 25, "tasks": ["22.2"] },
    { "id": 26, "tasks": ["22.3", "24.1", "25.1"] }
  ]
}
```
