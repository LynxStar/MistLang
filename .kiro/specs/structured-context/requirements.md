# Requirements Document

## Introduction

This document specifies the requirements for adding structured context to the Mist programming language. Structured context is a mechanism inspired by Racket's `parameterize` that allows a developer to declare a context value at one point in the call graph and consume it at a deeper point without explicitly threading the value through intermediate methods. Context values are keyed by exact type, form a stack (new pushes shadow previous ones), and are scoped to the declaring lexical block. A compile-time checker ensures that any method consuming a context is always called within a scope that provides one.

The `with` keyword already exists in Mist for trait satisfaction blocks (`with TypeName : TraitName { mappings }`). This feature extends the keyword's usage to new syntactic positions for context declaration and consumption. The existing trait-with usage remains unchanged and the grammar must disambiguate between the two usages.

Context stacks belong to a logical execution flow. Awaited callees and child tasks inherit the current context through the runtime execution context; subsequent pushes and pops in a child or sibling do not change another flow's stacks. Suppressing execution-context flow starts a child without inherited context. Context pushes and pops remain tied to lexical scope exit, including exception unwinding.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in two passes: Pass 1 registers types and members, Pass 2 resolves type references and expressions.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Context_Checker**: A new analysis component that validates structured context availability at compile time by walking the call graph.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Context_Value**: A value pushed onto the context stack for a given type, available to callees that bind that type via a `with` parameter.
- **Context_Stack**: A per-type stack of context values maintained at runtime. The top of the stack is the current context for that type.
- **Context_Declaration**: A `with` statement or `with` block that pushes a value onto the Context_Stack for a given type.
- **Context_Binding**: A `with` clause on a method signature that binds the current context value for a given type to a local identifier within the method body.
- **Context_Block**: A scoped `with(expression) { ... }` block that pushes a value for the duration of the block only.
- **Trait_With_Block**: The existing `with TypeName : TraitName { mappings }` syntax for trait satisfaction, which remains unchanged.

## Requirements

### Requirement 1: Context Declaration Statement Grammar

**User Story:** As a Mist developer, I want to declare a context value using a `with` statement inside a method body, so that callees can access the value without explicit parameter passing.

#### Acceptance Criteria

1. THE Grammar SHALL define a `WithVariableDeclarationStatement` rule matching `with var identifier = expression ;` that both pushes a context value and binds a local variable.
2. THE Grammar SHALL define a `WithTypedDeclarationStatement` rule matching `with TypeReference identifier = expression ;` that pushes a context value with an explicit type annotation and binds a local variable.
3. THE Grammar SHALL define a `WithDiscardDeclarationStatement` rule matching `with TypeReference _ = expression ;` that pushes a context value with an explicit type annotation without binding a local variable.
4. THE Grammar SHALL define a `WithPushStatement` rule matching `with expression ;` that pushes a context value without binding a local variable, where the context type is inferred from the expression type.
5. WHEN a `with var identifier = expression ;` statement is parsed, THE Parser SHALL produce an AST node capturing the identifier and the initializer expression.
6. WHEN a `with TypeReference identifier = expression ;` statement is parsed, THE Parser SHALL produce an AST node capturing the type reference, identifier, and the initializer expression.
7. WHEN a `with TypeReference _ = expression ;` statement is parsed, THE Parser SHALL produce an AST node capturing the type reference, the discard marker, and the initializer expression.
8. WHEN a `with expression ;` statement is parsed, THE Parser SHALL produce an AST node capturing the expression.
9. THE Grammar SHALL disambiguate context declaration statements from the existing Trait_With_Block syntax by recognizing that Trait_With_Block uses the pattern `WITH IDENTIFIER COLON IDENTIFIER` at the top level, while context declarations use `WITH VAR`, `WITH TypeReference IDENTIFIER EQUAL`, `WITH TypeReference _ EQUAL`, or `WITH Expression SEMICOLON` inside method bodies (the `Statement` rule).

### Requirement 2: Context Block Grammar

**User Story:** As a Mist developer, I want to push a context value for a limited scope using a `with(expression) { ... }` block, so that the context is automatically popped when the block exits.

#### Acceptance Criteria

