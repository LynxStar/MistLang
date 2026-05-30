# Implementation Plan: First-Class Functions and Lambdas

## Overview

This plan implements first-class functions and lambda expressions for the Mist language, progressing through the compiler pipeline: standard library → grammar → parsing → semantic model → type checking → C# emission → interop → editor support → sample app → documentation. Each task builds incrementally on the previous, ensuring no orphaned code.

## Tasks

- [x] 1. Standard library: Func type family
  - [x] 1.1 Create `Source/Standard/Bootstrap/Func.mist` with the `Func<TResult>` through `Func<T1,...,T16,TResult>` intrinsic class declarations
    - Declare 17 variants of `intrinsic public class Func<...>` in the `Standard` namespace (arity 1 through 17 type parameters)
    - Each variant has no members — invocation is handled by the type checker
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 1.2 Create `Source/Standard/Bootstrap/Action.mist` with the `Action` type alias family
    - Declare 17 type aliases: `type Action = Func<void>;` through `type Action<T1,...,T16> = Func<T1,...,T16, void>;`
    - _Requirements: 3.1, 3.2, 3.3, 3.5_

  - [x] 1.3 Add `Standard.Func` → `System.Func`/`System.Action` mapping to `CSharpTypeMap.cs`
    - Extend `GetTypeName` to handle `ConstructedType` whose `GenericDefinition` is a `Standard.Func` class
    - When last type argument is `Void`: emit `System.Action` (non-generic) or `System.Action<T1,...,TN-1>`
    - When last type argument is not `Void`: emit `System.Func<T1,...,TN>`
    - _Requirements: 2.6, 14.1, 14.2, 14.3_

- [ ] 2. Grammar and keyword definitions
  - [x] 2.1 Add `FUNC` keyword token to `keywords.storm`
    - Add `token FUNC = "func";` to `Source/Mist/Grammar/keywords.storm`
    - _Requirements: 2.1, 12.1_

  - [ ] 2.2 Add `FuncTypeRef` alternative to the `TypeReference` rule in `Mist.storm`
    - Add `| FUNC typeArgs:TypeArgumentList as FuncTypeRef` to the `TypeReference` rule
    - Also add to `SingleTypeRef` rule for union type contexts: `| FUNC typeArgs:TypeArgumentList as FuncSingleTypeRef`
    - _Requirements: 2.2, 12.2_

  - [ ] 2.3 Add `LambdaExpression` grammar rules to `Mist.storm`
    - Add `LambdaExpression` as a new alternative in the `Primary` rule
    - Define `LambdaExpression` rule with modifiers (`static`, `async`), parameter list, `SUCH_THAT`, and body (expression or block)
    - Define `LambdaParameterList` rule (parenthesized with optional types, or single identifier)
    - Define `LambdaParam` rule (typed: `TypeReference IDENTIFIER`, untyped: `IDENTIFIER`)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 8.1, 9.1, 12.3, 12.4, 12.5_

- [ ] 3. Pretty-printer support
  - [ ] 3.1 Add `FuncTypeRef` emission to `MistPrettyPrinter.cs`
    - Handle `FuncTypeRefTypeReferenceNode` emitting `func<T1, T2, ..., TReturn>`
    - _Requirements: 13.1, 13.7_

  - [ ] 3.2 Add `LambdaExpression` emission to `MistPrettyPrinter.cs`
    - Handle expression-bodied lambdas: `params => expr`
    - Handle block-bodied lambdas: `params => { stmts }`
    - Handle single implicit param without parens: `x => expr`
    - Handle zero params: `() => expr`
    - Handle typed params: `(int x) => expr`
    - Handle `static` and `async` modifiers
    - _Requirements: 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8_

- [ ] 4. Checkpoint — Verify grammar, parsing, and pretty-printing
  - Build the project and ensure all existing tests pass
  - Verify that `func<int, string>` parses as a type reference
  - Verify that lambda expressions parse correctly in all forms

