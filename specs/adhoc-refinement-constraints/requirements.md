# Requirements Document

## Introduction

This document specifies the requirements for adding ad-hoc refinement constraints to the Mist programming language. Ad-hoc refinement constraints allow `where` clauses to attach refinement predicates directly to dependent type arguments on generic declarations and to parameters on constructors, methods, and indexers. This extends the existing refinement type system (which requires named type alias declarations) with inline, anonymous refinement constraints.

Currently, Mist supports:
- **Dependent type parameters** (`<Size:int>`) that embed compile-time constant values in types
- **Refinement types** (`type AdultAge = int >= 18;`) that constrain valid values via named type aliases
- **Generic constraints** (`where T : class`) that restrict type parameters to specific capabilities

This feature introduces a new form of `where` clause that uses refinement predicates (the same pattern-match syntax used in refinement type aliases) to constrain:
1. **Dependent type arguments** — e.g., `class Array<TValue, Size:int> where Size >= 0` constrains the dependent value `Size` to non-negative integers
2. **Method/constructor/indexer parameters** — e.g., `void Foo(int x) where x < 100` constrains the parameter `x` to values less than 100

These constraints are enforced entirely at compile time, following the same enforcement model as named refinement types. The compiler rejects arguments that cannot be statically proven to satisfy the constraint — no runtime guards are generated.

**Key design decisions addressed in this spec:**
- Reuses existing refinement predicate syntax (relational patterns, logical `and`, property patterns)
- `where` clauses for refinement constraints are syntactically distinct from generic constraint `where` clauses (refinement `where` binds to a value name, not `Name : Type`)
- Dependent type argument refinement constraints are checked at the call site when constructing the type
- Parameter refinement constraints are enforced at compile time with the same rules as named refinement type parameters — non-provable arguments are rejected, not deferred to runtime
- Multiple `where` clauses can appear on the same declaration (one per constrained name)
- Compound predicates use `and` within a single `where` clause
- The `or` combinator is not supported in ad-hoc refinement constraints (use named union refinement types instead)
- Ad-hoc refinement constraints on dependent type arguments participate in type identity (two types with different constraints are distinct)
- Flow narrowing applies within the body after a parameter refinement constraint is established

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Adhoc_Refinement_Constraint**: A `where` clause on a declaration that attaches a refinement predicate to a dependent type argument or a parameter, without requiring a named refinement type alias. The predicate uses the same pattern-match syntax as refinement type definitions.
- **Dependent_Type_Parameter**: A type parameter annotated with a value type using the `:Type` syntax (e.g., `Size:int` in `class Array<Size:int>`). Instead of accepting a type argument, it accepts a compile-time constant value of the specified type.
- **Refinement_Predicate**: A pattern-match expression that constrains valid values. Uses existing pattern match syntax (relational patterns, logical `and` combinators, constant patterns).
- **Parameter_Refinement**: An Adhoc_Refinement_Constraint that constrains a method, constructor, or indexer parameter (e.g., `where x < 100`).
- **Dependent_Argument_Refinement**: An Adhoc_Refinement_Constraint that constrains a dependent type argument on a generic declaration (e.g., `where Size >= 0`).
- **Generic_Constraint_Clause**: The existing `where T : constraint` syntax for restricting type parameters to specific types or capabilities. Syntactically distinct from Adhoc_Refinement_Constraints because it uses `Name : Constraint` form.
- **Compile_Time_Constant**: A value that can be fully evaluated at compile time. Includes integer literals, boolean literals, character literals, and references to `const` declarations.
- **Runtime_Guard**: A conditional check emitted at the entry point of a method, constructor, or indexer that validates parameter values against their refinement constraints at runtime. NOT used for Adhoc_Refinement_Constraints — these are enforced entirely at compile time. Runtime guards are only emitted for named refinement type parameters when the argument is proven at compile time but the method may be called from external code that bypasses the Mist type checker.
- **RefinementSolver**: The component that determines logical relationships between refinement predicates, including whether one predicate implies another (i.e., every value satisfying the first also satisfies the second). Used for forwarding checks, flow narrowing, and interface satisfaction validation.

## Requirements

### Requirement 1: Adhoc Refinement Constraint Grammar for Dependent Type Arguments

**User Story:** As a Mist developer, I want to attach refinement predicates to dependent type parameters using `where` clauses on type declarations, so that I can constrain the valid values of dependent type arguments without defining a separate named refinement type.

