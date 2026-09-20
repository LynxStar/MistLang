# Requirements Document

## Introduction

This feature adds first-class functions and lambda expressions to the Mist programming language. Since `void` is a real unit type in Mist, the language unifies all callable types under a single `Func<TParams..., TResult>` generic type family. A function that returns nothing is simply `Func<T, void>`. The `Action<TParams...>` type alias is provided as sugar for the void-return case, matching C# developer expectations. The lowercase keyword `func` serves as a reserved alias for `Standard.Func`, following the same pattern as `int` → `Int32` and `string` → `String`.

The feature encompasses: the `Func` type family (arity 0–16), the `func` keyword alias, the `Action` type alias, lambda expressions (both expression-bodied and block-bodied), type inference for lambda parameters and return types, static lambdas that prohibit variable capture, async lambdas, and well-defined capture semantics that interact correctly with Mist's ownership system and ref struct restrictions.

The `delegate` keyword is deliberately omitted. Since Mist has type aliases and `Func` types are structural (two `Func` types with the same type arguments are the same type), there is no scenario where a named delegate declaration adds value beyond what a type alias provides.

## Glossary

- **Func_Type**: The `Standard.Func<TParams..., TResult>` generic type family representing callable values. The last type argument is always the return type; preceding type arguments are parameter types. Arity ranges from 0 (just a return type: `Func<TResult>`) to 16 parameters (`Func<T1, ..., T16, TResult>`).
- **Action_Type**: A type alias family where `Action` = `Func<void>`, `Action<T>` = `Func<T, void>`, etc. Sugar for the void-return case.
- **Func_Keyword**: The reserved keyword `func`, a lowercase alias for `Standard.Func` following the same pattern as `int`/`string`/`void`.
- **Lambda_Expression**: An anonymous function expression using the `=>` syntax. May be expression-bodied (`x => x + 1`) or block-bodied (`(x) => { return x + 1; }`).
- **Capture**: The act of a lambda expression referencing a variable from its enclosing scope. The captured variable's value is closed over and available when the lambda executes.
- **Closure**: The runtime representation of a lambda that has captured variables from its enclosing scope. Implemented as a compiler-generated class containing the captured variables as fields.
- **Static_Lambda**: A lambda declared with the `static` modifier (`static x => x + 1`) that is prohibited from capturing any variables from enclosing scopes. Guarantees no heap allocation for the closure.
- **Compiler**: The Mist compiler pipeline, encompassing lexing, parsing, semantic analysis, type checking, and code generation.
- **Parser**: The Storm-generated parser that produces an AST from a token stream according to the Mist grammar.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into syntactically valid Mist source text.
- **Semantic_Model_Builder**: The `SemanticModelBuilder` component that constructs the semantic model from the parsed AST.
- **Type_Checker**: The `TypeCheckerAnalyzer` component that validates type correctness.
- **Emitter**: The `CSharpEmitter` backend component that generates C# source code.
- **Type_Map**: The `CSharpTypeMap` component that maps Mist types to their C# source representations.
- **Standard_Library**: The built-in Mist library at `Source/Standard/` providing core types and utilities.
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.

## Requirements

### Requirement 1: Func Type Family

