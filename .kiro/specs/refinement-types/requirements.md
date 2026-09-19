# Requirements Document

## Introduction

This document specifies the requirements for adding refinement types to the Mist programming language. Refinement types allow a type alias to define a subset of valid values from a base type by attaching a pattern-match predicate to the type definition. The refinement predicate uses existing pattern match syntax and becomes a compile-time constraint that the compiler enforces on all assignments to values of the refined type.

For example, `type AdultAge = int >= 18;` defines a type `AdultAge` that is a subset of `int` where only values greater than or equal to 18 are valid. The compiler rejects assignments of compile-time-known values that violate the refinement (e.g., `AdultAge y = 7;` is a compile-time error), and runtime values are validated through pattern matching (e.g., `if (userInput is AdultAge age) { ... }`).

Refinements can only be specified in type alias declarations — they cannot be inlined in field types, parameter types, or return types. A refinement type used in a parameter position (e.g., `public string GetJokeFromAge(AdultAge age)`) guarantees that the argument satisfies the constraint at the call site.

Type unions with refinements are supported: each constituent of a union can carry its own refinement predicate (e.g., `type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;`). The refinement is part of the type's definition, so the `|` union operator comes after the refinement.

**Key design decisions addressed in this spec:**
- Refinements use existing pattern match syntax (relational and logical patterns, property patterns)
- Refinements are restricted to type alias declarations only
- Refinement predicates may only reference readonly fields/properties (mutation safety)
- Compile-time enforcement for statically-known values
- Runtime validation via pattern matching (`is` expressions) for non-constant values — no special `checked` syntax
- Control-flow narrowing limited to local variables provably unmodified in scope (no closures, no mutable fields)
- Arithmetic on refinement types produces the base type (not the refined type)
- Refinement types are nominal subtypes of their base type (widening is always safe)
- Compound refinements support `and` combinators; `or` is expressed via type unions
- Assignability between refinement types requires proving subset relationships
- Nullability interaction: refinements apply to the non-nullable base type
- Generic type parameters with refinement constraints are out of scope
- Collection/array refinements are out of scope

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Grammar**: The Storm grammar definition that specifies the syntax of the Mist language (`Mist.storm` and `keywords.storm`).
- **Parser**: The component that reads Mist source tokens and produces an abstract syntax tree (AST) conforming to the Grammar.
- **Pretty_Printer**: The component (`MistPrettyPrinter`) that formats a Mist AST back into valid Mist source text.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.
- **Refinement_Type**: A type alias whose definition includes a pattern-match predicate that constrains the valid values of the base type. The predicate is part of the type's identity.
- **Refinement_Predicate**: The pattern-match expression attached to a type alias definition that specifies which values of the base type are valid for the refined type. Uses existing pattern match syntax (relational patterns, logical combinators, property patterns). When a property pattern is used, all referenced fields/properties must be readonly.
- **Base_Type**: The underlying type from which a Refinement_Type is derived (e.g., `int` is the base type of `type AdultAge = int >= 18;`).
- **Runtime_Validation**: The process of testing a runtime value against a Refinement_Predicate using pattern matching (`is` expressions). The sole mechanism for converting non-constant base-typed values into refinement-typed values.
- **Flow_Narrowing**: The process by which a conditional check that proves a refinement predicate causes the compiler to treat the checked variable as the refined type within the branch where the condition holds.
- **Type_Alias_Declaration**: A top-level or namespace-level declaration of the form `type {identifier} = {type_expression};` that introduces a named alias for a type expression, optionally with a refinement predicate.

## Requirements

### Requirement 1: Refinement Type Grammar

**User Story:** As a Mist developer, I want to attach pattern-match predicates to type alias declarations, so that I can define types that represent subsets of valid values.

#### Acceptance Criteria

