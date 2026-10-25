# Requirements Document

## Introduction

This document specifies the requirements for replacing the current write-reachability-based member filtering on `Closed_Generic_With_Unknown` types with a readonly-context-based approach. The current system (Requirement 9 and 10 of the `unknown-type` spec) uses a `ReachabilityAnalyzer` to classify each member of a generic type as `WritesT` or `DoesNotWriteT`, then hides `WritesT` members when the type is closed with `unknown`. This works but introduces a parallel analysis system that duplicates concerns already handled by the readonly feature.

The new approach leverages the existing readonly infrastructure: when a generic type is closed with `unknown` at one or more type argument positions, the variable's type is treated as readonly by the Readonly_Analyzer. This means only readonly-qualified members (explicit or inferred) are callable on that variable. The variable itself remains reassignable — it is the type context that becomes readonly, not the variable binding.

This change removes the `ReachabilityAnalyzer`, the `IReachabilityMap` contract, and the member-filtering logic in the Type_Checker that consults it, replacing them with a single rule: "a variable whose static type is a Closed_Generic_With_Unknown is treated as though its type were readonly for the purposes of the Readonly_Analyzer."

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Readonly_Analyzer**: The analysis component (`ReadonlyAnalyzer`) that enforces readonly context restrictions and reports violations.
- **Readonly_Inference_Engine**: The compiler component that performs local analysis to determine which members implicitly satisfy readonly constraints.
- **Readonly_Site**: Any location (field, property, method, constructor, block) that is either explicitly marked readonly or inferred to be readonly.
- **Closed_Generic_With_Unknown**: A Constructed_Type in which at least one type argument is `unknown` (directly or transitively). For example, `List<unknown>`, `Map<string, unknown>`, `Optional<List<unknown>>`.
- **Readonly_Type_Context**: The semantic state applied to a variable whose static type is a Closed_Generic_With_Unknown. The variable itself is not readonly (it can be reassigned), but any member access through the variable is subject to readonly context rules — only readonly-qualified members are callable.
- **ReachabilityAnalyzer**: The existing component (`ReachabilityAnalyzer.cs`) that classifies members as `WritesT` or `DoesNotWriteT`. This component is being removed by this feature.
- **IReachabilityMap**: The existing contract (`IReachabilityMap.cs`) consulted by the Type_Checker for member filtering. This contract is being removed by this feature.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST in multiple passes.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist AST into C# source files.

## Requirements

### Requirement 1: Remove the ReachabilityAnalyzer

**User Story:** As a Mist compiler contributor, I want the write-reachability analysis system removed, so that the codebase no longer maintains a parallel mechanism for controlling member access on unknown-closed generics.

#### Acceptance Criteria

1. THE Compiler source SHALL remove the `ReachabilityAnalyzer` class (`Source/Mist/Semantics/Model/ReachabilityAnalyzer.cs`).
2. THE Compiler source SHALL remove the `IReachabilityMap` interface, the `MemberClassification` enum, and the `PropertyClassification` record struct from `Source/Mist/Semantics/Contracts/IReachabilityMap.cs`, deleting the file entirely.
3. THE Compiler source SHALL remove the `ReachabilityMap` property from both the `ISemanticModel` interface (`Source/Mist/Semantics/Contracts/ISemanticModel.cs`) and the `SemanticModel` class, including the corresponding constructor parameter.
4. THE Semantic_Model_Builder SHALL no longer invoke the `ReachabilityAnalyzer.Analyze` step during model construction, and SHALL not pass a reachability map to the `SemanticModel` constructor.
5. THE Type_Checker SHALL remove the member-filtering logic that consults the `IReachabilityMap` on Closed_Generic_With_Unknown receivers, while preserving any other uses of the `MIST_TC_0062 (historical)` diagnostic ID (such as tuple element type mismatch in deconstruction).
6. WHEN all ReachabilityAnalyzer-related source removals are applied, THE Compiler SHALL build successfully with zero errors and zero warnings.
7. WHEN all ReachabilityAnalyzer-related source removals are applied, THE Compiler source SHALL remove the following test classes: `ReachabilityAnalyzerFieldPropertyIndexerTests`, `ReachabilityAnalyzerMethodConstructorTests`, `ReachabilityAnalyzerFixedPointPropertyTests`, `ReachabilityMapIntegrationTests`, and `UnknownMemberFilteringTests`. Tests that validated reachability-based member filtering SHALL be replaced by tests validating the readonly-based approach defined in Requirement 2 of this spec.