#### Acceptance Criteria

1. THE Grammar SHALL extend type declarations (classes, structs, interfaces, traits, type aliases) to accept Adhoc_Refinement_Constraint clauses of the form `where <name> <refinement_predicate>`, where `<name>` is a Dependent_Type_Parameter name and `<refinement_predicate>` is an AndPattern (the same pattern subset used in refinement type alias definitions: relational patterns, constant patterns, and logical `and` combinators).
2. WHEN the Parser encounters `class Array<TValue, Size:int> where Size >= 0 { }`, THE Parser SHALL produce an AST node that captures `Size` as the constrained name and `>= 0` as the refinement predicate, distinct from a Generic_Constraint_Clause.
3. THE Grammar SHALL support compound refinement predicates using `and` combinators (e.g., `where Size >= 0 and <= 1024`), reusing the existing AndPattern grammar rule.
4. THE Grammar SHALL support multiple Adhoc_Refinement_Constraint clauses on the same declaration (e.g., `class Matrix<Rows:int, Cols:int> where Rows >= 1 where Cols >= 1`).
5. THE Grammar SHALL allow Adhoc_Refinement_Constraint clauses to coexist with Generic_Constraint_Clauses on the same declaration in any interleaved order (e.g., `class Container<T, Size:int> where T : class where Size >= 0` and `class Container<T, Size:int> where Size >= 0 where T : class` are both valid).
6. THE Grammar SHALL disambiguate Adhoc_Refinement_Constraint clauses from Generic_Constraint_Clauses by the absence of the `:` token after the name — `where T : class` is a generic constraint, while `where Size >= 0` is a refinement constraint.
7. WHEN a `where` clause references a name that is neither a type parameter nor a dependent type parameter on the declaration, THE Parser SHALL still produce a valid AST node (semantic validation occurs later).
8. THE Grammar SHALL NOT support the `or` combinator within Adhoc_Refinement_Constraint predicates — only `and` combinators are permitted. IF the Parser encounters an `or` token within a refinement predicate position, THEN the Parser SHALL treat it as a syntax error.

### Requirement 2: Adhoc Refinement Constraint Grammar for Parameters

**User Story:** As a Mist developer, I want to attach refinement predicates to method, constructor, and indexer parameters using `where` clauses, so that I can express value constraints on parameters without defining separate named refinement types.

#### Acceptance Criteria

1. THE Grammar SHALL extend method declarations to accept Adhoc_Refinement_Constraint clauses that reference parameter names (e.g., `void Foo(int x) where x < 100 { }`), positioned after the closing parenthesis of the parameter list and any Generic_Constraint_Clauses, and before the method body.
2. THE Grammar SHALL extend constructor declarations to accept Adhoc_Refinement_Constraint clauses that reference parameter names (e.g., `MyClass(int size) where size > 0 { }`), positioned after the closing parenthesis of the parameter list and before the optional constructor chain (`: this(...)` or `: base(...)`) or method body.
3. THE Grammar SHALL extend indexer declarations to accept Adhoc_Refinement_Constraint clauses that reference parameter names (e.g., `int this[int index] where index >= 0 { get; }`), positioned after the closing bracket of the parameter list and before the accessor block.
4. THE Grammar SHALL support compound refinement predicates on parameters using `and` combinators (e.g., `where x >= 0 and < 100`).
5. THE Grammar SHALL support multiple Adhoc_Refinement_Constraint clauses on the same method, each constraining a different parameter (e.g., `void Clamp(int value, int min, int max) where min <= max where value >= min and <= max`).
6. THE Grammar SHALL allow Adhoc_Refinement_Constraint clauses on parameters to coexist with Generic_Constraint_Clauses on the same method in any interleaved order (e.g., `void Process<T>(T item, int count) where T : class where count > 0` and `void Process<T>(T item, int count) where count > 0 where T : class` are both valid).
7. WHEN a `where` clause on a method, constructor, or indexer references a name that is not a parameter of that declaration, THE Parser SHALL still produce a valid AST node (semantic validation occurs later).

### Requirement 3: Pretty-Printer Support for Adhoc Refinement Constraints