1. THE Grammar SHALL define a `WithBlockStatement` rule matching the token sequence `WITH LEFT_PARENTHESIS Expression RIGHT_PARENTHESIS Block`, where the expression field captures the context value expression and the body field captures the block.
2. THE Grammar SHALL include `WithBlockStatement` as an alternative in the `Statement` rule so that `with(expression) { ... }` is recognized within method bodies.
3. WHEN a `with ( expression ) { ... }` statement is parsed, THE Parser SHALL produce a `WithBlockStatement` AST node containing an `expression` child (the parsed context expression) and a `body` child (the parsed block).
4. THE Grammar SHALL disambiguate the context block from the existing top-level `WithDeclaration` by placing `WithBlockStatement` in the `Statement` rule (method-body scope) where the `WITH LEFT_PARENTHESIS` token sequence distinguishes it from `WithDeclaration`'s `WITH IDENTIFIER COLON` sequence at the top level.

### Requirement 3: Context Binding on Method Signatures

**User Story:** As a Mist developer, I want to declare that a method consumes a context value by adding a `with` clause to the method signature, so that the context value is available as a local variable inside the method body.

#### Acceptance Criteria

1. THE Grammar SHALL define a `WithClause` rule matching `WITH TypeReference IDENTIFIER` that appears after the closing parenthesis of a method's parameter list and before any `ConstraintClause` rules or the method body.
2. THE Grammar SHALL allow zero or more `WithClause` entries on a single method signature by accepting them as a repeated list.
3. WHEN a method declaration includes one or more `with` clauses, THE Parser SHALL produce an AST node for each clause that contains the bound type reference and the bound identifier as distinct child nodes.
4. WHEN a source file contains both a top-level `WithDeclaration` (trait mapping) and a method-level `with` clause, THE Parser SHALL parse each according to its positional context, producing a `WithDeclaration` node for the top-level usage and a `WithClause` child node within the method signature for the method-level usage.
5. THE Grammar SHALL support `with` clauses on `MethodDeclaration`, `ConstructorDeclaration`, `InterfaceMethodDeclaration`, and `TraitMethodDeclaration` rules, so that all method-like declarations can declare context requirements as part of their signature.
6. WHEN a `ConstructorDeclaration` includes a `with` clause, THE Context_Checker SHALL verify context availability at each `new` expression call site that invokes that constructor.
7. WHEN an `InterfaceMethodDeclaration` or `TraitMethodDeclaration` includes a `with` clause, THE Context_Checker SHALL use those declared clauses to verify context availability at call sites dispatching through the interface or trait type.

### Requirement 4: Context Declaration Semantics

**User Story:** As a Mist developer, I want context declarations to push values onto a per-type stack and pop them when the declaring scope exits, so that context values are properly scoped and do not leak.

#### Acceptance Criteria

1. WHEN a `with var x = expression ;` statement is executed, THE Compiler SHALL push the value of the expression onto the Context_Stack keyed by the inferred type of the expression, and bind `x` as an immutable local variable in the enclosing block scope.
2. WHEN a `with TypeReference x = expression ;` statement is executed, THE Compiler SHALL push the value of the expression onto the Context_Stack keyed by the specified TypeReference, and bind `x` as an immutable local variable in the enclosing block scope.
3. WHEN a `with TypeReference _ = expression ;` statement is executed, THE Compiler SHALL push the value of the expression onto the Context_Stack keyed by the specified TypeReference without binding a local variable.
4. WHEN a `with expression ;` statement is executed, THE Compiler SHALL push the value of the expression onto the Context_Stack keyed by the inferred type of the expression without binding a local variable.
5. WHEN the enclosing block scope (`{ }`) containing one or more context declarations exits, THE Compiler SHALL pop the corresponding values from the Context_Stack in reverse declaration order (last declared is popped first).
6. THE Semantic_Model_Builder SHALL register the local variable introduced by `with var x = expression` and `with TypeReference x = expression` as a symbol with `SymbolKind.LocalVariable` in the declaring block scope.
7. WHEN a `with var x = expression` statement uses type inference, THE Semantic_Model_Builder SHALL resolve the type of `x` to the static type of the expression.
8. WHEN multiple context declarations for the same type appear in the same block scope, THE Compiler SHALL push each value onto the Context_Stack in declaration order, shadowing the previous value of that type until the scope exits.

### Requirement 5: Context Type Keying Semantics

**User Story:** As a Mist developer, I want context values to be keyed by exact type without inheritance or variance, so that the context lookup is precise and predictable.

#### Acceptance Criteria