### Requirement 2: Readonly Type Context for Closed_Generic_With_Unknown

**User Story:** As a Mist developer, I want the compiler to treat any variable whose type is a Closed_Generic_With_Unknown as having a readonly type context, so that only readonly-qualified members are callable and the language uses a single unified mechanism for controlling mutation.

#### Acceptance Criteria

1. WHEN a variable's static type is a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL treat the type as though it were readonly — all existing readonly restrictions (as defined in the readonly-contexts specification) apply to member access on that variable with no exceptions and no special-case logic.
2. THE Readonly_Analyzer SHALL determine the readonly type context by inspecting the variable's resolved type: if the type is a Constructed_Type and at least one type argument is `unknown` (directly or transitively through nested type arguments), the readonly type context applies.
3. WHEN a Closed_Generic_With_Unknown is nested inside another generic (e.g., `Optional<List<unknown>>`), THE Readonly_Analyzer SHALL apply the readonly type context to the outermost variable if any type argument at any depth is `unknown`.
4. THE readonly type context is a local treatment of the type at the use site — it does not modify the type definition, does not propagate to other variables of different types, and does not affect the variable's reassignability (see Requirement 3).

### Requirement 3: Variable Reassignment Remains Permitted

**User Story:** As a Mist developer, I want to reassign a variable of type `List<unknown>` to a different list, so that the readonly restriction applies to the type context (member access) and not to the variable binding itself.

#### Acceptance Criteria

1. WHEN a variable's static type is a Closed_Generic_With_Unknown, THE Type_Checker SHALL permit reassignment of the variable to a new value whose type is assignable to the variable's declared type under Covariant_Unknown_Widening (Requirement 8) or exact type match.
2. WHEN a variable `myList` is declared as `List<unknown> myList = new List<int>()`, THE Type_Checker SHALL accept `myList = new List<string>()` as a valid reassignment via Covariant_Unknown_Widening.
3. THE Type_Checker SHALL NOT treat a variable whose static type is a Closed_Generic_With_Unknown as readonly for the purposes of reassignment — only member access through the variable is subject to the readonly type context rules defined in Requirement 2.
4. WHEN a variable's static type is a Closed_Generic_With_Unknown and the variable is passed as an argument to a method whose corresponding parameter type is the same Closed_Generic_With_Unknown or a wider type under Covariant_Unknown_Widening, THE Type_Checker SHALL permit the argument pass.
5. IF a reassignment is attempted on a variable whose static type is a Closed_Generic_With_Unknown and the source value's type is not assignable to the variable's declared type under Covariant_Unknown_Widening or exact type match, THEN THE Type_Checker SHALL emit a diagnostic error indicating the type mismatch.

### Requirement 4: Diagnostic Messages for Readonly Type Context Violations

**User Story:** As a Mist developer, I want clear error messages when I attempt to call a non-readonly member on a Closed_Generic_With_Unknown variable, so that I understand the restriction comes from the unknown type argument making the type context readonly.

#### Acceptance Criteria