1. THE Grammar SHALL extend the `TypeAliasDeclaration` rule so that the type expression can be followed by an optional Refinement_Predicate using existing pattern match syntax (relational and logical patterns).
2. WHEN the Parser encounters `type AdultAge = int >= 18;`, THE Parser SHALL produce a TypeAliasDeclaration AST node where the target type is `int` and the refinement predicate is `>= 18`.
3. WHEN the Parser encounters `type Percentage = int >= 0 and <= 100;`, THE Parser SHALL produce a TypeAliasDeclaration AST node where the target type is `int` and the refinement predicate is `>= 0 and <= 100`.
4. WHEN the Parser encounters a type alias without a refinement predicate (e.g., `type MyInt = int;`), THE Parser SHALL produce a TypeAliasDeclaration AST node with no refinement predicate, preserving backward compatibility.
5. THE Grammar SHALL support refinement predicates on each constituent of a union type, so that `type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;` parses as a union of two refined types.
6. THE Grammar SHALL give the refinement predicate higher binding than the union `|` operator, so that the predicate attaches to the immediately preceding type reference rather than the entire union.
7. THE Grammar SHALL support all pattern combinators valid in pattern match syntax within refinement predicates: relational patterns (`<`, `>`, `<=`, `>=`), logical `and`, constant patterns, and property patterns (`{ FieldName: subpattern }`).

### Requirement 2: Pretty-Printer Support for Refinement Types

**User Story:** As a Mist tooling developer, I want the pretty-printer to correctly format refinement type declarations, so that round-trip printing preserves the source structure.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a TypeAliasDeclaration with a refinement predicate, THE Pretty_Printer SHALL emit the base type followed by a space and the refinement predicate (e.g., `type AdultAge = int >= 18;`).
2. WHEN the Pretty_Printer encounters a compound refinement predicate, THE Pretty_Printer SHALL emit the combinators with appropriate spacing (e.g., `type Percentage = int >= 0 and <= 100;`).
3. WHEN the Pretty_Printer encounters a union type with refinements on each constituent, THE Pretty_Printer SHALL emit each constituent with its refinement followed by ` | ` (e.g., `type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;`).
4. FOR ALL valid Mist ASTs containing refinement type declarations, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 3: Refinement Predicate Readonly Constraint

**User Story:** As a Mist developer, I want the compiler to enforce that refinement predicates only reference readonly fields and properties, so that a value proven to satisfy a refinement cannot be silently invalidated by mutation.

#### Acceptance Criteria

1. WHEN a Refinement_Predicate uses a property pattern (e.g., `type ValidPerson = Person { Age: >= 18 };`), THE Type_Checker SHALL verify that every field or property referenced in the pattern is declared as `readonly`.
2. IF a Refinement_Predicate references a mutable field or property, THEN THE Type_Checker SHALL emit a diagnostic error of the form: "Refinement predicate on type '{TypeName}' references mutable member '{MemberName}'. All members in a refinement predicate must be readonly."
3. WHEN the Base_Type is a primitive type (int, bool, char, etc.), THE Type_Checker SHALL allow relational and constant patterns without readonly checks because primitive values are inherently immutable.
4. THE readonly constraint SHALL apply to all members referenced at any depth in nested property patterns within a refinement predicate.
5. A member is readonly for this requirement WHEN nothing can change what it answers for one instance, a value being proven once by an `is` and never checked again: a `readonly` field; or a property with no `set` accessor whose getter is auto, or reads only such members, or has no body in Mist source (intrinsic, CLR-projected) and is declared by a `readonly` concrete type. An `init` accessor is not a `set` accessor here, since nothing can call it once the value is built. `type Low = Jar { Size: < 3 }` over `int size; public int Size { get { return size; } }` is refused, as is `List<int> { Count: > 0 }`; `string { Length: > 3 }` and `Size { get; init; }` are valid. (Decided by Arthur, 2026-09-19: "If init is sound then let's do it. And the mutate case needs to be blocked.")
6. A getter that calls a member, indexes, assigns, loops, or uses a declared operator or conversion SHALL be treated as not readonly, and so SHALL a property of an abstract, `readonly` or not, until conformance holds an implementer to that modifier.
7. THE readonly constraint SHALL apply to a property pattern in a parameter's `where` as it does to one in a type alias.

### Requirement 4: Semantic Model for Refinement Types