**User Story:** As a Mist tooling developer, I want the pretty-printer to correctly format ad-hoc refinement constraint clauses, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a declaration with an Adhoc_Refinement_Constraint on a dependent type argument, THE Pretty_Printer SHALL emit `where <name> <predicate>` with a single space between the `where` keyword and the name, and a single space between the name and the predicate (e.g., `where Size >= 0`).
2. WHEN the Pretty_Printer encounters a declaration with an Adhoc_Refinement_Constraint on a parameter, THE Pretty_Printer SHALL emit `where <name> <predicate>` with a single space between the `where` keyword and the name, and a single space between the name and the predicate (e.g., `where x < 100`).
3. WHEN the Pretty_Printer encounters a compound refinement predicate using the `and` combinator, THE Pretty_Printer SHALL emit a single space before `and` and a single space after `and` (e.g., `where Size >= 0 and <= 1024`).
4. WHEN the Pretty_Printer encounters multiple `where` clauses (both Generic_Constraint_Clauses and Adhoc_Refinement_Constraint clauses), THE Pretty_Printer SHALL emit them in declaration order, each prefixed by a single space (e.g., ` where T : class where Size >= 0`).
5. THE Pretty_Printer SHALL produce output such that parsing the pretty-printed text and then pretty-printing the resulting AST yields identical text to the first pretty-print (idempotence property), for all valid Mist ASTs containing Adhoc_Refinement_Constraint clauses.
6. WHEN the Pretty_Printer encounters an Adhoc_Refinement_Constraint on a constructor or indexer parameter, THE Pretty_Printer SHALL emit the constraint clause following the same spacing rules as method parameter constraints (e.g., `MyClass(int size) where size > 0`).

### Requirement 4: Semantic Model Registration of Adhoc Refinement Constraints

**User Story:** As a Mist developer, I want the compiler to register ad-hoc refinement constraints in the semantic model, so that the type system can enforce them during type checking and code generation.

#### Acceptance Criteria

1. WHEN a type declaration includes an Adhoc_Refinement_Constraint on a Dependent_Type_Parameter, THE Semantic_Model_Builder SHALL attach the refinement predicate to the dependent type parameter's semantic representation such that downstream consumers (Type_Checker, Emitter) can query the predicate for that parameter.
2. WHEN a method, constructor, or indexer declaration includes an Adhoc_Refinement_Constraint on a parameter, THE Semantic_Model_Builder SHALL attach the refinement predicate to the parameter's semantic representation such that downstream consumers (Type_Checker, Emitter) can query the predicate for that parameter.
3. THE Semantic_Model_Builder SHALL store the Adhoc_Refinement_Constraint predicate such that the Type_Checker can evaluate it against Compile_Time_Constant values and produce a boolean pass/fail result during type checking.
4. IF an Adhoc_Refinement_Constraint references a name that does not correspond to a dependent type parameter on the type declaration or a parameter on the method/constructor/indexer, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error of the form: "Refinement constraint references '{Name}', which is not a dependent type parameter or parameter on this declaration." and SHALL continue processing the declaration without the invalid constraint.
5. IF an Adhoc_Refinement_Constraint references a regular type parameter (not a dependent type parameter), THEN THE Semantic_Model_Builder SHALL emit a diagnostic error of the form: "Refinement constraints cannot be applied to type parameter '{Name}'. Refinement constraints apply only to dependent type parameters and value parameters." and SHALL continue processing the declaration without the invalid constraint.
6. IF multiple Adhoc_Refinement_Constraint clauses reference the same name on the same declaration, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error of the form: "Duplicate refinement constraint on '{Name}'. Combine predicates using 'and' in a single where clause." and SHALL register only the first constraint encountered for that name.

### Requirement 5: Compile-Time Enforcement of Dependent Argument Refinement Constraints

**User Story:** As a Mist developer, I want the compiler to reject dependent type arguments that violate the refinement constraint at compile time, so that I get immediate feedback when constructing types with invalid values.

#### Acceptance Criteria

