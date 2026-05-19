# Requirements Document

## Introduction

This document specifies the requirements for adding `readonly` as a modifier on local variable declarations in the Mist programming language. Currently, `readonly` can be applied to fields, properties, methods, types, and blocks, but local variables have no immutability mechanism. This feature allows developers to declare local variables that cannot be reassigned after initialization, providing a lightweight way to express intent and catch accidental mutation at compile time.

The syntax is `readonly var x = 7;` (inferred type) or `readonly int x = 7;` (explicit type). Once declared readonly, the variable cannot appear as the target of an assignment expression. This is a compile-time-only restriction with no runtime overhead.

## Glossary

- **Readonly_Local**: A local variable declared with the `readonly` modifier that cannot be reassigned after its initial declaration.
- **Parser**: The Storm-generated parser that reads Mist source and produces a concrete syntax tree.
- **Semantic_Model**: The internal representation of resolved types, symbols, and their relationships after semantic analysis.
- **Type_Checker**: The component that validates type correctness and enforces semantic rules including assignment restrictions.
- **CSharp_Emitter**: The backend that translates the Mist semantic model into C# source code.
- **Readonly_Inference_Engine**: The existing compiler component that classifies symbols by their readonly status.

## Requirements

### Requirement 1: Readonly Local Variable Parsing

**User Story:** As a Mist developer, I want to declare local variables with the `readonly` modifier, so that I can express my intent that a variable should not be reassigned.

#### Acceptance Criteria

1. WHEN the `readonly` keyword precedes a `var` declaration (e.g., `readonly var x = 7;`), THE Parser SHALL produce an InferredVariableDeclaration node with a Readonly modifier attached.
2. WHEN the `readonly` keyword precedes a typed declaration (e.g., `readonly int x = 7;`), THE Parser SHALL produce a TypedVariableDeclaration node with a Readonly modifier attached.
3. WHEN the `readonly` keyword precedes a tuple deconstruction declaration (e.g., `readonly var (a, b) = expr;`), THE Parser SHALL produce a VarTupleDeconstruction node with a Readonly modifier attached.
4. WHEN the `readonly` keyword precedes a typed tuple deconstruction declaration (e.g., `readonly (int a, string b) = expr;`), THE Parser SHALL produce a TypedTupleDeconstruction node with a Readonly modifier attached.
5. WHEN the `readonly` modifier is combined with an ownership specifier (e.g., `readonly owned var x = expr;`), THE Parser SHALL accept the combination with `readonly` appearing before the ownership specifier and attach both the Readonly modifier and the ownership specifier to the declaration node.
6. IF a `readonly` local variable declaration omits the `=` initializer (e.g., `readonly var x;` or `readonly int x;`), THEN THE Parser SHALL report a syntax error indicating that readonly locals require an initializer expression.
7. WHEN a `readonly` local variable declaration uses a discard name (e.g., `readonly var _ = expr;`), THE Parser SHALL produce an InferredVariableDeclaration node with a Readonly modifier attached and the discard token as the name.

### Requirement 2: Readonly Local Variable Semantic Registration

**User Story:** As a language developer, I want the semantic model to record readonly status on local variable symbols, so that downstream analysis passes can enforce immutability.

#### Acceptance Criteria

1. WHEN a local variable is declared with the `readonly` modifier (inferred type, explicit type, or ref local), THE Semantic_Model SHALL set the symbol's ReadonlyStatus to ExplicitReadonly.
2. WHEN a local variable is declared without the `readonly` modifier, THE Semantic_Model SHALL leave the symbol's ReadonlyStatus unchanged (Unknown or as determined by inference).
3. WHEN a readonly tuple deconstruction declares multiple variables (both var deconstruction and typed deconstruction forms), THE Semantic_Model SHALL set ReadonlyStatus to ExplicitReadonly on each individual variable symbol introduced by the deconstruction.
4. WHEN a readonly local variable declaration includes an ownership specifier, THE Semantic_Model SHALL set the symbol's ReadonlyStatus to ExplicitReadonly regardless of which ownership specifier is present.

### Requirement 3: Readonly Local Reassignment Prohibition

**User Story:** As a Mist developer, I want the compiler to reject reassignment to readonly local variables, so that I get a compile-time error if I accidentally try to mutate a readonly binding.

#### Acceptance Criteria

1. WHEN code assigns to a local variable whose ReadonlyStatus is ExplicitReadonly via the simple assignment operator (`x = expr;`), THE Type_Checker SHALL report a diagnostic error that includes the variable name and indicates the variable is readonly and cannot be reassigned.
2. WHEN code assigns to a local variable whose ReadonlyStatus is not ExplicitReadonly, THE Type_Checker SHALL permit the assignment without reporting a readonly-related diagnostic.
3. IF compound assignment operators (e.g., `+=`, `-=`, `*=`, `/=`, `%=`) are added to the language, THEN WHEN a readonly local variable is used as the target of a compound assignment operator, THE Type_Checker SHALL report a diagnostic error that includes the variable name and indicates the variable is readonly.
4. IF prefix or postfix increment/decrement operators (e.g., `++`, `--`) are added to the language, THEN WHEN a readonly local variable is used as the operand of such an operator, THE Type_Checker SHALL report a diagnostic error that includes the variable name and indicates the variable is readonly.
5. WHEN a readonly local variable is used as the expression in a `ref` argument at a call site (i.e., the argument matches the `RefArgument` grammar production with the readonly local as its expression), THE Type_Checker SHALL report a diagnostic error that includes the variable name and indicates the variable is readonly and cannot be passed by mutable reference.
6. WHEN a readonly local variable declared via tuple deconstruction (where ReadonlyStatus is ExplicitReadonly on each introduced symbol) is subsequently assigned via simple assignment, THE Type_Checker SHALL report the same readonly reassignment diagnostic as criterion 1 for each affected variable.

