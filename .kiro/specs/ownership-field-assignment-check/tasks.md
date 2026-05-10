# Implementation Plan: Ownership Field Assignment Check

## Overview

This bugfix adds a lending compatibility check to `AnalyzeAssignment` in `OwnershipAnalyzer` for field assignments. When assigning to a field with a non-Unrestricted ownership tier, the source variable's tier must be compatible per the existing `IsLendingCompatible` matrix. The fix introduces a single new private method `CheckFieldAssignmentLendingCompatibility`, called from `AnalyzeAssignment` after the existing non-storability check.

## Tasks

- [ ] 1. Implement field assignment lending compatibility check
  - [ ] 1.1 Add `CheckFieldAssignmentLendingCompatibility` private method to `OwnershipAnalyzer`
    - Add a new private static method that:
      - Returns early if target is not a field/index access (`IsFieldOrIndexAccess`)
      - Unwraps the expression chain to find the `PostfixNode`, gets the last suffix as `MemberAccessSuffixPostfixSuffixNode`
      - Resolves the field symbol by determining the receiver type via `model.GetTypeOf()` and looking up the member name on the type's `MemberScope` (walking the class hierarchy for inherited fields)
      - Returns early if symbol is null, not `SymbolKind.Field`, or has `OwnershipTier.Unrestricted`
      - Resolves the RHS symbol via `TryResolveAssignmentValueSymbol`
      - Returns early if RHS symbol is null or RHS declared type is a value type
      - Determines source tier (default `Unrestricted` if not a `Symbol`)
      - Returns early if source tier equals target tier or `IsLendingCompatible` returns true
      - Emits `MIST_OWN_UNRESTRICTED_TO_REF` when source is Unrestricted and target is Ref/FrozenRef/Owned
      - Emits `MIST_OWN_INCOMPATIBLE_LEND` for other incompatible pairs
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 4.1, 4.2, 4.3_

  - [ ] 1.2 Call `CheckFieldAssignmentLendingCompatibility` from `AnalyzeAssignment`
    - Insert the call after the existing non-storability check block and before the `CheckFieldAssignmentMutationBlocking` call
    - Pass `assignment.Target`, `assignment.Value`, `state`, `model`, `diagnostics`
    - _Requirements: 1.1, 2.1_

- [ ] 2. Checkpoint - Verify build and existing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 3. Property-based test for field assignment lending compatibility
  - [ ] 3.1 Write property test `OwnershipFieldAssignmentLendingPropertyTests`
    - **Property 1: Field assignment lending compatibility matrix**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.3, 2.4, 3.1**
    - Create `Test/Mist.Tests/Analysis/OwnershipFieldAssignmentLendingPropertyTests.cs`
    - Follow the same pattern as `OwnershipLendingPropertyTests`: shared `CompilerDriver`, in-process compilation, no process spawning per iteration
    - Generate all (source_tier × target_tier) pairs for non-Unrestricted targets crossed with receiver forms (`this.Field` and `obj.Field`)
    - Include value-type scenarios that should always pass regardless of tier combination
    - Build Mist programs with a class containing a field at the target tier, then assign a variable at the source tier to that field
    - Assert: permitted pairs produce no lending diagnostic; forbidden pairs produce `MIST_OWN_INCOMPATIBLE_LEND` or `MIST_OWN_UNRESTRICTED_TO_REF`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.3, 2.4, 3.1_

- [ ] 4. Verify sample app still compiles
  - [ ] 4.1 Compile `samples/OwnershipSystem/Program.mist` and confirm no new diagnostics
    - Run the compiler against the existing OwnershipSystem sample
    - Verify no `MIST_OWN_INCOMPATIBLE_LEND` or `MIST_OWN_UNRESTRICTED_TO_REF` diagnostics are emitted
    - The sample already uses `owned` on the `OwnedData` constructor parameter, so no changes are needed
    - _Requirements: 1.5, 1.6_

- [ ] 5. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The fix is entirely contained within `OwnershipAnalyzer.cs` — no new files for production code
- The existing `IsLendingCompatible` matrix and diagnostic IDs are reused; no new diagnostics are introduced
- The property test uses in-process compilation via `CompilerDriver` (no process spawning per iteration) per the test process spawning policy
- The sample app (`samples/OwnershipSystem/Program.mist`) already has `owned` on the relevant parameter, so no sample changes are needed
- **IMPORTANT**: `GetDeclaredSymbol` does NOT work for `MemberAccessSuffixPostfixSuffixNode` — resolve the field by looking up the member name on the receiver's type via `MemberScope.Lookup()`

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["3.1", "4.1"] }
  ]
}
```