- [ ] 5. Semantic model: Func type resolution
  - [ ] 5.1 Implement `func` keyword resolution in `SemanticModelBuilder.cs`
    - When resolving a `FuncTypeRefTypeReferenceNode`, look up `Standard.Func` with the matching arity and construct a `ConstructedType`
    - Ensure `func<int, string>` and `Func<int, string>` resolve to the same type
    - _Requirements: 2.2, 2.3, 1.6_

  - [ ] 5.2 Implement Func-typed invocation recognition in `SemanticModelBuilder.cs`
    - When an `InvocationSuffix` is applied to an expression whose resolved type is a `Func` constructed type, extract parameter types (all but last type arg) and return type (last type arg)
    - Record the invocation expression's type as the return type
    - _Requirements: 7.6_

  - [ ] 5.3 Implement method group conversion in `SemanticModelBuilder.cs`
    - When an identifier expression appears in a context expecting a `Func` type and resolves to a method symbol, perform method group conversion
    - Verify method signature compatibility with target `Func` type
    - Record expression type as the target `Func` type
    - _Requirements: 7.7_

- [ ] 6. Semantic model: Lambda expression type resolution
  - [ ] 6.1 Implement lambda expression type inference — target-type-driven
    - When a lambda with implicitly-typed parameters is assigned to a variable with explicit `Func` type or passed to a `Func`-typed parameter, infer parameter types from the target type's type arguments
    - _Requirements: 6.1, 6.2_

  - [ ] 6.2 Implement lambda expression type inference — explicit parameters with `var`
    - When a lambda has explicitly-typed parameters and is assigned using `var`, infer return type from body and construct the `Func` type
    - Expression body: return type = expression type
    - Block body: return type = common type of all return statements
    - _Requirements: 6.3, 6.4, 6.5_

  - [ ] 6.3 Implement lambda body type resolution
    - Resolve expressions and statements within lambda bodies using the inferred parameter types
    - Handle `return` statements in block-bodied lambdas
    - Handle implicit `Void.Void` return for void block lambdas
    - _Requirements: 5.3, 5.4, 5.5_

  - [ ] 6.4 Implement async lambda return type wrapping
    - For async lambdas, wrap the inferred return type in `Task<T>`
    - Set async context flag within async lambda bodies (enables `await`)
    - For non-async lambdas calling async methods, apply auto-await rules
    - _Requirements: 9.2, 9.3, 9.4, 9.6, 9.8, 9.9_

- [ ] 7. Checkpoint — Verify semantic model resolution
  - Build the project and ensure all existing tests pass
  - Verify that `func<int, string> f = x => ...` resolves correctly
  - Verify that Func-typed invocation produces the correct return type

- [ ] 8. Type checker: validation rules
  - [ ] 8.1 Implement lambda parameter validation
    - Error `MIST_TC_0115`: mixed typed/untyped parameters in same lambda
    - Error `MIST_TC_0116`: cannot infer parameter types (no target type, implicit params)
    - Error `MIST_TC_0117`: inconsistent return types in block lambda
    - Error `MIST_TC_0118`: missing return in non-void block lambda
    - _Requirements: 4.7, 4.8, 6.6, 6.7, 5.4_

  - [ ] 8.2 Implement Func invocation validation
    - Error `MIST_TC_0119`: argument count mismatch on Func-typed invocation
    - Error `MIST_TC_0120`: argument type mismatch on Func-typed invocation
    - Error `MIST_TC_0121`: ambiguous method group (multiple overloads match target Func type)
    - _Requirements: 7.8, 7.9, 7.10_

  - [ ] 8.3 Implement static lambda validation
    - Error `MIST_TC_0113`: static lambda references enclosing variable, instance field, or `this`
    - Permit references to: lambda's own parameters, static fields, constants, type names
    - _Requirements: 8.2, 8.3, 8.4_

  - [ ] 8.4 Implement async lambda validation
    - Error `MIST_TC_0114`: `await` expression in non-async lambda body
    - Permit `await` in async lambda bodies
    - Validate `static async` combination
    - _Requirements: 9.5, 9.7_

  - [ ] 8.5 Implement capture restriction validation
    - Error `MIST_TC_0110`: lambda captures ref struct variable
    - Error `MIST_TC_0111`: lambda captures owned-type variable
    - Error `MIST_TC_0112`: lambda captures borrowed/frozen ref/readonly ref variable
    - Permit capture of unrestricted reference types and value types
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_

- [ ] 9. Checkpoint — Verify type checking
  - Build the project and ensure all existing tests pass
  - Verify that capture restriction errors fire correctly
  - Verify that static lambda validation works

