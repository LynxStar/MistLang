# Requirements Document

## Introduction

This document specifies the requirements for adding C#-style method overloading to the Mist compiler. Currently, the Mist semantic model registers at most one symbol per name in each scope (via `IScope.Lookup(string name)` returning a single `ISymbol`). This prevents declaring multiple methods with the same name but different parameter signatures within the same type. Method overloading is a foundational C# feature that enables more expressive APIs and is a prerequisite for many other planned features.

The scope of this feature covers: allowing multiple method declarations with the same name but different parameter lists within a single type, resolving the correct overload at each call site based on argument types, supporting constructor overloading (multiple constructors per type), and producing clear diagnostics for ambiguous or unresolvable calls.

## Glossary

- **Overload_Set**: The collection of all methods (or constructors) sharing the same name within a single type scope.
- **Parameter_Signature**: The ordered list of parameter types for a method or constructor, excluding the return type and parameter names.
- **Overload_Resolver**: The compiler component that selects the single best-matching method or constructor from an Overload_Set given the argument types at a call site.
- **Scope**: A lexical scope containing symbol declarations, implemented by `IScope`. Currently returns a single `ISymbol` per name via `Lookup`.
- **Semantic_Model_Builder**: The compiler pass (`SemanticModelBuilder`) that registers symbols, types, and scopes from the parsed AST.
- **Type_Checker**: The analysis pass (`TypeCheckerAnalyzer`) that validates type correctness of expressions, statements, and declarations.
- **Emitter**: The C# transpiler backend (`CSharpEmitter`) that produces C# source from the semantic model.
- **Candidate**: A single method or constructor from an Overload_Set that could potentially match a given set of argument types.
- **Exact_Match**: A Candidate whose Parameter_Signature types are identical to the argument types at the call site.
- **Applicable_Candidate**: A Candidate where every argument is assignable to the corresponding parameter type (including implicit conversions already supported by the compiler). An argument that is an integer constant is assignable to every integral parameter type whose range holds its value, exactly as `byte b = 2;` is valid.
- **Ambiguous_Call**: A call site where two or more Applicable_Candidates are equally valid and no single best match can be determined.

## Requirements

### Requirement 1: Multiple Method Declarations with the Same Name

**User Story:** As a Mist developer, I want to declare multiple methods with the same name but different parameter lists within a single class, struct, or interface, so that I can provide overloaded APIs following C# conventions.

#### Acceptance Criteria

1. WHEN a type contains two or more method declarations with the same name but different Parameter_Signatures, THE Semantic_Model_Builder SHALL register all of them as distinct symbols in the type's member scope.
2. WHEN a type contains two method declarations with the same name and identical Parameter_Signatures, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating a duplicate method signature.
3. THE Scope SHALL support retrieving all symbols with a given name, not only the first match.
4. WHEN two methods share the same name but differ only in return type (with identical Parameter_Signatures), THE Semantic_Model_Builder SHALL emit a diagnostic error because return type alone does not distinguish overloads.

### Requirement 2: Constructor Overloading

**User Story:** As a Mist developer, I want to declare multiple constructors with different parameter lists within a single class or struct, so that I can provide flexible object initialization.

#### Acceptance Criteria

1. WHEN a type contains two or more constructor declarations with different Parameter_Signatures, THE Semantic_Model_Builder SHALL register all of them as distinct constructor symbols in the type's member scope.
2. WHEN a type contains two constructor declarations with identical Parameter_Signatures, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating a duplicate constructor signature.
3. WHEN a `new T(args)` expression is evaluated, THE Overload_Resolver SHALL select the constructor whose Parameter_Signature best matches the argument types.
4. WHEN constructor chaining via `: this(args)` is used, THE Overload_Resolver SHALL select the matching constructor from the same type's Overload_Set, excluding the current constructor.
5. WHEN constructor chaining via `: base(args)` is used, THE Overload_Resolver SHALL select the matching constructor from the base type's Overload_Set.

### Requirement 3: Overload Resolution at Method Call Sites

