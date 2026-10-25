# Requirements Document

## Introduction

This document specifies the requirements for adding C#-style `ref` semantics to the Mist programming language. This feature introduces pass-by-reference parameters, ref returns, and ref locals — allowing functions to operate directly on the caller's storage location rather than on a copy of the value.

C#-style `ref` is distinct from Mist's existing ownership system. The ownership system (`owned`, `borrowed`, `frozen`, `readonly ref`) governs object-level access rights and lifetime. C#-style `ref` governs *variable-level indirection* — it creates an alias to a storage location (a variable, field, or array element) so that assignments through the ref modify the original location. This is particularly useful for value types (structs) where pass-by-value creates copies, and for scenarios where a function needs to return a reference into a data structure.

After the ownership keyword revision (which freed `ref` from its previous role as the mutable borrow specifier, renaming it to `borrowed`), the `ref` keyword is now available for this purpose. The existing `readonly ref` compound specifier will gain dual meaning: in ownership contexts it continues to mean "readonly borrowed view," and in ref-parameter contexts it means "a ref that cannot be used to assign through."

**Modifier order equivalence**: The keyword orderings `readonly ref` and `ref readonly` are semantically identical — both mean "a readonly reference." The parser SHALL accept either order in all positions (return types, local variable declarations) and produce the same AST node. Throughout this document, both forms are used interchangeably.

## Glossary