1. THE Compiler SHALL key context values by the exact nominal static type of the declaration, with no inheritance-based lookup or subtype traversal.
2. WHEN a context is declared with `with var x = new DerivedType()`, THE Compiler SHALL push the value onto the Context_Stack keyed by `DerivedType`, not by any base type of `DerivedType`.
3. WHEN a method binds a context with `with BaseType y`, THE Compiler SHALL look up the Context_Stack using the exact type `BaseType` only, returning no result if only a `DerivedType` context exists on the stack.
4. WHEN a context is declared with `with BaseType _ = new DerivedType()`, THE Compiler SHALL push the value onto the Context_Stack keyed by `BaseType` (the explicitly annotated type), allowing the developer to control which type key is used.
5. THE Compiler SHALL expose only the top value of the Context_Stack for a given type as the current context value; multiple values of the same type may exist on the stack due to shadowing, but only the most recently pushed value SHALL be accessible to consumers.
6. WHEN a context type is a generic type, THE Compiler SHALL treat each distinct type argument combination as a separate key (e.g., `List<int>` and `List<string>` SHALL be independent context keys).
7. WHEN a type alias is used (e.g., `type Name = string;`), THE Compiler SHALL resolve the alias to its underlying type before keying, so that `with Name _ = "hello"` and `with string _ = "world"` push to the same context key.
8. WHEN a union type is used as a context key (e.g., `type intorstring = int | string;`), THE Compiler SHALL treat the union type as a distinct key from any of its constituent types (e.g., `intorstring` and `int` are independent context keys).

### Requirement 6: Context Block Semantics

**User Story:** As a Mist developer, I want `with(expression) { ... }` blocks to push a context for the block duration only, so that I can temporarily override a context value.

#### Acceptance Criteria

1. WHEN a `with ( expression ) { ... }` block is entered, THE Compiler SHALL push the value of the expression onto the Context_Stack keyed by the inferred type of the expression.
2. WHEN a `with ( expression ) { ... }` block exits through any path (normal completion, early return, or exception), THE Compiler SHALL pop the value from the Context_Stack for that type, restoring the previous context value.
3. WHEN a context block shadows an existing context of the same type, THE Compiler SHALL ensure that callees invoked within the block receive the shadowing value.
4. WHEN a context block exits after shadowing, THE Compiler SHALL ensure that callees invoked after the block receive the original (restored) value.
5. WHEN a `with ( expression ) { ... }` block is the first context declaration for its type (no prior value exists on the Context_Stack), THE Compiler SHALL push the value normally, and upon block exit SHALL pop the value leaving the Context_Stack empty for that type.

### Requirement 7: Context Binding Semantics

**User Story:** As a Mist developer, I want a method's `with` clause to bind the current context value to a local variable, so that I can use the context value within the method body.

#### Acceptance Criteria

1. WHEN a method with a `with TypeReference identifier` clause is called, THE Compiler SHALL read the top value from the Context_Stack for the specified type and bind it to the identifier as a read-only local variable within the method body.
2. THE Semantic_Model_Builder SHALL register the identifier from a `with` clause as a symbol with `SymbolKind.LocalVariable` in the method body scope, with its resolved type set to the type denoted by the `with` clause's TypeReference.
3. THE Type_Checker SHALL verify that the type specified in the `with` clause resolves to a defined type in the semantic model.
4. WHEN a method has multiple `with` clauses, THE Compiler SHALL bind each one independently from the Context_Stack for its respective type.
5. IF the identifier from a `with` clause is the target of an assignment expression within the method body, THEN THE Compiler SHALL emit a diagnostic error indicating that context-bound variables are not reassignable.

### Requirement 8: Interface and Trait Contract Enforcement for With Clauses

**User Story:** As a Mist developer, I want the compiler to enforce that implementing types match the `with` clauses declared on interface and trait method signatures, so that context requirements are consistently satisfied regardless of dispatch target.

#### Acceptance Criteria

1. WHEN an interface method declares one or more `with` clauses, THE Compiler SHALL require that any class or struct implementing that interface method also declares the same `with` clauses (matching by exact resolved type).
2. IF an implementing method omits a `with` clause that is declared on the interface method, THEN THE Compiler SHALL emit a diagnostic error naming the missing context type, the interface method, and the implementing type.
3. IF an implementing method declares a `with` clause with a type that differs from the interface method's `with` clause (including via inheritance — no variance allowed), THEN THE Compiler SHALL emit a diagnostic error indicating the type mismatch.
4. WHEN a trait method declares one or more `with` clauses, THE Compiler SHALL only consider a type's method as structurally matching the trait method if the type's method also declares the same `with` clauses (matching by exact resolved type, no variance).
5. IF a type's method matches a trait method in name, parameters, and return type but differs in `with` clauses, THE Compiler SHALL NOT consider it a structural match for trait satisfaction.
6. WHEN a call is dispatched through an interface or trait type, THE Context_Checker SHALL use the `with` clauses from the interface or trait method signature to determine context requirements at the call site.

