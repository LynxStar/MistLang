# Requirements Document

## Introduction

This document specifies the requirements for adding generics support to traits in the Mist programming language. Traits currently support structural typing with fixed member signatures, but cannot be parameterized by type parameters. This feature extends trait declarations to accept type parameter lists, enabling generic structural contracts such as `trait Comparable<T> { int CompareTo(T other); }`.

With generic traits, a type can structurally satisfy a trait by matching the trait's member signatures after type argument substitution. For example, a class with `int CompareTo(int other)` structurally satisfies `Comparable<int>`. Constructed trait types (e.g., `Comparable<int>`) can appear in type references — as parameter types, return types, field types, and variable types. `with` blocks can map members to generic traits by specifying the trait with type arguments.

Additionally, this spec introduces trait inheritance (composition): a trait can extend one or more other traits, meaning that satisfying the child trait implies satisfying all parent traits as well. For example, `trait Sortable<T> : Comparable<T> { ... }` means any type satisfying `Sortable<T>` must also satisfy `Comparable<T>`. This enables building rich trait hierarchies while preserving structural typing semantics.

This feature builds on two existing specs:
- **Traits** (`specs/traits/`): Structural typing, `with` blocks, trait member access, C# interface emission.
- **Generics** (`specs/generics-type-arguments/`): Type parameter lists, type argument lists, constructed types, generic type resolution.

The generics spec explicitly excludes traits. This spec fills that gap, and also adds trait inheritance which was not part of the original traits spec.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **TraitType**: The semantic model representation of a user-defined trait type, including its member scope and the set of member signatures that constitute the trait contract.
- **Type_Parameter**: A placeholder type declared in angle brackets on a generic trait declaration (e.g., `T` in `trait Comparable<T>`).
- **Type_Parameter_List**: A comma-separated list of one or more Type_Parameter names enclosed in angle brackets (e.g., `<T>`, `<K, V>`), appearing on a trait declaration.
- **Type_Argument**: A concrete type supplied when referencing a generic trait (e.g., `int` in `Comparable<int>`).
- **Type_Argument_List**: A comma-separated list of one or more Type_Argument types enclosed in angle brackets (e.g., `<int>`, `<string, int>`), appearing in type references.
- **Constructed_Trait_Type**: A generic trait with all type parameters substituted by concrete Type_Arguments (e.g., `Comparable<int>`, `Converter<string, int>`).
- **Open_Trait_Type**: A generic trait where one or more type parameters remain unsubstituted (e.g., `Comparable<T>` within the body of a generic class `class Sorter<T>`).
- **Structural_Match**: A type satisfies a constructed trait when every trait member (after type argument substitution) has a corresponding public member on the type with the same name and compatible type signature.
- **With_Block**: A top-level declaration of the form `with TypeName : TraitName<Args> { mappings... }` that explicitly maps members from a type to a constructed generic trait.
- **TraitSatisfactionMap**: The data structure that records which types satisfy which traits, consulted by the type checker and emitter.
- **Trait_Inheritance**: A trait declaration that extends one or more parent traits using a colon-separated list (e.g., `trait Sortable<T> : Comparable<T> { ... }`). A type satisfying the child trait must also satisfy all parent traits.
- **Parent_Trait**: A trait referenced in the inheritance clause of another trait declaration. Parent traits contribute their member contracts to the child trait's satisfaction requirements.
- **Effective_Members**: The complete set of member signatures that a type must satisfy for a trait, including both the trait's own declared members and all members inherited from parent traits (transitively).

## Requirements

### Requirement 1: Generic Trait Declaration Grammar

**User Story:** As a Mist developer, I want to declare traits with type parameters, so that I can define generic structural contracts that vary by type.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `TraitDeclaration` rule to include an optional `TypeParameterList` after the trait name and before the left brace (e.g., `trait Comparable<T> { ... }`).
2. WHEN a trait declaration includes a Type_Parameter_List, THE Parser SHALL produce an AST node that captures the list of type parameter names as distinct from the trait name.
3. WHEN a trait declaration includes no Type_Parameter_List, THE Parser SHALL produce an AST node with an absent type parameter list, preserving backward compatibility with existing non-generic traits.
4. THE Grammar SHALL allow type parameters declared on a trait to appear as type references within trait member signatures (e.g., `T` in `int CompareTo(T other);`).
5. THE Grammar SHALL support multiple type parameters on a single trait declaration (e.g., `trait Converter<TIn, TOut> { TOut Convert(TIn input); }`).