### Requirement 4: Readonly Local Diagnostics

**User Story:** As a Mist developer, I want clear error messages when I attempt to reassign a readonly local variable, so that I can understand and fix the issue.

#### Acceptance Criteria

1. WHEN a readonly local variable is reassigned, THE Type_Checker SHALL report an error-severity diagnostic whose message includes the variable name and states that the variable is readonly and cannot be reassigned.
2. WHEN a readonly local variable is the target of a compound assignment, THE Type_Checker SHALL report an error-severity diagnostic whose message includes the variable name and states that the variable is readonly.
3. WHEN a readonly local variable is the operand of an increment or decrement operator, THE Type_Checker SHALL report an error-severity diagnostic whose message includes the variable name and states that the variable is readonly.
4. WHEN a readonly local variable is passed as a `ref` argument, THE Type_Checker SHALL report an error-severity diagnostic whose message includes the variable name and states that readonly variables cannot be passed by mutable reference.
5. THE Type_Checker SHALL attach source location information (file path, line number, and column number of the violating expression) to every readonly-local diagnostic so that the formatted output follows the pattern `path(line,column): error ID: message`.

### Requirement 5: Readonly Local C# Emission

**User Story:** As a language developer, I want readonly locals to be emitted as idiomatic C# without runtime overhead, so that the feature is zero-cost.

#### Acceptance Criteria

1. THE CSharp_Emitter SHALL NOT emit any runtime checks for readonly local variable enforcement (all enforcement is compile-time in the Mist compiler).
2. WHEN a readonly local variable is emitted, THE CSharp_Emitter SHALL emit a standard local variable declaration (readonly enforcement is a Mist-only compile-time concept with no C# equivalent for locals).

### Requirement 6: Readonly Local Interaction with Readonly Contexts

**User Story:** As a Mist developer, I want readonly locals to work naturally within readonly contexts, so that the two features compose without surprises.

#### Acceptance Criteria

1. WHILE in a readonly context, THE Readonly_Inference_Engine SHALL continue to permit local variable declarations and assignments to non-readonly local variables (existing base rule 4 behavior is unchanged).
2. WHILE in a readonly context, THE Type_Checker SHALL still prohibit reassignment to readonly local variables (the readonly-local restriction applies regardless of whether the enclosing context is itself readonly).
3. WHEN a readonly local variable is declared inside a readonly block, THE Type_Checker SHALL enforce the readonly-local reassignment prohibition on that variable throughout its scope.

### Requirement 7: Round-Trip Parsing

**User Story:** As a language developer, I want readonly local variable declarations to survive a parse-then-print cycle, so that tooling can reliably reconstruct source from the syntax tree.

#### Acceptance Criteria

1. WHEN a valid readonly local variable declaration (inferred type, explicit type, tuple deconstruction, typed tuple deconstruction, or combined with ownership specifiers) is parsed without errors and the resulting syntax tree is printed back to source, THE Parser SHALL produce no errors when re-parsing the printed output.
2. WHEN the printed source from criterion 1 is parsed a second time and printed again, THE pretty-printer SHALL produce output identical to the first printed source (print-parse idempotence).
3. IF the original source contains parse errors, THEN THE round-trip property SHALL NOT be required to hold (round-trip applies only to error-free inputs).

### Requirement 8: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `readonly` before local variable declarations to be syntax-highlighted correctly, so that I get visual cues for readonly bindings.

#### Acceptance Criteria

1. WHEN `readonly` appears immediately before a local variable declaration keyword (`var`) or a type name in a local variable declaration, THE TextMate grammar SHALL assign it the scope `storage.modifier.mist`.
2. WHEN a local variable declaration is preceded by the `readonly` modifier, THE TextMate grammar SHALL highlight the type annotation, variable name, and initializer expression using the same scopes as a local variable declaration without the `readonly` modifier.
3. WHEN `readonly` appears as a substring of a longer identifier (e.g., `readonlyValue`), THE TextMate grammar SHALL NOT assign it the `storage.modifier.mist` scope.

### Requirement 9: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating readonly locals, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ReadonlyLocals/Program.mist` with a corresponding `samples/ReadonlyLocals/mist.toml` declaring the project name as `"ReadonlyLocals"`.
2. The sample SHALL demonstrate each of the following readonly local syntax variants: readonly with inferred type (`readonly var x = expr;`), readonly with explicit type (`readonly int x = expr;`), readonly with tuple deconstruction (`readonly var (a, b) = expr;`), readonly with typed tuple deconstruction (`readonly (int a, string b) = expr;`), readonly combined with ownership specifiers, readonly locals declared inside a readonly block, readonly locals declared inside a method body and a constructor body, and multiple readonly locals declared in the same scope.
3. WHEN the sample is executed, THE System SHALL print a section header (e.g., `--- Section Name ---`) before each demonstrated variant, followed by at least one `key=value` formatted output line per variant, so that each variant's correctness is independently verifiable from stdout.
4. `samples/AllFeatures/Program.mist` SHALL call `ReadonlyLocals.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../ReadonlyLocals"`.
5. The sample SHALL compile and run without errors or warnings using the Mist compiler.
