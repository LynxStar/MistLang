# Implementation Plan: Ref Parameters

## Overview

This plan implements C#-style `ref` semantics for the Mist programming language — ref parameters, ref returns, ref locals, and ref-annotated arguments. The implementation proceeds through the compiler pipeline: grammar → semantic model → type checker → C# emitter → pretty printer → TextMate grammar → sample app, with tests integrated alongside each component.

## Tasks

- [ ] 1. Grammar and AST changes
  - [ ] 1.1 Update the `Parameter` rule in `Mist.storm` to add `refModifier:REF?`
    - Add `refModifier:REF?` field between `modifier:READONLY?` and `ownershipSpecifier:OwnershipSpecifier?`
    - The Storm source generator will produce the updated `ParameterNode` with the new field
    - _Requirements: 1.1, 1.2, 1.4_

  - [ ] 1.2 Update the `ArgumentList` and add `Argument` rule in `Mist.storm`
    - Replace `rule ArgumentList = Expression (COMMA Expression)*;` with `rule ArgumentList = Argument (COMMA Argument)*;`
    - Add `rule Argument = REF expression:Expression as RefArgument | expression:Expression as ValueArgument;`
    - _Requirements: 2.1, 2.4_

  - [ ] 1.3 Update the `ReturnStatement` rule in `Mist.storm` to add `refKeyword:REF?`
    - Change to `rule ReturnStatement = RETURN refKeyword:REF? value:Expression? SEMICOLON;`
    - _Requirements: 4.1_

  - [ ] 1.4 Rebuild the project and fix compilation errors from grammar changes
    - Run `dotnet build` on the solution to regenerate AST nodes from Storm
    - Fix any compilation errors in downstream code caused by the `ArgumentList` rule change (callers that previously accessed `Expression` nodes directly now need to unwrap `Argument` nodes)
    - Update `MistParser.cs` if manual parser adjustments are needed
    - _Requirements: 1.1, 2.4_

- [ ] 2. Checkpoint - Ensure the project builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 3. Semantic model extensions
  - [ ] 3.1 Create the `RefKind` enum in the semantic model
    - Create `Source/Mist/Semantics/Model/RefKind.cs` with values `None`, `Ref`, `RefReadonly`
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ] 3.2 Add `RefKind` property to parameter, method return, and local variable symbols
    - Extend `ParameterSignature` (or the relevant parameter symbol type) with a `RefKind` property
    - Extend method symbol representation with a `ReturnRefKind` property
    - Extend local variable symbol representation with a `RefKind` property
    - _Requirements: 7.1, 7.2, 7.3_

  - [ ] 3.3 Update `SemanticModelBuilder` to populate `RefKind` from AST nodes
    - When processing `ParameterNode`, check for `refModifier` field presence → set `RefKind.Ref`
    - When processing method declarations, interpret `RefSpecifier` / `ReadonlyRefSpecifier` in `returnOwnership` as ref return → set `ReturnRefKind`
    - When processing variable declarations with `RefSpecifier` / `ReadonlyRefSpecifier` as ownership specifier, interpret as ref local → set `RefKind`
    - Handle error case: `refModifier` and `owned` ownership specifier present → emit `MIST_REF_0004` (error), record `RefKind.Ref` and `OwnershipTier.Unrestricted`
    - Handle warning case: `refModifier` and `borrowed`/`frozen` ownership specifier present → emit `MIST_REF_0013` (warning), record `RefKind.Ref` and `OwnershipTier.Unrestricted` (ownership specifier ignored as redundant)
    - _Requirements: 7.1, 7.2, 7.3, 1.3, 1.4, 9.1_

  - [ ]* 3.4 Write property test for semantic model RefKind recording (Property 9)
    - **Property 9: Semantic Model RefKind Recording**
    - Generate random ref-annotated parameter/return/local declarations, build semantic model in-process via `SemanticModelBuilder`, verify `RefKind` values on resulting symbols
    - **Validates: Requirements 7.1, 7.2, 7.3**

