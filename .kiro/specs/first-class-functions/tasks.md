# Implementation Plan: First-Class Functions and Lambdas

## Overview

This plan implements first-class functions and lambda expressions for the Mist language, progressing through the compiler pipeline: standard library → grammar → AST migration → pretty-printer → parsing/precedence tests → semantic model → type checking → C# emission → interop → editor support → sample app → documentation.

The grammar work follows the **revised lowest-precedence lambda model** (design §3): lambda is placed at the `Expression` level as a peer of (but looser than) assignment, is **removed from `Primary`**, the assignment RHS is widened to `Expression` so lambdas are reachable from every value position, and the switch-arm guard plus relational-pattern constants are bounded to the `VoidCoalescing` rule (the structural analogue of Roslyn's `Precedence.Coalescing` floor). Splitting `Expression` turns `ExpressionNode` into an abstract base with `LambdaFormExpressionNode`/`AssignmentFormExpressionNode` subtypes, which forces an accessor-migration pass before the tree will compile again. That migration is an early, hard dependency for all downstream semantic, type-checking, and emission work.

Each task builds incrementally on the previous, ensuring no orphaned code.

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

- [x] 2. Grammar and keyword definitions
  - [x] 2.1 Add `FUNC` keyword token to `keywords.storm`
    - Add `token FUNC = "func";` to `Source/Mist/Grammar/keywords.storm`
    - _Requirements: 2.1, 12.1_

  - [x] 2.2 Add `FuncSingleTypeRef` alternative to the `SingleTypeRef` rule in `Mist.storm`
    - Add `| FUNC typeArgs:TypeArgumentList as FuncSingleTypeRef` to the `SingleTypeRef` rule (alongside `PrimitiveSingleTypeRef`, `VoidSingleTypeRef`, `UnknownSingleTypeRef`, `NamedSingleTypeRef`, `ParenthesizedTypeRef`, `TupleSingleTypeRef`)
    - Do NOT add it to `TypeReference`, which is now the flat union `first:SingleTypeRef (PIPE rest:SingleTypeRef)*`; composing `SingleTypeRef` automatically makes `func<...>` usable as a union member
    - This generates the `FuncSingleTypeRefSingleTypeRefNode` AST node
    - _Requirements: 2.2, 12.2_

  - [x] 2.3 Define the lambda rule cluster and re-anchor lambda precedence at the `Expression` level in `Mist.storm`
    - Define the `LambdaExpression` rule with two alternatives — `BlockLambda` (`modifiers:LambdaModifier* params:LambdaParameterList SUCH_THAT body:Block`) and `ExpressionLambda` (`... SUCH_THAT body:Expression`), block tried first so a brace-led body parses as a `Block`
    - Define `LambdaModifier` (`STATIC as StaticLambdaModifier | ASYNC as AsyncLambdaModifier`), `LambdaParameterList` (`ParenthesizedLambdaParams | SingleImplicitParam`), `LambdaParamList`, and `LambdaParam` (`TypedLambdaParam | UntypedLambdaParam`)
    - Split the top-level `Expression` rule into `lambda:LambdaExpression as LambdaForm | assignment:Assignment as AssignmentForm`, making `ExpressionNode` abstract with `LambdaFormExpressionNode`/`AssignmentFormExpressionNode` subtypes and placing lambda at lowest precedence (looser than assignment)
    - **REMOVE the `LambdaExpressionPrimary` alternative from the `Primary` rule** that the cancelled run added — `Primary` must revert to its pre-feature alternatives with no lambda; update the stale `Primary`/`Unary` comments accordingly (an `async`-prefixed lambda is now recognized at the `Expression` level, not in `Primary`)
    - Widen the assignment RHS from `value:Assignment` to `value:Expression` so lambdas are reachable from assignment RHS, call arguments, return values, and nested lambda bodies (right-associativity falls out naturally)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 8.1, 9.1, 9.7, 12.3, 12.4, 12.5, 7.1, 7.2, 7.3_

  - [x] 2.4 Bound the switch-arm guard and relational-pattern constants to `VoidCoalescing` in `Mist.storm`
    - Change `WhenGuard` from `WHEN condition:Expression` to `WHEN condition:VoidCoalescing` so the arm's `=>` is never consumed by a trailing lambda (the actual fix for the guard-swallows-lambda defect)
    - Change the four relational-pattern constants in `PrimaryPattern` (`LessThanRelationalPattern`, `GreaterThanRelationalPattern`, `LessThanOrEqualRelationalPattern`, `GreaterThanOrEqualRelationalPattern`) from `Expression` to `VoidCoalescing` (the structural analogue of Roslyn's `Precedence.Coalescing` floor)
    - Leave the expression-level `Relational` rule's `IsExpression` operator unchanged — it is not adjacent to an arm `=>`
    - _Requirements: 12.5_

- [x] 3. Accessor migration for the abstract `ExpressionNode`
  - [x] 3.1 Migrate all `ExpressionNode` accessor sites, the assignment-RHS type change, and the guard/pattern rebinds so the tree compiles
    - **`ExpressionNode.Assignment` dispatch (~13 source sites):** add a single helper (e.g. `ExpressionNode.AsAssignmentOrNull()` / a two-way subtype `switch`) and route the existing `.Assignment` reads through it across `SemanticModelBuilder.cs` (≈5), `TypeCheckerAnalyzer.cs` (≈9 navigation helpers + `CheckExpression`), `CSharpEmitter.cs` (≈6), `MistPrettyPrinter.cs` (1), `SemanticModel.cs` (1), `OwnershipAnalyzer.cs` (≈4), and `RefinementPredicate.cs` (1); each site handles the lambda subtype or reads `.Assignment` as before
    - **Assignment-RHS type change:** `AssignmentNode.Value` becomes `ExpressionNode`; redirect `ResolveAssignment(assignment.Value, ...)` → `ResolveExpression(...)` in `SemanticModelBuilder`, and update the few pretty-printer/emitter sites that read `assignment.Value` to call the `Expression`-level entry point
    - **Guard/pattern rebind (~7 source sites, zero node-shape change):** `WhenGuardNode.Condition` and the relational-pattern `Constant` are now `VoidCoalescingNode`; redirect to the existing counterparts — `ResolveVoidCoalescing` (semantic model), `CheckVoidCoalescingNode` (type checker), `EmitVoidCoalescing` (pretty printer and C# emitter), and the constant readers in `RefinementPredicate.cs`/`TypeCheckerAnalyzer.cs`
    - **Mechanical test-code fixes (required for the test project to compile):** update `new ExpressionNode { Assignment = ... }` constructions to `new AssignmentFormExpressionNode { Assignment = ... }`, and update `Test/Mist.Tests/Parsing/LambdaExpressionParserTests.cs` (left over from the cancelled run) to reference the new node shapes (`LambdaFormExpressionNode` / `LambdaExpressionNode` at the `Expression` level) instead of the removed `Primary`-level `LambdaExpressionPrimary` nodes
    - _Requirements: 12.3, 12.5, 7.1, 7.2, 7.3_

- [x] 4. Checkpoint — Tree compiles after grammar + accessor migration
  - Build the project and ensure it compiles with zero warnings after the `Expression`-split node-shape change
  - Ensure all existing tests pass (no regressions from the accessor migration or guard/pattern rebind)
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Pretty-printer support
  - [x] 5.1 Add `FuncSingleTypeRef` emission to `MistPrettyPrinter.cs`
    - Handle `FuncSingleTypeRefSingleTypeRefNode` emitting `func<T1, T2, ..., TReturn>` using the lowercase keyword form
    - _Requirements: 13.1, 13.7_

  - [x] 5.2 Add lambda and abstract-`Expression` dispatch to `MistPrettyPrinter.cs`
    - Make `EmitExpression(ExpressionNode)` dispatch on subtype: `LambdaFormExpressionNode` emits the lambda; `AssignmentFormExpressionNode` delegates to the existing `EmitAssignment` path
    - Emit lambdas: expression body `params => expr`, block body `params => { stmts }`, single implicit param without parens `x => expr`, zero params `() => expr`, typed params `(int x) => expr`, and the `static`/`async` modifiers
    - _Requirements: 13.3, 13.4, 13.5, 13.6, 13.8_

- [ ] 6. Parser and precedence tests
  - [x] 6.1 Add parser unit tests for lambda forms and the precedence model
    - Extend/replace `Test/Mist.Tests/Parsing/LambdaExpressionParserTests.cs` to assert each lambda form produces the correct AST node at the `Expression` level: single/multi/zero params, typed params, block body, expression body, `static`, `async`, `static async`
    - Assert `f = x => x + 1` parses as `f = (x => x + 1)` (assignment RHS is the lambda) and `x => y => x + y` parses right-associatively as `x => (y => (x + y))`
    - **Regression:** assert `value switch { int n when predicate => n.ToString(), _ => "none" }` parses into two well-formed arms; the first arm's guard condition is the identifier `predicate` (not a lambda) and its result is `n.ToString()`; also cover `when n > 7 => ...` and `when isOdd(n) => ...`
    - In-process parser tests only (no `dotnet` process spawning)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 8.1, 9.1, 12.5_

  - [x] 6.2 Write property test for switch-arm guard precedence
    - **Property 8: Switch-Arm Guard Never Captures a Trailing Lambda**
    - **Validates: Requirements 12.5**
    - New file (e.g. `SwitchGuardLambdaPrecedencePropertyTests.cs`); generator: arms of the form `pattern when <VoidCoalescing-level guard> => <result>` plus the explicit `when predicate => ...` regression case; assert the guard/result split is correct and the guard condition is not a lambda node; ≥100 iterations, parse-only/in-process (no `dotnet` spawning)
    - Test tag: `Feature: first-class-functions, Property 8: Switch-arm guard never captures a trailing lambda`

  - [x] 6.3 Write property test for lambda lowest-precedence and right-associativity
    - **Property 9: Lambda Is Lowest-Precedence and Right-Associative**
    - **Validates: Requirements 12.5, 4.1**
    - New file (e.g. `LambdaPrecedencePropertyTests.cs`); generator: `f = <lambda>` and nested `x => y => <body>`; assert the parse-tree shape (`f = (L)`, never `(f = ...) => ...`; right-nested lambdas); ≥100 iterations, parse-only/in-process
    - Test tag: `Feature: first-class-functions, Property 9: Lambda is lowest-precedence and right-associative`

  - [x] 6.4 Write property test for lambda reachability from every value position
    - **Property 10: Lambda Is Reachable From Every Value Position**
    - **Validates: Requirements 7.1, 7.2, 7.3, 12.3**
    - New file (e.g. `LambdaReachabilityPropertyTests.cs`); generator: embed a generated lambda as an assignment RHS (`f = L`), a call argument (`g(L)`), a return value (`return L;`), and a nested lambda body (`x => L`); assert a lambda node appears in each AST slot; ≥100 iterations, parse-only/in-process
    - Test tag: `Feature: first-class-functions, Property 10: Lambda is reachable from every value position`

  - [x] 6.5 Write pretty-print round-trip property test for func types and lambdas
    - **Property 6: Pretty-Print Round-Trip**
    - **Validates: Requirements 13.7, 13.8**
    - New file (e.g. `LambdaRoundTripPropertyTests.cs`); generator strengthened to emit lambdas in RHS / argument / return / nested-body positions and inside bounded-guard switch arms; assert `print(parse(print(ast))) == print(ast)`; ≥100 iterations, parse + pretty-print only/in-process (no `dotnet` spawning)
    - Test tag: `Feature: first-class-functions, Property 6: Pretty-print round-trip`

- [ ] 7. Checkpoint — Verify grammar, parsing, and pretty-printing
  - Build the project and ensure all tests pass
  - Verify `func<int, string>` parses as a type reference and lambda expressions parse in all forms at the `Expression` level
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Semantic model: Func type resolution
  - [x] 8.1 Implement `func` keyword resolution in `SemanticModelBuilder.cs`
    - When resolving a `FuncSingleTypeRefSingleTypeRefNode`, look up `Standard.Func` with the matching arity and construct a `ConstructedType`
    - Ensure `func<int, string>` and `Func<int, string>` resolve to the same type
    - _Requirements: 2.2, 2.3, 1.6_

  - [x] 8.2 Implement Func-typed invocation recognition in `SemanticModelBuilder.cs`
    - When an `InvocationSuffix` is applied to an expression whose resolved type is a `Func` constructed type, extract parameter types (all but last type arg) and return type (last type arg), and record the invocation expression's type as the return type
    - Add the `IsFuncType(IType)` helper (ConstructedType whose `GenericDefinition` is a `Standard.Func` backend-backed class)
    - _Requirements: 7.6_

  - [x] 8.3 Implement method group conversion in `SemanticModelBuilder.cs`
    - When an identifier expression appears in a context expecting a `Func` type and resolves to a method symbol, verify signature compatibility and record the expression type as the target `Func` type
    - _Requirements: 7.7_

  - [x] 8.4 Write unit tests for Func resolution and invocation
    - Cover `func`/`Func` structural equality (Property 1), Func-typed invocation return types, and method group conversion; in-process via the shared `CompilerDriver`/semantic model (no `dotnet` spawning)
    - **Property 1: Func Type Structural Equality**
    - **Validates: Requirements 1.6, 2.3, 7.6, 7.7, 16.4**

- [ ] 9. Semantic model: Lambda expression type resolution
  - [x] 9.1 Implement target-type-driven lambda inference in `SemanticModelBuilder.cs`
    - For the `LambdaFormExpressionNode` subtype, when implicitly-typed params are assigned to an explicit `Func`-typed variable or passed to a `Func`-typed parameter, infer param types from the target type's type arguments
    - _Requirements: 6.1, 6.2_

  - [x] 9.2 Implement explicit-parameter inference with `var` in `SemanticModelBuilder.cs`
    - When a lambda has explicitly-typed params and is assigned using `var`, infer the return type from the body and construct the `Func` type (expression body: expression type; block body: common type of all return statements)
    - _Requirements: 6.3, 6.4, 6.5_

  - [x] 9.3 Implement lambda body type resolution in `SemanticModelBuilder.cs`
    - Resolve expressions/statements within lambda bodies using inferred parameter types; handle `return` statements in block bodies and implicit `Void.Void` return for void block lambdas
    - _Requirements: 5.3, 5.4, 5.5_

  - [x] 9.4 Implement async lambda return-type wrapping in `SemanticModelBuilder.cs`
    - For async lambdas wrap the inferred return type in `Task<T>` and set the async context flag (enables `await`); for non-async lambdas calling async methods apply the existing auto-await rules
    - _Requirements: 9.2, 9.3, 9.4, 9.6, 9.8, 9.9_

  - [x] 9.5 Write unit tests for lambda type inference
    - Cover target-type inference, `var` + explicit-param inference, block-body common-type inference, and async wrapping; in-process (no `dotnet` spawning)
    - **Property 2: Lambda Type Inference Completeness**
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**

- [ ] 10. Checkpoint — Verify semantic model resolution
  - Build the project and ensure all tests pass
  - Verify `func<int, string> f = x => ...` resolves correctly and Func-typed invocation produces the correct return type
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Type checker: validation rules
  - [x] 11.1 Implement lambda parameter validation in `TypeCheckerAnalyzer.cs`
    - `MIST_TC_0115` mixed typed/untyped params; `MIST_TC_0116` cannot infer param types (no target type, implicit params); `MIST_TC_0117` inconsistent return types in block lambda; `MIST_TC_0118` missing return in non-void block lambda
    - _Requirements: 4.7, 4.8, 6.6, 6.7, 5.4_

  - [x] 11.2 Implement Func invocation validation in `TypeCheckerAnalyzer.cs`
    - `MIST_TC_0119` argument count mismatch; `MIST_TC_0120` argument type mismatch; `MIST_TC_0121` ambiguous method group
    - _Requirements: 7.8, 7.9, 7.10_

  - [x] 11.3 Implement static lambda validation in `TypeCheckerAnalyzer.cs`
    - `MIST_TC_0113`: static lambda references enclosing variable, instance field, or `this`; permit own params, static fields, constants, type names
    - _Requirements: 8.2, 8.3, 8.4_

  - [x] 11.4 Implement async lambda validation in `TypeCheckerAnalyzer.cs`
    - `MIST_TC_0114`: `await` in non-async lambda body; permit `await` in async bodies; validate `static async` combination
    - _Requirements: 9.5, 9.7_

  - [x] 11.5 Implement capture restriction validation in `TypeCheckerAnalyzer.cs`
    - `MIST_TC_0110` ref struct capture; `MIST_TC_0111` owned-type capture; `MIST_TC_0112` borrowed/frozen/readonly-ref capture; permit unrestricted reference and value types
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8_

  - [x] 11.6 Write unit tests for lambda diagnostics and static-lambda isolation
    - Cover each diagnostic (`MIST_TC_0110`–`MIST_TC_0121`) and static-lambda isolation; in-process (no `dotnet` spawning)
    - **Property 4: Static Lambda Isolation**
    - **Validates: Requirements 8.2, 8.3, 8.4, 8.5**

- [ ] 12. Checkpoint — Verify type checking
  - Build the project and ensure all tests pass
  - Verify capture-restriction errors and static lambda validation fire correctly
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 13. C# code generation
  - [x] 13.1 Implement lambda expression emission in `CSharpEmitter.cs`
    - Emit `(params) => expr` / `(params) => { stmts }`; emit `static` (C# 9 static lambdas) and `async`; for non-async lambdas that call async methods (auto-await), emit `async` on the C# lambda
    - Dispatch through the abstract `ExpressionNode` subtypes (`LambdaFormExpressionNode` emits the lambda; `AssignmentFormExpressionNode` uses the existing assignment path)
    - _Requirements: 14.4, 14.5, 14.6, 14.9_

  - [x] 13.2 Implement method group emission in `CSharpEmitter.cs`
    - When emitting a method group conversion, emit just the method name (C# handles the implicit delegate conversion)
    - _Requirements: 14.7_

  - [x] 13.3 Implement Func-typed invocation emission in `CSharpEmitter.cs`
    - Emit standard C# delegate invocation: `variable(args)`
    - _Requirements: 14.8_

  - [x] 13.4 Write unit tests for Func type emission mapping
    - Assert `CSharpTypeMap` emits `System.Func<...>` / `System.Action<...>` / `System.Action` correctly by return type; in-process string-level assertions (no `dotnet` spawning)
    - **Property 7: C# Emission Correctness**
    - **Validates: Requirements 14.1, 14.2, 14.3**

- [ ] 14. Checkpoint — Verify end-to-end compilation
  - Build the project and ensure all tests pass
  - Add a batched integration test (one compile, one run) for `func<int, int> f = x => x * 2; f(5);` and verify it compiles and runs correctly through the full pipeline
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 15. Assembly type provider: delegate projection
  - [x] 15.1 Project `System.Func<>`/`System.Action<>` as Mist `Func` types in `TypeProjector`
    - `System.Func<T1,...,TN,TResult>` → `Standard.Func<T1,...,TN,TResult>`; `System.Action<T1,...,TN>` → `Standard.Func<T1,...,TN,Void>`; `System.Action` → `Standard.Func<Void>`
    - _Requirements: 15.1, 15.2_

  - [x] 15.2 Project named C# delegate types as type aliases to equivalent `Func` types in `TypeProjector`
    - For `Predicate<T>`, `Comparison<T>`, `EventHandler`, etc., project as a (generic) type alias based on the delegate's `Invoke` signature
    - _Requirements: 15.3, 15.6_

  - [x] 15.3 Ensure the emitter uses original delegate type names for interop in `CSharpEmitter.cs`
    - When Mist passes a lambda to a .NET API expecting a specific named delegate type, emit the original delegate type name in C#
    - _Requirements: 15.7_

- [ ] 16. Checkpoint — Verify interop
  - Build the project and ensure all tests pass
  - Add batched tests verifying `List<T>.ForEach(Action<T>)` and LINQ-style `Func<T, bool>` methods accept Mist lambdas
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 17. Advanced feature integration
  - [x] 17.1 Implement Func type ownership rules in the type checker
    - Treat Func-typed values as reference types; permit unrestricted/`owned`/`readonly ref`/`frozen ref`/`ref`; invocation is a read (permitted on `readonly ref`/`frozen ref`); reassignment requires `ref`; static lambdas are still reference types
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5, 17.6_

  - [x] 17.2 Verify type-alias interaction for Func types end-to-end
    - `type Predicate<T> = func<T, bool>;` resolves; lambda inference sees through aliases; structural equivalence `Predicate<int>` == `func<int, bool>` == `Func<int, bool>`
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

  - [x] 17.3 Implement lambda participation in generic type argument inference
    - When a lambda is passed to a generic method whose type argument depends on the lambda's type, propagate the lambda's inferred type to resolve the generic
    - _Requirements: 6.8_

- [ ] 18. Checkpoint — Full feature verification
  - Build the project and ensure all tests pass
  - Run existing batched sample/integration tests to verify no regressions
  - Ensure all tests pass, ask the user if questions arise.

- [x] 19. TextMate grammar update
  - [x] 19.1 Add `func` to the type keywords pattern in `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add `func` to the primitive/type keyword regex (consistent with `int`, `string`, `void`)
    - _Requirements: 18.1_

  - [x] 19.2 Verify `static`/`async` before lambda params highlight as modifiers in `mist.tmLanguage.json`
    - Confirm existing `storage.modifier.mist` patterns cover them (fix if needed); confirm no `delegate` scope is introduced
    - _Requirements: 18.2, 18.3, 18.4_

- [x] 20. CSHARP_FEATURES.md update
  - [x] 20.1 Update the feature coverage matrix in `CSHARP_FEATURES.md`
    - `Delegates` `🔲` → `✅` (`Func<T, TResult> type family with func keyword; no delegate keyword`); `Lambda expressions (=>)` `🔲` → `✅` (`Expression and block bodies, static, async`); retain `Anonymous methods (delegate { })` `⛔` (`Superseded by lambdas`); `Generic delegates` `🔲` → `✅` (`Func type family supports 0-16 parameters`)
    - _Requirements: 19.1, 19.2, 19.3, 19.4_

- [ ] 21. Sample application
  - [x] 21.1 Create `samples/FirstClassFunctions/mist.toml` with `name = "FirstClassFunctions"`
    - _Requirements: 20.1_

  - [x] 21.2 Create `samples/FirstClassFunctions/Program.mist` with the proof code from Requirement 20 verbatim
    - Copy the proof code block verbatim (Allman braces, all `// PROOF:` comments and the `extension(int[] self)` block); it MUST compile and produce the exact expected output
    - _Requirements: 20.2, 20.3, 20.4, 20.6, 20.7_

  - [ ] 21.3 Integrate the sample into the AllFeatures orchestrator
    - Add `FirstClassFunctions.Program.RunDemo()` call to `samples/AllFeatures/Program.mist` and a project reference entry to `samples/AllFeatures/mist.toml`
    - _Requirements: 20.5_

  - [ ] 21.4 Verify the sample compiles and produces the expected output
    - Compile and run the sample (batched: one compile, one run) and verify stdout matches the Requirement 20 expected output line-by-line
    - _Requirements: 20.6, 20.7_

- [x] 22. Book highlight.js update
  - [x] 22.1 Add `func` to `RESERVED_TYPES` in `book/theme/mist-lang.js`
    - Highlight `func` with the same scope as other type keywords
    - _Requirements: 21.1, 21.2_

- [ ] 23. Book documentation
  - [ ] 23.1 Create `book/src/functions/first-class-functions.md` chapter
    - Cover the `func` keyword, `Func` type family, `Action` alias, expression/block lambdas, type inference, higher-order functions, method groups, static lambdas, async lambdas, capture semantics, capture restrictions, and type aliases for function types; include a comparison with C#'s Func/Action/delegate system
    - _Requirements: 22.1, 22.3, 22.4_

  - [ ] 23.2 Add the chapter to `book/src/SUMMARY.md`
    - Add under an appropriate section
    - _Requirements: 22.2_

  - [ ] 23.3 Create compilable snippet files in `book/snippets/functions/`
    - Create `.mist`/`.cs` pairs for each code example and generate the `.cs` files via the snippet emitter tool
    - _Requirements: 22.5_

- [ ] 24. Final checkpoint — Full suite green
  - Build the project with zero warnings and ensure the entire test suite (including the new in-process property tests for Properties 6, 8, 9, 10) and the batched sample run pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional test sub-tasks and can be skipped for a faster MVP; they are still scheduled in the dependency graph.
- **Why the grammar + accessor migration is an early hard dependency:** splitting `Expression` into `lambda | assignment` turns `ExpressionNode` into an abstract base, so `ExpressionNode.Assignment` disappears and the whole tree fails to compile until Task 3.1 routes the ~13 source accessor sites (through one helper), redirects the widened assignment-RHS type, and rebinds the ~7 guard/pattern sites to the existing `VoidCoalescing` plumbing. Everything downstream (semantic model, type checker, emitter, interop, sample) depends on Task 3.1.
- **The guard fix** binds `WhenGuard.condition` and the four relational-pattern constants to the existing `VoidCoalescing` rule (not a new alias rule) so it reuses `ResolveVoidCoalescing`/`CheckVoidCoalescingNode`/`EmitVoidCoalescing` and keeps the change to a ~2-line grammar edit plus call-site redirects.
- **Property-based tests** (Properties 6, 8, 9, 10) are parse-only / pretty-print round-trip and run fully in-process (no `dotnet` process spawning), per the test-process-spawning policy. Each is tagged `Feature: first-class-functions, Property {n}: {text}`. Properties 1, 2, 4, 7 are covered by in-process unit tests; Properties 3, 5 are exercised by the sample app (capture, async).
- The `func` keyword follows the `int`/`string`/`void` pattern — a keyword alias for a stdlib type. The `Action` aliases use the existing type-alias mechanism. Closure generation is delegated entirely to C#.
- Diagnostic IDs `MIST_TC_0110`–`MIST_TC_0121` are allocated for this feature; confirm no conflicts with IDs added between spec creation and implementation.
- The sample proof code (Requirement 20) is immutable during implementation — if it cannot be made to compile and produce the expected output, stop and report the gap rather than weakening it.

## Session State (for continuation)

**Last session ended:** Wave 3 tasks (5.1, 6.1, 6.2, 6.3, 6.4, 15.1) are all implemented and their individual tests pass. The tasks.md file still shows them as `[ ]` (in_progress) because the orchestrator was interrupted before marking them complete.

**Action for next session:** Mark tasks 5.1, 6.1, 6.2, 6.3, 6.4, and 15.1 as completed, then continue with wave 4+ tasks.

**Verification state:**
- Solution builds with 0 errors, 0 warnings.
- All new task tests pass when run individually (DelegateProjection: 14 pass, LambdaPrecedenceProperty: 8 pass, LambdaReachability: 1 pass, SwitchGuardLambdaPrecedence: 4 pass, FuncTypeRefPrettyPrinter: 6 pass, LambdaExpressionParser: 18 pass).
- Full suite (3000+ tests) passes but has a **pre-existing flaky failure** unrelated to this feature: `Collection was modified; enumeration operation may not execute` in semantic type resolution under parallel execution. This affects `ClrRefEmitterTests`, `OptionalChainingPropertyTests`, and `EndToEndCompilationTests.AllFeatures_InterfaceDispatch` intermittently. All pass deterministically in isolation. This is NOT a regression from first-class-functions work.

**Files present from wave 3 work (untracked, need `git add`):**
- `Source/Mist/Generated/ExpressionNodeExtensions.cs` (task 3.1)
- `Source/Standard/Bootstrap/Action.mist` (task 1.2)
- `Source/Standard/Bootstrap/Func.mist` (task 1.1)
- `Test/Mist.Tests/Backend/DelegateProjectionTests.cs` (task 15.1)
- `Test/Mist.Tests/Parsing/FuncTypeRefPrettyPrinterTests.cs` (task 5.1)
- `Test/Mist.Tests/Parsing/LambdaExpressionParserTests.cs` (task 6.1)
- `Test/Mist.Tests/Parsing/LambdaPrecedencePropertyTests.cs` (task 6.3)
- `Test/Mist.Tests/Parsing/LambdaReachabilityPropertyTests.cs` (task 6.4)
- `Test/Mist.Tests/Parsing/SwitchGuardLambdaPrecedencePropertyTests.cs` (task 6.2)
- `samples/FirstClassFunctions/` (task 21.1)

**Note:** There is a stray `csharp-grammar.g4` file in the repo root that appears unrelated — can be deleted.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["2.3", "19.1", "20.1", "21.1", "22.1"] },
    { "id": 1, "tasks": ["2.4", "19.2"] },
    { "id": 2, "tasks": ["3.1"] },
    { "id": 3, "tasks": ["5.1", "6.1", "6.2", "6.3", "6.4", "15.1"] },
    { "id": 4, "tasks": ["5.2", "8.1", "15.2"] },
    { "id": 5, "tasks": ["6.5", "8.2"] },
    { "id": 6, "tasks": ["8.3"] },
    { "id": 7, "tasks": ["9.1"] },
    { "id": 8, "tasks": ["9.2", "8.4"] },
    { "id": 9, "tasks": ["9.3"] },
    { "id": 10, "tasks": ["9.4"] },
    { "id": 11, "tasks": ["9.5", "17.3"] },
    { "id": 12, "tasks": ["11.1", "17.2"] },
    { "id": 13, "tasks": ["11.2"] },
    { "id": 14, "tasks": ["11.3"] },
    { "id": 15, "tasks": ["11.4"] },
    { "id": 16, "tasks": ["11.5"] },
    { "id": 17, "tasks": ["11.6", "17.1"] },
    { "id": 18, "tasks": ["13.1"] },
    { "id": 19, "tasks": ["13.2"] },
    { "id": 20, "tasks": ["13.3"] },
    { "id": 21, "tasks": ["15.3", "13.4"] },
    { "id": 22, "tasks": ["21.2"] },
    { "id": 23, "tasks": ["21.3", "23.1", "23.3"] },
    { "id": 24, "tasks": ["21.4", "23.2"] }
  ]
}
```