### Requirement 9: Compile-Time Context Availability Checker

**User Story:** As a Mist developer, I want the compiler to verify at compile time that every method consuming a context is always called within a scope that provides one, so that I get an error instead of a runtime failure.

#### Acceptance Criteria

1. THE Context_Checker SHALL walk the call graph from each call site of a method that has a `with` clause to verify that a matching context declaration (matched by exact type as defined in Requirement 5) exists somewhere up the call chain, checking each `with` clause on the method independently.
2. WHEN the Context_Checker reaches the program entry point (`public static Main`) without finding a context declaration for the required type, THE Context_Checker SHALL emit a compile-time diagnostic error naming the required type, the method that requires it, and the call site location.
3. THE Context_Checker SHALL treat methods that neither declare nor consume a context as transparent, continuing the walk through them when traversing up the call chain.
4. WHEN a method with a `with` clause is called inside a `with(expression) { ... }` block whose expression type matches the required type, THE Context_Checker SHALL consider the requirement satisfied.
5. WHEN a method with a `with` clause is called after any context declaration statement (`with var x = expression`, `with TypeReference x = expression`, `with TypeReference _ = expression`, or `with expression`) in the same scope or an enclosing scope whose declared context type matches the required type, THE Context_Checker SHALL consider the requirement satisfied.
6. IF a method with a `with` clause is called without any reachable context declaration for the required type, THEN THE Compiler SHALL emit a diagnostic error identifying the unsatisfied context type and the call site location.
7. WHEN a method with a `with` clause is called through an interface or virtual dispatch, THE Context_Checker SHALL verify context availability based on the `with` clauses declared on the interface or base method signature being dispatched through.
8. IF the Context_Checker encounters a cycle in the call graph during its walk, THEN THE Context_Checker SHALL terminate traversal of that path without emitting a false-positive error for the cycle itself.

### Requirement 10: Context Propagation Through Call Graph

**User Story:** As a Mist developer, I want context values to automatically carry through intermediate methods that do not declare or consume contexts, so that deeply nested callees can access context values declared higher up.

#### Acceptance Criteria

1. WHEN a method `Foo` declares a context of type `T` and calls method `Bar` (which has no context declarations or bindings for any type), and `Bar` calls method `Baz` (which binds context of type `T`), THE Compiler SHALL ensure `Baz` receives the value declared by `Foo`.
2. THE Compiler SHALL propagate context values through any number of intermediate method calls that neither declare nor bind a context of the propagated type, with no compiler-imposed depth limit.
3. WHEN multiple context declarations of the same type exist at different levels of the call stack, THE Compiler SHALL provide the most recently pushed (innermost) value to the consumer.
4. WHEN an intermediate method in the call chain declares a context of the same type `T` (shadowing the outer declaration), and that intermediate method returns, THE Compiler SHALL restore the outer value of type `T` so that subsequent callees of the original caller receive the original value.
5. WHEN an intermediate method in the call chain declares or pops a context of a different type `U`, THE Compiler SHALL continue to propagate the context of type `T` unaffected.

### Requirement 11: Pretty-Printer Support for Structured Context

**User Story:** As a Mist developer, I want the pretty-printer to correctly format all structured context constructs, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a `with var identifier = expression ;` statement preserving the `with var` prefix, identifier, and initializer.
2. THE Pretty_Printer SHALL format a `with TypeReference identifier = expression ;` statement preserving the type annotation, identifier, and initializer.
3. THE Pretty_Printer SHALL format a `with TypeReference _ = expression ;` statement preserving the type annotation, discard marker, and initializer.
4. THE Pretty_Printer SHALL format a `with expression ;` statement preserving the `with` keyword and expression.
5. THE Pretty_Printer SHALL format a `with ( expression ) { ... }` block preserving the parenthesized expression and block body with indented statements.
6. THE Pretty_Printer SHALL format a method `with` clause as `with TypeReference identifier` after the parameter list, and WHEN multiple `with` clauses exist, each SHALL be separated by a space.
7. FOR ALL valid Mist ASTs containing structured context constructs, print(parse(print(ast))) SHALL equal print(ast) (round-trip idempotence property).