- **Ref_Parameter**: A method parameter declared with the `ref` keyword that receives a reference to the caller's storage location. Assignments to the parameter inside the method modify the caller's variable.
- **Ref_Return**: A method return type annotated with `ref` that returns a reference to a storage location (field or array element) rather than a copy of the value. The caller receives an alias to the original location.
- **Ref_Local**: A local variable declared with `ref` that holds a reference to another storage location. Assignments to the ref local modify the referenced location.
- **Ref_Readonly_Return**: A method return type annotated with `ref readonly` (or `readonly ref`) that returns a readonly reference — the caller can read but not assign through it.
- **Ref_Readonly_Local**: A local variable declared with `ref readonly` (or `readonly ref`) that holds a readonly reference to a storage location.
- **Storage_Location**: A named location that can hold a value — a local variable, a field, a property with a backing field, or an array element. Ref parameters and ref locals alias a storage location.
- **Parser**: The component that builds an AST from tokens using the Storm grammar rules defined in `Mist.storm`.
- **Lexer**: The component that tokenizes Mist source text into keyword and identifier tokens.
- **SemanticModelBuilder**: The component that maps AST nodes to semantic model types and symbols.
- **Type_Checker**: The analyzer that validates type correctness, assignability, and ref safety rules.
- **CSharp_Emitter**: The backend that generates C# source code from the Mist semantic model.
- **Pretty_Printer**: The `MistPrettyPrinter` class that converts AST nodes back to Mist source text.
- **TextMate_Grammar**: The JSON file at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` that provides VS Code syntax highlighting.

## Requirements

### Requirement 1: Ref Parameter Syntax

**User Story:** As a Mist developer, I want to declare method parameters with the `ref` modifier, so that I can pass variables by reference.

#### Acceptance Criteria

1. THE Parser SHALL recognize a bare `ref` keyword preceding a parameter type in a parameter position as a ref parameter modifier, producing a distinct AST node that is separate from ownership specifier nodes.
2. THE Parser SHALL accept ref modifiers on method parameters, constructor parameters, indexer parameters, operator parameters, and cast operator parameters. A ref parameter position is a method, constructor, indexer or local-function parameter; on an operator or cast-operator parameter the token is parsed but not admitted (see the note below and criterion 14.12).

> **Corrected (2026-10-21, operator decision):** operator parameters and cast-operator parameters are NOT `ref` positions. Requirements 2.1-2.2 require the `ref` annotation on the argument at every call site, and an operator expression (`a + b`, `-a`, a cast or an implicit conversion) has no argument list to carry it, so such a declaration could never be called; C# refuses the same declaration (CS0631). THE Parser still accepts the token in those positions, and THE Compiler SHALL refuse each bare `ref` operator or cast-operator parameter at the parameter, before any C# is emitted. The `ref` operator-return refusal (`MIST0040`) is unchanged. The decision was recorded on 2026-10-21 UTC (the evening of 2026-10-20, America/New_York).

> **Decided (2026-10-22, operator decision):** an operator or cast-operator parameter is not a `readonly ref` position either: an operator takes its operands by value. THE Compiler SHALL refuse each `readonly ref` operator or cast-operator parameter at the parameter with `MIST0042` (criterion 14.13), before any C# is emitted, rather than lowering it to C# `in`. A `readonly ref` method, constructor, indexer or local-function parameter is unchanged.
3. IF a parameter has both a ref modifier and the `owned` ownership specifier, THEN THE Parser SHALL report a diagnostic error indicating that `ref` and `owned` are mutually exclusive on the same parameter, and SHALL not produce a valid parameter node for that declaration.
4. IF a parameter has both a ref modifier and a `borrowed` or `frozen` ownership specifier, THEN THE Parser SHALL report a diagnostic warning indicating that the ownership specifier is redundant because `ref` already implies a borrow, but SHALL still produce a valid parameter node (treating the parameter as `ref` with the ownership specifier ignored).
4. THE Parser SHALL distinguish between `ref` as a ref-parameter modifier and `ref` as part of the `readonly ref` ownership specifier by context: when `readonly` immediately precedes `ref` in a parameter position, the compound token sequence is parsed as the `ReadonlyRefSpecifier` ownership specifier; a bare `ref` not preceded by `readonly` in a parameter position is parsed as the ref-parameter modifier.
5. IF a parameter has the `ref` modifier applied more than once, THEN THE Parser SHALL report a diagnostic error indicating a duplicate ref modifier.

### Requirement 2: Ref Parameter Call-Site Syntax

**User Story:** As a Mist developer, I want to explicitly annotate arguments at call sites with `ref`, so that it is clear which variables are being passed by reference.

#### Acceptance Criteria

1. WHEN calling a method with a `ref` parameter, THE caller SHALL prefix the corresponding positional argument with the `ref` keyword (e.g., `Swap(ref a, ref b)`).
2. IF a `ref` argument is missing the `ref` keyword at the call site, THEN THE Type_Checker SHALL report a diagnostic error indicating that the `ref` annotation is required for that argument.
3. IF a `ref` keyword appears at a call site for a parameter that is not declared `ref`, THEN THE Type_Checker SHALL report a diagnostic error indicating that the parameter does not accept a `ref` argument.
4. THE Parser SHALL recognize `ref` as an argument prefix in method call argument lists, producing a distinct AST node for annotated arguments.
5. IF a `ref`-annotated argument expression is not an assignable variable (e.g., a literal, a temporary, or a read-only binding), THEN THE Type_Checker SHALL report a diagnostic error indicating that `ref` requires a mutable variable as the argument.

### Requirement 3: Ref Parameter Semantics

**User Story:** As a Mist developer, I want `ref` parameters to alias the caller's variable, so that assignments inside the method modify the caller's storage location.

#### Acceptance Criteria

1. WHEN a method assigns to a `ref` parameter, THE assignment SHALL modify the caller's original variable.
2. WHEN a method reads a `ref` parameter, THE read SHALL observe the current value of the caller's original variable.
3. THE Type_Checker SHALL require that a `ref` argument is a writable storage location (local variable, mutable field, or array element) — not a literal, expression result, property access, readonly field, `frozen` variable, or `readonly ref` variable/local.
4. THE Type_Checker SHALL require that the argument type matches the parameter type by exact type identity (no implicit conversions and no subtype/supertype substitution for ref parameters).
5. THE Type_Checker SHALL require that a `ref` argument is definitely assigned before the call.
6. WHEN a `ref` parameter is passed to another method expecting a `ref` parameter of the same type, THE Type_Checker SHALL permit the forwarding.
7. WHEN a `ref` parameter is passed as a by-value argument to another method, THE Type_Checker SHALL permit the call by reading the current value of the referenced storage location.

### Requirement 4: Ref Return Syntax and Semantics

**User Story:** As a Mist developer, I want methods to return references to storage locations, so that callers can read or modify elements within data structures without copying.

#### Acceptance Criteria

1. THE Parser SHALL recognize `ref` preceding a method return type as a ref return modifier, producing a distinct AST annotation on the method declaration.
2. THE Parser SHALL recognize `ref readonly` (or `readonly ref`) preceding a method return type as a ref-readonly return modifier, producing a distinct AST annotation on the method declaration.
3. WHEN a method is declared with a `ref` return type, THE Type_Checker SHALL require that the return expression is a writable storage location whose lifetime exceeds the method call (a field or array element — not a local variable or parameter).
4. WHEN a method is declared with a `ref readonly` return type, THE Type_Checker SHALL require that the return expression is a storage location whose lifetime exceeds the method call.
5. IF a `ref`-returning or `ref readonly`-returning method attempts to return a local variable or parameter (unless the parameter is itself `ref`), THEN THE Type_Checker SHALL report a diagnostic error (dangling reference).
6. IF a `ref`-returning or `ref readonly`-returning method attempts to return a literal or expression result, THEN THE Type_Checker SHALL report a diagnostic error.
7. WHEN a caller invokes a `ref`-returning method, THE caller SHALL receive a reference to the original storage location — subsequent reads and writes through the result alias the original location.
8. WHEN a caller invokes a `ref readonly`-returning method, THE caller SHALL receive a readonly reference — reads alias the original location, but writes are prohibited.

### Requirement 5: Ref Local Syntax and Semantics

**User Story:** As a Mist developer, I want to declare local variables that are references to other storage locations, so that I can create aliases within a method body.

#### Acceptance Criteria

1. THE Parser SHALL recognize `ref` preceding a local variable type (or `ref var`) as a ref local declaration.
2. THE Parser SHALL recognize `ref readonly` (or `readonly ref`) preceding a local variable type as a ref-readonly local declaration.
3. WHEN a ref local is declared, THE Type_Checker SHALL require that the initializer is a writable storage location (for `ref`) or any storage location (for `ref readonly`).
4. WHEN a ref local is assigned to (e.g., `x = newValue`), THE assignment SHALL modify the referenced storage location, not rebind the ref.
5. WHEN a ref local or ref readonly local is reassigned with `ref` (e.g., `ref x = ref otherVar`), THE ref local SHALL rebind to the new storage location, subject to the same initializer rules as declaration: the new target must be a writable storage location for `ref` locals, or any storage location for `ref readonly` locals.
6. THE Type_Checker SHALL prohibit assignment through a `ref readonly` local. (Note: this is subsumed by the existing rule that `readonly` locals cannot be assigned to — the `ref` aspect does not change that. No additional implementation is needed beyond the existing readonly check, but the behavior is called out here for completeness.)
7. IF a ref local or ref readonly local is declared without an initializer, THEN THE Type_Checker SHALL report an error indicating that ref locals require an initializer.
8. THE Type_Checker SHALL verify that the referenced storage location's lifetime encompasses the ref local's scope (no dangling references).
9. THE Type_Checker SHALL require that the declared type of a ref local matches the referenced storage location's type exactly (no implicit conversions).

### Requirement 6: Ref Safety Rules

**User Story:** As a Mist developer, I want the compiler to prevent dangling references, so that ref parameters and ref locals cannot outlive the storage they reference.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT permit a ref local to be initialized from, or reassigned to reference, a local variable whose scope is narrower than the ref local's own scope (i.e., the referenced variable is declared in an inner block that ends before the ref local goes out of scope).
2. THE Type_Checker SHALL NOT permit a `ref` return to reference a local variable or non-ref parameter (their storage is destroyed when the method returns).
3. THE Type_Checker SHALL permit a `ref` return to reference an instance field accessible through `this`, including fields reached through a chain of instance field accesses (e.g., `this.inner.value`), because the object's storage outlives the method call.
4. THE Type_Checker SHALL permit a `ref` return to reference an array element accessed through an instance field of `this`.
5. THE Type_Checker SHALL NOT permit storing a ref in a field (refs are stack-only aliases).
6. IF a ref parameter is returned as a ref return, THEN THE Type_Checker SHALL permit this only if the parameter itself was declared `ref` (the caller's storage outlives the callee).
7. THE Type_Checker SHALL NOT permit a ref local to be captured by a lambda or closure, reporting a diagnostic error indicating that ref locals cannot be captured.
8. THE Type_Checker SHALL permit a `ref` return to reference a static field (static fields outlive any method call).

### Requirement 7: Semantic Model Representation

**User Story:** As a language developer, I want the semantic model to represent ref parameters, ref returns, and ref locals distinctly, so that the type checker and emitter can reason about them.

#### Acceptance Criteria

1. THE SemanticModelBuilder SHALL record a `RefKind` property on parameter symbols with values: `None`, `Ref`, `RefReadonly`.
2. THE SemanticModelBuilder SHALL record a `RefKind` property on method symbols for the return type with values: `None`, `Ref`, `RefReadonly`.
3. THE SemanticModelBuilder SHALL record a `RefKind` property on local variable symbols with values: `None`, `Ref`, `RefReadonly`.
4. WHEN comparing method signatures for overload resolution, THE Type_Checker SHALL treat two parameters as distinct if they differ only in `RefKind` value — a `ref int` parameter and an `int` parameter are separate overloads.
5. WHEN comparing method signatures for interface satisfaction, THE Type_Checker SHALL require that the implementing method's parameter `RefKind` values match the interface method's parameter `RefKind` values exactly, and SHALL emit a diagnostic error when a mismatch is found.
6. WHEN comparing method signatures for trait structural matching, THE Type_Checker SHALL require that the implementing method's parameter `RefKind` values match the trait method's parameter `RefKind` values exactly — a method with a non-ref parameter SHALL NOT satisfy a trait method requiring a `ref` parameter.

### Requirement 8: C# Emission

**User Story:** As a language developer, I want the C# emitter to produce correct `ref` annotations in the generated code, so that the ref semantics are preserved at runtime.

#### Acceptance Criteria

1. WHEN emitting a `ref` parameter, THE CSharp_Emitter SHALL emit the C# `ref` keyword before the parameter type.
2. WHEN emitting a `ref` return type, THE CSharp_Emitter SHALL emit `ref` before the return type in the method signature.
3. WHEN emitting a `ref readonly` return type, THE CSharp_Emitter SHALL emit `ref readonly` before the return type.
4. WHEN emitting a ref local declaration, THE CSharp_Emitter SHALL emit `ref` (or `ref readonly`) before the local type.
5. WHEN emitting a call-site argument annotated with `ref`, THE CSharp_Emitter SHALL emit the `ref` keyword before the argument expression.
6. WHEN emitting a `ref` return statement, THE CSharp_Emitter SHALL emit `return ref expression;`.

### Requirement 9: Interaction with Ownership System

**User Story:** As a Mist developer, I want clear rules about how ref parameters interact with the ownership system, so that both features compose predictably.

#### Acceptance Criteria

1. THE Type_Checker SHALL prohibit combining the ref parameter modifier with the `owned` ownership specifier on the same parameter (error). THE Type_Checker SHALL warn (but permit) combining `ref` with `borrowed` or `frozen` ownership specifiers, treating the ownership specifier as redundant.
2. WHEN a `ref` parameter references an `owned` variable, THE Type_Checker SHALL treat the ref as a temporary borrow — the owner retains ownership but cannot move the variable while the ref is outstanding.
3. WHEN a `ref` parameter references a `borrowed` variable, THE Type_Checker SHALL permit the ref (the borrower can grant sub-borrows).
4. THE Type_Checker SHALL NOT permit passing a `readonly ref` (ownership tier) variable as a `ref` argument (cannot grant write access to a readonly view).
5. THE Type_Checker SHALL NOT permit passing a `frozen` (ownership tier) variable as a `ref` argument (cannot grant write access to a frozen view).
6. WHEN a `ref` parameter references an unrestricted variable (a variable with no ownership specifier), THE Type_Checker SHALL permit the call without ownership restrictions.

### Requirement 10: Interaction with Value Types

**User Story:** As a Mist developer, I want ref parameters to work naturally with structs, so that I can avoid unnecessary copies of large value types.

#### Acceptance Criteria

1. WHEN a struct variable is passed as a `ref` argument, THE callee SHALL operate on the original struct storage — mutations to the parameter are visible to the caller after the call returns.
2. WHEN a struct field (including nested struct fields such as `s.inner.x`) is passed as a `ref` argument, THE callee SHALL operate on the field's storage location directly — no copy of the field is created.
3. WHEN a struct array element is passed as a `ref` argument, THE callee SHALL operate on the array element's storage location directly — no copy of the element is created.
4. IF a struct literal or temporary expression result (such as a method return value, arithmetic result, or constructor invocation) is passed as a `ref` argument, THEN THE Type_Checker SHALL report a diagnostic error indicating that the expression has no addressable storage location.
5. WHEN a method returns `ref` to a struct field, THE caller SHALL receive a reference to the field's storage — mutations through the ref modify the struct's field.

### Requirement 11: Pretty Printer

**User Story:** As a language developer, I want the pretty printer to emit ref annotations correctly, so that round-trip parsing produces correct output.

#### Acceptance Criteria

1. WHEN a parameter declaration has a ref modifier, THE Pretty_Printer SHALL emit `ref ` immediately before the parameter's type name.
2. WHEN a method or interface method declaration has a ref modifier on its return type, THE Pretty_Printer SHALL emit `ref ` immediately before the return type name.
3. WHEN a method or interface method declaration has a readonly-ref modifier on its return type, THE Pretty_Printer SHALL emit `readonly ref ` immediately before the return type name.
4. WHEN a local variable declaration (typed or inferred) has a ref modifier, THE Pretty_Printer SHALL emit `ref ` immediately before the variable's type name or `var` keyword.
5. WHEN a call-site argument has a ref annotation in the syntax tree, THE Pretty_Printer SHALL emit `ref ` immediately before the argument expression.
6. FOR ALL valid ref-annotated declarations and expressions, THE Pretty_Printer SHALL satisfy the round-trip property: parsing the emitted text and pretty-printing the resulting syntax tree SHALL produce output identical to the first emission (character-for-character equality).

### Requirement 12: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want ref-related keywords to be syntax-highlighted correctly, so that I get visual cues for pass-by-reference annotations.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL assign the scope `storage.modifier.mist` to the token `ref` when it appears as a standalone word in parameter declarations (e.g., `ref int x`).
2. THE TextMate_Grammar SHALL match `ref` only at word boundaries (using `\b` anchors) so that identifiers containing `ref` as a substring (e.g., `reference`, `refresh`, `preferential`) do NOT receive the `storage.modifier.mist` scope.
3. THE TextMate_Grammar SHALL assign the same `storage.modifier.mist` scope to `ref` when it appears as a standalone word preceding an argument expression at a call site (e.g., `foo(ref myVar)`).
4. IF `ref` appears inside a string literal, comment, or interpolation expression, THEN THE TextMate_Grammar SHALL NOT assign the `storage.modifier.mist` scope to that occurrence.

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating ref parameters, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RefParameters/Program.mist` with a corresponding `samples/RefParameters/mist.toml` declaring `name = "RefParameters"`.
2. The sample SHALL demonstrate the following syntax variants and usage patterns defined in the preceding requirements: `ref` parameters with value types (structs), `ref` parameters with reference types (classes), `ref` parameters with interface-typed variables (demonstrating that the variable itself is aliased, not the underlying value), `ref` parameters with trait-typed variables, ref returns from methods returning references to fields, ref returns from methods returning references to array elements, ref locals aliasing variables, ref locals aliasing fields, ref locals aliasing array elements, `ref readonly` locals, ref forwarding (passing a ref parameter as a ref argument to another method), and interaction with structs showing mutation visibility to the caller.
3. Each demonstrated pattern SHALL print a labeled output line to stdout using the bracket-prefix format `[label] description`, where label is one of: `ref-param-struct`, `ref-param-class`, `ref-param-interface`, `ref-param-trait`, `ref-return-field`, `ref-return-array`, `ref-local-var`, `ref-local-field`, `ref-local-array`, `ref-readonly-local`, `ref-forward`, `ref-struct-mutation`.
4. `samples/AllFeatures/Program.mist` SHALL call `RefParameters.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../RefParameters"`.
5. The sample SHALL compile and run with exit code 0 and zero compiler diagnostics (errors or warnings) using the Mist compiler.
6. The sample SHALL verify ref semantics by printing the value of the mutated variable both before and after the ref-parameter call for at least the `ref-param-struct` and `ref-param-class` patterns, demonstrating that the caller observes the mutation.

