# Requirements Document: Extension Methods (Extension Blocks)

## Introduction

This feature adds C# 14-style extension blocks to the Mist programming language. Extension blocks allow developers to add methods and properties to types they do not own, including the ability to provide universal methods on all types via `extension(unknown self)`. Extensions are grouped by receiver type in a block syntax, support generics, and lower to C# static methods (or C# 14 extension blocks when targeting .NET 10+).

Extension blocks complement Mist's existing traits (structural typing) and interfaces (nominal typing) by providing a mechanism to augment a type's API surface without modifying its declaration or requiring structural matching.

## Glossary

- **Extension_Block**: A top-level declaration that groups one or more extension members (methods, properties) for a single receiver type. Syntax: `extension(ReceiverType self) { ... }`.
- **Receiver_Type**: The type being extended. Appears in the parenthesized parameter of the `extension` declaration.
- **Receiver_Parameter**: The named parameter representing the instance being extended (e.g., `self` in `extension(string self)`). Available to instance members but not static members within the block.
- **Instance_Extension_Member**: A method or property inside an extension block that is not marked `static`. Has access to the receiver parameter and is callable via dot-notation on instances.
- **Static_Extension_Member**: A method or property inside an extension block that is marked `static`. Cannot access the receiver parameter and is callable via the type name.
- **Universal_Extension**: An extension block with receiver type `unknown`, providing members available on all types.
- **Extension_Method**: A method declared inside an extension block.
- **Extension_Property**: A property declared inside an extension block.
- **Extension_Registry**: A data structure in the semantic model that indexes all extension blocks by their receiver type for efficient lookup during member resolution.
- **Compiler**: The Mist compiler pipeline (Lexer → Parser → Semantic Model → Type Checker → Emitter).
- **Parser**: The Mist parser that produces AST nodes from token streams.
- **Pretty_Printer**: The component that formats AST nodes back into Mist source text.
- **Semantic_Model_Builder**: The component that constructs the semantic model (types, symbols, scopes) from the AST.
- **Type_Checker**: The component that validates type correctness, including member resolution and assignability.
- **Emitter**: The C# transpiler backend that produces C# source from the semantic model.
- **Syntax_Highlighter**: The TextMate grammar that provides editor colorization for Mist source files.

## Requirements

### Requirement 1: Grammar — `extension` Keyword

**User Story:** As a Mist developer, I want the `extension` keyword to be a reserved word, so that the parser can unambiguously recognize extension block declarations.

#### Acceptance Criteria

1. THE Compiler SHALL recognize `extension` as a keyword token in the lexer.
2. WHEN `extension` appears in an identifier position, THE Parser SHALL reject it with a syntax error.
3. THE Pretty_Printer SHALL emit the `extension` keyword verbatim when formatting extension block declarations.

---

### Requirement 2: Grammar — Extension Block Syntax

**User Story:** As a Mist developer, I want to declare extension blocks with a named receiver, so that I can add methods and properties to the receiver type — both instance members (that access the receiver) and static members (that are namespaced under the type).

#### Acceptance Criteria

1. THE Parser SHALL parse `extension(TypeReference receiverName) { members }` as an extension block declaration.
2. THE Parser SHALL produce an AST node containing the receiver type reference, the receiver parameter name, and the list of member declarations.
3. THE Parser SHALL accept any valid type reference as the receiver type, including primitive types, named types, array types, and generic constructed types.
4. THE Parser SHALL accept `unknown` as the receiver type for universal extensions.
5. THE Parser SHALL accept both instance members (without `static`) and static members (with `static` modifier) within the same extension block.
6. THE Pretty_Printer SHALL format extension blocks as `extension(Type name) { ... }` with proper indentation of members.
7. FOR ALL valid extension block ASTs, parsing then pretty-printing then parsing again SHALL produce an equivalent AST (round-trip property).

---

### Requirement 3: Grammar — Static Members in Extension Blocks

**User Story:** As a Mist developer, I want to mark individual members inside an extension block as `static`, so that they are callable on the type itself rather than on instances.