- [ ] 4. Type checker — ref safety rules
  - [ ] 4.1 Implement ref argument validation: assignable storage location check
    - In `TypeCheckerAnalyzer`, when visiting a call expression with `RefArgument` nodes, verify the argument expression is an assignable storage location (local variable, mutable field, array element)
    - Reject literals, temporaries, method return values, arithmetic results, readonly bindings, frozen variables, readonly-ref-ownership variables
    - Emit `MIST_REF_0001` on failure
    - _Requirements: 2.5, 3.3, 9.4, 9.5, 10.4, 14.1_

  - [ ] 4.2 Implement ref call-site annotation enforcement
    - Verify that arguments for ref parameters have the `ref` keyword → emit `MIST_REF_0002` if missing
    - Verify that `ref` does not appear on arguments for non-ref parameters → emit `MIST_REF_0003` if spurious
    - _Requirements: 2.2, 2.3, 14.2, 14.3_

  - [ ] 4.3 Implement ref type exact match validation
    - When a `RefArgument` is present, require exact type identity between argument and parameter (no implicit conversions, no subtype substitution)
    - Emit `MIST_REF_0007` on mismatch
    - _Requirements: 3.4, 5.9, 14.7_

  - [ ] 4.4 Implement ref readonly assignment prohibition
    - Detect assignments through `ref readonly` locals and `ref readonly` return values
    - Emit `MIST_REF_0005` for ref readonly local assignment
    - Emit `MIST_REF_0009` for ref readonly return value assignment
    - _Requirements: 4.8, 5.6, 14.4, 14.9_

  - [ ] 4.5 Implement ref return lifetime safety validation
    - For methods with `ReturnRefKind == Ref` or `RefReadonly`, verify the return expression references a storage location whose lifetime exceeds the method call
    - Permit: instance fields (via `this`), static fields, array elements via instance fields, ref parameters
    - Reject: local variables, non-ref parameters, literals, expression results
    - Emit `MIST_REF_0006` on violation
    - _Requirements: 4.3, 4.5, 4.6, 6.2, 6.3, 6.4, 6.6, 6.8, 14.5_

  - [ ] 4.6 Implement ref field storage prohibition
    - Detect attempts to assign a ref (parameter or local) to a field
    - Emit `MIST_REF_0008`
    - _Requirements: 6.5, 14.8_

  - [ ] 4.7 Implement ref lambda capture prohibition
    - Detect ref locals captured by lambdas or closures
    - Emit `MIST_REF_0011`
    - _Requirements: 6.7, 14.11_

  - [ ] 4.8 Implement ref local requires initializer validation
    - Detect `ref` local declarations without an initializer
    - Emit `MIST_REF_0010`
    - _Requirements: 5.7, 14.10_

  - [ ] 4.9 Implement RefKind overload distinction
    - Update overload resolution to treat parameters differing only in `RefKind` as distinct overloads
    - _Requirements: 7.4_

  - [ ] 4.10 Implement RefKind interface and trait matching
    - When checking interface satisfaction and trait structural matching, require `RefKind` values to match exactly
    - Emit `MIST_REF_0012` on mismatch
    - _Requirements: 7.5, 7.6_

  - [ ]* 4.11 Write property tests for type checker ref diagnostics (Properties 3–8, 10–11, 13–14)
    - **Property 3: Ref Requires Assignable Storage Location**
    - **Property 4: Ref and Ownership Mutual Exclusivity**
    - **Property 5: Ref Type Exact Match**
    - **Property 6: Ref Return Lifetime Safety**
    - **Property 7: Ref Readonly Assignment Prohibition**
    - **Property 8: Ref Call-Site Annotation Enforcement**
    - **Property 10: RefKind Overload Distinction**
    - **Property 11: RefKind Interface and Trait Matching**
    - **Property 13: Ref Field Storage Prohibition**
    - **Property 14: Ref Lambda Capture Prohibition**
    - Generate programs with specific ref violations, compile in-process via `CompilerDriver`, verify expected diagnostic IDs
    - **Validates: Requirements 2.2, 2.3, 2.5, 3.3, 3.4, 4.3, 4.5, 4.6, 4.8, 5.6, 5.9, 6.2, 6.5, 6.7, 7.4, 7.5, 7.6, 9.1, 9.4, 9.5, 14.1–14.11**

- [ ] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. C# emitter extensions
  - [ ] 6.1 Emit `ref` / `ref readonly` for parameters in `CSharpEmitter`
    - When emitting parameter declarations, check `RefKind` and prepend `ref` or `ref readonly` before the type
    - _Requirements: 8.1_

  - [ ] 6.2 Emit `ref` / `ref readonly` for method return types in `CSharpEmitter`
    - When emitting method signatures, check `ReturnRefKind` and prepend `ref` or `ref readonly` before the return type
    - _Requirements: 8.2, 8.3_

  - [ ] 6.3 Emit `ref` / `ref readonly` for local variable declarations in `CSharpEmitter`
    - When emitting local variable declarations, check `RefKind` and prepend `ref` or `ref readonly` before the type
    - _Requirements: 8.4_

  - [ ] 6.4 Emit `ref` for arguments and `return ref` statements in `CSharpEmitter`
    - When emitting call-site arguments, check for `RefArgument` nodes and prepend `ref`
    - When emitting return statements with `refKeyword` present, emit `return ref expr;`
    - _Requirements: 8.5, 8.6_

  - [ ]* 6.5 Write property test for C# emission correctness (Property 12)
    - **Property 12: C# Emission Preserves Ref Annotations**
    - Generate random valid ref-annotated programs, compile in-process via `CompilerDriver`, use emitter to get C# strings, verify `ref` / `ref readonly` keywords appear in correct positions
    - **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6**