**User Story:** As a Mist developer, I want the compiler to register refinement types in the semantic model with their predicates, so that the type system can enforce refinement constraints.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters a TypeAliasDeclaration with a Refinement_Predicate, THE Semantic_Model_Builder SHALL create a Refinement_Type representation that stores the Base_Type and the predicate.
2. THE Semantic_Model_Builder SHALL resolve the Base_Type of a Refinement_Type using the same type resolution rules as non-refined type aliases.
3. WHEN a Refinement_Type is used in a type annotation position (variable type, parameter type, return type, field type), THE Semantic_Model_Builder SHALL resolve the type reference to the Refinement_Type, not the bare Base_Type.
4. THE Semantic_Model_Builder SHALL store the Refinement_Predicate in a form that can be evaluated against compile-time constant values during type checking.
5. IF a Refinement_Predicate references operators or patterns that are not valid for the Base_Type (e.g., `>= 18` on a `string` base type), THEN THE Semantic_Model_Builder SHALL emit a diagnostic error indicating the predicate is incompatible with the base type.

### Requirement 5: Compile-Time Enforcement of Refinement Predicates

**User Story:** As a Mist developer, I want the compiler to reject assignments of compile-time-known values that violate a refinement predicate, so that I get immediate feedback on invalid values.

#### Acceptance Criteria

1. WHEN a compile-time constant value is assigned to a Refinement_Type variable and the value satisfies the Refinement_Predicate, THE Type_Checker SHALL accept the assignment without error.
2. WHEN a compile-time constant value is assigned to a Refinement_Type variable and the value violates the Refinement_Predicate, THE Type_Checker SHALL emit a diagnostic error indicating the value does not satisfy the refinement constraint.
3. WHEN a Refinement_Type is used as a method parameter type and a compile-time constant argument violates the predicate, THE Type_Checker SHALL emit a diagnostic error at the call site.
4. THE Type_Checker SHALL evaluate refinement predicates against integer literals, character literals, and boolean literals.
5. WHEN a variable of the same Refinement_Type is assigned to another variable of the same Refinement_Type, THE Type_Checker SHALL accept the assignment without re-checking the predicate (the source type already guarantees compliance).

### Requirement 6: Runtime Validation via Pattern Matching

**User Story:** As a Mist developer, I want to validate runtime values against refinement types using pattern matching and local variables, so that I can safely convert unverified values into refined types without special syntax.

#### Acceptance Criteria

1. WHEN a non-constant expression of the Base_Type is assigned directly to a Refinement_Type variable, THE Type_Checker SHALL emit a diagnostic error indicating that the value cannot be statically proven to satisfy the refinement.
2. WHEN a developer uses an `is` expression to test a base-typed value against a Refinement_Type (e.g., `if (userInput is AdultAge validAge)`), THE Type_Checker SHALL introduce the pattern variable with the Refinement_Type within the then-branch.
3. THE pattern matching mechanism SHALL evaluate the Refinement_Predicate at runtime against the tested value and produce `true` when the value satisfies the predicate, `false` otherwise.
4. WHEN a base-typed value is tested against a Refinement_Type via `is` and the match succeeds, THE pattern variable SHALL hold the same value with the Refinement_Type, allowing assignment to other Refinement_Type variables without further checks.
5. THE developer SHALL use pattern matching as the sole mechanism for converting runtime values to refinement types — no special `checked` keyword or conversion syntax is required.
6. A cast SHALL NOT be a way into a Refinement_Type. A cast is a call to a conversion function (cast-operators Requirement 8), and its operand meets a refinement as an argument does: WHEN a cast's target type is a Refinement_Type or a union with Refinement_Type constituents, or the cast calls a declared cast operator whose parameter is a Refinement_Type, THE Type_Checker SHALL require the operand to satisfy the Refinement_Predicate as it requires of a value assigned to that type -- a constant by evaluation (Requirement 5), a refinement by implication (Requirement 9), a local by the narrowing in force (Requirement 7) -- and SHALL otherwise report the error of Requirement 5.2 or 6.1 at the cast. So `(AdultAge)10`, `(AdultAge)n` for an unproven `int n`, and `(SmallAdult)5` over `byte >= 18` are refused, while `(AdultAge)21`, `(AdultAge)senior` over `int >= 65`, and `(AdultAge)m` inside `if (m >= 18)` are valid. The cast emits no runtime check. A cast from a Refinement_Type to its Base_Type owes nothing (Requirement 8.3), and a cast that has no conversion at all is refused by the cast rule alone.