### Requirement 14: Diagnostics

**User Story:** As a Mist developer, I want clear compile-time error messages for ref-related violations, so that I can understand and fix issues.

#### Acceptance Criteria

1. WHEN a `ref` argument is not a writable storage location, THE Type_Checker SHALL report an error diagnostic identifying the argument and explaining that ref requires an assignable variable, field, or array element.
2. WHEN a `ref` keyword is missing at a call site for a ref parameter, THE Type_Checker SHALL report an error diagnostic identifying the parameter and explaining that the `ref` keyword is required at the call site.
3. WHEN a `ref` keyword appears at a call site for a non-ref parameter, THE Type_Checker SHALL report an error diagnostic explaining that the parameter is not declared as ref.
4. WHEN code assigns to a `ref readonly` local, THE Type_Checker SHALL report an error diagnostic explaining that the reference is readonly.
5. WHEN a ref return attempts to return a local variable or a non-ref parameter, THE Type_Checker SHALL report an error diagnostic explaining that the storage location's lifetime is too short for a ref return.
6. WHEN `ref` and `owned` are combined on the same parameter, THE Type_Checker SHALL report an error diagnostic explaining mutual exclusivity. WHEN `ref` and `borrowed` or `frozen` are combined, THE Type_Checker SHALL report a warning diagnostic explaining that the ownership specifier is redundant.
7. WHEN a `ref` argument type does not match the parameter type exactly, THE Type_Checker SHALL report a type mismatch error diagnostic (no implicit conversions allowed for ref arguments).
8. WHEN a ref is assigned to a field, THE Type_Checker SHALL report an error diagnostic explaining that refs cannot be stored in fields.
9. WHEN code assigns through a `ref readonly` return value, THE Type_Checker SHALL report an error diagnostic explaining that the returned reference is readonly and cannot be used as an assignment target.
10. WHEN a ref local is declared without an initializer, THE Type_Checker SHALL report an error diagnostic explaining that ref locals must be initialized at declaration.
11. WHEN a ref local is captured by a lambda or closure, THE Type_Checker SHALL report an error diagnostic explaining that ref locals cannot be captured by lambdas.
12. WHEN an operator or cast-operator parameter is declared with a bare `ref` modifier, THE Compiler SHALL report `MIST0041` at that parameter, explaining that an operator is applied without an argument list and so no use of it can write the `ref` annotation Requirements 2.1-2.2 require (2026-10-21, operator decision; see the note under criterion 1.2).
13. WHEN an operator or cast-operator parameter is declared `readonly ref`, THE Compiler SHALL report `MIST0042` at that parameter, explaining that an operator takes its operands by value (2026-10-22, operator decision; see the notes under criterion 1.2).
