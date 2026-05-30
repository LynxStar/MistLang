# Implementation Plan: Void Value Literal

## Overview

This plan implements context-sensitive `void` keyword resolution in expression position, an implicit `Void → Type` conversion operator, deprecation of `Void.Void`, and updates to samples and documentation.

## Tasks

- [x] 1. Add implicit operator Type to Void.mist
  - Add `public static implicit operator Type(Void v)` to `Source/Standard/Void.mist` that returns `Type.Of(v)`
  - Verify the stdlib compiles without errors by running `dotnet build` on the Mist project
  - Confirm the operator follows the existing cast operator pattern used elsewhere in the stdlib
  - **Requirements:** 4.1
  - **Design:** Section 2a (Void.mist Update)
  - **Files:** `Source/Standard/Void.mist`

- [x] 2. Target-type-aware void resolution in SemanticModelBuilder
  - Introduce an optional `targetType` parameter (defaulting to `null`) to the `ResolvePrimary` method (or the relevant resolution path for `TypeLiteralExpressionPrimaryNode`)
  - In the `TypeLiteralExpressionPrimaryNode` case, when `typeLiteral.TypeRef` is a `VoidTypeRefTypeReferenceNode` and `targetType?.IsVoid() == true`, resolve the expression type as `VoidType` (the referenced type) instead of `Standard.Type`
  - Thread the target type from variable declaration resolution (typed variable initializer) into the expression resolution call
  - Thread the target type from argument resolution (method parameter type) into the expression resolution call
  - Thread the target type from return statement resolution (method return type) into the expression resolution call
  - Thread the target type from generic constructor argument resolution (substituted type parameter) into the expression resolution call
  - Ensure `var` inference (no target type) continues to resolve as `Standard.Type` by passing `null`
  - Ensure target type `Standard.Type` continues to resolve as `Standard.Type` (falls through existing path)
  - Build the project and verify zero warnings
  - **Requirements:** 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 3.1, 3.2
  - **Design:** Section 1a (Target Type Propagation), Section 1b (Target Type Sources)
  - **Files:** `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

- [x] 3. Deprecation warning for Void.Void
  - Declare a new diagnostic ID `MIST_SM_0023` (`VoidDotVoidDeprecated`) in `SemanticModelBuilder`
  - In the existing `MemberAccessSuffixPostfixSuffixNode` special case for `Void.Void` (~line 19570), emit a warning diagnostic with the message: "Use 'void' in expression position instead of 'Void.Void' to obtain the Void singleton."
  - Ensure the diagnostic includes the source location of the `Void.Void` expression
  - Verify `Void.Void` still resolves as `VoidType` (no functional change, just adds warning)
  - Build the project and verify zero warnings in the compiler itself
  - **Requirements:** 5.1, 5.2, 5.3
  - **Design:** Section 1c (Deprecation Warning for Void.Void)
  - **Files:** `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

- [x] 4. Context-sensitive void emission in CSharpEmitter
  - In the `TypeLiteralExpressionPrimaryNode` emission case, check the resolved expression type from the semantic model
  - When the resolved type is `VoidType` (value literal), emit `MistVoid.Instance` and set `_needsMistVoidStruct = true`
  - When the resolved type is `VoidType` AND `_emitVoidAsNull` is true (optional context), emit `null` instead
  - When the resolved type is `Standard.Type` (type literal), preserve existing `typeof(MistVoid)` emission
  - Add `public static implicit operator System.Type(MistVoid v) => typeof(MistVoid);` to the `MistVoid.g.cs` struct template
  - Build the project and verify zero warnings
  - **Requirements:** 6.1, 6.2, 6.3, 6.4, 6.5
  - **Design:** Section 3a, 3b, 3c (CSharpEmitter), Section 4 (MistVoid.g.cs Update)
  - **Files:** `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`

- [x] 5. Unit tests for void value literal resolution
  - Add test: `void x = void;` compiles, expression type in semantic model is `VoidType` (Req 1.2)
  - Add test: `Type t = void;` compiles, expression type is `Standard.Type` (Req 2.2)
  - Add test: `var x = void;` infers `Standard.Type` (Req 3.2)
  - Add test: `var x = void; void y = x;` produces type mismatch diagnostic (Req 3.3)
  - Add test: `return void;` in void method accepted without error (Req 1.4)
  - Add test: `void` as argument to void-typed parameter accepted (Req 1.3)
  - Add test: `void` in generic context `new Result<void>(void, true)` accepted (Req 1.5)
  - Add test: `void v = void; Type t = v;` accepted via implicit conversion (Req 4.2)
  - Add test: `Void.Void` still resolves as `VoidType` (Req 5.1)
  - Add test: `Void.Void` emits `MIST_SM_0023` warning (Req 5.2)
  - All tests pass with `dotnet test`
  - **Requirements:** 1.2, 1.3, 1.4, 1.5, 2.2, 3.2, 3.3, 4.2, 5.1, 5.2
  - **Design:** Testing Strategy (Unit Tests)
  - **Files:** `Test/Mist.Tests/Semantics/` (new test file)

