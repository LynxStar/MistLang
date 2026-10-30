# Implementation Plan: Readonly Locals

## Overview

This plan implements `readonly` as a modifier on local variable declarations in the Mist programming language. The implementation proceeds through the compiler pipeline: grammar → semantic model → type checker → sample app, with verification at each step. The C# emitter requires no changes (readonly enforcement is compile-time only), and the TextMate grammar already handles `readonly` highlighting.

## Tasks

- [x] 1. Grammar — Add readonly modifier to local variable declarations
  - [x] 1.1 Add `readonlyModifier:READONLY?` to the `TypedVariableDeclaration` alternative in the `VariableDeclarationStatement` rule in `Source/Mist/Grammar/Mist.storm`
    - Place it before `ownershipSpecifier:OwnershipSpecifier?` so the full alternative becomes: `readonlyModifier:READONLY? ownershipSpecifier:OwnershipSpecifier? type:TypeReference name:IDENTIFIER EQUAL initializer:Expression SEMICOLON as TypedVariableDeclaration`
    - _Requirements: 1.1, 1.2_

  - [x] 1.2 Add `readonlyModifier:READONLY?` to the `InferredVariableDeclaration` alternative
    - Place it before `ownershipSpecifier:OwnershipSpecifier?` so the full alternative becomes: `readonlyModifier:READONLY? ownershipSpecifier:OwnershipSpecifier? VAR name:(IDENTIFIER | DISCARD) EQUAL initializer:Expression SEMICOLON as InferredVariableDeclaration`
    - _Requirements: 1.1, 1.5, 1.7_

  - [x] 1.3 Add `readonlyModifier:READONLY?` to the `VarTupleDeconstruction` alternative
    - Place it before `VAR` so the full alternative becomes: `readonlyModifier:READONLY? VAR LEFT_PARENTHESIS firstName:IDENTIFIER (COMMA restNames:IDENTIFIER)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON as VarTupleDeconstruction`
    - _Requirements: 1.3_

  - [x] 1.4 Add `readonlyModifier:READONLY?` to the `TypedTupleDeconstruction` alternative
    - Place it before `LEFT_PARENTHESIS` so the full alternative becomes: `readonlyModifier:READONLY? LEFT_PARENTHESIS first:TupleDeconstructionElement COMMA rest:TupleDeconstructionElement (COMMA additional:TupleDeconstructionElement)* RIGHT_PARENTHESIS EQUAL initializer:Expression SEMICOLON as TypedTupleDeconstruction`
    - _Requirements: 1.4_

  - [x] 1.5 Rebuild the project and verify zero warnings
    - Run `dotnet build` on the solution to regenerate AST nodes from Storm
    - Fix any compilation errors caused by the new `ReadonlyModifier` field on the generated node types
    - _Requirements: 1.1–1.7_

  - [x] 1.6 Verify parsing correctness
    - Verify `readonly var x = 7;` parses as InferredVariableDeclaration with ReadonlyModifier token present
    - Verify `readonly int x = 7;` parses as TypedVariableDeclaration with ReadonlyModifier token present
    - Verify `readonly var (a, b) = (1, 2);` parses as VarTupleDeconstruction with ReadonlyModifier token present
    - Verify `readonly (int a, string b) = (1, "hi");` parses as TypedTupleDeconstruction with ReadonlyModifier token present
    - Verify `readonly owned var x = expr;` parses with both readonly modifier and ownership specifier
    - Verify existing `readonly { }` block syntax still parses correctly (no regression)
    - _Requirements: 1.1–1.7, 7.1–7.3_