1. WHEN a non-readonly method is called on a variable whose type is a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL emit a diagnostic that includes the method name, the variable's resolved type display name, and a clause stating that the type is treated as readonly because it contains an `unknown` type argument.
2. WHEN a non-readonly property getter is called on a variable whose type is a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL emit a diagnostic that includes the property name, the variable's resolved type display name, and a clause stating that the type is treated as readonly because it contains an `unknown` type argument.
3. WHEN a property setter is called on a variable whose type is a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL emit a diagnostic that includes the property name, the variable's resolved type display name, and a clause stating that the type is treated as readonly because it contains an `unknown` type argument.
4. THE diagnostic messages for readonly type context violations on Closed_Generic_With_Unknown SHALL use a diagnostic code from the `MIST_TC (historical)` family (specifically `MIST_TC_0062 (historical)`) rather than the `MIST_RO (historical)` family used by general readonly context violations, so that the developer can distinguish restrictions originating from an `unknown` type argument from those originating from an explicit `readonly` annotation.
5. THE diagnostic code for readonly type context violations on Closed_Generic_With_Unknown SHALL be `MIST_TC_0062 (historical)`, replacing the previous reachability-based member-filtering diagnostic that used the same code, so that tooling referencing `MIST_TC_0062 (historical)` continues to function without modification.
6. WHEN a non-readonly method, non-readonly property getter, or property setter is called on a variable whose type is NOT a Closed_Generic_With_Unknown but IS in a general readonly context (e.g., inside a readonly method body or on a readonly parameter), THE Readonly_Analyzer SHALL emit the existing `MIST_RO (historical)` family diagnostics rather than the `MIST_TC_0062 (historical)` diagnostic.

### Requirement 5: Interaction with Readonly Inference

**User Story:** As a Mist developer, I want the readonly inference engine to correctly classify members of generic types so that the right set of members is available on Closed_Generic_With_Unknown variables without requiring explicit `readonly` annotations on every member.

#### Acceptance Criteria

1. THE Readonly_Inference_Engine SHALL classify members of generic types using the same local analysis rules that apply to non-generic types (Requirement 10 of the readonly-contexts spec).
2. IF a method of a generic type `Foo<T>` satisfies the readonly inference rules (its body only accesses readonly sites per Requirement 10 of the readonly-contexts spec), THEN THE Readonly_Inference_Engine SHALL classify that method as implicitly readonly, making it callable on `Foo<unknown>` variables.
3. IF a method of a generic type `Foo<T>` assigns to a field of type `T`, calls a non-readonly method, or invokes a method on a value of type `T` whose readonly status cannot be statically determined, THEN THE Readonly_Inference_Engine SHALL classify that method as non-readonly, making it uncallable on `Foo<unknown>` variables.
4. IF a property getter of a generic type satisfies the readonly inference rules (its body only accesses readonly sites per Requirement 10 of the readonly-contexts spec), THEN THE Readonly_Inference_Engine SHALL classify that getter as implicitly readonly, making it accessible on Closed_Generic_With_Unknown variables.
5. IF a property setter or indexer setter of a generic type assigns to a field of type `T` or performs any non-readonly operation, THEN THE Readonly_Inference_Engine SHALL classify that setter as non-readonly, making it inaccessible on Closed_Generic_With_Unknown variables.
6. THE Readonly_Inference_Engine SHALL NOT require special-case logic for generic types — the standard inference rules naturally produce the correct classifications because methods that write to T-typed storage will fail the readonly inference check (they assign to fields).

### Requirement 6: Interaction with Pattern Narrowing

**User Story:** As a Mist developer, I want pattern narrowing from a Closed_Generic_With_Unknown to a concrete type to remove the readonly type context, so that I regain full member access after narrowing.

#### Acceptance Criteria

1. WHEN a variable of type `List<unknown>` is narrowed via pattern matching to a fully-concrete constructed type (e.g., `List<int>`), THE Readonly_Analyzer SHALL NOT apply the readonly type context to the narrowed binding, because the narrowed type is not a Closed_Generic_With_Unknown.
2. WHEN a variable of type `unknown` is narrowed via pattern matching to a type `T` that is not a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL NOT apply the readonly type context to the narrowed binding.
3. THE existing rejection of narrowing to `unknown` (Requirement 11.3 of the unknown-type spec) and narrowing to a Closed_Generic_With_Unknown (Requirement 11.4 of the unknown-type spec) SHALL remain in effect, which means the case where `T` itself contains `unknown` type arguments cannot arise as a valid narrowing target.
4. THE Readonly_Analyzer SHALL determine whether the readonly type context applies to a narrowed binding by inspecting the narrowed binding's resolved type (not the original variable's type): if the narrowed type is not a Closed_Generic_With_Unknown, no readonly type context is applied.

### Requirement 7: Interaction with Covariant Unknown Widening

**User Story:** As a Mist developer, I want the readonly type context to apply consistently after covariant widening, so that assigning `List<int>` to `List<unknown>` produces a variable with readonly type context.