- [x] 6. Unit tests for void value literal emission
  - Add test: emitter produces `MistVoid.Instance` for void value literal (Req 6.1)
  - Add test: emitter produces `typeof(MistVoid)` for void type literal (Req 6.2)
  - Add test: emitter produces `null` for void in optional context (Req 6.4)
  - Add test: `MistVoid.g.cs` output includes `implicit operator System.Type` (Req 6.5)
  - All tests pass with `dotnet test`
  - **Requirements:** 6.1, 6.2, 6.4, 6.5
  - **Design:** Testing Strategy (Unit Tests)
  - **Files:** `Test/Mist.Tests/Backend/` (new test file)

- [x] 7. Property-based tests for void value literal
  - Add Property 1 test: generate void literals in various target-type contexts (void, Type, int, string, var), assert expression type is `VoidType` iff target is void — in-process, 0 process spawns
  - Add Property 2 test: generate void-typed variables assigned to Type targets in various positions, assert no diagnostics — in-process, 0 process spawns
  - Add Property 3 test: generate programs with 1–5 `Void.Void` occurrences, assert warning count equals occurrence count — in-process, 0 process spawns
  - Add Property 4 test: generate void literals in value-literal and type-literal contexts, inspect emitted C# for `MistVoid.Instance` vs `typeof(MistVoid)` — in-process, 0 process spawns
  - All property tests pass with `dotnet test`
  - **Requirements:** 1.1, 1.6, 2.1, 3.1, 4.2, 4.3, 5.2, 5.3, 6.1, 6.2, 6.4
  - **Design:** Testing Strategy (Property-Based Testing), Correctness Properties 1–4
  - **Files:** `Test/Mist.Tests/Semantics/` and `Test/Mist.Tests/Backend/` (new test files)

- [x] 8. Update sample applications to use void keyword
  - Find all occurrences of `Void.Void` in `samples/` directory
  - Replace `Void.Void` with `void` in `samples/VoidType/VoidDemo.mist` where target type is void
  - Replace `Void.Void` with `void` in `samples/OptionalTypes/Program.mist` where target type is optional
  - Replace `Void.Void` with `void` in any other sample files that use it
  - Compile and run each updated sample, verify output matches baseline in `samples/__baselines__/`
  - Verify zero errors and zero warnings on all updated samples
  - **Requirements:** 7.1, 7.2, 7.3, 7.4, 7.5
  - **Files:** `samples/VoidType/VoidDemo.mist`, `samples/OptionalTypes/Program.mist`, other samples as needed

- [-] 9. Create VoidValueLiteral sample application
  - Create `samples/VoidValueLiteral/mist.toml` project file
  - Create `samples/VoidValueLiteral/Program.mist` containing the Proof Code from Requirement 9 verbatim
  - Add `VoidValueLiteral.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
  - Add VoidValueLiteral project reference to `samples/AllFeatures/mist.toml`
  - Compile and run the sample, verify output matches expected output exactly
  - Compile and run AllFeatures sample, verify it includes VoidValueLiteral output
  - **Requirements:** 9.1, 9.2, 9.3, 9.4, 9.5, 9.6
  - **Files:** `samples/VoidValueLiteral/Program.mist`, `samples/VoidValueLiteral/mist.toml`, `samples/AllFeatures/Program.mist`, `samples/AllFeatures/mist.toml`

- [ ] 10. Update language book
  - Update `book/src/basics/void-type.md` to document void as value literal with context-sensitive resolution rules
  - Update `book/src/basics/functions.md` to replace `Void.Void` reference with `void`
  - Update all `.mist` snippets in `book/snippets/basics/void-type/` to use `void` instead of `Void.Void`
  - Update all `.mist` snippets in `book/snippets/type-system/optional-types/` to use `void` instead of `Void.Void`
  - Run `dotnet run --project tools/SnippetEmitter` and verify zero failures
  - Verify all updated snippets compile successfully
  - **Requirements:** 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7
  - **Files:** `book/src/basics/void-type.md`, `book/src/basics/functions.md`, `book/snippets/basics/void-type/`, `book/snippets/type-system/optional-types/`

## Task Dependency Graph

```json
{
  "waves": [
    {
      "name": "Wave 1: Core Compiler Changes",
      "tasks": [1, 3, 4],
      "description": "Stdlib operator, deprecation warning, and emitter changes can be done in parallel"
    },
    {
      "name": "Wave 2: Semantic Resolution",
      "tasks": [2],
      "dependsOn": [1],
      "description": "Target-type-aware resolution depends on the implicit operator existing in Void.mist"
    },
    {
      "name": "Wave 3: Tests",
      "tasks": [5, 6, 7],
      "dependsOn": [1, 2, 3, 4],
      "description": "All tests depend on the core compiler changes being complete"
    },
    {
      "name": "Wave 4: Samples and Documentation",
      "tasks": [8, 9, 10],
      "dependsOn": [1, 2, 3, 4],
      "description": "Samples and book updates depend on the compiler supporting the new feature"
    }
  ]
}
```

Tasks 1, 3, and 4 can be worked in parallel. Task 2 depends on Task 1 (the implicit operator must exist for the type checker to accept `Void → Type`). Tasks 5–10 depend on the core compiler changes (Tasks 1–4) being complete.

## Notes

- All property-based tests use in-process compilation with the shared `CompilerDriver` — zero external process spawns per the test-process-spawning policy.
- The deprecation warning (Task 3) will cause existing tests that compile programs with `Void.Void` to see a new warning. Those tests may need their assertions updated to expect the warning, or the test programs updated to use `void` instead.
- Task 8 (updating existing samples) must be done before Task 9 (new sample) to ensure the baselines are stable.