- [ ] 7. Pretty printer extensions
  - [ ] 7.1 Update `MistPrettyPrinter` to emit ref annotations
    - Emit `ref ` before parameter types when `refModifier` AST field is present
    - Emit `ref ` or `readonly ref ` before method return types based on `returnOwnership` node type in ref-return context
    - Emit `ref ` or `readonly ref ` before local variable types for ref locals
    - Emit `ref ` before arguments in call expressions when the argument is a `RefArgument` node
    - Emit `ref ` before return expression when `refKeyword` is present in `ReturnStatement`
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

  - [ ]* 7.2 Write property test for pretty printer round-trip (Property 1)
    - **Property 1: Pretty Printer Round-Trip**
    - Generate random valid ref-annotated Mist source fragments, parse → print → parse → print, verify idempotence (character-for-character equality on both emissions)
    - Uses existing `MistLexer` + `MistParser` + `MistPrettyPrinter` pipeline in-process
    - **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6**

- [ ] 8. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 9. TextMate grammar update
  - [ ] 9.1 Verify and update TextMate grammar for `ref` in new positions
    - Check `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` to confirm `ref` receives `storage.modifier.mist` scope in argument prefix and local variable prefix positions
    - If gaps exist, add patterns to match `ref` at word boundaries in these new positions
    - Ensure identifiers containing `ref` as a substring (e.g., `reference`, `refresh`) do NOT receive the scope
    - Ensure `ref` inside string literals, comments, or interpolation expressions does NOT receive the scope
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

- [ ] 10. Sample application
  - [ ] 10.1 Create `samples/RefParameters/mist.toml` and `samples/RefParameters/Program.mist`
    - Create `mist.toml` with `name = "RefParameters"`
    - Implement `Program.mist` with `public static void RunDemo()` demonstrating all ref patterns:
      - `ref` parameters with value types (structs) — label `[ref-param-struct]`
      - `ref` parameters with reference types (classes) — label `[ref-param-class]`
      - Ref returns from methods returning references to fields — label `[ref-return-field]`
      - Ref returns from methods returning references to array elements — label `[ref-return-array]`
      - Ref locals aliasing variables — label `[ref-local-var]`
      - Ref locals aliasing fields — label `[ref-local-field]`
      - Ref locals aliasing array elements — label `[ref-local-array]`
      - `ref readonly` locals — label `[ref-readonly-local]`
      - Ref forwarding (passing a ref parameter as a ref argument) — label `[ref-forward]`
      - Struct mutation visibility — label `[ref-struct-mutation]`
    - Print before/after values for `ref-param-struct` and `ref-param-class` patterns
    - _Requirements: 13.1, 13.2, 13.3, 13.5, 13.6_

  - [ ] 10.2 Integrate into `samples/AllFeatures`
    - Add `RefParameters.Program.RunDemo()` call in `samples/AllFeatures/Program.mist`
    - Add `[[references]]` entry with `path = "../RefParameters"` in `samples/AllFeatures/mist.toml`
    - _Requirements: 13.4_

  - [ ]* 10.3 Write property test for ref mutation visibility (Property 2)
    - **Property 2: Ref Mutation Visibility**
    - Generate Mist programs where a variable is passed as `ref` and the callee assigns a new value; verify the caller observes the mutation
    - Uses batched approach: generate all scenarios, build one combined program, compile once, run once, compare output lines
    - **Validates: Requirements 3.1, 3.2, 4.7, 5.4, 10.1, 10.2, 10.5**

- [ ] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The `REF` keyword token already exists in `keywords.storm` — no lexer changes needed
- The grammar change to `ArgumentList` is the most impactful structural change; it affects all existing code that processes argument lists
- All property-based tests use in-process compilation (no process spawning) except Property 2 which uses the batched approach (1 compile, 1 run, N output lines)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "1.3"] },
    { "id": 1, "tasks": ["1.4"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["3.2"] },
    { "id": 4, "tasks": ["3.3", "9.1"] },
    { "id": 5, "tasks": ["3.4", "4.1", "4.2", "4.3", "4.4", "4.8", "4.9"] },
    { "id": 6, "tasks": ["4.5", "4.6", "4.7", "4.10"] },
    { "id": 7, "tasks": ["4.11", "6.1", "6.2", "6.3"] },
    { "id": 8, "tasks": ["6.4"] },
    { "id": 9, "tasks": ["6.5", "7.1"] },
    { "id": 10, "tasks": ["7.2"] },
    { "id": 11, "tasks": ["10.1"] },
    { "id": 12, "tasks": ["10.2", "10.3"] }
  ]
}
```