### Requirement 7: Flow-Sensitive Narrowing to Refinement Types

**User Story:** As a Mist developer, I want the compiler to narrow a base-typed variable to a refinement type when a condition proves the refinement predicate, so that I can avoid explicit pattern matching when the proof is in the control flow.

#### Acceptance Criteria

1. WHEN an `if` condition contains a comparison that proves a Refinement_Predicate for a local variable (e.g., `if (x >= 18)` where `AdultAge = int >= 18`), THE Type_Checker SHALL narrow the variable's type to the Refinement_Type within the `then` branch.
2. WHEN a variable is narrowed to a Refinement_Type via flow analysis, THE Type_Checker SHALL allow assignment of that variable to a Refinement_Type target without requiring pattern matching validation.
3. THE Type_Checker SHALL only perform narrowing when the condition exactly matches or logically implies the Refinement_Predicate of a refinement type in scope.
4. WHEN the narrowed variable is reassigned within the branch, THE Type_Checker SHALL invalidate the narrowing for subsequent statements in that branch.
5. THE Type_Checker SHALL support narrowing through `is` pattern expressions (e.g., `if (x is >= 18)` narrows `x` to `AdultAge` within the then-branch).
6. THE Type_Checker SHALL only perform flow narrowing on variables that are provably unmodified for the duration of the narrowed scope. For local variables of primitive types, this means no reassignment occurs between the condition and the usage.
7. THE Type_Checker SHALL NOT perform flow narrowing on variables that are captured by closures, because concurrent modification cannot be ruled out.
8. THE Type_Checker SHALL NOT perform flow narrowing when the refinement predicate involves a pattern that is not valid as a refinement (i.e., the pattern references mutable fields or properties). Only patterns that interact exclusively with readonly members or primitive values are eligible for narrowing.

### Requirement 8: Arithmetic and Operations on Refinement Types

**User Story:** As a Mist developer, I want clear rules about what type arithmetic operations produce when applied to refinement types, so that I understand when refinements are preserved or lost.

#### Acceptance Criteria

1. WHEN an arithmetic operation is performed on a Refinement_Type value (e.g., `adultAge + 1`), THE Type_Checker SHALL produce a result of the Base_Type, not the Refinement_Type.
2. WHEN a comparison operation is performed on a Refinement_Type value, THE Type_Checker SHALL use the Base_Type's comparison semantics.
3. A Refinement_Type SHALL be a nominal subtype of its Base_Type. WHEN a Refinement_Type value is passed to a method expecting the Base_Type, THE Type_Checker SHALL accept the assignment (widening from refinement to base is always safe).
4. WHEN a method returns a Refinement_Type, THE Type_Checker SHALL enforce the refinement constraint on the return value using the same rules as variable assignment (compile-time check for literals, pattern matching required for non-constant expressions).

### Requirement 9: Assignability Between Refinement Types

**User Story:** As a Mist developer, I want the compiler to allow assignment between refinement types when one is provably a subset of another, so that I can use more-constrained types where less-constrained types are expected.

#### Acceptance Criteria

1. WHEN a Refinement_Type value is assigned to a variable of a different Refinement_Type with the same Base_Type, THE Type_Checker SHALL accept the assignment only if the source refinement is provably a subset of the target refinement.
2. THE Type_Checker SHALL prove subset relationships for simple relational predicates (e.g., `int >= 21` is a subset of `int >= 18`).
3. WHEN the Type_Checker cannot prove a subset relationship between two refinement predicates, THE Type_Checker SHALL emit a diagnostic error indicating that the assignment is not provably safe and pattern matching should be used to validate at runtime.
4. WHEN a Refinement_Type value is assigned to a variable of the Base_Type (no refinement), THE Type_Checker SHALL accept the assignment (widening is always safe).
5. THE Type_Checker SHALL prove subset relationships for compound predicates using logical implication (e.g., `int >= 5 and <= 10` is a subset of `int >= 0 and <= 100`).