#### Acceptance Criteria

1. WHEN a value of type `Foo<A>` is assigned to a variable of type `Foo<unknown>` via Covariant_Unknown_Widening, THE Readonly_Analyzer SHALL apply the readonly type context to the target variable, regardless of how many type argument positions are widened to `unknown`.
2. WHEN a value of type `Foo<unknown>` is passed as an argument to a parameter of type `Foo<unknown>`, THE Readonly_Analyzer SHALL apply the readonly type context within the callee's scope for that parameter, consistent with Requirement 9 criterion 3.
3. WHEN a method's declared return type is a Closed_Generic_With_Unknown (e.g., `Foo<unknown>`), THE Readonly_Analyzer SHALL apply the readonly type context to any variable that receives the return value and to any member access expression applied directly to the return expression without an intermediate variable (e.g., `GetList().Count`).
4. WHEN a generic method's type parameter is inferred as `unknown` at the call site (e.g., `Method<unknown>(...)` or inference resolves `T` to `unknown`), THE Readonly_Analyzer SHALL apply the readonly type context to any variable or expression whose resolved type is a Closed_Generic_With_Unknown as a result of that inference.

### Requirement 8: Open Generic Types Are Not Affected

**User Story:** As a Mist developer, I want the body of a generic type `List<T>` to have full mutable access to its own members, so that the readonly type context only applies to closed types with unknown arguments and not to the generic definition itself.

#### Acceptance Criteria

1. WHILE inside the body of a generic type `Foo<T>`, THE Readonly_Analyzer SHALL NOT apply the Readonly_Type_Context to `this` or to any member access on `this`, because `this` has the open type `Foo<T>` where `T` is an unsubstituted type parameter and therefore is not a Closed_Generic_With_Unknown.
2. WHILE inside the body of a generic method `Bar<T>`, THE Readonly_Analyzer SHALL NOT apply the Readonly_Type_Context to local variables whose declared type is `T`, or to local variables whose declared type is a Constructed_Type where every occurrence of `unknown` is absent and at least one type argument is the open type parameter `T` (e.g., `List<T>`, `Map<T, int>`, `Optional<T>`), because a type containing an open type parameter is not a Closed_Generic_With_Unknown.
3. THE Readonly_Analyzer SHALL only apply the Readonly_Type_Context when the resolved type of a variable or expression is a Closed_Generic_With_Unknown — that is, a Constructed_Type in which at least one type argument is `unknown` (directly or transitively) and no type argument is an unsubstituted type parameter.
4. WHILE inside the body of a generic type `Foo<T>`, IF a local variable is declared with a type that is independently a Closed_Generic_With_Unknown (e.g., `Map<string, unknown> m`), THEN THE Readonly_Analyzer SHALL apply the Readonly_Type_Context to that local variable, because the Closed_Generic_With_Unknown status is determined by the variable's own resolved type, not by the enclosing generic context.
5. WHILE inside the body of a generic type `Foo<T>`, IF a local variable is declared with a partially-open Constructed_Type that also contains `unknown` (e.g., `Map<T, unknown>`), THEN THE Readonly_Analyzer SHALL NOT apply the Readonly_Type_Context to that variable, because the presence of the open type parameter `T` means the type is not fully closed and therefore is not a Closed_Generic_With_Unknown.

### Requirement 9: Interaction with Readonly Parameters

**User Story:** As a Mist developer, I want to pass a Closed_Generic_With_Unknown value to a method with a readonly parameter, so that the callee can safely access readonly members without violating the type context.

#### Acceptance Criteria