### Requirement 2: Trait Inheritance Grammar

**User Story:** As a Mist developer, I want a trait to extend one or more other traits, so that I can compose trait contracts into richer hierarchies without repeating member declarations.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `TraitDeclaration` rule to include an optional inheritance clause after the optional `TypeParameterList` and before the left brace, consisting of a `COLON` token followed by a comma-separated list of trait type references (e.g., `trait Sortable<T> : Comparable<T> { ... }`).
2. THE Grammar SHALL allow the inheritance clause to reference multiple parent traits (e.g., `trait ReadWriteStream : Readable, Writable { ... }`).
3. THE Grammar SHALL allow parent trait references to include Type_Argument_Lists (e.g., `trait Sortable<T> : Comparable<T>` where the parent trait is generic).
4. THE Grammar SHALL allow parent trait references to use concrete type arguments (e.g., `trait Named : Displayable<string> { ... }`).
5. WHEN a trait declaration includes an inheritance clause, THE Parser SHALL produce an AST node that captures the list of parent trait type references.
6. WHEN a trait declaration includes no inheritance clause, THE Parser SHALL produce an AST node with an absent parent trait list, preserving backward compatibility with existing traits.

### Requirement 3: Trait Inheritance Semantic Validation

**User Story:** As a Mist developer, I want the compiler to validate trait inheritance declarations, so that I get clear errors when I reference invalid or circular parent traits.

#### Acceptance Criteria

1. WHEN a trait declaration includes an inheritance clause, THE Semantic_Model_Builder SHALL resolve each parent reference to a TraitType (or Constructed_Trait_Type for generic parents).
2. WHEN a parent reference in a trait's inheritance clause does not resolve to a declared trait, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the parent type is not a trait.
3. WHEN a parent reference is a generic trait referenced without type arguments, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that type arguments are required.
4. WHEN a parent reference is a generic trait referenced with the wrong number of type arguments, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the arity mismatch.
5. WHEN a trait inheritance chain contains a cycle (e.g., trait A extends trait B which extends trait A), THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the circular inheritance.
6. WHEN a trait lists the same parent trait more than once in its inheritance clause, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the duplicate parent.
7. THE Semantic_Model_Builder SHALL store the resolved list of parent traits on the TraitType so that Effective_Members can be computed.

### Requirement 4: Trait Inheritance and Structural Satisfaction

**User Story:** As a Mist developer, I want a type that satisfies a child trait to automatically satisfy all of its parent traits, so that trait composition works transitively.

#### Acceptance Criteria

1. WHEN a type is checked for structural satisfaction of a trait that has parent traits, THE Semantic_Model_Builder SHALL require the type to satisfy the Effective_Members — both the trait's own declared members and all members inherited from parent traits (transitively).
2. WHEN a type satisfies a child trait (structurally or via a `with` block), THE TraitSatisfactionMap SHALL also record the type as satisfying each parent trait (with the appropriate type argument substitutions).
3. WHEN a parent trait is generic and the child trait passes its own type parameters to the parent (e.g., `trait Sortable<T> : Comparable<T>`), THE Semantic_Model_Builder SHALL substitute the child's type arguments into the parent's type parameters when computing satisfaction for a constructed child trait (e.g., satisfying `Sortable<int>` implies satisfying `Comparable<int>`).
4. WHEN a parent trait has members with the same name and compatible signature as the child trait's own members, THE Semantic_Model_Builder SHALL treat them as a single requirement (no duplication).
5. WHEN a type is assigned to a parent trait-typed variable and the type satisfies a child trait that extends that parent, THE Type_Checker SHALL treat the assignment as valid.

### Requirement 5: Trait Inheritance C# Emission

**User Story:** As a Mist developer, I want the emitted C# interfaces for traits to reflect the inheritance hierarchy, so that the C# type system enforces the same relationships.

#### Acceptance Criteria