### Requirement 10: Refinement Types in Union Type Contexts

**User Story:** As a Mist developer, I want to define union types where each constituent has its own refinement predicate, so that I can express complex value constraints across multiple base types.

#### Acceptance Criteria

1. WHEN a type alias defines a union with refinements on each constituent (e.g., `type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;`), THE Semantic_Model_Builder SHALL create a UnionType where each constituent is a Refinement_Type.
2. WHEN a value is assigned to a union-of-refinements type, THE Type_Checker SHALL verify that the value satisfies at least one constituent's base type and refinement predicate.
3. WHEN pattern matching is performed on a union-of-refinements type, THE Type_Checker SHALL narrow to the specific Refinement_Type constituent that matches.
4. THE Type_Checker SHALL apply the same compile-time enforcement rules to each constituent of a union-of-refinements as it does to standalone refinement types.

### Requirement 11: Error Diagnostics for Refinement Types

**User Story:** As a Mist developer, I want clear error messages when I violate refinement constraints, so that I can quickly identify the constraint, the violating value, and the type definition location.

#### Acceptance Criteria

1. WHEN a compile-time constant violates a Refinement_Predicate, THE Compiler SHALL emit a diagnostic error of the form: "Value '{Value}' does not satisfy refinement '{Predicate}' on type '{TypeName}'. The type is defined at {Location}."
2. WHEN a non-constant expression is assigned directly to a Refinement_Type without narrowing through pattern matching, THE Compiler SHALL emit a diagnostic error of the form: "Cannot assign '{ExpressionType}' to '{RefinementTypeName}' without proving the refinement. Use pattern matching (e.g., 'if (value is {RefinementTypeName} name)') to validate at runtime."
3. WHEN a Refinement_Predicate is incompatible with the Base_Type, THE Compiler SHALL emit a diagnostic error of the form: "Refinement predicate '{Predicate}' is not valid for base type '{BaseType}'. Relational patterns require a comparable type."
4. WHEN a Refinement_Predicate references a mutable field or property, THE Compiler SHALL emit a diagnostic error of the form: "Refinement predicate on type '{TypeName}' references mutable member '{MemberName}'. All members in a refinement predicate must be readonly."
5. WHEN the Type_Checker cannot prove a subset relationship between two refinement types, THE Compiler SHALL emit a diagnostic error of the form: "Cannot assign '{SourceType}' to '{TargetType}' because the compiler cannot prove that '{SourcePredicate}' implies '{TargetPredicate}'. Use pattern matching to validate at runtime."

### Requirement 12: C# Emission for Refinement Types

**User Story:** As a Mist developer, I want refinement types to be correctly transpiled to C#, so that the compiled output preserves refinement semantics at runtime.

#### Acceptance Criteria

1. THE Emitter SHALL emit variables of a Refinement_Type using the C# representation of the Base_Type (e.g., `AdultAge` becomes `int` in C#).
2. THE Emitter SHALL emit pattern matching against a Refinement_Type as a C# runtime predicate evaluation (e.g., `x is AdultAge age` becomes a conditional check of the predicate with a variable binding).
3. THE Emitter SHALL emit compile-time-verified assignments as direct assignments to the base type (no runtime check needed since the compiler already proved compliance).
4. WHEN a Refinement_Type is used as a method parameter, THE Emitter SHALL emit the parameter as the Base_Type in C# and emit a runtime validation guard at the method entry point for arguments whose compliance was not statically proven at the call site.
5. THE Emitter SHALL produce C# code that compiles without errors for all valid refinement type usages.

### Requirement 13: Compile-Time Evaluation Limits

**User Story:** As a Mist developer, I want to understand what the compiler will and will not attempt to prove about refinement predicates, so that I know when to expect compile-time errors versus when I need checked conversions.

#### Acceptance Criteria