### Requirement 12: C# Code Emission for Structured Context

**User Story:** As a Mist developer, I want structured context to be correctly transpiled to C# using a runtime context mechanism, so that the compiled output behaves as specified.

#### Acceptance Criteria

1. THE Emitter SHALL generate a runtime context infrastructure (a static dictionary mapping types to stacks of values) that supports push, pop, and peek operations.
2. WHEN the Emitter encounters a `with var x = expression` statement, THE Emitter SHALL emit code that evaluates the expression, pushes the value onto the runtime context stack keyed by the inferred type, and assigns the value to a local variable named `x`.
3. WHEN the Emitter encounters a `with TypeReference x = expression` statement, THE Emitter SHALL emit code that evaluates the expression, pushes the value onto the runtime context stack keyed by the specified TypeReference, and assigns the value to a local variable named `x`.
4. WHEN the Emitter encounters a `with TypeReference _ = expression` statement, THE Emitter SHALL emit code that evaluates the expression and pushes the value onto the runtime context stack keyed by the specified TypeReference without declaring a local variable.
5. WHEN the Emitter encounters a `with expression` statement, THE Emitter SHALL emit code that evaluates the expression and pushes the value onto the runtime context stack keyed by the inferred type without declaring a local variable.
6. THE Emitter SHALL emit scope-exit cleanup code using a `try/finally` block that pops the context value from the stack when the declaring scope exits, and WHEN multiple context declarations exist in the same scope, each declaration SHALL be wrapped in its own `try/finally` block such that values are popped in reverse declaration order.
7. WHEN the Emitter encounters a `with ( expression ) { ... }` block, THE Emitter SHALL emit code that pushes the value onto the runtime context stack keyed by the inferred type before the block body, executes the block body inside a `try` block, and pops the value in the corresponding `finally` clause.
8. WHEN the Emitter encounters a method with a `with TypeReference identifier` clause, THE Emitter SHALL emit code at the method entry that peeks the top value from the runtime context stack for the specified type, casts it to the specified TypeReference, and assigns the result to a local variable named after the identifier.
9. IF any compilation unit in the program uses structured context declarations or bindings, THEN THE Emitter SHALL generate the runtime context infrastructure in a dedicated helper file named `MistContext.g.cs` included in the output.

### Requirement 13: Interaction with Existing Trait-With Blocks

**User Story:** As a Mist developer, I want the existing `with TypeName : TraitName { mappings }` syntax for trait satisfaction to continue working unchanged, so that structured context does not break existing code.

#### Acceptance Criteria

1. THE Grammar SHALL continue to parse `with TypeName : TraitName { mappings }` as a `WithDeclaration` member of a `CompilationUnit`, using the existing rule structure (`WITH IDENTIFIER COLON IDENTIFIER TypeArgumentList? LEFT_BRACE MemberMapping+ RIGHT_BRACE`).
2. THE Compiler SHALL NOT interpret a top-level `with TypeName : TraitName` declaration as a structured context declaration.
3. WHEN both trait-with blocks and structured context declarations exist in the same compilation unit, THE Compiler SHALL parse each `with` occurrence as its respective AST node type (`WithDeclaration` for top-level trait-with blocks, context statement nodes for `with` inside method bodies) and emit zero disambiguation-related diagnostic errors.
4. WHEN a program that uses only trait-with blocks (and no structured context) is compiled, THE Compiler SHALL produce the same diagnostics and semantic model results as it did before structured context was introduced (no new errors, warnings, or behavioral changes).
5. THE Compiler SHALL continue to populate the `WithBlockRegistry` with validated member mappings from trait-with blocks and resolve trait satisfaction using the same logic as before structured context was introduced.

### Requirement 14: Diagnostic Messages for Structured Context

**User Story:** As a Mist developer, I want clear and specific error messages when structured context is used incorrectly, so that I can quickly identify and fix the issue.

#### Acceptance Criteria