1. WHEN a trait extends one or more parent traits, THE Emitter SHALL emit the trait's C# interface with the parent trait interfaces in its inheritance list (e.g., `interface Sortable<T> : Comparable<T>`).
2. WHEN a trait extends a parent trait with concrete type arguments (e.g., `trait Named : Displayable<string>`), THE Emitter SHALL emit the constructed parent interface in the inheritance list (e.g., `interface Named : Displayable<string>`).
3. WHEN a type satisfies a child trait, THE Emitter SHALL emit only the child trait interface in the type's inheritance list; the C# type system will transitively enforce the parent interfaces through interface inheritance.

### Requirement 6: Pretty-Printer Support for Trait Inheritance

**User Story:** As a Mist developer, I want the pretty-printer to correctly format trait declarations with inheritance clauses, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a trait declaration with inheritance as `trait Name<T> : ParentTrait<T>, OtherTrait {` with the inheritance clause between the type parameter list and the left brace.
2. THE Pretty_Printer SHALL format parent trait references with type arguments as `ParentTrait<Arg1, Arg2>` with no spaces inside the angle brackets.
3. FOR ALL valid Mist ASTs containing trait declarations with inheritance clauses, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 7: Constructed Trait Types in Type References

**User Story:** As a Mist developer, I want to use generic traits with concrete type arguments in type references, so that I can declare variables, parameters, and return types using constructed trait types like `Comparable<int>`.

#### Acceptance Criteria

1. THE Grammar SHALL allow a trait type reference to include a Type_Argument_List (e.g., `Comparable<int>`, `Converter<string, int>`), using the existing `NamedTypeRef` rule with optional `TypeArgumentList`.
2. WHEN a type reference names a generic trait and includes a Type_Argument_List, THE Parser SHALL produce an AST node that captures the type arguments.
3. WHEN a type reference names a generic trait without providing a Type_Argument_List, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that type arguments are required.
4. WHEN a type reference provides a different number of type arguments than the generic trait declares type parameters, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the arity mismatch.
5. THE Grammar SHALL support nested generic type arguments in trait type references (e.g., `Comparable<List<int>>`).

### Requirement 8: With Block Grammar for Generic Traits

**User Story:** As a Mist developer, I want to write `with` blocks that map a type's members to a generic trait's members, so that I can satisfy a generic trait even when my type's member names differ.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `WithDeclaration` rule so that the trait name can include a Type_Argument_List (e.g., `with Product : Comparable<int> { ... }`).
2. WHEN a `with` block references a generic trait with type arguments, THE Parser SHALL produce an AST node that captures the trait name and its type arguments.
3. WHEN a `with` block references a generic trait without providing type arguments, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that type arguments are required.
4. WHEN a `with` block references a generic trait with the wrong number of type arguments, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the arity mismatch.

### Requirement 9: Pretty-Printer Support for Generic Traits

**User Story:** As a Mist developer, I want the pretty-printer to correctly format generic trait declarations and constructed trait type references, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a generic trait declaration as `trait Name<T, U> {` with the type parameter list immediately after the trait name.
2. THE Pretty_Printer SHALL format trait member signatures that reference type parameters using the type parameter names (e.g., `int CompareTo(T other);`).
3. THE Pretty_Printer SHALL format constructed trait type references as `TraitName<Arg1, Arg2>` with no spaces inside the angle brackets.
4. THE Pretty_Printer SHALL format `with` blocks referencing generic traits as `with TypeName : TraitName<Arg1> {`.
5. FOR ALL valid Mist ASTs containing generic trait declarations, constructed trait type references, and `with` blocks referencing generic traits, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 10: Generic Trait Type Registration in the Semantic Model

**User Story:** As a Mist developer, I want the compiler to register generic traits in the semantic model with their type parameters, so that constructed trait types can be resolved and validated.

#### Acceptance Criteria

1. WHEN a trait declaration includes a Type_Parameter_List, THE Semantic_Model_Builder SHALL create a type parameter representation for each type parameter name and register it in the trait's Member_Scope.
2. THE Semantic_Model_Builder SHALL store the list of type parameters on the TraitType so that the trait's arity (number of type parameters) is available for validation.
3. THE Semantic_Model_Builder SHALL make type parameters resolvable as types within the trait's member declarations, so that trait field types, method parameter types, and method return types can reference them.
4. WHEN a type parameter name duplicates another type parameter name in the same Type_Parameter_List on a trait, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating duplicate type parameter names.

