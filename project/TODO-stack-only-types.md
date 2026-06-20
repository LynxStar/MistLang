# Stack-Only Types — Continuation Instructions

## Status

**Branch:** `stackalloc`  
**Commit:** `c894061` — feat(stack-only-types): implement Span<T>/ReadOnlySpan<T>/stackalloc  
**Spec:** `.kiro/specs/stack-only-types/`  
**Tasks:** 65/65 marked complete in `tasks.md`, but **6 tests fail** due to one root cause.

## What's Done (Working)

- Build: **zero warnings** (CS*, STORM*, MSBuild/SDK)
- **4,251 of 4,257 tests pass**
- All 21 property-based correctness tests pass (using arity-2 `Span<T, unknown>` form)
- `SpanType` IType, grammar, parsing, resolution, type-checking, emission, projection, frame-escape analysis — all implemented and verified
- Book docs, TextMate grammar, sample app, AllFeatures wiring — all done

## What's Broken (6 Failing Tests)

All 6 failures share **one root cause**: the arity-1 type aliases `Span<T>` / `ReadOnlySpan<T>` from `Source/Standard/Bootstrap/Span.mist` are not being registered in the `Standard` namespace.

### Failing Tests

1. `SpanSliceLengthInferencePropertyTests.SliceCall_YieldsSameKindSpanTypeWithCorrectLength`
2. `StackOnlyTypesEmissionCompileTests.EmittedSpanPrograms_CompileWithZeroErrorsAndZeroCsWarnings`
3. `SamplesCompileAndMatchBaselineTest.AllFeatures_CompilesRunsAndMatchesBaseline`
4. `EndToEndCompilationTests.AllFeatures_InterfaceDispatch_ProducesExpectedOutput`
5. `EndToEndCompilationTests.AllFeatures_CompilesWithZeroErrorDiagnostics`
6. `StackOnlyTypesSampleTests.StackOnlyTypesSample_CompilesAndProducesExactExpectedOutput`

### Root Cause Analysis

The `Span.mist` file declares:

```mist
intrinsic public ref struct Span<T, Length:ZeroOrPositiveInt> : IEnumerable<T>
{
    public type SpanBounds = ZeroOrPositiveInt and < Length;
    ...
}

type Span<T> = Span<T, unknown>;
type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;
```

**Problem:** The `public type SpanBounds = ZeroOrPositiveInt and < Length;` member-level type alias syntax inside struct bodies doesn't match any `MemberDeclaration` grammar alternative in `Mist.storm`. The grammar has:

- `ModifiedAssociatedTypeSlotDeclaration` — ends with just `SEMICOLON` (no `= Type` part)
- `AssociatedTypeSlotDeclaration` — uses `COLON constraints` (not `= Type`)

Neither handles `type Name = RefinableTypeExpression;` as a struct member.

**Effect:** The PEG parser likely fails the entire `Span.mist` file (or produces an AST excluding the namespace body items), preventing both the struct declarations AND the type aliases from being registered in the `Standard` namespace. The 63 other members of `Standard` work because they come from other `.mist` files.

### Secondary Issue

`StackOnlyTypesEmissionCompileTests` has an **additional** problem: the emitted C# from `stackalloc` produces pointer-based code (`int* buffer = stackalloc int[4]`) instead of span-typed code (`System.Span<int> buffer = stackalloc int[4]`). This causes CS0214/CS8346 Roslyn errors. The fix is in `CSharpEmitter.cs` — the stackalloc emission case needs to emit the assignment to a `System.Span<T>` local, not a bare `stackalloc` expression.

## Fix Plan

### Fix 1: Make `Span.mist` parse correctly

**Option A (recommended):** Add a `MemberDeclaration` grammar alternative for member-level type aliases:

```storm
| Modifiers TYPE Identifier EQUALS RefinableTypeExpression SEMICOLON as MemberTypeAliasDeclaration
```

Then handle `MemberTypeAliasDeclarationMemberDeclarationNode` in `SemanticModelBuilder` to register the alias within the struct's scope.

**Option B (simpler, may be sufficient):** Remove the `public type SpanBounds = ...;` lines from `Span.mist` and register `SpanBounds` as part of the intrinsic type processing in `SemanticModelBuilder` (similar to how `ArrayBounds` is handled for `Array.mist`). The SpanBounds refinement is already implemented in the type checker via `ValidateLengthIndexRefinement` — the declaration in `Span.mist` is documentation, not functional.

### Fix 2: Stackalloc emission format

In `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`, the `StackallocExpressionPrimaryNode` case currently emits `stackalloc T[n]` as a standalone expression. When it's the initializer of a local variable, the emitter should emit:

```csharp
System.Span<T> varName = stackalloc T[n];
// or for ReadOnlySpan:
System.ReadOnlySpan<T> varName = stackalloc T[n];
```

Look at how the emitter handles `VariableDeclarationStatement` when the initializer is a `StackallocExpressionPrimaryNode` — it likely needs a special case to emit the proper `Span<T>` type annotation rather than using `var` (which would give `Span<T>` in C# 7.2+ but might not match the expected pattern).

## How To Verify

After fixing:

```bash
# Build (should already be clean)
dotnet build

# Run all tests
dotnet test

# Or just the 6 failing ones:
dotnet test --filter "FullyQualifiedName~SpanSliceLengthInference|FullyQualifiedName~StackOnlyTypesEmissionCompile|FullyQualifiedName~AllFeatures_Compiles|FullyQualifiedName~AllFeatures_Interface|FullyQualifiedName~AllFeatures_CompilesRuns|FullyQualifiedName~StackOnlyTypesSample"
```

## Key Files

| File | Role |
|------|------|
| `Source/Standard/Bootstrap/Span.mist` | Stdlib declaration — fix the member-level alias syntax |
| `Source/Mist/Grammar/Mist.storm` | Add `MemberTypeAliasDeclaration` alternative if using Option A |
| `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` | Handle new grammar node OR register SpanBounds intrinsically |
| `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` | Fix stackalloc emission to produce Span-typed locals |
| `samples/StackOnlyTypes/Program.mist` | The proof code — DO NOT MODIFY (immutable per policy) |
| `.kiro/specs/stack-only-types/tasks.md` | All tasks marked complete |
| `.kiro/specs/stack-only-types/requirements.md` | Full requirements with proof code and expected output |
| `.kiro/specs/stack-only-types/design.md` | Architecture and design decisions |