#### Acceptance Criteria

1. THE Parser SHALL accept the `static` modifier on methods and properties declared inside an extension block.
2. WHEN a member is marked `static`, THE Parser SHALL produce an AST node that records the static modifier.
3. WHEN a static member body references the receiver parameter name, THE Type_Checker SHALL emit a diagnostic error stating that static extension members cannot access the receiver.
4. THE Pretty_Printer SHALL preserve the `static` modifier when formatting static extension members.

---

### Requirement 4: Grammar — Generic Extension Block Syntax

**User Story:** As a Mist developer, I want to declare generic extension blocks with type parameters, so that I can extend generic types like `List<T>` with type-safe methods.

#### Acceptance Criteria

1. THE Parser SHALL parse `extension<TypeParams>(TypeReference receiverName) { members }` as a generic extension block.
2. WHEN type parameters are declared on the extension block, THE Parser SHALL make those type parameters available for use in the receiver type reference and in member signatures within the block.
3. THE Pretty_Printer SHALL format generic extension blocks as `extension<T>(Type<T> name) { ... }` preserving type parameter lists.
4. FOR ALL valid generic extension block ASTs, parsing then pretty-printing then parsing again SHALL produce an equivalent AST (round-trip property).

---

### Requirement 5: Grammar — Extension Method Declarations

**User Story:** As a Mist developer, I want to declare methods inside extension blocks, so that I can add callable behavior to the extended type.

#### Acceptance Criteria

1. THE Parser SHALL parse `ReturnType Name(params) { body }` inside an extension block as an extension method declaration.
2. THE Parser SHALL accept methods with zero or more parameters, a return type, and a block body.
3. THE Parser SHALL allow the receiver parameter name to be referenced inside method bodies as an expression (analogous to `this`).
4. THE Parser SHALL accept access modifiers (`public`, `private`) on extension methods.
5. THE Pretty_Printer SHALL format extension methods with the standard method syntax (return type, name, parameter list, body).
6. FOR ALL valid extension method ASTs, parsing then pretty-printing then parsing again SHALL produce an equivalent AST (round-trip property).

---

### Requirement 6: Grammar — Extension Property Declarations

**User Story:** As a Mist developer, I want to declare properties inside extension blocks, so that I can add computed properties to the extended type.

#### Acceptance Criteria

1. THE Parser SHALL parse `Type Name => expression;` inside an extension block as an expression-bodied extension property.
2. THE Parser SHALL parse `Type Name { get { ... } }` inside an extension block as a block-bodied extension property.
3. THE Parser SHALL allow the receiver parameter name to be referenced inside property accessor bodies.
4. THE Parser SHALL accept access modifiers (`public`, `private`) on extension properties.
5. IF an extension property declares a `set` accessor, THEN THE Parser SHALL reject it with a diagnostic, because extension properties are read-only (no backing storage).
6. THE Pretty_Printer SHALL format extension properties preserving their accessor style.
7. FOR ALL valid extension property ASTs, parsing then pretty-printing then parsing again SHALL produce an equivalent AST (round-trip property).

---

### Requirement 7: Grammar — Extension Block as Top-Level Item

**User Story:** As a Mist developer, I want extension blocks to be top-level declarations alongside classes and namespaces, so that they can be defined in any source file.

#### Acceptance Criteria

1. THE Parser SHALL accept extension block declarations as top-level items in a compilation unit.
2. THE Parser SHALL accept extension block declarations inside namespace declarations (both file-scoped and block-scoped).
3. WHEN multiple extension blocks appear in the same file, THE Parser SHALL parse each independently.
4. THE Pretty_Printer SHALL format extension blocks at the same indentation level as other top-level declarations.

---

### Requirement 8: Semantic Model — Extension Block Registration