### Requirement 11: Constructed Trait Type Resolution

**User Story:** As a Mist developer, I want the compiler to resolve trait type references with type arguments into constructed trait types, so that `Comparable<int>` is recognized as a distinct type from `Comparable<string>`.

#### Acceptance Criteria

1. WHEN a type reference includes a Type_Argument_List and the referenced type is a generic TraitType, THE Semantic_Model_Builder SHALL construct a Constructed_Trait_Type by substituting each type parameter with the corresponding type argument.
2. WHEN a type reference uses a type parameter as a type argument for a trait (e.g., `Comparable<T>` inside `class Sorter<T>`), THE Semantic_Model_Builder SHALL resolve the type parameter to its declaration and construct the trait type accordingly.
3. WHEN a type reference uses nested generics in a trait type argument (e.g., `Comparable<List<int>>`), THE Semantic_Model_Builder SHALL resolve the inner type arguments first and then construct the trait type.
4. THE Semantic_Model_Builder SHALL resolve trait type arguments after all types in the compilation are registered, so that forward references to generic traits are supported.

### Requirement 12: Structural Match with Type Argument Substitution

**User Story:** As a Mist developer, I want the compiler to check structural satisfaction of generic traits after substituting type arguments, so that a class with `int CompareTo(int other)` satisfies `Comparable<int>`.

#### Acceptance Criteria

1. WHEN a type is checked for structural satisfaction of a Constructed_Trait_Type, THE Semantic_Model_Builder SHALL substitute the trait's type parameters with the provided type arguments before comparing member signatures.
2. WHEN every trait member (after type argument substitution) has a corresponding public member on the type with the same name and compatible type signature, THE Semantic_Model_Builder SHALL consider the type a Structural_Match for the constructed trait.
3. WHEN a trait method member uses a type parameter as a parameter type (e.g., `int CompareTo(T other)` with `T=int`), THE Semantic_Model_Builder SHALL match against a method with the substituted concrete type (e.g., `int CompareTo(int other)`).
4. WHEN a trait method member uses a type parameter as a return type (e.g., `T GetValue()` with `T=string`), THE Semantic_Model_Builder SHALL match against a method with the substituted concrete return type (e.g., `string GetValue()`).
5. WHEN a trait field member uses a type parameter as its type (e.g., `T Value;` with `T=int`), THE Semantic_Model_Builder SHALL match against a public field or property with the substituted concrete type (e.g., `int Value`).
6. WHEN a candidate type has a matching member by name but with a type that does not match after substitution, THE Semantic_Model_Builder SHALL treat the trait as not satisfied by that type.

### Requirement 13: With Block Resolution for Generic Traits

**User Story:** As a Mist developer, I want the compiler to validate `with` blocks that reference generic traits with type arguments, so that member mappings are checked against the substituted trait member types.

#### Acceptance Criteria

1. WHEN a `with` block references a generic trait with type arguments, THE Semantic_Model_Builder SHALL resolve the trait to a Constructed_Trait_Type and validate member mappings against the substituted member signatures.
2. WHEN a Member_Mapping maps a source member to a target trait member whose type involves a type parameter, THE Semantic_Model_Builder SHALL check type compatibility using the substituted type (e.g., if the trait member is `T Value` and `T=int`, the source member must be compatible with `int`).
3. WHEN a `with` block provides valid mappings for all unmapped trait members (after type argument substitution), THE Semantic_Model_Builder SHALL register the type as satisfying the constructed trait in the TraitSatisfactionMap.
4. WHEN a `with` block does not cover all trait members after substitution, THE Semantic_Model_Builder SHALL emit a diagnostic error listing the unsatisfied trait members with their substituted types.

### Requirement 14: Trait Assignability with Generic Traits

**User Story:** As a Mist developer, I want to assign a value of any type that satisfies a constructed generic trait to a variable typed as that constructed trait, so that I can write generic code using trait-typed parameters.

#### Acceptance Criteria