**User Story:** As a Mist developer, I want a `Func` generic type family that represents callable values, so that I can declare variables, parameters, fields, and return types that hold functions.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Func` as an `intrinsic public class` family in the `Standard` namespace, with variants from 1 to 17 type parameters (representing 0 to 16 input parameters plus 1 return type), in a file at `Source/Standard/Bootstrap/Func.mist`.
2. THE last type argument of any `Func` instantiation SHALL always be the return type. Preceding type arguments SHALL be the parameter types in order. For example, `Func<int, string, bool>` represents a function taking `int` and `string`, returning `bool`.
3. WHEN a program references `Func<TResult>` (single type argument), THE Compiler SHALL treat it as a zero-parameter function returning `TResult`.
4. WHEN a program references `Func<T1, TResult>` (two type arguments), THE Compiler SHALL treat it as a one-parameter function taking `T1` and returning `TResult`.
5. THE Compiler SHALL support `Func` with up to 17 type arguments (16 parameters + 1 return type), matching C#'s delegate arity limit.
6. Two `Func` types SHALL be considered the same type if and only if they have the same number of type arguments and each corresponding type argument is the same type (structural equality).
7. THE Type_Checker SHALL permit `Func` types in all positions where types are accepted: variable declarations, parameter types, return types, field types, generic type arguments, and type alias right-hand sides.

### Requirement 2: The `func` Keyword

**User Story:** As a Mist developer, I want `func` as a lowercase keyword alias for `Standard.Func`, so that function types feel like a foundational language concept alongside `int`, `string`, and `void`.

#### Acceptance Criteria

1. THE Lexer SHALL recognize `func` as a reserved keyword token.
2. WHEN `func` is used as a type reference with type arguments (e.g., `func<int, string>`), THE Semantic_Model_Builder SHALL resolve it to `Standard.Func` with the same type arguments.
3. THE keyword `func` SHALL be interchangeable with `Func` in all type positions — `func<int, string>` and `Func<int, string>` SHALL resolve to the same type.
4. WHEN `func` appears in identifier position (e.g., as a variable name), THE Parser SHALL report a syntax error indicating the keyword cannot be used as an identifier.
5. THE Pretty_Printer SHALL emit `func` (lowercase) when printing Func types, following the convention of preferring keyword aliases over qualified names (same as `int` over `Int32`).
6. THE CSharp_Type_Map SHALL contain entries mapping `Standard.Func` variants to the corresponding C# `System.Func<>` or `System.Action<>` delegate types based on whether the return type is `void`.

### Requirement 3: Action Type Alias

**User Story:** As a Mist developer familiar with C#, I want `Action<T>` as sugar for `Func<T, void>`, so that void-returning function types read naturally.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare `Action` as a type alias family in the `Standard` namespace: `Action` = `Func<void>`, `Action<T1>` = `Func<T1, void>`, through `Action<T1, ..., T16>` = `Func<T1, ..., T16, void>`.
2. WHEN a program references `Action<int>`, THE Semantic_Model_Builder SHALL resolve it to `Func<int, void>`.
3. THE Type_Checker SHALL consider `Action<int>` and `Func<int, void>` as the same type (structural equivalence through the alias).
4. WHEN a lambda is assigned to a variable of type `Action<T>`, THE Type_Checker SHALL infer the lambda's return type as `void`.
5. THE Compiler SHALL support `Action` with 0 to 16 type arguments, matching the `Func` family's parameter arity.

### Requirement 4: Lambda Expression Syntax — Expression-Bodied

**User Story:** As a Mist developer, I want to write concise lambda expressions using `x => expr` syntax, so that I can create inline functions without verbose boilerplate.

#### Acceptance Criteria

1. WHEN the Parser encounters `identifier => expression`, THE Parser SHALL produce a LambdaExpression AST node with a single implicitly-typed parameter and an expression body.
2. WHEN the Parser encounters `(identifier) => expression`, THE Parser SHALL produce a LambdaExpression AST node with a single implicitly-typed parameter in parentheses and an expression body.
3. WHEN the Parser encounters `(id1, id2) => expression`, THE Parser SHALL produce a LambdaExpression AST node with multiple implicitly-typed parameters and an expression body.
4. WHEN the Parser encounters `() => expression`, THE Parser SHALL produce a LambdaExpression AST node with zero parameters and an expression body.
5. WHEN the Parser encounters `(Type id) => expression`, THE Parser SHALL produce a LambdaExpression AST node with a single explicitly-typed parameter and an expression body.
6. WHEN the Parser encounters `(Type1 id1, Type2 id2) => expression`, THE Parser SHALL produce a LambdaExpression AST node with multiple explicitly-typed parameters and an expression body.
7. THE Parser SHALL NOT permit mixing implicitly-typed and explicitly-typed parameters in the same lambda (e.g., `(int x, y) => ...` is invalid).
8. IF a lambda parameter list mixes typed and untyped parameters, THEN THE Parser SHALL report a diagnostic error indicating that all parameters must be either typed or untyped.

### Requirement 5: Lambda Expression Syntax — Block-Bodied

**User Story:** As a Mist developer, I want to write multi-statement lambdas using `(params) => { statements }` syntax, so that I can create inline functions with complex logic.

#### Acceptance Criteria

1. WHEN the Parser encounters `identifier => { statements }`, THE Parser SHALL produce a LambdaExpression AST node with a single implicitly-typed parameter and a block body.
2. WHEN the Parser encounters `(params) => { statements }`, THE Parser SHALL produce a LambdaExpression AST node with the specified parameters and a block body.
3. WHEN a block-bodied lambda contains `return expr;` statements, THE Parser SHALL parse them as return statements within the lambda body.
4. WHEN a block-bodied lambda with a non-void return type reaches the end of its block without a return statement, THE Type_Checker SHALL report a diagnostic error indicating a missing return statement.
5. WHEN a block-bodied lambda with a `void` return type reaches the end of its block without a return statement, THE Compiler SHALL treat the lambda as implicitly returning `Void.Void`.

### Requirement 6: Lambda Type Inference

**User Story:** As a Mist developer, I want the compiler to infer lambda parameter types and return types from context, so that I can write concise lambdas without redundant type annotations.

#### Acceptance Criteria

1. WHEN a lambda with implicitly-typed parameters is assigned to a variable with an explicit `Func` type (e.g., `func<int, string> foo = x => ...`), THE Type_Checker SHALL infer the parameter types from the target type's type arguments.
2. WHEN a lambda with implicitly-typed parameters is passed as an argument to a method parameter with a `Func` type, THE Type_Checker SHALL infer the parameter types from the parameter's Func type arguments.
3. WHEN a lambda has explicitly-typed parameters and an expression body, THE Type_Checker SHALL infer the return type from the expression's type.
4. WHEN a lambda has explicitly-typed parameters and a block body with return statements, THE Type_Checker SHALL infer the return type as the common type of all return expressions in the block.
5. WHEN a lambda is assigned using `var` with explicitly-typed parameters (e.g., `var foo = (int x) => x + 1;`), THE Semantic_Model_Builder SHALL infer the variable type as the `Func` type constructed from the explicit parameter types and the inferred return type.
6. IF a lambda is assigned using `var` with implicitly-typed parameters (e.g., `var foo = x => x + 1;`), THEN THE Type_Checker SHALL report a diagnostic error indicating that lambda parameter types cannot be inferred without a target type.
7. WHEN a lambda's return type is inferred from multiple return statements with different types, THE Type_Checker SHALL report a diagnostic error indicating inconsistent return types unless a common base type or union type can be determined.
8. WHEN a lambda is used in a generic method call where the type argument depends on the lambda's type, THE Type_Checker SHALL participate in generic type argument inference by propagating the lambda's inferred type.

### Requirement 7: Function Type as First-Class Value

**User Story:** As a Mist developer, I want to store functions in variables, pass them as arguments, and return them from methods, so that I can use higher-order programming patterns.

#### Acceptance Criteria

1. THE Type_Checker SHALL permit assigning a lambda expression to a variable whose type is a compatible `Func` type.
2. THE Type_Checker SHALL permit passing a lambda expression as an argument to a parameter whose type is a compatible `Func` type.
3. THE Type_Checker SHALL permit returning a lambda expression from a method whose return type is a compatible `Func` type.
4. THE Type_Checker SHALL permit storing `Func`-typed values in fields of classes and structs.
5. THE Type_Checker SHALL permit using `Func` types as generic type arguments (e.g., `List<func<int, string>>`).
6. WHEN a variable of `Func` type is followed by an invocation suffix (parenthesized argument list), THE Type_Checker SHALL treat the expression as a function invocation, type-checking the arguments against the Func's parameter types and producing the Func's return type.
7. WHEN a method group (method name without invocation) is used in a context expecting a `Func` type, THE Type_Checker SHALL convert the method group to a delegate value if the method signature is compatible with the target Func type. A GENERIC method's type arguments SHALL be taken from the target: each type parameter is the type standing at the positions of the target that mention it, the result included, and the method converts only where every type parameter meets exactly one type and the signature so closed is the target's. `func<int, int> same = Api.Same;` converts `T Same<T>(T)` at `int`; a method the target leaves open (`Pick<T, R>(T | R)` against `func<int | string, string>`) or pulls two ways (`First<T>(T, T)` against `func<int, long, long>`) does not convert, a non-generic overload of the same signature is preferred as method-overloading Requirement 3.11 prefers it at a call, and an inferred type argument is held to the method's constraints.
8. IF a method group is ambiguous (multiple overloads match the target Func type and method-overloading Requirement 3.8 orders none of them above the rest), THEN THE Type_Checker SHALL report a diagnostic error indicating the ambiguity.
9. IF a Func-typed variable is invoked with the wrong number of arguments, THEN THE Type_Checker SHALL report a diagnostic error indicating the argument count mismatch.
10. IF a Func-typed variable is invoked with arguments of incompatible types, THEN THE Type_Checker SHALL report a diagnostic error indicating the type mismatch for each incompatible argument.

### Requirement 8: Static Lambdas

**User Story:** As a Mist developer, I want to declare lambdas as `static` to guarantee they capture no variables, so that I can avoid accidental closure allocations and make capture intent explicit.

#### Acceptance Criteria

1. WHEN the `static` modifier precedes a lambda expression (e.g., `static x => x + 1` or `static (int x) => { return x + 1; }`), THE Parser SHALL produce a LambdaExpression AST node with the static flag set.
2. IF a static lambda references a variable from an enclosing scope (other than static fields, constants, or method parameters marked `static`), THEN THE Type_Checker SHALL report a diagnostic error indicating that static lambdas cannot capture variables.
3. THE Type_Checker SHALL permit static lambdas to reference: static fields, constants, type names, and the lambda's own parameters.
4. THE Type_Checker SHALL NOT permit static lambdas to reference: local variables from enclosing scopes, instance fields (via `this`), or non-static method parameters from enclosing methods.
5. WHEN a static lambda is assigned to a Func-typed variable, THE resulting Func type SHALL be the same type as a non-static lambda with the same signature — the `static` modifier affects only capture validation, not the type.

### Requirement 9: Async Lambdas

**User Story:** As a Mist developer, I want to declare async lambdas, so that I can create inline asynchronous functions that participate in structured concurrency.

#### Acceptance Criteria

1. WHEN the `async` modifier precedes a lambda expression (e.g., `async x => await SomeMethod(x)` or `async (int x) => { return await Compute(x); }`), THE Parser SHALL produce a LambdaExpression AST node with the async flag set.
2. WHEN an async lambda has an expression body of type `T`, THE Type_Checker SHALL infer the lambda's return type as `Task<T>`, following the same wrapping rules as async methods.
3. WHEN an async lambda has a block body with return statements returning type `T`, THE Type_Checker SHALL infer the lambda's return type as `Task<T>`.
4. THE Type_Checker SHALL permit `await` expressions inside async lambda bodies (async context).
5. IF an `await` expression appears inside a non-async lambda body, THEN THE Type_Checker SHALL report a diagnostic error indicating that `await` requires an async context.
6. WHEN an async lambda is assigned to a variable, THE variable's Func type SHALL reflect the `Task<T>` return type (e.g., an async lambda `async (int x) => x + 1` has type `func<int, Task<int>>`).
7. THE Parser SHALL support combining `static` and `async` modifiers on a lambda (e.g., `static async x => await Compute(x)`), producing a lambda that is both static (no capture) and async.
8. WHEN a non-async lambda body calls an async method, THE Compiler SHALL apply the same auto-await and unwrap rules as any other synchronous context — the async method's result is implicitly awaited and the lambda receives the unwrapped `T` value, not `Task<T>`.
9. WHEN a non-async lambda body calls an async method, THE Emitter SHALL emit the lambda as `async` in C# (since the auto-await requires the C# lambda to be async), even though the Mist lambda is not declared async. This matches the existing behavior for sync methods that call async methods.

### Requirement 10: Variable Capture Semantics

**User Story:** As a Mist developer, I want clear rules about how lambdas capture variables from enclosing scopes, so that I can reason about closure behavior and lifetime.

#### Acceptance Criteria

1. WHEN a lambda references a local variable from an enclosing scope, THE Compiler SHALL capture the variable by reference — mutations to the variable inside the lambda are visible outside, and mutations outside are visible inside the lambda.
2. WHEN a lambda captures a value-type variable (struct), THE Compiler SHALL lift the variable into a compiler-generated closure class so that the lambda and the enclosing scope share the same storage location.
3. WHEN multiple lambdas in the same scope capture the same variable, THE Compiler SHALL share a single closure instance among all lambdas that capture overlapping variable sets from the same scope.
4. WHEN a lambda captures a variable declared in a loop body, THE Compiler SHALL create a fresh capture per loop iteration (each iteration's lambda captures its own copy of the loop variable).
5. IF a lambda captures a variable that is declared after the lambda in source order, THEN THE Type_Checker SHALL report a diagnostic error indicating use of a variable before its declaration.
6. THE Compiler SHALL permit lambdas to capture `this` (the enclosing instance) when the lambda appears inside an instance method.
7. WHEN a lambda captures `this`, THE Compiler SHALL store a reference to the enclosing instance in the closure, not a copy of the instance.

### Requirement 11: Capture Restrictions — Ref Structs and Owned Types

**User Story:** As a Mist developer, I want the compiler to prevent capturing ref structs and owned values in lambdas, so that stack-only and ownership invariants are preserved.

#### Acceptance Criteria

1. IF a lambda captures a variable whose type is a ref struct, THEN THE Type_Checker SHALL report a diagnostic error indicating that ref struct values cannot be captured by lambdas because they cannot be stored on the heap.
2. IF a lambda captures a variable whose type is an owned type, THEN THE Type_Checker SHALL report a diagnostic error indicating that owned values cannot be captured because capture would create a shared reference to a value that must have sole ownership.
3. IF a lambda captures a variable with an `owned` ownership specifier, THEN THE Type_Checker SHALL report a diagnostic error indicating that owned variables cannot be captured.
4. IF a lambda captures a variable with a `ref` (borrowed) ownership specifier, THEN THE Type_Checker SHALL report a diagnostic error indicating that borrowed references cannot be captured because they are non-storable and the lambda's closure would outlive the borrow.
5. IF a lambda captures a variable with a `frozen ref` ownership specifier, THEN THE Type_Checker SHALL report a diagnostic error indicating that frozen references cannot be captured because they are non-storable.
6. IF a lambda captures a variable with a `readonly ref` ownership specifier, THEN THE Type_Checker SHALL report a diagnostic error indicating that readonly references cannot be captured because they are non-storable.
7. THE Type_Checker SHALL permit lambdas to capture unrestricted reference-type variables (the default tier) without restriction.
8. THE Type_Checker SHALL permit lambdas to capture value-type variables (non-ref-struct structs, primitives) without restriction.

### Requirement 12: Grammar Integration

**User Story:** As a compiler developer, I want the Storm grammar to define the `func` keyword and lambda expressions, so that the parser produces correct AST nodes.

#### Acceptance Criteria

1. THE Grammar SHALL define `FUNC = "func";` as a keyword token in `keywords.storm`.
2. THE Grammar SHALL include `FUNC` followed by a type-argument list as a dedicated alternative in the `SingleTypeRef` rule (e.g., `FUNC typeArgs:TypeArgumentList`), so that `func<int, string>` is parsed as a single type element with type arguments. WHERE `TypeReference` composes one or more `SingleTypeRef` elements into a union via `PIPE`, THE `func` type element SHALL be usable wherever a `SingleTypeRef` is valid, including as a union member.
3. THE Grammar SHALL define a `LambdaExpression` alternative in the `Primary` rule supporting: optional modifiers (`static`, `async`), a parameter list (either a single identifier, or a parenthesized list of optionally-typed identifiers), the `SUCH_THAT` (`=>`) token, and either an expression body or a block body.
4. THE Grammar SHALL define `LambdaParameter` as either `type:TypeReference name:IDENTIFIER` (explicitly typed) or `name:IDENTIFIER` (implicitly typed).
5. THE `LambdaExpression` rule SHALL have lower precedence than assignment to avoid ambiguity with the `=>` token used in switch arms and expression-bodied properties.
6. THE Grammar SHALL NOT introduce a `delegate` keyword or delegate declaration rule.

### Requirement 13: Pretty-Printing Func Types and Lambdas

**User Story:** As a compiler developer, I want the pretty printer to format Func types and lambda expressions back into valid Mist source text, so that round-trip fidelity is maintained.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a Func type, THE Pretty_Printer SHALL emit `func<T1, T2, ..., TReturn>` using the lowercase keyword form.
2. WHEN the Pretty_Printer encounters an Action type alias usage, THE Pretty_Printer SHALL emit `Action<T1, ...>` preserving the alias form.
3. WHEN the Pretty_Printer encounters a lambda expression with an expression body, THE Pretty_Printer SHALL emit `params => expr` with appropriate parenthesization of the parameter list.
4. WHEN the Pretty_Printer encounters a lambda expression with a block body, THE Pretty_Printer SHALL emit `params => { statements }` with the block formatted according to existing block formatting rules.
5. WHEN the Pretty_Printer encounters a static lambda, THE Pretty_Printer SHALL emit `static` before the parameter list.
6. WHEN the Pretty_Printer encounters an async lambda, THE Pretty_Printer SHALL emit `async` before the parameter list.
7. FOR ALL valid Func type references, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).
8. FOR ALL valid lambda expressions, parsing the pretty-printed output and then pretty-printing again SHALL produce identical text (round-trip property).

### Requirement 14: C# Code Generation

**User Story:** As a compiler developer, I want the C# backend to emit correct code for Func types and lambda expressions, so that Mist programs using first-class functions compile and run correctly on .NET.

#### Acceptance Criteria

1. WHEN the Emitter encounters a `Func<T1, T2, TReturn>` type where TReturn is not void, THE Emitter SHALL emit the corresponding C# `System.Func<T1, T2, TReturn>` delegate type.
2. WHEN the Emitter encounters a `Func<T1, void>` type (or `Action<T1>`), THE Emitter SHALL emit the corresponding C# `System.Action<T1>` delegate type.
3. WHEN the Emitter encounters a `Func<void>` type (or `Action`), THE Emitter SHALL emit the C# `System.Action` delegate type (non-generic).
4. WHEN the Emitter encounters a lambda expression, THE Emitter SHALL emit a C# lambda expression with the same parameter list and body.
5. WHEN the Emitter encounters a static lambda, THE Emitter SHALL emit the C# `static` modifier on the lambda (C# 9 static lambdas).
6. WHEN the Emitter encounters an async lambda, THE Emitter SHALL emit the C# `async` modifier on the lambda.
7. WHEN the Emitter encounters a method group conversion (method name used as a function value), THE Emitter SHALL emit the method name as a method group reference in C# (which C# implicitly converts to the appropriate delegate type).
8. WHEN the Emitter encounters an invocation of a Func-typed variable, THE Emitter SHALL emit a standard C# delegate invocation (`variable(args)`).
9. THE Emitter SHALL NOT emit any explicit closure class — C# handles closure generation automatically from lambda expressions.

### Requirement 15: C# Interop — Ambient Delegate Projection

**User Story:** As a Mist developer consuming .NET libraries, I want C# delegate types from ambient assemblies to be projected as Mist `func` types or type aliases, so that I can use .NET APIs that accept or return delegates seamlessly.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider encounters `System.Func<T1, ..., TN, TResult>` in an ambient assembly, THE Assembly_Type_Provider SHALL project it as the corresponding Mist `func<T1, ..., TN, TResult>` type.
2. WHEN the Assembly_Type_Provider encounters `System.Action<T1, ..., TN>` in an ambient assembly, THE Assembly_Type_Provider SHALL project it as the corresponding Mist `Action<T1, ..., TN>` type (which resolves to `func<T1, ..., TN, void>`).
3. WHEN the Assembly_Type_Provider encounters a named C# delegate type (e.g., `EventHandler`, `Comparison<T>`, `Predicate<T>`), THE Assembly_Type_Provider SHALL project it as a type alias to the equivalent `func` type based on the delegate's `Invoke` method signature.
4. WHEN a .NET method parameter has a delegate type, THE Type_Checker SHALL accept a Mist lambda expression as an argument if the lambda's signature is compatible with the projected func type.
5. WHEN a .NET method returns a delegate type, THE Type_Checker SHALL treat the return value as the projected func type, permitting invocation and assignment to func-typed variables.
6. WHEN a named C# delegate type has generic type parameters (e.g., `Comparison<T>`), THE Assembly_Type_Provider SHALL project it as a generic type alias to the corresponding generic func type.
7. THE Emitter SHALL emit the original C# delegate type name (not `Func`/`Action`) when the Mist code interacts with an API that expects a specific named delegate type, ensuring C# compilation succeeds.

### Requirement 16: Type Alias Interaction

**User Story:** As a Mist developer, I want to create type aliases for Func types, so that I can give meaningful names to function signatures used repeatedly.

#### Acceptance Criteria

1. WHEN a type alias is declared with a Func type as the right-hand side (e.g., `type Predicate<T> = func<T, bool>;`), THE Semantic_Model_Builder SHALL register the alias and resolve usages to the underlying Func type.
2. WHEN a type alias for a Func type is used as a variable type, parameter type, or return type, THE Type_Checker SHALL treat it identically to the expanded Func type.
3. WHEN a lambda is assigned to a variable whose type is a type alias for a Func type, THE Type_Checker SHALL infer lambda parameter types from the expanded Func type's type arguments.
4. THE Type_Checker SHALL consider a type alias for a Func type and the equivalent expanded Func type as the same type (structural equivalence through the alias).

### Requirement 17: Func Type in Ownership System

**User Story:** As a Mist developer, I want Func-typed values to interact correctly with the ownership system, so that function values follow the same rules as other reference types.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat Func-typed values as reference types for ownership purposes (closures are heap-allocated objects).
2. THE Type_Checker SHALL permit Func-typed variables to have any ownership specifier that is valid for reference types: unrestricted (default), `owned`, `readonly ref`, `frozen ref`, `ref`.
3. WHEN a Func-typed variable has a `readonly ref` specifier, THE Type_Checker SHALL permit invoking the function (invocation is a read operation) but SHALL NOT permit reassigning the variable.
4. WHEN a Func-typed variable has a `frozen ref` specifier, THE Type_Checker SHALL permit invoking the function but SHALL NOT permit reassigning the variable.
5. WHEN a Func-typed variable has a `ref` specifier, THE Type_Checker SHALL permit both invocation and reassignment.
6. Static lambdas (which have no closure) SHALL still be treated as reference types for type system uniformity — the optimization is a backend concern.

### Requirement 18: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want lambda expressions and `func` types to be syntax-highlighted correctly, so that I get visual cues for function type syntax.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL recognize `func` as a type keyword (scope `keyword.type.mist` or `storage.type.mist`) when it appears as a complete token bounded by word boundaries, consistent with other type keywords like `int`, `string`, `void`.
2. THE TextMate_Grammar SHALL highlight `static` before a lambda parameter list as a storage modifier (scope `storage.modifier.mist`), consistent with its existing scope.
3. THE TextMate_Grammar SHALL highlight `async` before a lambda parameter list as a storage modifier (scope `storage.modifier.mist`), consistent with its existing scope.
4. THE TextMate_Grammar SHALL NOT introduce any new keyword scopes for `delegate` since the keyword is not part of Mist.

### Requirement 19: CSHARP_FEATURES.md Update

**User Story:** As a Mist developer reviewing the feature coverage matrix, I want the `CSHARP_FEATURES.md` document to reflect that first-class functions and lambdas are now supported.

#### Acceptance Criteria

1. THE `CSHARP_FEATURES.md` document SHALL update the `Delegates` row status from `🔲` to `✅` with Notes `Func<T, TResult> type family with func keyword; no delegate keyword`.
2. THE `CSHARP_FEATURES.md` document SHALL update the `Lambda expressions (=>)` row status from `🔲` to `✅` with Notes `Expression and block bodies, static, async`.
3. THE `CSHARP_FEATURES.md` document SHALL retain the `Anonymous methods (delegate { })` row with status `⛔` and Notes `Superseded by lambdas`.
4. THE `CSHARP_FEATURES.md` document SHALL update the `Generic delegates` row status from `🔲` to `✅` with Notes `Func type family supports 0-16 parameters`.

### Requirement 20: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating first-class functions and lambdas, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/FirstClassFunctions/Program.mist` with a corresponding `samples/FirstClassFunctions/mist.toml` project file whose `name` field is set to `"FirstClassFunctions"`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: func type declarations, Action type alias usage, expression-bodied lambdas, block-bodied lambdas, lambdas with zero/one/multiple parameters, type inference from target type, type inference from explicit parameters with `var`, method group conversion, higher-order functions (functions accepting and returning functions), static lambdas, async lambdas (sync lambda calling async, async lambda with explicit await, async lambda with scope-exit await, async functor usage), variable capture, type aliases for func types, invocation of func-typed variables, lambdas passed to Mist extension methods, and lambdas passed to C# ambient extension methods.
3. Each demonstrated pattern SHALL print labeled output to stdout so correctness is observable.
4. The sample SHALL include a `public static void RunDemo()` method that exercises all demonstrated patterns.
5. `samples/AllFeatures/Program.mist` SHALL call `FirstClassFunctions.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a reference entry for the FirstClassFunctions project.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.
7. The sample SHALL contain the proof code blocks below verbatim and produce the specified expected output, matched line-by-line exactly.

#### Proof Code

```mist
using Standard;