1. WHEN a type with a Dependent_Argument_Refinement is constructed with a Compile_Time_Constant that satisfies the refinement predicate, THE Type_Checker SHALL accept the construction without error.
2. WHEN a type with a Dependent_Argument_Refinement is constructed with a Compile_Time_Constant that violates the refinement predicate, THE Type_Checker SHALL emit a diagnostic error of the form: "Dependent type argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of type '{Type}'."
3. WHEN a type with a Dependent_Argument_Refinement is constructed with a `const` reference whose value violates the refinement predicate, THE Type_Checker SHALL emit the same diagnostic error as for a literal violation.
4. WHEN a dependent type argument is forwarded from an open dependent type parameter (e.g., `class Wrapper<N:int> where N >= 0` used as `Array<N>`), THE Type_Checker SHALL verify that the source parameter's refinement constraint implies the target parameter's refinement constraint.
5. IF the Type_Checker cannot prove that a forwarded dependent type parameter's constraint implies the target constraint, THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Cannot forward dependent parameter '{SourceName}' with constraint '{SourcePredicate}' to parameter '{TargetName}' with constraint '{TargetPredicate}' because the compiler cannot prove the implication."
6. IF a dependent type argument is forwarded from an open dependent type parameter that has no Adhoc_Refinement_Constraint to a target parameter that has a Dependent_Argument_Refinement, THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Cannot forward dependent parameter '{SourceName}' with no constraint to parameter '{TargetName}' with constraint '{TargetPredicate}' because the compiler cannot prove the implication."
7. IF a type with a Dependent_Argument_Refinement is constructed with an expression that is not a Compile_Time_Constant and not a reference to an open dependent type parameter, THEN THE Type_Checker SHALL emit a diagnostic error indicating that dependent type arguments must be compile-time constants.

### Requirement 6: Compile-Time Enforcement of Parameter Refinement Constraints

**User Story:** As a Mist developer, I want the compiler to enforce parameter refinement constraints at compile time with the same rules as named refinement type parameters, so that adhoc constraints and named refinement types provide identical safety guarantees.

#### Acceptance Criteria

1. WHEN a method with a Parameter_Refinement is called with a Compile_Time_Constant argument that satisfies the refinement predicate, THE Type_Checker SHALL accept the call without error.
2. WHEN a method with a Parameter_Refinement is called with a Compile_Time_Constant argument that violates the refinement predicate, THE Type_Checker SHALL emit a diagnostic error of the form: "Argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of method '{Method}'."
3. WHEN a method with a Parameter_Refinement is called with a non-constant argument whose type is a named refinement type whose predicate logically implies the parameter's refinement predicate, THE Type_Checker SHALL accept the call without error.
4. WHEN a method with a Parameter_Refinement is called with a non-constant argument whose type is a named refinement type whose predicate does NOT logically imply the parameter's refinement predicate, THE Type_Checker SHALL emit a diagnostic error requiring explicit pattern matching to prove the constraint.
5. WHEN a method with a Parameter_Refinement is called with a non-constant argument whose type is the unrefined Base_Type (no refinement), THE Type_Checker SHALL emit a diagnostic error indicating that the argument must be proven to satisfy the constraint via pattern matching (e.g., `if (x is >= 21) { Bar(x); }`).
6. WHEN a constructor with a Parameter_Refinement is called with a Compile_Time_Constant argument that violates the refinement predicate, THE Type_Checker SHALL emit a diagnostic error of the form: "Argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of constructor '{Type}'."
7. WHEN an indexer with a Parameter_Refinement is called with a Compile_Time_Constant argument that violates the refinement predicate, THE Type_Checker SHALL emit a diagnostic error of the form: "Argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of indexer '{Type}'."
8. THE Type_Checker SHALL apply criteria 1, 3, 4, and 5 equivalently to constructor parameters and indexer parameters with Adhoc_Refinement_Constraints.
9. THE enforcement behavior for Parameter_Refinements SHALL be identical to the enforcement behavior for named refinement type parameters — both reject non-provable arguments at compile time and both accept arguments whose refinement is statically proven.

### Requirement 7: No Runtime Guards for Parameter Refinement Constraints

**User Story:** As a Mist developer, I want parameter refinement constraints to be enforced entirely at compile time with no runtime overhead, so that the safety guarantee is static and the generated code is efficient.

#### Acceptance Criteria

1. THE Emitter SHALL NOT generate runtime guard checks for parameters with Adhoc_Refinement_Constraints. All enforcement is performed at compile time by the Type_Checker (Requirement 6).
2. WHEN a method has a parameter with an Adhoc_Refinement_Constraint, THE Emitter SHALL emit the method body directly without any injected `if`-condition guards or `ArgumentException` throws for that parameter's constraint.
3. THE generated C# code for a method with a Parameter_Refinement SHALL be identical to the generated code for a method whose parameter type is a named refinement type with the same predicate — both produce direct assignments with no runtime validation at the call site or method entry.