1. WHEN the target type of an assignment is a Constructed_Trait_Type and the source type structurally satisfies the constructed trait (after type argument substitution), THE Type_Checker SHALL treat the assignment as valid.
2. WHEN the target type of an assignment is a Constructed_Trait_Type and the source type satisfies the constructed trait through a `with` block, THE Type_Checker SHALL treat the assignment as valid.
3. WHEN the target type of an assignment is a Constructed_Trait_Type and the source type does not satisfy the constructed trait, THE Type_Checker SHALL emit a diagnostic error indicating the type does not satisfy the trait, listing unsatisfied members with their substituted types.
4. WHEN a method parameter is typed as a Constructed_Trait_Type, THE Type_Checker SHALL accept an argument of any type that satisfies the constructed trait.
5. WHEN a method return type is a Constructed_Trait_Type, THE Type_Checker SHALL accept a return statement returning a value of any type that satisfies the constructed trait.
6. WHEN two Constructed_Trait_Types have the same generic trait but different type arguments (e.g., `Comparable<int>` vs `Comparable<string>`), THE Type_Checker SHALL treat them as distinct, non-assignable types.

### Requirement 15: Trait Member Access on Constructed Trait Types

**User Story:** As a Mist developer, I want to access members on a variable typed as a constructed generic trait, with member types reflecting the substituted type arguments.

#### Acceptance Criteria

1. WHEN a member access expression targets a variable typed as a Constructed_Trait_Type, THE Semantic_Model_Builder SHALL resolve the member name against the trait's Member_Scope and return the member's type with type parameters substituted by the corresponding type arguments.
2. WHEN a method call targets a variable typed as a Constructed_Trait_Type, THE Type_Checker SHALL validate the arguments against the trait method's parameter types after type argument substitution.
3. WHEN the member name is not found in the trait's Member_Scope, THE Semantic_Model_Builder SHALL emit a `MIST_UNDEFINED_MEMBER` diagnostic error.

### Requirement 16: C# Code Emission for Generic Traits

**User Story:** As a Mist developer, I want the transpiled C# code to correctly emit generic trait interfaces with type parameters, so that the compiled output preserves generic type safety.

#### Acceptance Criteria

1. THE Emitter SHALL emit each generic Trait_Declaration as a C# generic interface declaration with the trait's type parameters (e.g., `interface Comparable<T>`).
2. THE Emitter SHALL emit trait member signatures using the type parameter names in the C# interface (e.g., `int CompareTo(T other);`).
3. WHEN a type structurally satisfies a Constructed_Trait_Type, THE Emitter SHALL emit the C# type declaration with the constructed trait interface in its inheritance list (e.g., `class IntSorter : Comparable<int>`).
4. WHEN a type satisfies a Constructed_Trait_Type through a `with` block, THE Emitter SHALL emit explicit interface implementations on the type that delegate to the mapped members, using the substituted types (e.g., `int Comparable<int>.CompareTo(int other) => this.Compare(other);`).
5. WHEN a generic class satisfies a generic trait using the class's own type parameter (e.g., `class Wrapper<T>` satisfies `Comparable<T>`), THE Emitter SHALL emit the open generic trait interface in the inheritance list (e.g., `class Wrapper<T> : Comparable<T>`).
6. THE Emitter SHALL emit constructed trait type references in C# syntax wherever they appear (e.g., parameter types, return types, field types).

### Requirement 17: Diagnostic Messages for Generic Traits

**User Story:** As a Mist developer, I want clear error messages when generic trait usage is incorrect, so that I can quickly identify and fix issues.

#### Acceptance Criteria