**User Story:** As a Mist developer, I want the compiler to automatically select the correct method overload based on the argument types I provide at a call site, so that I do not need to manually disambiguate.

#### Acceptance Criteria

1. WHEN a method call has arguments that exactly match the Parameter_Signature of one Candidate in the Overload_Set, THE Overload_Resolver SHALL select that Candidate.
2. WHEN a method call has arguments that are assignable (via implicit conversions already supported by the compiler) to the Parameter_Signature of exactly one Candidate, THE Overload_Resolver SHALL select that Candidate.
3. WHEN a method call has arguments that match multiple Candidates via implicit conversions, THE Overload_Resolver SHALL prefer the Candidate with the most specific parameter types (where a type A is more specific than type B if A is assignable to B but B is not assignable to A). Between two integral types neither of which is assignable to the other -- a signed and an unsigned type such as `short` and `uint` -- neither is more specific, and a call they match equally is ambiguous (Requirement 4): a byte argument over `Take(short)` and `Take(uint)`, and `Take(2)` over `Take(sbyte)` and `Take(byte)`, are both refused. The caller chooses by giving the argument one of the types with a cast, as `Take((short)b)`, or `Take((sbyte)2)` for a constant, whose cast is valid when the type holds its value (cast-operators Requirement 8.8).
4. WHEN a method call has no Applicable_Candidates in the Overload_Set, THE Type_Checker SHALL emit a diagnostic error listing the method name and the argument types provided.
5. WHEN a method call matches zero methods because the argument count does not match any Candidate's parameter count, THE Type_Checker SHALL emit a diagnostic error indicating no overload accepts the given number of arguments.
6. THE Overload_Resolver SHALL consider the full Overload_Set including methods inherited from base classes.
7. WHEN an argument is an integer constant, THE Overload_Resolver SHALL treat it as reaching every integral parameter type that holds its value, and otherwise as a value of its own type: `Take(2)` over `Take(byte)` and `Take(string)` selects `Take(byte)`, `Take(2)` over `Take(byte)` and `Take(long)` selects the more specific `Take(byte)`, and `Take(300)` over `Take(byte)` and `Take(string)` has no Applicable_Candidate. An integral value that is not a constant reaches no narrower integral type.
8. WHEN two or more Candidates remain and their Parameter_Signatures are identical once the receiver's type arguments are substituted, THE Overload_Resolver SHALL prefer the less generic signature, in this order: a Candidate declaring no type parameters of its own beats one that declares them; otherwise the Candidate whose parameter types are FIXED AT THE DECLARATION wins over one whose parameter types are supplied by the receiver's type arguments. A parameter declared as a type denotes that type in every instantiation; one declared as a type parameter denotes whatever the instantiation supplies, a family that merely happens to include it, so the fixed parameter is the more specific of the two. The same holds inside a type: a constructed type, array, tuple or function type is more specific than another of its shape when at least one part is more specific and no part is less, a fixed parameter being fixed all the way down. So `Keyed<K>` declaring `Get(K)` and `Get(int)`, or `this[K key]` and `this[int i]`, selects the `int` form at `Keyed<int>`, and `new Box<int>(3)` over `Box(T)` and `Box(int)` selects `Box(int)`. Without this preference neither form is callable at `Keyed<int>`, since the cast Requirement 3.3 offers can only name the type they already share.
9. THE preference in Requirement 3.8 SHALL apply only to identical Parameter_Signatures, so that it orders Candidates the arguments cannot separate rather than settling a tie Requirement 3.3 leaves open: a byte argument over `Take(short, int)` and a generic `Take(uint, T)` is ambiguous, as it is over `Take(short, int)` and `Take(uint, int)`.
10. WHEN no position separates two Candidates under Requirement 3.8, THE Type_Checker SHALL report the call as ambiguous (Requirement 4). `Pair<A, B>` declaring `Take(A)` and `Take(B)` is ambiguous at `Pair<int, int>`, because a type parameter is no more specific than another; so are `Take(K, int)` against `Take(int, K)`, each more specific in one position. Neither Candidate is the fixed one in either case, which is what leaves them tied.
11. WHERE Requirement 3.8 would prefer a Candidate but the emitted program would run the other, THE Overload_Resolver SHALL report the call as ambiguous rather than select it. `Take(K?)` against `Take(int?)` is such a call: `int?` is fixed where `K?` follows the instantiation, so `Take(int?)` should win and will, once an optional over an open type parameter is representable. Today `K?` reaches the backend as `K`, which cannot hold the absent value at all, so a selection of `Take(int?)` would run `Take(K?)`.