namespace FirstClassFunctions;

// PROOF: Type alias for a Func type works
type Predicate<T> = func<T, bool>;
type Transform<TIn, TOut> = func<TIn, TOut>;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== FirstClassFunctions Sample ===");

        // PROOF: func keyword as type, expression-bodied lambda
        // PROOF: Type inference of lambda params from target func type
        Console.WriteLine("--- Expression Lambda ---");
        func<int, int> doubler = x => x * 2;
        Console.WriteLine($"doubler(5)={doubler(5)}");
        Console.WriteLine($"doubler(0)={doubler(0)}");

        // PROOF: Multi-parameter func type with lambda
        Console.WriteLine("--- Multi-Param Lambda ---");
        func<int, int, int> adder = (x, y) => x + y;
        Console.WriteLine($"adder(3,4)={adder(3, 4)}");

        // PROOF: Zero-parameter func (Func<TResult> form)
        Console.WriteLine("--- Zero-Param Lambda ---");
        func<string> greeter = () => "Hello, Mist!";
        Console.WriteLine($"greeter()={greeter()}");

        // PROOF: Block-bodied lambda with explicit return
        Console.WriteLine("--- Block Lambda ---");
        func<int, string> describer = (int n) =>
        {
            if (n > 0)
            {
                return "positive";
            }
            if (n < 0)
            {
                return "negative";
            }
            return "zero";
        };
        Console.WriteLine($"describer(5)={describer(5)}");
        Console.WriteLine($"describer(-1)={describer(-1)}");
        Console.WriteLine($"describer(0)={describer(0)}");

        // PROOF: var with explicitly-typed lambda params infers func type
        Console.WriteLine("--- Var Inference ---");
        var square = (int x) => x * x;
        Console.WriteLine($"square(6)={square(6)}");

        // PROOF: Action type alias (void return) works
        Console.WriteLine("--- Action Lambda ---");
        Action<string> printer = msg => Console.WriteLine($"printed: {msg}");
        printer("test");

        // PROOF: Higher-order function — func as parameter
        Console.WriteLine("--- Higher-Order Parameter ---");
        int result = Apply(10, x => x + 5);
        Console.WriteLine($"Apply(10, x=>x+5)={result}");

        // PROOF: Higher-order function — func as return value
        Console.WriteLine("--- Higher-Order Return ---");
        var multiplier = MakeMultiplier(3);
        Console.WriteLine($"multiplier(7)={multiplier(7)}");
        Console.WriteLine($"multiplier(0)={multiplier(0)}");

        // PROOF: Method group conversion to func type
        Console.WriteLine("--- Method Group ---");
        func<int, int> fn = Negate;
        Console.WriteLine($"fn(42)={fn(42)}");

        // PROOF: Type alias for func type works as target type
        Console.WriteLine("--- Type Alias ---");
        Predicate<int> isEven = x => x % 2 == 0;
        Console.WriteLine($"isEven(4)={isEven(4)}");
        Console.WriteLine($"isEven(7)={isEven(7)}");
        Transform<int, string> toStr = x => $"val={x}";
        Console.WriteLine($"toStr(99)={toStr(99)}");

        // PROOF: Variable capture — lambda reads and mutates enclosing variable
        Console.WriteLine("--- Capture ---");
        int counter = 0;
        func<int> increment = () =>
        {
            counter = counter + 1;
            return counter;
        };
        Console.WriteLine($"increment()={increment()}");
        Console.WriteLine($"increment()={increment()}");
        Console.WriteLine($"counter={counter}");

        // PROOF: Static lambda — no capture allowed, uses only parameters
        Console.WriteLine("--- Static Lambda ---");
        func<int, int, int> staticAdd = static (a, b) => a + b;
        Console.WriteLine($"staticAdd(10,20)={staticAdd(10, 20)}");

        // PROOF: Func type storing a function that returns a function
        Console.WriteLine("--- Nested Func Type ---");
        func<int, func<int, int>> curryAdd = a => b => a + b;
        var add5 = curryAdd(5);
        Console.WriteLine($"add5(3)={add5(3)}");
        Console.WriteLine($"curryAdd(1)(2)={curryAdd(1)(2)}");

        // PROOF: Sync lambda calling async method — auto-awaits, gets unwrapped T
        Console.WriteLine("--- Sync Lambda Calls Async ---");
        func<int, string> syncLookup = id => AsyncLookup(id);
        Console.WriteLine($"syncLookup(1)={syncLookup(1)}");
        Console.WriteLine($"syncLookup(2)={syncLookup(2)}");

        // PROOF: Async lambda calling async method with explicit await
        Console.WriteLine("--- Async Lambda Explicit Await ---");
        func<int, Task<string>> asyncLookup = async id => await AsyncLookup(id);
        Console.WriteLine($"asyncLookup(3)={asyncLookup(3)}");

        // PROOF: Async lambda calling async method without await — structured concurrency awaits at scope exit
        Console.WriteLine("--- Async Lambda Scope Exit ---");
        func<int, Task<void>> asyncFire = async id =>
        {
            AsyncLookup(id);
        };
        asyncFire(4);
        Console.WriteLine("asyncFire completed");

        // PROOF: Sync lambda using async functor to lift a sync call
        Console.WriteLine("--- Sync Lambda Async Lift ---");
        func<int, Task<int>> liftedSync = id => async ComputeSync(id);
        Console.WriteLine($"liftedSync(10)={liftedSync(10)}");

        // PROOF: Async lambda using async functor to lift a sync call
        Console.WriteLine("--- Async Lambda Async Lift ---");
        func<int, Task<Task<int>>> asyncLifted = async id => async ComputeSync(id);
        var innerTask = asyncLifted(20);
        Console.WriteLine($"asyncLifted(20)={await innerTask}");

        // PROOF: Sync lambda returning a Task explicitly (not auto-awaited because it's the return value)
        Console.WriteLine("--- Sync Lambda Returns Task ---");
        func<int, Task<string>> taskReturner = id => async AsyncLookup(id);
        Console.WriteLine($"taskReturner(5)={taskReturner(5)}");

        // PROOF: Lambda passed to a Mist extension method
        Console.WriteLine("--- Mist Extension Method ---");
        int[] nums = [1, 2, 3, 4, 5];
        int[] evens = nums.Filter(x => x % 2 == 0);
        Console.WriteLine($"evens.Length={evens.Length}");
        if (evens.Length > 1)
        {
            Console.WriteLine($"evens[0]={evens[0]}");
            Console.WriteLine($"evens[1]={evens[1]}");
        }

        // PROOF: Lambda passed to a C# ambient extension method (List<T>.ForEach)
        Console.WriteLine("--- C# Extension Method ---");
        List<int> items = new List<int>();
        items.Add(10);
        items.Add(20);
        items.Add(30);
        items.ForEach(item => Console.WriteLine($"item={item}"));

        Console.WriteLine("=== FirstClassFunctions Done ===");
    }

    // Helper async method for concurrency demos
    public static async string AsyncLookup(int id)
    {
        return $"item-{id}";
    }

    // Helper sync method for async functor demos
    public static int ComputeSync(int x)
    {
        return x * 2;
    }

    // PROOF: Method with func-type parameter (higher-order)
    public static int Apply(int value, func<int, int> transform)
    {
        return transform(value);
    }

    // PROOF: Method returning a func type (higher-order)
    public static func<int, int> MakeMultiplier(int factor)
    {
        return x => x * factor;
    }

    // PROOF: Named method used as method group
    public static int Negate(int x)
    {
        return -x;
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}