- [x] 2. Semantic Model — Record ExplicitReadonly on local variable symbols
  - [x] 2.1 In `SemanticModelBuilder.ResolveVariableDeclaration` (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs`), for the `TypedVariableDeclarationVariableDeclarationStatementNode` case, after creating `varSymbol`, check if `typed.ReadonlyModifier` is not null and set `varSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly`
    - _Requirements: 2.1_

  - [x] 2.2 For the `InferredVariableDeclarationVariableDeclarationStatementNode` case, after creating `varSymbol`, check if `inferred.ReadonlyModifier` is not null and set `varSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly`
    - _Requirements: 2.1_

  - [x] 2.3 For the `VarTupleDeconstructionVariableDeclarationStatementNode` case, check if `varTuple.ReadonlyModifier` is not null and set `ReadonlyStatus.ExplicitReadonly` on `firstSymbol` and each `restSymbol`
    - _Requirements: 2.3_

  - [x] 2.4 For the `TypedTupleDeconstructionVariableDeclarationStatementNode` case, check if `typedTuple.ReadonlyModifier` is not null and set `ReadonlyStatus.ExplicitReadonly` on each element symbol inside `RegisterDeconstructionElement`
    - _Requirements: 2.3_

  - [x] 2.5 Rebuild the project and verify zero warnings
    - _Requirements: 2.1–2.4_

  - [x] 2.6 Verify semantic model correctness
    - Write a test or manually verify that a readonly local variable symbol has `ReadonlyStatus.ExplicitReadonly` after semantic model construction
    - Verify that a non-readonly local variable symbol does NOT have `ReadonlyStatus.ExplicitReadonly`
    - Verify that readonly tuple deconstruction sets ExplicitReadonly on each introduced symbol
    - _Requirements: 2.1–2.4_

- [x] 3. Type Checker — Reject reassignment to readonly locals
  - [x] 3.1 Add a `CheckReadonlyLocalReassignment` method to `TypeCheckerAnalyzer` (`Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`)
    - The method resolves the assignment target to a symbol
    - If the symbol is a `SymbolKind.LocalVariable` with `ReadonlyStatus.ExplicitReadonly`, emit diagnostic with ID `MIST_READONLY_LOCAL_0001 (historical)` and message `"Cannot reassign readonly local variable '{name}'"`
    - _Requirements: 3.1, 4.1, 4.5_

  - [x] 3.2 Call `CheckReadonlyLocalReassignment` from the assignment handling code
    - Insert the call in the section where other assignment checks run (near `CheckRefReadonlyAssignmentProhibition`), guarded by `assignment.Value is not null`
    - _Requirements: 3.1_

  - [x] 3.3 Add ref argument check for readonly locals
    - In the existing ref argument validation logic, add a check: if the expression being passed as a `ref` argument resolves to a local variable with `ReadonlyStatus.ExplicitReadonly`, emit diagnostic `MIST_READONLY_LOCAL_0001 (historical)` with message `"Cannot pass readonly local variable '{name}' by mutable reference"`
    - _Requirements: 3.5, 4.4_

  - [x] 3.4 Rebuild the project and verify zero warnings
    - _Requirements: 3.1–3.6, 4.1–4.5_

  - [x] 3.5 Verify type checker correctness
    - Verify `readonly var x = 7; x = 10;` produces diagnostic `MIST_READONLY_LOCAL_0001 (historical)`
    - Verify `var x = 7; x = 10;` does NOT produce a readonly diagnostic
    - Verify passing a readonly local as a `ref` argument produces the expected diagnostic
    - Verify a readonly local declared inside a `readonly { }` block still gets the readonly-local diagnostic on reassignment
    - Verify readonly tuple deconstruction variables cannot be reassigned
    - _Requirements: 3.1–3.6, 4.1–4.5, 6.1–6.3_

- [x] 4. Sample Application
  - [x] 4.1 Create `samples/ReadonlyLocals/mist.toml` with `name = "ReadonlyLocals"`
    - _Requirements: 9.1_

  - [x] 4.2 Create `samples/ReadonlyLocals/Program.mist` with a `public static void RunDemo()` method
    - Demonstrate readonly with inferred type: `readonly var x = 7;`
    - Demonstrate readonly with explicit type: `readonly int y = 42;`
    - Demonstrate readonly with tuple deconstruction: `readonly var (a, b) = (1, 2);`
    - Demonstrate readonly with typed tuple deconstruction: `readonly (int c, string d) = (3, "hello");`
    - Demonstrate readonly combined with ownership specifiers: `readonly owned var obj = new Foo();`
    - Demonstrate readonly locals inside a readonly block
    - Demonstrate readonly locals inside a method body and constructor body
    - Demonstrate multiple readonly locals in the same scope
    - Each variant prints a section header (e.g., `--- Inferred Type ---`) and key=value output
    - _Requirements: 9.1–9.3_

  - [x] 4.3 Add `[[references]] path = "../ReadonlyLocals"` to `samples/AllFeatures/mist.toml`
    - _Requirements: 9.4_

  - [x] 4.4 Add `ReadonlyLocals.Program.RunDemo();` call to `samples/AllFeatures/Program.mist`
    - _Requirements: 9.4_

  - [x] 4.5 Verify the ReadonlyLocals sample compiles and runs without errors
    - _Requirements: 9.5_

  - [x] 4.6 Verify the AllFeatures sample still compiles and runs without errors (no regression)
    - _Requirements: 9.4, 9.5_

- [x] 5. TextMate Grammar Verification
  - [x] 5.1 Verify that the existing TextMate grammar pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` already highlights `readonly` as `storage.modifier.mist` when it appears before a local variable declaration
    - The pattern `\\b(public|private|static|intrinsic|implicit|explicit|readonly|mutable|owned|frozen|ref|borrowed)\\b` already includes `readonly`
    - The `\b` word boundaries ensure `readonlyValue` is not matched
    - Document that no changes are required
    - _Requirements: 8.1–8.3_

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3", "1.4"] },
    { "id": 1, "tasks": ["1.5", "1.6"] },
    { "id": 2, "tasks": ["2.1", "2.2", "2.3", "2.4", "5.1"] },
    { "id": 3, "tasks": ["2.5", "2.6"] },
    { "id": 4, "tasks": ["3.1", "3.2", "3.3"] },
    { "id": 5, "tasks": ["3.4", "3.5"] },
    { "id": 6, "tasks": ["4.1", "4.2", "4.3", "4.4"] },
    { "id": 7, "tasks": ["4.5", "4.6"] }
  ]
}
```
