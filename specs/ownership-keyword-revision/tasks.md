# Implementation Plan: Ownership Keyword Revision

## Overview

A mechanical keyword revision that renames two ownership specifiers (`frozen ref` → `frozen`, `ref` → `borrowed`) and adds `borrowed` as a new keyword token. Changes flow through the compiler pipeline: lexer → parser grammar → semantic model → analyzer → pretty printer → tests → sample app → TextMate grammar. The Storm source generator produces AST node types from the grammar, so grammar changes must come first.

## Tasks

- [x] 1. Add `borrowed` keyword and revise grammar rules
  - [x] 1.1 Add `BORROWED` token to `keywords.storm` and revise `OwnershipSpecifier` rule in `Mist.storm`
    - In `Source/Mist/Grammar/keywords.storm`: add `token BORROWED = "borrowed";` in the ownership keywords section
    - In `Source/Mist/Grammar/Mist.storm`: change `OwnershipSpecifier` rule from `FROZEN REF as FrozenRefSpecifier` to `FROZEN as FrozenSpecifier`, and from `REF as RefSpecifier` to `BORROWED as BorrowedSpecifier`
    - Keep `OWNED as OwnedSpecifier` and `READONLY REF as ReadonlyRefSpecifier` unchanged
    - _Requirements: 1.1, 1.2, 1.4, 2.1, 2.2, 2.3, 2.4, 2.8_

- [x] 2. Rename OwnershipTier enum and update SemanticModelBuilder
  - [x] 2.1 Rename `OwnershipTier` enum values and update `MapOwnershipSpecifier`
    - In `Source/Mist/Semantics/Model/OwnershipTier.cs`: rename `FrozenRef` to `Frozen`, rename `Ref` to `Borrowed`, update doc comments
    - In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`: update `MapOwnershipSpecifier` to map `FrozenSpecifierOwnershipSpecifierNode` → `OwnershipTier.Frozen` and `BorrowedSpecifierOwnershipSpecifierNode` → `OwnershipTier.Borrowed`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 3. Update OwnershipAnalyzer enum references
  - [x] 3.1 Update `OwnershipAnalyzer.cs` to use renamed enum values
    - Replace all `OwnershipTier.FrozenRef` with `OwnershipTier.Frozen`
    - Replace all `OwnershipTier.Ref` with `OwnershipTier.Borrowed`
    - This includes variable declaration borrow creation, non-storability checks, exclusivity checks, and pattern-matching expressions
    - _Requirements: 4.1, 4.2, 4.7_

  - [x] 3.2 Update `MethodFlowState.cs` to use renamed enum values
    - Replace `OwnershipTier.Ref` with `OwnershipTier.Borrowed` in `RestoreLenderState` switch case
    - Replace `OwnershipTier.FrozenRef` with `OwnershipTier.Frozen` in `RestoreLenderState` switch case and `HasRemainingFrozenBorrows` comparison
    - Update doc comments referencing old names (`FrozenRef`, `Ref` as tier names) to `Frozen` and `Borrowed`
    - _Requirements: 4.3, 4.4, 4.7_

  - [x] 3.3 Update `BorrowEntry.cs` doc comment
    - Update the `BorrowTier` parameter doc comment from `(Ref, FrozenRef, or ReadonlyRef)` to `(Borrowed, Frozen, or ReadonlyRef)`
    - _Requirements: 4.5, 10.4_

- [x] 4. Update Pretty Printer
  - [x] 4.1 Update `EmitOwnershipSpecifier` in `MistPrettyPrinter.cs`
    - Change `FrozenRefSpecifierOwnershipSpecifierNode => "frozen ref "` to `FrozenSpecifierOwnershipSpecifierNode => "frozen "`
    - Change `RefSpecifierOwnershipSpecifierNode => "ref "` to `BorrowedSpecifierOwnershipSpecifierNode => "borrowed "`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

- [x] 5. Checkpoint - Verify build compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Update test generators and property tests
  - [x] 6.1 Update `MistOwnershipGenerator.cs`
    - In `GenOwnershipSpecifier()`: change `"ref "` to `"borrowed "` and `"frozen ref "` to `"frozen "`
    - In `GenOptionalOwnershipSpecifier()`: apply the same keyword changes
    - _Requirements: 8.5_

  - [x] 6.2 Update `OwnershipLendingPropertyTests.cs`
    - Update `TierToSpecifier` method: `OwnershipTier.Borrowed => "borrowed "` and `OwnershipTier.Frozen => "frozen "`
    - Replace all `OwnershipTier.FrozenRef` references with `OwnershipTier.Frozen`
    - Replace all `OwnershipTier.Ref` references with `OwnershipTier.Borrowed`
    - _Requirements: 8.1, 8.2, 8.6, 8.7_

  - [x] 6.3 Update `OwnershipStorabilityPropertyTests.cs`
    - Change `RefTierKeywords` array from `["ref", "readonly ref", "frozen ref"]` to `["borrowed", "readonly ref", "frozen"]`
    - _Requirements: 8.3_

  - [x] 6.4 Update `OwnershipSignaturePropertyTests.cs`
    - Change specifier/label pairs: `("ref ", "ref")` → `("borrowed ", "borrowed")` and `("frozen ref ", "frozen ref")` → `("frozen ", "frozen")`
    - _Requirements: 8.4_

  - [x] 6.5 Update remaining ownership property test files
    - Update any `OwnershipTier.FrozenRef` → `OwnershipTier.Frozen` and `OwnershipTier.Ref` → `OwnershipTier.Borrowed` references in `OwnershipMovePropertyTests`, `OwnershipFrozenBlockingPropertyTests`, `OwnershipExclusivityPropertyTests`, and any other test files referencing the old enum values
    - _Requirements: 8.6, 8.7, 8.8_

- [x] 7. Checkpoint - Verify all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Update sample application
  - [x] 8.1 Update `samples/OwnershipSystem/Program.mist`
    - Replace all `frozen ref` ownership specifiers with `frozen`
    - Replace all bare `ref` ownership specifiers with `borrowed`
    - Update output labels: `[frozen-ref]` → `[frozen]`, `[ref-borrow]` → `[borrowed]`
    - Update method names: `DemoFrozenRef` → `DemoFrozen`, `DemoRefBorrowing` → `DemoBorrowed`
    - Ensure the sample demonstrates all five tiers: unrestricted, `readonly ref`, `frozen`, `borrowed`, `owned`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_

- [x] 9. Update TextMate grammar
  - [x] 9.1 Add `borrowed` to storage modifier pattern in `mist.tmLanguage.json`
    - Add `borrowed` to the regex alternation in the storage modifier match pattern
    - Ensure `\b` word boundaries are preserved so `borrowedItem` does not match
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_

- [x] 10. Final checkpoint - Verify full build and tests
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- The Storm source generator automatically produces AST node class names from grammar rule alternatives, so task 1.1 must complete before tasks 2–4 can compile
- `REF` remains defined in `keywords.storm` as a reserved keyword for future C#-style ref semantics
- `VariableOwnershipState` enum values (`LentRef`, `LentFrozen`, `LentReadonly`) are NOT renamed — they describe lending states, not tier names
- All property tests use in-process compilation via shared `CompilerDriver` — no new process spawns are introduced
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["3.1", "3.2", "3.3", "4.1"] },
    { "id": 3, "tasks": ["6.1", "6.2", "6.3", "6.4", "6.5"] },
    { "id": 4, "tasks": ["8.1", "9.1"] }
  ]
}
```