**User Story:** As a compiler developer, I want extension blocks to be registered in the semantic model, so that the type checker can discover extension members during member lookup.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder encounters an extension block declaration, THE Semantic_Model_Builder SHALL resolve the receiver type reference to an IType.
2. WHEN the receiver type resolves successfully, THE Semantic_Model_Builder SHALL register the extension block in the Extension_Registry keyed by the resolved receiver type.
3. THE Semantic_Model_Builder SHALL allow multiple extension blocks for the same receiver type, accumulating their members in the Extension_Registry.
4. WHEN the extension block has a named receiver parameter, THE Semantic_Model_Builder SHALL register a parameter symbol for the receiver in the extension block's scope.
5. WHEN the extension block has type parameters, THE Semantic_Model_Builder SHALL register TypeParameterType symbols for each type parameter in the extension block's scope.
6. THE Semantic_Model_Builder SHALL register each extension method as a symbol with SymbolKind.Method in the extension block's member scope.
7. THE Semantic_Model_Builder SHALL register each extension property as a symbol with SymbolKind.Property in the extension block's member scope.
8. IF the receiver type reference cannot be resolved, THEN THE Semantic_Model_Builder SHALL emit a diagnostic and skip the extension block.
9. AFTER extension blocks are registered, IF source extension members in the same namespace have the same receiver type and member name with identical signatures, whether declared in one block or separate blocks, THEN THE Compiler SHALL emit a duplicate member diagnostic. Registration SHALL retain both declarations for this check.

---

### Requirement 9: Semantic Model — Universal Extensions via `unknown`

**User Story:** As a Mist developer, I want `extension(unknown self)` to provide methods available on all types, so that I can define universal operations like `ToString()` and `GetHashCode()`.

#### Acceptance Criteria

1. WHEN an extension block has receiver type `unknown`, THE Semantic_Model_Builder SHALL register it as a universal extension applicable to all types.
2. WHEN the Type_Checker resolves member access on any type, THE Type_Checker SHALL include universal extension members in the candidate set.
3. WHEN a universal extension method is called on a value of concrete type, THE Type_Checker SHALL bind the receiver parameter to the concrete type of the value.
4. THE Semantic_Model_Builder SHALL allow multiple universal extension blocks to coexist, each contributing their members to the universal set.

---

### Requirement 10: Semantic Model — Receiver Parameter Scoping

**User Story:** As a Mist developer, I want the receiver parameter to be accessible inside extension method and property bodies, so that I can operate on the instance being extended.

#### Acceptance Criteria

1. WHEN an extension method body references the receiver parameter name, THE Semantic_Model_Builder SHALL resolve it to the receiver parameter symbol.
2. WHEN an extension property accessor references the receiver parameter name, THE Semantic_Model_Builder SHALL resolve it to the receiver parameter symbol.
3. THE Type_Checker SHALL assign the receiver parameter the declared receiver type for type checking purposes.
4. IF a member declaration inside an extension block shadows the receiver parameter name, THEN THE Semantic_Model_Builder SHALL emit a diagnostic warning about the shadowing.

---

### Requirement 11: Type Checker — Extension Member Lookup

**User Story:** As a Mist developer, I want extension methods and properties to be found automatically when I use dot-notation on a value, so that extensions feel like native members.

#### Acceptance Criteria

1. WHEN member lookup on a type fails to find a matching member in the type's own scope, THE Type_Checker SHALL search the Extension_Registry for extension blocks whose receiver type is assignable from the value's type.
2. WHEN an extension member is found, THE Type_Checker SHALL validate the call arguments against the extension method's parameter list (excluding the receiver parameter).
3. WHEN multiple extension members with the same name are found from different extension blocks, THE Type_Checker SHALL apply overload resolution rules to select the best candidate.
4. THE Type_Checker SHALL prefer a type's own members over extension members with the same name (own members shadow extensions).
5. THE Type_Checker SHALL prefer more-specific receiver types over less-specific ones (e.g., `extension(string self)` is preferred over `extension(unknown self)` when calling on a string).
6. IF no matching extension member is found after searching all applicable extensions, THEN THE Type_Checker SHALL emit the standard `MIST_UNDEFINED_MEMBER` diagnostic.

---

### Requirement 12: Type Checker — Generic Extension Type Inference

**User Story:** As a Mist developer, I want the compiler to infer type arguments for generic extensions automatically, so that I do not need to specify them explicitly at call sites.