### Requirement 8: Flow Narrowing from Parameter Refinement Constraints

**User Story:** As a Mist developer, I want parameters with refinement constraints to be treated as having the refined type within the method body, so that I can pass them to other methods expecting the same or weaker refinement without additional validation.

#### Acceptance Criteria

1. WHEN a parameter has an Adhoc_Refinement_Constraint, THE Type_Checker SHALL treat the parameter as having an anonymous refinement type (base type + predicate) within the method body, such that the parameter is assignable to any named refinement type whose predicate is implied by the parameter's constraint predicate.
2. WHEN a parameter with a refinement constraint is passed to another method expecting a named refinement type whose predicate is implied by the parameter's refinement predicate, THE Type_Checker SHALL accept the call without requiring additional pattern matching.
3. IF a parameter with a refinement constraint is passed to another method expecting a refinement type whose predicate is NOT implied by the parameter's refinement predicate, THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Cannot pass '{Name}' with refinement constraint '{SourcePredicate}' to parameter expecting '{TargetType}' with constraint '{TargetPredicate}' because the compiler cannot prove the implication."
4. WHEN a parameter with a refinement constraint is passed to another method expecting the base type without any refinement, THE Type_Checker SHALL accept the call (widening from refined type to base type).
5. WHEN a parameter with a refinement constraint is reassigned or compound-assigned (e.g., `+=`, `-=`) within the method body, THE Type_Checker SHALL invalidate the refinement narrowing for all subsequent statements referencing that parameter.

### Requirement 9: Dependent Argument Refinement and Type Identity

**User Story:** As a Mist developer, I want dependent argument refinement constraints to be part of the type's contract, so that types with different constraints are distinguishable and the constraint is enforced consistently.

#### Acceptance Criteria

1. THE Type_Checker SHALL treat the Dependent_Argument_Refinement as part of the type's contract — any construction of the type (direct instantiation with a literal or const, inheritance with a fixed value, or forwarding from another dependent parameter) must satisfy the constraint.
2. WHEN a derived type inherits from a base type with a Dependent_Argument_Refinement and fixes the dependent value (e.g., `class SmallArray : Array<int, 10>` where the base requires `Size >= 0`), THE Type_Checker SHALL verify the fixed value satisfies the base type's refinement constraint.
3. WHEN a derived type forwards a dependent parameter to a base type with a Dependent_Argument_Refinement, THE Type_Checker SHALL require the derived type's refinement constraint on that parameter to imply the base type's constraint.
4. IF a derived type forwards a dependent parameter to a base type with a Dependent_Argument_Refinement and the derived type declares no refinement constraint on that parameter, THEN THE Type_Checker SHALL emit a diagnostic error indicating that the forwarded parameter lacks a constraint required to satisfy the base type's refinement.
5. THE Type_Checker SHALL treat two constructed types that differ only in their Dependent_Argument_Refinement constraints as distinct types that are not mutually assignable (e.g., `Array<int, Size:int> where Size >= 0` is not assignable to or from `Array<int, Size:int> where Size >= 1`).
6. THE Semantic_Model_Builder SHALL include the refinement predicate and the constrained parameter name on the type's semantic representation so that the Type_Checker can evaluate the predicate against compile-time constant values and the Emitter can query whether a refinement constraint exists.

### Requirement 10: Interaction with Existing Generic Constraints

**User Story:** As a Mist developer, I want ad-hoc refinement constraints to coexist cleanly with existing generic constraint clauses, so that I can use both features on the same declaration without ambiguity.

#### Acceptance Criteria

1. THE Grammar SHALL parse `where T : class` as a Generic_Constraint_Clause and `where Size >= 0` as an Adhoc_Refinement_Constraint based on the presence or absence of the `:` token after the name.
2. THE Pretty_Printer SHALL emit Generic_Constraint_Clauses and Adhoc_Refinement_Constraint clauses in their original declaration order.
3. THE Semantic_Model_Builder SHALL process Generic_Constraint_Clauses and Adhoc_Refinement_Constraint clauses independently, attaching each to the appropriate semantic entity (type parameter vs. dependent parameter/value parameter).
4. WHEN both a Generic_Constraint_Clause and an Adhoc_Refinement_Constraint appear on the same declaration, THE Compiler SHALL validate each independently without interference — a valid generic constraint SHALL NOT be rejected due to an invalid refinement constraint on the same declaration, and vice versa.
5. THE Grammar SHALL accept Generic_Constraint_Clauses and Adhoc_Refinement_Constraint clauses in any interleaved order on the same declaration.