1. THE Type_Checker SHALL evaluate refinement predicates against integer literals, boolean literals, and character literals.
2. THE Type_Checker SHALL prove simple relational subset relationships (e.g., `>= 21` implies `>= 18`, `<= 5` implies `<= 10`).
3. THE Type_Checker SHALL prove compound subset relationships using logical implication for `and` combinators (e.g., `>= 5 and <= 10` implies `>= 0 and <= 100`).
4. THE Type_Checker SHALL NOT attempt to prove relationships involving arithmetic expressions (e.g., the compiler will not prove that `x + 1 >= 18` when `x >= 17`).
5. THE Type_Checker SHALL NOT read a member's body where the member is used. What a member's value admits reaches the use site through the member's type alone, declared or lifted from its own returns by Requirement 18, so `ledger.Count` over `public int Count { get { return count; } }` with a mutable `int count` is an unknown int wherever it is read.
6. WHEN the Type_Checker cannot statically determine whether a value satisfies a refinement, THE Type_Checker SHALL require pattern matching validation rather than silently accepting or rejecting the assignment.
7. A predicate the Type_Checker has no way to evaluate at compile time -- a property pattern, a regex, a comparison over a real, a string constant -- SHALL be met only by a value whose type is already that refinement: `Small s = new Box(1)` over `type Small = Box { Size: < 3 }` is refused as unproven, `if (box is Small small) { Small t = small; }` is valid. A predicate refused at its own declaration owes nothing further at its uses. (Decided by Arthur, 2026-09-19.)
8. WHEN a predicate's bound names a property or a field of the declaring type -- `where entry >= 0 and < Count`, `< this.Count`, `< size` -- THE Type_Checker SHALL read that member at each use through its type or its lifted return type (Requirement 18), never through the flow, which does not cross a member (13.5). Over `public int Count { get { return 3; } }`, `ledger[1]` is valid and `ledger[7]` violates the bound; over a Count that returns 3 or 5, `ledger[4]` is unproven rather than violating; over a Count or a field whose type states no value, every use is unproven. (Decided by Arthur, 2026-09-19.)

### Requirement 14: Pattern Match Integration

**User Story:** As a Mist developer, I want to use refinement types in pattern matching contexts, so that I can branch on whether a value satisfies a refinement predicate.

#### Acceptance Criteria

1. WHEN an `is` expression tests a value against a Refinement_Type (e.g., `x is AdultAge`), THE Type_Checker SHALL evaluate whether the value satisfies the Refinement_Predicate.
2. WHEN a type pattern in a match expression uses a Refinement_Type, THE Type_Checker SHALL narrow the matched variable to the Refinement_Type within the match arm.
3. WHEN a value is matched against a Refinement_Type in a switch expression, THE Emitter SHALL emit runtime predicate evaluation code for the match arm guard.
4. THE Type_Checker SHALL allow Refinement_Types as valid type patterns in `is` expressions and switch arms.

### Requirement 15: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want refinement type declarations to be syntax-highlighted correctly, so that the refinement predicate is visually distinct from the base type.

#### Acceptance Criteria

1. THE TextMate grammar SHALL continue to recognize `type` as a declaration keyword (scope `keyword.declaration.mist`) for refinement type alias declarations.
2. THE TextMate grammar SHALL highlight the identifier following `type` as a type name (scope `entity.name.type.alias.mist`).
3. THE TextMate grammar SHALL highlight relational operators within type alias refinement predicates with the scope `keyword.operator.relational.mist`.
4. THE TextMate grammar SHALL highlight `and` within type alias refinement predicates with the scope `keyword.operator.logical.mist`.

### Requirement 16: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the CSHARP_FEATURES.md document updated to reflect the newly implemented refinement types feature, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN the refinement types feature is implemented, THE Compiler team SHALL update CSHARP_FEATURES.md to add a `Refinement Types` entry marking it as implemented.
2. THE Compiler team SHALL update the `Summary` table at the bottom of CSHARP_FEATURES.md to reflect the new implementation counts.