1. WHEN a method with a `with` clause is called without a matching context in scope, THE Compiler SHALL emit a diagnostic error that names the required context type, the method being called, and the call site location.
2. WHEN a `with` declaration uses a type that does not exist, THE Compiler SHALL emit a diagnostic error that names the undefined type and the location of the declaration.
3. WHEN a method has multiple `with` clauses binding the same type, THE Compiler SHALL emit a diagnostic error indicating duplicate context bindings, naming the duplicated type and the location of each conflicting clause.
4. WHEN any context declaration statement (`with var identifier = expression`, `with TypeReference identifier = expression`, `with TypeReference _ = expression`, `with expression`, or `with ( expression ) { ... }`) appears outside a method body (e.g., at the top level or inside a type body but outside a method), THE Compiler SHALL emit a diagnostic error indicating that context declarations are only valid inside method bodies.
5. WHEN a context block `with ( expression ) { ... }` or a context push statement `with expression ;` uses an expression whose type cannot be determined, THE Compiler SHALL emit a diagnostic error indicating the type could not be inferred and naming the location of the expression.

### Requirement 15: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want structured context syntax to be highlighted correctly, so that I get visual cues for context declarations, context blocks, and context bindings.

#### Acceptance Criteria

1. THE TextMate grammar SHALL highlight the `with` keyword in context declaration statements (`with var identifier =`, `with TypeReference identifier =`, `with TypeReference _ =`, `with expression ;`) with the scope `keyword.other.mist` (consistent with its existing scope).
2. THE TextMate grammar SHALL highlight the `with` keyword in context block statements (`with ( expression ) { ... }`) with the scope `keyword.other.mist`.
3. THE TextMate grammar SHALL highlight the `with` keyword in method signature context bindings (`with TypeReference identifier`) with the scope `keyword.other.mist`.
4. THE TextMate grammar SHALL highlight the type reference in a context declaration statement (`with TypeReference identifier =`, `with TypeReference _ =`) and in a method `with` clause (`with TypeReference identifier`) with the appropriate type scope (`storage.type.primitive.mist` for primitive types, `entity.name.type.mist` for named types beginning with an uppercase letter).
5. THE TextMate grammar SHALL highlight the identifier in a method `with` clause as a parameter (scope `variable.parameter.mist`).
6. THE TextMate grammar SHALL highlight the identifier in a context declaration statement (`with var identifier =`, `with TypeReference identifier =`) with the scope `variable.other.mist`.
7. WHEN a context declaration uses the discard marker (`with TypeReference _ = expression ;`), THE TextMate grammar SHALL highlight `_` with the scope `variable.language.mist`.

### Requirement 16: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating structured context, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/StructuredContext/Program.mist`.
2. The sample SHALL demonstrate `with var x = expression` (inferred type context declaration with local binding).
3. The sample SHALL demonstrate `with TypeReference x = expression` (explicit type context declaration with local binding).
4. The sample SHALL demonstrate `with TypeReference _ = expression` (explicit type context declaration without local binding).
5. The sample SHALL demonstrate `with expression` (inferred type context push without local binding).
6. The sample SHALL demonstrate `with ( expression ) { ... }` (scoped context block).
7. The sample SHALL demonstrate context propagation through intermediate methods that are transparent to the context system.
8. The sample SHALL demonstrate context shadowing via nested `with` declarations and context blocks, showing that the innermost value is received by consumers.
9. The sample SHALL demonstrate context restoration after a shadowing scope exits.
10. The sample SHALL demonstrate a method with multiple `with` clauses binding different types.
11. The sample SHALL demonstrate explicit type annotation for variance control (`with BaseType _ = new DerivedType()`).
12. The sample SHALL demonstrate multiple same-type context pushes in a single scope (e.g., `with "a"; with "b";`) showing that only the most recently pushed value is received by consumers.
13. The sample SHALL demonstrate generic types as independent context keys (e.g., `List<int>` and `List<string>` as separate contexts that do not interfere with each other).
14. The sample SHALL demonstrate a constructor with a `with` clause, showing that context is available during object construction.
15. The sample SHALL demonstrate passing a class instance, a struct instance, a trait-satisfying type, and an interface-implementing type as context values.
16. The sample SHALL demonstrate a type alias, a type union, a refinement type, and a dependent type each used as a context key.
17. The sample SHALL demonstrate calling an interface method that has a `with` clause, where the concrete implementing type satisfies the contract.
18. The sample SHALL demonstrate calling a trait method (via structural dispatch) that has a `with` clause, where the concrete type's method also declares the matching `with` clause.
19. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
20. `samples/AllFeatures/Program.mist` SHALL call `StructuredContext.Program.RunDemo()` to include the feature in the full-suite sample run.
21. The sample SHALL compile and run without errors using the Mist compiler.