### Requirement 11: Predicate Compatibility Validation

**User Story:** As a Mist developer, I want the compiler to validate that refinement predicates are compatible with the type of the constrained name, so that I get clear errors for nonsensical constraints.

#### Acceptance Criteria

1. WHEN an Adhoc_Refinement_Constraint uses a relational predicate (e.g., `>= 0`) on a dependent type parameter whose value type is `bool`, THE Type_Checker SHALL emit a diagnostic error of the form: "Refinement predicate '{Predicate}' is not valid for type '{Type}'. Relational patterns require a comparable type."
2. WHEN an Adhoc_Refinement_Constraint uses a relational predicate on a method, constructor, or indexer parameter whose type is `bool`, THE Type_Checker SHALL emit a diagnostic error of the form: "Refinement predicate '{Predicate}' is not valid for type '{Type}'. Relational patterns require a comparable type."
3. THE Type_Checker SHALL accept relational predicates on all integer types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`) and `char`.
4. THE Type_Checker SHALL accept constant patterns (e.g., `true`, `false`) and logical `and` combinators on `bool`-typed parameters and `bool`-typed dependent type parameters.
5. THE Type_Checker SHALL accept relational predicates on `float`, `double`, and `decimal` parameters and dependent type parameters.
6. THE Type_Checker SHALL accept constant patterns and logical `and` combinators on all types that support equality comparison, including integer types, `char`, `bool`, `float`, `double`, and `decimal`.
7. WHEN an Adhoc_Refinement_Constraint uses a relational predicate on a `string`-typed parameter or dependent type parameter, THE Type_Checker SHALL emit a diagnostic error of the form: "Refinement predicate '{Predicate}' is not valid for type '{Type}'. Relational patterns require a comparable type."

### Requirement 12: C# Emission for Adhoc Refinement Constraints

**User Story:** As a Mist developer, I want ad-hoc refinement constraints to be correctly transpiled to C#, so that the generated code is correct and efficient.

#### Acceptance Criteria

1. THE Emitter SHALL NOT emit runtime guard checks for parameter refinement constraints. Since all enforcement is compile-time (Requirement 6), no runtime validation code is needed.
2. THE Emitter SHALL emit dependent argument refinement constraints as compile-time validation only — no runtime code is generated because dependent values are always known at compile time.
3. THE Emitter SHALL produce C# code that compiles without errors for all valid usages of Adhoc_Refinement_Constraints.
4. WHEN a method has both generic constraints and parameter refinement constraints, THE Emitter SHALL emit the generic constraints as C# `where` clauses on the method signature. No runtime code is emitted for the parameter refinement constraints.
5. THE generated C# method body for a method with Parameter_Refinements SHALL contain only the user-written statements — no injected guard code.

### Requirement 13: Error Diagnostics for Adhoc Refinement Constraints

**User Story:** As a Mist developer, I want clear error messages when I misuse ad-hoc refinement constraints, so that I can quickly identify and fix problems.

#### Acceptance Criteria

1. WHEN a dependent type argument violates a Dependent_Argument_Refinement, THE Compiler SHALL emit a diagnostic error of the form: "Dependent type argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of type '{Type}'."
2. WHEN a compile-time constant method argument violates a Parameter_Refinement, THE Compiler SHALL emit a diagnostic error of the form: "Argument '{Value}' does not satisfy refinement constraint '{Predicate}' on parameter '{Name}' of method '{Method}'."
3. WHEN an Adhoc_Refinement_Constraint references an unresolvable name, THE Compiler SHALL emit a diagnostic error of the form: "Refinement constraint references '{Name}', which is not a dependent type parameter or parameter on this declaration."
4. WHEN an Adhoc_Refinement_Constraint is applied to a regular type parameter, THE Compiler SHALL emit a diagnostic error of the form: "Refinement constraints cannot be applied to type parameter '{Name}'. Refinement constraints apply only to dependent type parameters and value parameters."
5. WHEN duplicate Adhoc_Refinement_Constraint clauses target the same name, THE Compiler SHALL emit a diagnostic error of the form: "Duplicate refinement constraint on '{Name}'. Combine predicates using 'and' in a single where clause."
6. WHEN a refinement predicate is incompatible with the constrained type, THE Compiler SHALL emit a diagnostic error of the form: "Refinement predicate '{Predicate}' is not valid for type '{Type}'. Relational patterns require a comparable type."
7. WHEN a forwarded dependent parameter's constraint does not imply the target constraint, THE Compiler SHALL emit a diagnostic error of the form: "Cannot forward dependent parameter '{SourceName}' with constraint '{SourcePredicate}' to parameter '{TargetName}' with constraint '{TargetPredicate}' because the compiler cannot prove the implication."
8. WHEN a forwarded dependent parameter has no constraint and the target requires one, THE Compiler SHALL emit a diagnostic error of the form: "Cannot forward dependent parameter '{SourceName}' with no constraint to parameter '{TargetName}' with constraint '{TargetPredicate}' because the compiler cannot prove the implication."

### Requirement 14: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want ad-hoc refinement constraint `where` clauses to be syntax-highlighted correctly, so that I get visual cues distinguishing refinement constraints from generic constraints.

#### Acceptance Criteria

1. THE TextMate grammar SHALL continue to highlight `where` as a keyword (scope `keyword.other.mist`) in both Generic_Constraint_Clause and Adhoc_Refinement_Constraint contexts.
2. WHEN the TextMate grammar encounters a `where` clause that is not followed by `<name> :` (i.e., is not a Generic_Constraint_Clause), THE TextMate grammar SHALL highlight the constrained name with the scope `entity.name.type.parameter.mist`, since the grammar cannot semantically distinguish parameters from dependent type parameters at the lexical level.
3. THE TextMate grammar SHALL highlight the relational operators `>=`, `<=`, `>`, and `<` within Adhoc_Refinement_Constraint predicates with the existing scope `keyword.operator.comparison.mist`.
4. THE TextMate grammar SHALL highlight `and` within Adhoc_Refinement_Constraint predicates with the scope `keyword.operator.logical.mist`.
5. THE TextMate grammar SHALL add a pattern that matches the Adhoc_Refinement_Constraint form (`where <name> <relational_operator>`) and assigns scopes to the `where` keyword and the constrained name, ordered before the Generic_Constraint_Clause pattern so that the more specific colon-based pattern takes priority for generic constraints.

### Requirement 15: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating ad-hoc refinement constraints, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AdhocRefinementConstraints/Program.mist`.
2. The sample SHALL demonstrate a class with a Dependent_Argument_Refinement on a single dependent type parameter (e.g., `class Array<TValue, Size:int> where Size >= 0`).
3. The sample SHALL demonstrate a class with Dependent_Argument_Refinements on multiple dependent type parameters (e.g., `class Matrix<Rows:int, Cols:int> where Rows >= 1 where Cols >= 1`).
4. The sample SHALL demonstrate a class with both a Generic_Constraint_Clause and a Dependent_Argument_Refinement (e.g., `class Container<T, Size:int> where T : class where Size >= 0`).
5. The sample SHALL demonstrate a method with a Parameter_Refinement on a single parameter (e.g., `void Foo(int x) where x < 100`).
6. The sample SHALL demonstrate a method with Parameter_Refinements on multiple parameters (e.g., `void Clamp(int min, int max) where min <= max`).
7. The sample SHALL demonstrate a constructor with a Parameter_Refinement (e.g., `MyClass(int size) where size > 0`).
8. The sample SHALL demonstrate an indexer with a Parameter_Refinement (e.g., `int this[int index] where index >= 0`).
9. The sample SHALL demonstrate a compound refinement predicate using `and` (e.g., `where Size >= 0 and <= 1024`).
10. The sample SHALL demonstrate constructing a type with a valid dependent value that satisfies the refinement constraint.
11. The sample SHALL demonstrate calling a method with a valid literal argument that satisfies the parameter refinement constraint.
12. The sample SHALL demonstrate a method with both a generic constraint and a parameter refinement constraint.
13. The sample SHALL demonstrate flow narrowing within a method body where the parameter's refinement type is used.
14. The sample SHALL demonstrate inheritance where a derived type fixes a dependent value satisfying the base type's refinement constraint.
15. The sample SHALL demonstrate forwarding a dependent parameter with a constraint that implies the base type's constraint.
16. The sample SHALL demonstrate an interface with a method that has a Parameter_Refinement, and a class that implements that method with the same constraint (valid — same predicate).
17. The sample SHALL demonstrate an interface with a method that has a Parameter_Refinement, and a class that implements that method with no constraint (valid — weaker, accepts everything).
18. The sample SHALL demonstrate an interface with a method that has a Parameter_Refinement, and a class that implements that method with a weaker constraint (valid — accepts a superset of values).
19. WHEN the sample runs, each demonstration from criteria 2 through 18 SHALL print at least one line to stdout prefixed with a section header identifying the pattern being demonstrated (e.g., `--- 1. Single dependent argument refinement ---`).
20. The sample SHALL include a `public static void RunDemo()` entry point that executes all demonstrations.
21. `samples/AllFeatures/Program.mist` SHALL call `AdhocRefinementConstraints.Program.RunDemo()` to include the feature in the full-suite sample run.
22. The sample SHALL compile with zero diagnostics and run to completion without throwing exceptions using the Mist compiler.