### Requirement 17: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating refinement types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/RefinementTypes/Program.mist`.
2. The sample SHALL demonstrate a simple refinement type with a single relational predicate (e.g., `type AdultAge = int >= 18;`).
3. The sample SHALL demonstrate a compound refinement type with `and` combinators (e.g., `type Percentage = int >= 0 and <= 100;`).
4. The sample SHALL demonstrate a union type with refinements on each constituent (e.g., `type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;`).
5. The sample SHALL demonstrate a refinement type using a property pattern on a type with readonly fields (e.g., `type ValidRange = Range { Start: >= 0 };`).
6. The sample SHALL demonstrate compile-time accepted assignments (literal values satisfying the predicate).
7. The sample SHALL demonstrate runtime validation via pattern matching (e.g., `if (userInput is AdultAge age) { ... }`).
8. The sample SHALL demonstrate a refinement type used as a method parameter type.
9. The sample SHALL demonstrate a refinement type used as a method return type.
10. The sample SHALL demonstrate a refinement type used as a field type.
11. The sample SHALL demonstrate flow-sensitive narrowing from a condition to a refinement type (e.g., `if (x >= 18)` allowing `x` to be used as `AdultAge` in the then-branch).
12. The sample SHALL demonstrate pattern matching against a refinement type using `is`.
13. The sample SHALL demonstrate arithmetic on a refinement type producing the base type.
14. The sample SHALL demonstrate assignability between refinement types (subset relationship).
15. The sample SHALL demonstrate widening from a refinement type to its base type.
16. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
17. The sample SHALL include a `public static void RunDemo()` entry point.
18. `samples/AllFeatures/Program.mist` SHALL call `RefinementTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
19. `samples/AllFeatures/mist.toml` SHALL be updated to add a `[[references]]` entry pointing to the RefinementTypes sample project.
20. The sample SHALL compile and run without errors using the Mist compiler.

### Requirement 18: A Member's Return Type Lifted to the Refinement Its Returns Prove

**User Story:** As a Mist developer, I want a member that always returns a known value, or a value of a refined type, to be read by its callers as returning that refinement, so that a declared `int` still lets the compiler prove what the member produces, and a later change to the body is caught at every use it breaks.

The lift is inference, as readonly inference is (readonly-contexts Requirement 10): the member proves its own signature from its body, and a use site reads the signature and never the body (Requirement 13.5). It is sound because Mist has no overriding, so a member's body is the only one a use can reach, and because every build recompiles the whole program, so no use can hold a lift that is out of date.

#### Acceptance Criteria

1. WHEN a method, a property getter or an indexer getter has a Mist body AND every `return` in it that carries a value returns a constant or a pass-through, THE Type_Checker SHALL read a value the member produces as the union of what those returns admit, intersected with the declared return type's own refinement. `int Count { get { return 9; } }` produces `int 9`, and `int Pick(Percentage p) { return p; }` produces `int >= 0 and <= 100`.
2. A constant is a value Requirement 5.4 evaluates: an integer, character or boolean literal, or its negation. A value whose type IS one value counts as that constant, so `return entries.Length;` over a field of type `int[3]` returns 3.
3. A pass-through is a value whose type is refined -- a Refinement_Type, a parameter's `where`, or a member's own lifted return type -- and admits what that refinement admits. A built-in implicit conversion between integral types preserves the value and is looked through.
4. A `return` inside a lambda or a local function belongs to that function, and SHALL NOT take part in the enclosing member's lift.
5. WHEN any `return` in the body is neither a constant nor a pass-through -- a local the flow has narrowed, an arithmetic result, a call to a member that does not lift -- THE Type_Checker SHALL read the member at its declared return type. A member with no Mist body (abstract, interface, intrinsic, CLR-projected), an async member, and a member whose declared return type is not integral, character or boolean SHALL likewise lift nothing.
6. WHEN computing a member's lift reaches the same member again, THE Type_Checker SHALL read that inner use at the declared return type, so that recursion settles rather than assuming its own answer.
7. The lift SHALL NOT be written into the program: emission and every rule other than refinement proving read the declared return type.
8. WHEN a change to a member's body changes its lift, THE Type_Checker SHALL judge each use of the member against the new lift, reporting at the use as it would for a declared refinement -- `ledger[^1]` becomes an error at `ledger[^1]` when `Count` starts returning 9 into an indexer whose parameter admits `< 3`.