// PROOF: Mist extension method that accepts a func parameter
extension(int[] self)
{
    public int[] Filter(func<int, bool> predicate)
    {
        var result = new List<int>();
        foreach (int item in self)
        {
            if (predicate(item))
            {
                result.Add(item);
            }
        }
        return result.ToArray() ?? new int[0];
    }
}
```

**Expected output:**
```
=== FirstClassFunctions Sample ===
--- Expression Lambda ---
doubler(5)=10
doubler(0)=0
--- Multi-Param Lambda ---
adder(3,4)=7
--- Zero-Param Lambda ---
greeter()=Hello, Mist!
--- Block Lambda ---
describer(5)=positive
describer(-1)=negative
describer(0)=zero
--- Var Inference ---
square(6)=36
--- Action Lambda ---
printed: test
--- Higher-Order Parameter ---
Apply(10, x=>x+5)=15
--- Higher-Order Return ---
multiplier(7)=21
multiplier(0)=0
--- Method Group ---
fn(42)=-42
--- Type Alias ---
isEven(4)=True
isEven(7)=False
toStr(99)=val=99
--- Capture ---
increment()=1
increment()=2
counter=2
--- Static Lambda ---
staticAdd(10,20)=30
--- Nested Func Type ---
add5(3)=8
curryAdd(1)(2)=3
--- Sync Lambda Calls Async ---
syncLookup(1)=item-1
syncLookup(2)=item-2
--- Async Lambda Explicit Await ---
asyncLookup(3)=item-3
--- Async Lambda Scope Exit ---
asyncFire completed
--- Sync Lambda Async Lift ---
liftedSync(10)=20
--- Async Lambda Async Lift ---
asyncLifted(20)=40
--- Sync Lambda Returns Task ---
taskReturner(5)=item-5
--- Mist Extension Method ---
evens.Length=2
evens[0]=2
evens[1]=4
--- C# Extension Method ---
item=10
item=20
item=30
=== FirstClassFunctions Done ===
```

### Requirement 21: Book Highlight.js Update

**User Story:** As a Mist developer reading the book, I want `func` to be syntax-highlighted in code examples, so that function types are visually distinct like other type keywords.

#### Acceptance Criteria

1. THE `book/theme/highlight.js` file SHALL include `func` in the `RESERVED_TYPES` string alongside `int`, `string`, `void`, and other type keywords.
2. THE highlight.js Mist language definition SHALL highlight `func` with the same scope as other type keywords (the `type` keyword class).

### Requirement 22: Book Documentation

**User Story:** As a Mist developer, I want a chapter in the Mist book documenting first-class functions and lambdas, so that I can learn the function type system and see practical examples.

#### Acceptance Criteria

1. A chapter SHALL exist at `book/src/functions/first-class-functions.md` covering first-class functions and lambda expressions.
2. THE chapter SHALL be listed in `book/src/SUMMARY.md` under an appropriate section.
3. THE chapter SHALL explain the following concepts with code examples: the `func` keyword and `Func` type family, `Action` as a void-return alias, expression-bodied lambdas, block-bodied lambdas, type inference for lambdas, higher-order functions, method group conversion, static lambdas, async lambdas, variable capture semantics, capture restrictions (ref structs, owned types), and type aliases for function types.
4. THE chapter SHALL include a comparison with C#'s Func/Action/delegate system explaining how Mist simplifies the model by making void a real type and using `func` as a keyword.
5. Code snippets in the chapter SHALL have corresponding compilable `.mist` files in `book/snippets/functions/` so they can be validated by the build.