### Requirement 4: Ambiguous Call Diagnostics

**User Story:** As a Mist developer, I want clear error messages when the compiler cannot determine which overload to call, so that I can fix the ambiguity.

#### Acceptance Criteria

1. WHEN a method call has two or more equally-applicable Candidates and no single best match can be determined, THE Type_Checker SHALL emit a diagnostic error indicating the call is ambiguous.
2. WHEN an ambiguous call is detected, THE Type_Checker SHALL include the signatures of the conflicting Candidates in the diagnostic message.
3. WHEN a constructor call (via `new T(args)`) is ambiguous, THE Type_Checker SHALL emit a diagnostic error with the same detail as for ambiguous method calls.

### Requirement 5: Overloaded Method Redeclaration Across Inheritance

**User Story:** As a Mist developer, I want the compiler to correctly handle overloaded methods when a derived class declares methods with the same name as base class methods, so that overloading works across the inheritance hierarchy.

#### Acceptance Criteria

1. WHEN a derived class declares a method with the same name as a base class method but a different Parameter_Signature, THE Semantic_Model_Builder SHALL treat the derived method as a new overload rather than a redeclaration error.
2. WHEN a derived class declares a method with the same name and identical Parameter_Signature as a base class method, THE Semantic_Model_Builder SHALL emit a redeclaration diagnostic error (preserving existing behavior for non-overloaded cases).
3. WHEN resolving a method call on a derived type, THE Overload_Resolver SHALL consider Candidates from both the derived type and all ancestor types.

### Requirement 6: Interface Method Overloading

**User Story:** As a Mist developer, I want to declare overloaded methods in interfaces and have implementing types satisfy all overloads, so that interfaces can define rich contracts.

#### Acceptance Criteria

1. WHEN an interface declares two or more methods with the same name but different Parameter_Signatures, THE Semantic_Model_Builder SHALL register all of them as distinct interface method symbols.
2. WHEN a class or struct implements an interface with overloaded methods, THE Semantic_Model_Builder SHALL verify that the implementing type provides a method matching each overloaded Parameter_Signature.
3. IF an implementing type is missing one or more overloaded interface methods, THEN THE Semantic_Model_Builder SHALL emit a diagnostic error identifying each missing overload signature.

### Requirement 7: C# Code Emission for Overloaded Methods

**User Story:** As a Mist developer, I want overloaded methods and constructors to be correctly emitted as valid C# code, so that the transpiled output compiles and runs correctly.

#### Acceptance Criteria

1. THE Emitter SHALL emit all method declarations from an Overload_Set as separate C# method declarations with the same name.
2. THE Emitter SHALL emit all constructor declarations from a constructor Overload_Set as separate C# constructor declarations.
3. FOR ALL valid Mist programs containing overloaded methods, emitting to C# and compiling with Roslyn SHALL produce a successful compilation (round-trip property).

### Requirement 8: Scope Lookup API Extension

**User Story:** As a compiler developer, I want the scope lookup API to support retrieving multiple symbols with the same name, so that the overload resolver and other compiler passes can access the full Overload_Set.

#### Acceptance Criteria

1. THE Scope SHALL provide a method to look up all symbols matching a given name, returning a collection rather than a single symbol.
2. THE Scope SHALL continue to provide the existing single-symbol `Lookup(string name)` method for backward compatibility with non-overloaded lookups.
3. WHEN the multi-symbol lookup traverses parent scopes, THE Scope SHALL collect matching symbols from the current scope and all ancestor scopes.