1. WHEN a value whose type is a Closed_Generic_With_Unknown is passed to a method parameter marked `readonly`, THE Type_Checker SHALL accept the call without error.
2. WHEN a value whose type is a Closed_Generic_With_Unknown is passed to a method parameter NOT marked `readonly`, THE Type_Checker SHALL accept the call without error (the readonly type context restricts what the callee can do with the value through the Readonly_Analyzer, not whether the value can be passed).
3. WHILE inside the callee's body, THE Readonly_Analyzer SHALL apply the readonly type context (Requirement 2, criteria 2–6) to any parameter whose resolved type is a Closed_Generic_With_Unknown, regardless of whether the parameter is explicitly marked `readonly`.
4. WHEN a parameter is both explicitly marked `readonly` AND its resolved type is a Closed_Generic_With_Unknown, THE Readonly_Analyzer SHALL enforce both restrictions (the explicit `readonly` parameter rules from the readonly-contexts spec Requirement 14 and the readonly type context rules from Requirement 2 of this spec apply jointly; in practice the restrictions are equivalent, so no additional diagnostic is emitted beyond what either mechanism alone would produce).

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating the readonly type context for unknown-closed generics, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/UnknownReadonlySemantics/Program.mist` with an accompanying `samples/UnknownReadonlySemantics/mist.toml` declaring the project name as `UnknownReadonlySemantics`.
2. The sample SHALL demonstrate each of the following patterns, and no pattern may be omitted: (a) declaring a `List<unknown>` variable and calling readonly-accessible members on it (e.g., a property getter or method that does not write to `T`-typed storage), (b) reassigning the `List<unknown>` variable to a different concrete list instance via covariant widening, (c) pattern narrowing from `List<unknown>` to `List<int>` and invoking a write-path member (e.g., `Add`) that was previously inaccessible, (d) covariant widening from `List<int>` to `List<unknown>`, (e) passing a closed generic with unknown (e.g., `List<unknown>`) to a method accepting a readonly parameter and calling readonly-accessible members within that method, and (f) demonstrating that an open generic type body (a method with its own type parameter `T`) retains full read and write access to `T`-typed members.
3. Each demonstrated pattern SHALL print a section header line to stdout identifying the pattern (e.g., "--- Readonly member access on List<unknown> ---") followed by one or more value-output lines showing the result, so that a tester can verify each section produced expected values.
4. `samples/AllFeatures/Program.mist` SHALL call `UnknownReadonlySemantics.Program.RunDemo()`, and `samples/AllFeatures/mist.toml` SHALL include a `[[references]]` entry with `path = "../UnknownReadonlySemantics"`.
5. The sample SHALL compile and run with zero errors and zero warnings using the Mist compiler, and the process SHALL exit with code 0.

### Requirement 11: Backward Compatibility

**User Story:** As a Mist developer, I want the new readonly-based approach to accept the same set of valid programs and reject the same set of invalid programs as the old reachability-based approach, so that existing code continues to work without changes.

#### Acceptance Criteria

1. FOR EACH member of a generic type that the ReachabilityAnalyzer classified as `DoesNotWriteT` with respect to a type parameter T, THE Readonly_Inference_Engine SHALL classify that member as readonly (either `ExplicitReadonly` or `InferredReadonly`), so that the member remains accessible when the type is closed with `unknown`.
2. FOR EACH member of a generic type that the ReachabilityAnalyzer classified as `WritesT` with respect to a type parameter T, THE Readonly_Inference_Engine SHALL classify that member as non-readonly (`Mutable`), so that the member remains inaccessible when the type is closed with `unknown`.
3. THE Compiler's existing test suite for member filtering on Closed_Generic_With_Unknown (tests that validated `MIST_TC_0062 (historical)` diagnostics and accessible-member assertions) SHALL continue to produce identical pass/fail results after the transition to the readonly-based approach, with only the diagnostic code and message text updated to reflect the new readonly type context mechanism.
4. IF any member of a standard library generic type was classified `DoesNotWriteT` by the ReachabilityAnalyzer but is NOT inferred as readonly by the Readonly_Inference_Engine, THEN THE Compiler team SHALL resolve the discrepancy by either adjusting the inference rules or adding an explicit `readonly` annotation to that member, and SHALL document the resolution in the commit that ships the change.
5. IF any member of a standard library generic type was classified `WritesT` by the ReachabilityAnalyzer but IS inferred as readonly by the Readonly_Inference_Engine, THEN THE Compiler team SHALL treat this as a soundness defect, resolve it by adjusting the inference rules or adding an explicit non-readonly annotation, and SHALL add a regression test that verifies the member is inaccessible on the corresponding Closed_Generic_With_Unknown.