### Requirement 16: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented ad-hoc refinement constraints feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the ad-hoc refinement constraints feature is implemented, THE Compiler team SHALL add an `Adhoc Refinement Constraints` section to CSHARP_FEATURES.md positioned after the existing `Refinement Types` section, containing a feature table with columns `Feature | Status | Notes` that lists each implemented sub-capability (dependent type argument constraints grammar, parameter constraints grammar, semantic model registration, compile-time enforcement, runtime guard generation, flow narrowing, type identity participation, predicate compatibility validation, and C# emission) with the `✅` status marker.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to include a row for the `Adhoc Refinement Constraints` section with accurate counts of implemented (`✅`), remaining (`🔲`), and skipped (`⛔`) features matching the entries in the new section's feature table.
3. THE Compiler team SHALL use the same table format (`| Feature | Status | Notes |`) and status legend (`✅ Implemented | 🔲 Remaining | ⛔ Skipped`) as all other sections in CSHARP_FEATURES.md.


### Requirement 17: Interface and Trait Method Refinement Constraint Satisfaction (Liskov Substitution)

**User Story:** As a Mist developer, I want the compiler to verify that when I implement an interface or trait method with a parameter refinement constraint, my implementation does not strengthen the constraint beyond what the interface/trait promises, so that callers relying on the interface contract are never rejected by the implementation.