#### Acceptance Criteria

1. WHEN a generic extension block is a candidate during member lookup, THE Type_Checker SHALL attempt to infer the extension's type parameters from the receiver type.
2. WHEN the receiver type is `List<int>` and the extension is `extension<T>(List<T> self)`, THE Type_Checker SHALL infer `T = int`.
3. IF type inference fails (the receiver type does not match the extension's receiver pattern), THEN THE Type_Checker SHALL exclude that extension from the candidate set without emitting a diagnostic.
4. WHEN type parameters appear in method parameter types or return types, THE Type_Checker SHALL substitute the inferred type arguments throughout the member signature.

---

### Requirement 13: Type Checker — Static Extension Member Lookup

**User Story:** As a Mist developer, I want static extension members to be callable using the type name, so that I can add factory methods and utilities to types I do not own.

#### Acceptance Criteria

1. WHEN a static member access on a type fails to find a matching static member in the type's own scope, THE Type_Checker SHALL search the Extension_Registry for extension blocks whose receiver type matches and that contain static members with the matching name.
2. WHEN a static extension method is found, THE Type_Checker SHALL validate the call arguments against the method's full parameter list.
3. THE Type_Checker SHALL prefer a type's own static members over static extension members with the same name.
4. WHEN a static extension member references the receiver parameter, THE Type_Checker SHALL emit a diagnostic error stating that static members cannot access the receiver instance.

---

### Requirement 14: Type Checker — Extension and Trait Interaction

**User Story:** As a Mist developer, I want extension methods to not interfere with trait satisfaction, so that the two features remain orthogonal.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT consider extension members when computing trait structural satisfaction.
2. WHEN a type satisfies a trait structurally, THE Type_Checker SHALL use only the type's own declared members for the match, excluding extension members.
3. WHEN a trait-typed variable has a member access, THE Type_Checker SHALL resolve against the trait's member scope and SHALL NOT fall back to extension lookup on the trait type itself.

---

### Requirement 15: Type Checker — Extension and Interface Interaction

**User Story:** As a Mist developer, I want extension methods to not interfere with interface implementation, so that the two features remain orthogonal.

#### Acceptance Criteria

1. THE Type_Checker SHALL NOT consider extension members when validating interface implementation.
2. WHEN an interface-typed variable has a member access, THE Type_Checker SHALL resolve against the interface's member scope and SHALL NOT fall back to extension lookup on the interface type itself.

---

### Requirement 16: C# Emission — Extension Methods as Static Methods

**User Story:** As a compiler developer, I want extension methods to lower to C# static methods, so that the emitted code is compatible with all .NET runtimes.

#### Acceptance Criteria

1. WHEN emitting an instance extension block, THE Emitter SHALL generate a static class containing static methods with the receiver as the first parameter.
2. THE Emitter SHALL name the generated static class using a deterministic scheme based on the source file and extension block position (e.g., `ExtensionBlock_FileName_LineNumber`).
3. WHEN emitting an extension method, THE Emitter SHALL emit the receiver parameter as the first parameter of the static method, annotated with `this` for C# extension method syntax.
4. WHEN emitting a call to an extension method, THE Emitter SHALL emit it as a static method invocation passing the receiver as the first argument.
5. WHEN emitting a generic extension block, THE Emitter SHALL emit the type parameters on the generated static class or on individual methods as appropriate. A static invocation SHALL preserve the bound selected callable and its closed receiver-block arguments followed by its own method arguments, including when the method declares no type parameters of its own.

---

### Requirement 17: C# Emission — Extension Properties

**User Story:** As a compiler developer, I want extension properties to lower to C# static getter methods, so that the emitted code is valid C# (C# does not support extension properties natively before C# 14).

#### Acceptance Criteria

1. WHEN emitting an expression-bodied extension property, THE Emitter SHALL generate a static method named `get_PropertyName` with the receiver as the first parameter.
2. WHEN emitting a block-bodied extension property with a get accessor, THE Emitter SHALL generate a static method named `get_PropertyName` with the receiver as the first parameter and the accessor body.
3. WHEN emitting a call site that accesses an extension property, THE Emitter SHALL emit it as a static method invocation of the generated getter method.

---

### Requirement 18: C# Emission — Universal Extensions

**User Story:** As a compiler developer, I want universal extensions (`extension(unknown self)`) to lower correctly, so that methods like `ToString()` are callable on all types at runtime.

#### Acceptance Criteria

1. WHEN emitting a universal extension method, THE Emitter SHALL emit the receiver parameter with type `object` in the generated C# static method.
2. WHEN emitting a call to a universal extension method on a value type, THE Emitter SHALL box the value by passing it as `object` to the static method.
3. WHEN emitting a call to a universal extension method on a reference type, THE Emitter SHALL pass the reference directly as `object`.

---

### Requirement 19: C# Emission — Static Extensions

**User Story:** As a compiler developer, I want static extension methods to lower to regular C# static methods, so that they are callable without an instance.

#### Acceptance Criteria

1. WHEN emitting a static extension member, THE Emitter SHALL generate a regular static method in the generated static class (without a `this` receiver parameter).
2. WHEN emitting a call to a static extension method, THE Emitter SHALL emit it as a regular static method invocation on the generated class.
3. THE Emitter SHALL NOT add a `this` parameter annotation to static extension methods.

---

### Requirement 20: Diagnostics — Extension Block Errors

**User Story:** As a Mist developer, I want clear error messages when I write invalid extension blocks, so that I can fix mistakes quickly.

#### Acceptance Criteria

1. IF the receiver type in an extension block cannot be resolved, THEN THE Compiler SHALL emit a diagnostic identifying the unresolved type name.
2. IF an extension block contains a member that is not a method or property (e.g., a field declaration), THEN THE Compiler SHALL emit a diagnostic stating that only methods and properties are allowed in extension blocks.
3. IF an extension property declares a `set` accessor, THEN THE Compiler SHALL emit a diagnostic stating that extension properties are read-only.
4. IF two source extension members in the same namespace have the same receiver type and member name with identical signatures, whether declared in one block or separate blocks, THEN THE Compiler SHALL emit a diagnostic at the later declaration indicating the duplicate member and identifying the earlier declaration's source location. This applies across source files in that namespace.
5. IF the receiver parameter name conflicts with a method parameter name in the same extension method, THEN THE Compiler SHALL emit a diagnostic about the name conflict.
6. IF a `void` type is used as the receiver type, THEN THE Compiler SHALL emit a diagnostic stating that `void` cannot be extended.
7. THE Compiler SHALL allow multiple extension blocks for the same receiver type and namespace when no two members with the same name have identical signatures. Blocks in different namespaces MAY declare identical members.

---

### Requirement 21: Diagnostics — Extension Member Resolution Errors

**User Story:** As a Mist developer, I want clear error messages when extension member resolution fails, so that I understand why a method call does not compile.

#### Acceptance Criteria

1. WHEN member lookup fails and extension lookup also fails, THE Type_Checker SHALL emit a diagnostic listing the type and the member name that was not found.
2. WHEN multiple extension methods are equally applicable (ambiguity), THE Type_Checker SHALL emit a diagnostic listing all candidate extension methods and their source locations.
3. WHEN a generic extension's type inference fails at a call site, THE Type_Checker SHALL exclude it silently from candidates (no diagnostic for inference failure alone).

---

### Requirement 22: Syntax Highlighting — Extension Block Support

**User Story:** As a Mist developer, I want extension blocks to be syntax-highlighted in my editor, so that the code is readable and visually structured.

#### Acceptance Criteria

1. THE Syntax_Highlighter SHALL highlight the `extension` keyword with the keyword declaration scope.
2. THE Syntax_Highlighter SHALL highlight the receiver type name with the type scope.
3. THE Syntax_Highlighter SHALL highlight the receiver parameter name with the parameter scope.
4. THE Syntax_Highlighter SHALL highlight extension method names with the function declaration scope.
5. THE Syntax_Highlighter SHALL highlight extension property names with the property scope.

---

### Requirement 23: Standard Library — Universal Object Methods

**User Story:** As a Mist developer, I want `ToString()`, `GetHashCode()`, and `GetType()` to be available on all types, so that I have basic reflection and string conversion capabilities.

#### Acceptance Criteria

1. THE Standard_Library SHALL provide a universal extension block `extension(unknown self)` declaring `string ToString()`.
2. THE Standard_Library SHALL provide a universal extension block declaring `int GetHashCode()`.
3. THE Standard_Library SHALL provide a universal extension block declaring `Type GetType()`.
4. WHEN a Mist program calls `ToString()` on any value, THE Type_Checker SHALL retain the universal zero-argument candidate even when the type declares formatting overloads with parameters. A user-declared nongeneric instance zero-argument `ToString` retains its own selection, and formatting overloads remain callable at their declared arities. This bounded universal contract SHALL NOT change own-member precedence for unrelated names.
5. WHEN the universal `ToString()` is emitted, THE Emitter SHALL lower it to a call to the .NET `object.ToString()` method on the receiver.

---

### Requirement 24: Interaction — Extensions and the `unknown` Type

**User Story:** As a Mist developer, I want extension methods to be callable on `unknown`-typed values, so that universal methods work even when the concrete type is not known.

#### Acceptance Criteria

1. WHEN member lookup is performed on a value of type `unknown`, THE Type_Checker SHALL search universal extensions (those with receiver type `unknown`) for matching members.
2. THE Type_Checker SHALL NOT search type-specific extensions when the value's static type is `unknown` (because the concrete type is not known at compile time).
3. WHEN a universal extension method is called on an `unknown`-typed value, THE Emitter SHALL emit the call with the receiver passed as `object`.

---

### Requirement 25: Interaction — Extensions and Generics

**User Story:** As a Mist developer, I want extension methods to work correctly with generic types, so that I can extend `List<T>`, `Optional<T>`, and other parameterized types.

#### Acceptance Criteria

1. WHEN member lookup is performed on a value of type `List<int>`, THE Type_Checker SHALL find extension methods from `extension<T>(List<T> self)` with `T` inferred as `int`.
2. WHEN the inferred type arguments are substituted into the extension method's return type, THE Type_Checker SHALL produce the correct concrete return type.
3. WHEN the inferred type arguments are substituted into the extension method's parameter types, THE Type_Checker SHALL validate call arguments against the substituted types.
4. THE Type_Checker SHALL support multiple type parameters on a single extension block (e.g., `extension<K, V>(Map<K, V> self)`).

---

### Requirement 26: Interaction — Extensions and Type Unions

**User Story:** As a Mist developer, I want extension methods to be discoverable on union-typed values when the extension applies to all constituents, so that common operations remain accessible.

#### Acceptance Criteria

1. WHEN member lookup is performed on a value of union type `A | B`, THE Type_Checker SHALL find extension methods that are applicable to both `A` and `B` with compatible signatures.
2. IF an extension method is applicable to some but not all constituents of a union type, THEN THE Type_Checker SHALL NOT include it in the candidate set for that union-typed value.
3. Universal extension methods (receiver type `unknown`) SHALL always be applicable to union-typed values.

---

### Requirement 27: Namespace Scoping — Extension Visibility

**User Story:** As a Mist developer, I want extension blocks to respect namespace scoping, so that extensions are only visible when their containing namespace is imported.

#### Acceptance Criteria

1. WHEN an extension block is declared inside a namespace, THE Type_Checker SHALL only consider it during member lookup if the namespace is imported via a `using` directive at the call site.
2. WHEN an extension block is declared at the top level (no namespace), THE Type_Checker SHALL consider it visible from all files in the same compilation.
3. WHEN multiple imported namespaces provide extensions for the same type with the same member name, THE Type_Checker SHALL apply the same overload and ambiguity rules as for other members at each use. Importing both namespaces without using the member SHALL NOT be an error; a use that has no unique candidate SHALL be diagnosed as ambiguous.