- [ ] 10. C# code generation
  - [ ] 10.1 Implement lambda expression emission in `CSharpEmitter.cs`
    - Emit C# lambda syntax: `(params) => expr` or `(params) => { stmts }`
    - Emit `static` modifier for static lambdas (C# 9 static lambdas)
    - Emit `async` modifier for async lambdas
    - For non-async lambdas that call async methods (auto-await), emit `async` on the C# lambda
    - _Requirements: 14.4, 14.5, 14.6, 14.9_

  - [ ] 10.2 Implement method group emission in `CSharpEmitter.cs`
    - When emitting a method group conversion, emit just the method name (C# handles implicit delegate conversion)
    - _Requirements: 14.7_

  - [ ] 10.3 Implement Func-typed invocation emission in `CSharpEmitter.cs`
    - Emit standard C# delegate invocation: `variable(args)`
    - _Requirements: 14.8_

- [ ] 11. Checkpoint — Verify end-to-end compilation
  - Build the project and ensure all existing tests pass
  - Write a minimal Mist program using `func<int, int> f = x => x * 2; f(5);` and verify it compiles and runs correctly through the full pipeline

- [ ] 12. Assembly type provider: delegate projection
  - [ ] 12.1 Project `System.Func<>` and `System.Action<>` from ambient assemblies as Mist `Func` types
    - When `TypeProjector` encounters `System.Func<T1,...,TN,TResult>`, project as `Standard.Func<T1,...,TN,TResult>`
    - When encountering `System.Action<T1,...,TN>`, project as `Standard.Func<T1,...,TN,Void>`
    - When encountering `System.Action` (non-generic), project as `Standard.Func<Void>`
    - _Requirements: 15.1, 15.2_

  - [ ] 12.2 Project named C# delegate types as type aliases to equivalent `Func` types
    - For delegates like `Predicate<T>`, `Comparison<T>`, `EventHandler`, project as type alias based on the delegate's `Invoke` method signature
    - _Requirements: 15.3, 15.6_

  - [ ] 12.3 Ensure emitter uses original delegate type names for interop
    - When Mist code passes a lambda to a .NET API expecting a specific named delegate type, emit the original delegate type name in C#
    - _Requirements: 15.7_

- [ ] 13. Checkpoint — Verify interop
  - Build the project and ensure all existing tests pass
  - Verify that `List<T>.ForEach(Action<T>)` accepts a Mist lambda
  - Verify that LINQ-style methods accepting `Func<T, bool>` work

- [ ] 14. Ownership system integration
  - [ ] 14.1 Implement Func type ownership rules in type checker
    - Treat Func-typed values as reference types for ownership purposes
    - Permit all reference-type ownership specifiers: unrestricted, `owned`, `readonly ref`, `frozen ref`, `ref`
    - Invocation is a read operation (permitted on `readonly ref` and `frozen ref`)
    - Reassignment requires `ref` specifier
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6_

- [ ] 15. Type alias interaction verification
  - [ ] 15.1 Verify type aliases for Func types work end-to-end
    - `type Predicate<T> = func<T, bool>;` resolves correctly
    - Lambda type inference sees through aliases to extract parameter/return types
    - Structural equivalence: `Predicate<int>` == `func<int, bool>` == `Func<int, bool>`
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

- [ ] 16. Generic type argument inference with lambdas
  - [ ] 16.1 Implement lambda participation in generic type argument inference
    - When a lambda is passed to a generic method where the type argument depends on the lambda's type, propagate the lambda's inferred type to resolve the generic
    - _Requirements: 6.8_

- [ ] 17. Checkpoint — Full feature verification
  - Build the project and ensure all existing tests pass
  - Run all existing sample apps to verify no regressions

- [ ] 18. TextMate grammar update
  - [ ] 18.1 Add `func` to the type keywords pattern in `mist.tmLanguage.json`
    - Add `func` to the primitive type keyword regex (scope `storage.type.primitive.mist`)
    - _Requirements: 18.1_

  - [ ] 18.2 Verify `static` and `async` before lambda params are highlighted as modifiers
    - These should already be covered by existing modifier patterns — verify and fix if needed
    - _Requirements: 18.2, 18.3, 18.4_

- [ ] 19. CSHARP_FEATURES.md update
  - [ ] 19.1 Update feature coverage matrix
    - `Delegates` row: `🔲` → `✅`, Notes: `Func<T, TResult> type family with func keyword; no delegate keyword`
    - `Lambda expressions (=>)` row: `🔲` → `✅`, Notes: `Expression and block bodies, static, async`
    - `Anonymous methods (delegate { })` row: retain `⛔`, Notes: `Superseded by lambdas`
    - `Generic delegates` row: `🔲` → `✅`, Notes: `Func type family supports 0-16 parameters`
    - _Requirements: 19.1, 19.2, 19.3, 19.4_

- [ ] 20. Sample application
  - [ ] 20.1 Create `samples/FirstClassFunctions/mist.toml` with `name = "FirstClassFunctions"`
    - _Requirements: 20.1_

  - [ ] 20.2 Create `samples/FirstClassFunctions/Program.mist` with the proof code from requirements
    - Copy the proof code verbatim from Requirement 20
    - The sample MUST compile and produce the exact expected output
    - _Requirements: 20.2, 20.3, 20.4, 20.6, 20.7_

  - [ ] 20.3 Add `FirstClassFunctions.Program.RunDemo()` call to `samples/AllFeatures/Program.mist`
    - Add project reference to `samples/AllFeatures/mist.toml`
    - _Requirements: 20.5_

  - [ ] 20.4 Verify sample compiles and produces expected output
    - Run the sample through the Mist compiler and verify stdout matches expected output line-by-line
    - _Requirements: 20.6, 20.7_

- [ ] 21. Book highlight.js update
  - [ ] 21.1 Add `func` to `RESERVED_TYPES` in `book/theme/mist-lang.js`
    - _Requirements: 21.1, 21.2_

- [ ] 22. Book documentation
  - [ ] 22.1 Create `book/src/functions/first-class-functions.md` chapter
    - Cover: `func` keyword, `Func` type family, `Action` alias, expression/block lambdas, type inference, higher-order functions, method groups, static lambdas, async lambdas, capture semantics, capture restrictions, type aliases for function types
    - Include comparison with C#'s Func/Action/delegate system
    - _Requirements: 22.1, 22.2, 22.3, 22.4_

  - [ ] 22.2 Add chapter to `book/src/SUMMARY.md`
    - Add under an appropriate section (e.g., "Advanced Features" or new "Functions" section)
    - _Requirements: 22.2_

  - [ ] 22.3 Create compilable snippet files in `book/snippets/functions/`
    - Create `.mist` and `.cs` pairs for each code example in the chapter
    - Run `dotnet run --project tools/SnippetEmitter` to generate the `.cs` files
    - _Requirements: 22.5_


## Task Dependency Graph

```json
{
  "waves": [
    { "name": "Wave 1: Foundation", "tasks": ["1"] },
    { "name": "Wave 2: Grammar", "tasks": ["2"] },
    { "name": "Wave 3: Pretty-Printer", "tasks": ["3"] },
    { "name": "Wave 4: Parse Checkpoint", "tasks": ["4"] },
    { "name": "Wave 5: Semantic Model", "tasks": ["5", "6"] },
    { "name": "Wave 6: Semantic Checkpoint", "tasks": ["7"] },
    { "name": "Wave 7: Type Checker", "tasks": ["8"] },
    { "name": "Wave 8: Type Check Checkpoint", "tasks": ["9"] },
    { "name": "Wave 9: Emission", "tasks": ["10"] },
    { "name": "Wave 10: E2E Checkpoint", "tasks": ["11"] },
    { "name": "Wave 11: Interop", "tasks": ["12"] },
    { "name": "Wave 12: Interop Checkpoint", "tasks": ["13"] },
    { "name": "Wave 13: Advanced Features", "tasks": ["14", "15", "16"] },
    { "name": "Wave 14: Full Checkpoint", "tasks": ["17"] },
    { "name": "Wave 15: Polish", "tasks": ["18", "19", "20", "21", "22"] }
  ]
}
```

## Notes

- Tasks 18–22 (editor support, docs, sample app) are independent of each other and can be done in any order after Task 17.
- The `func` keyword follows the same pattern as `int`/`string`/`void` — it's a keyword alias for a stdlib type. The implementation mirrors how those keywords are handled.
- Lambda expressions reuse the existing `SUCH_THAT` (`=>`) token. The grammar must handle disambiguation from switch arms and expression-bodied properties.
- Closure generation is entirely delegated to C# — the Mist compiler emits C# lambdas and lets the C# compiler handle closure class generation. This dramatically simplifies the implementation.
- The `Action` type aliases use the existing type alias mechanism with no special handling needed.
- Diagnostic IDs `MIST_TC_0110`–`MIST_TC_0121` are allocated for this feature. Check that these don't conflict with any IDs added between spec creation and implementation.