1. WHEN a generic trait is referenced without type arguments, THE Compiler SHALL emit a diagnostic error stating that type arguments are required and indicating the expected arity.
2. WHEN a generic trait is referenced with the wrong number of type arguments, THE Compiler SHALL emit a diagnostic error stating the expected and actual arity.
3. WHEN a type does not satisfy a constructed generic trait, THE Compiler SHALL emit a diagnostic that names the constructed trait (with type arguments), the type, and lists each unsatisfied member with its substituted type signature.
4. WHEN a `with` block references a generic trait without type arguments, THE Compiler SHALL emit a diagnostic error indicating that type arguments are required.
5. WHEN a `with` block maps a member with a type incompatible with the substituted trait member type, THE Compiler SHALL emit a diagnostic naming the source member, the target trait member, the expected substituted type, and the actual type.
6. WHEN a trait's inheritance clause references a type that is not a trait, THE Compiler SHALL emit a diagnostic error indicating that only traits can be extended by other traits.
7. WHEN a trait inheritance chain contains a cycle, THE Compiler SHALL emit a diagnostic error naming the traits involved in the cycle.
8. WHEN a trait lists the same parent trait more than once, THE Compiler SHALL emit a diagnostic error indicating the duplicate parent.

### Requirement 18: TextMate Grammar Update for Generic Traits

**User Story:** As a Mist developer using VS Code, I want generic trait declarations to be syntax-highlighted correctly, so that I get the same visual cues as other generic type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL include `trait` in the generic declaration-names pattern alongside `class`, `struct`, and `interface`, so that `trait Comparable<T>` highlights the trait name and type parameters correctly (trait name as `entity.name.type.mist`, type parameters as `entity.name.type.parameter.mist`).
2. THE existing non-generic `trait` declaration-name pattern SHALL remain as a fallback for non-generic trait declarations (highlighting the trait name as `entity.name.type.trait.mist`).
3. WHEN a Mist source file contains a generic trait declaration (e.g., `trait Comparable<T>`), THE editor SHALL highlight the type parameter `T` distinctly from the trait name `Comparable`, consistent with how generic classes are highlighted.

### Requirement 19: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating generic traits, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/GenericTraits/Program.mist`.
2. The sample SHALL demonstrate declaring a generic trait with a single type parameter (e.g., `trait Comparable<T> { int CompareTo(T other); }`).
3. The sample SHALL demonstrate declaring a generic trait with multiple type parameters (e.g., `trait Converter<TIn, TOut> { TOut Convert(TIn input); }`).
4. The sample SHALL demonstrate a trait extending another generic trait (e.g., `trait Sortable<T> : Comparable<T> { int Priority(); }`).
5. The sample SHALL demonstrate a trait extending multiple parent traits (e.g., `trait ReadWriteStream : Readable, Writable { ... }`).
6. The sample SHALL demonstrate a type that structurally satisfies a constructed generic trait without any explicit declaration (e.g., a class with `int CompareTo(int other)` satisfying `Comparable<int>`).
7. The sample SHALL demonstrate a type satisfying a child trait and being assignable to a parent trait-typed variable.
8. The sample SHALL demonstrate a `with` block that maps a type's members to a constructed generic trait (e.g., `with Product : Comparable<int> { price as CompareTo; }` or similar).
9. The sample SHALL demonstrate passing a structurally matched type to a function that accepts a constructed trait-typed parameter (e.g., `void Sort(Comparable<int> item)`).
10. The sample SHALL demonstrate passing a `with`-mapped type to a function that accepts a constructed trait-typed parameter.
11. The sample SHALL demonstrate a generic class that satisfies a generic trait using the class's own type parameter (e.g., `class Wrapper<T>` satisfying `Comparable<T>`).
12. The sample SHALL demonstrate nested generic type arguments in trait type references (e.g., `Comparable<List<int>>`).
13. The sample SHALL demonstrate trait member access on a constructed trait-typed variable, showing that member types reflect the substituted type arguments.
14. The sample SHALL demonstrate that `Comparable<int>` and `Comparable<string>` are distinct types (commented-out error case showing invalid assignment).
15. The sample SHALL demonstrate a compile-time error case (commented out) showing a type that satisfies a child trait's own members but not the inherited parent trait members.
16. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
17. The sample SHALL include a `public static void RunDemo()` entry point that exercises all demonstrated features.
18. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 20: AllFeatures Integration

**User Story:** As a Mist project maintainer, I want the AllFeatures sample to include generic traits, so that the comprehensive sample exercises this feature as part of the full suite.

#### Acceptance Criteria

1. `samples/AllFeatures/Program.mist` SHALL call `GenericTraits.Program.RunDemo()` to include the feature in the full-suite sample run.
2. The AllFeatures sample SHALL continue to compile and run successfully after the integration.