#### Acceptance Criteria

1. WHEN a class implements an interface method that has a Parameter_Refinement on a parameter, and the implementing method declares the same refinement predicate on that parameter, THE Type_Checker SHALL accept the implementation without error.
2. WHEN a class implements an interface method that has a Parameter_Refinement on a parameter, and the implementing method declares no refinement constraint on that parameter, THE Type_Checker SHALL accept the implementation without error (no constraint is weaker — it accepts all values).
3. WHEN a class implements an interface method that has a Parameter_Refinement on a parameter, and the implementing method declares a weaker refinement predicate on that parameter (one that accepts a superset of values), THE Type_Checker SHALL accept the implementation without error.
4. WHEN a class implements an interface method that has a Parameter_Refinement on a parameter, and the implementing method declares a stronger refinement predicate on that parameter (one that rejects values the interface promises to accept), THE Type_Checker SHALL emit a diagnostic error of the form: "Implementation of '{Method}' on '{Type}' has a stronger refinement constraint '{ImplPredicate}' on parameter '{Name}' than the interface '{Interface}' requires '{InterfacePredicate}'. The implementation must accept all values the interface promises to accept."
5. THE Type_Checker SHALL use `RefinementSolver.Implies` to determine constraint strength: the interface's predicate must imply the implementation's predicate (i.e., every value accepted by the interface constraint is also accepted by the implementation constraint). IF the implication does not hold, the implementation's constraint is considered stronger.
6. THE Type_Checker SHALL apply criteria 1 through 5 equivalently to trait method implementations.
7. THE Type_Checker SHALL apply criteria 1 through 5 to all parameter positions on the method (including indexer parameters).
8. WHEN an interface method has no Parameter_Refinement on a parameter, THE Type_Checker SHALL accept any refinement constraint (or no constraint) on the corresponding implementation parameter without error (the implementation may freely add constraints when the interface imposes none).
9. WHEN a class implements an interface method that has Parameter_Refinements on multiple parameters, THE Type_Checker SHALL validate each parameter's constraint independently — a violation on one parameter SHALL NOT prevent validation of other parameters.
