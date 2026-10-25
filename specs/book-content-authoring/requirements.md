# Requirements Document

## Introduction

This specification defines the content requirements for "The Mist Programming Language" book. The book is an mdBook project with 7 parts and 37 sections, all currently containing only placeholder stubs. Each section must be filled with accurate, progressive documentation that teaches the Mist language through prose explanations and tabbed code examples showing both Mist source and the corresponding emitted C# output.

## Glossary

- **Book**: The mdBook project located at `book/` in the repository root
- **Section**: A single markdown file within the book corresponding to one topic
- **Tab_Block**: A code example using the mdbook-tabs preprocessor syntax (`{{#tabs}}`, `{{#tab name="Mist"}}`, `{{#tab name="Emitted C#"}}`, `{{#endtab}}`, `{{#endtabs}}`)
- **Mist_Tab**: The tab within a Tab_Block containing Mist source code
- **CSharp_Tab**: The tab within a Tab_Block containing the C# code that the Mist compiler emits for the corresponding Mist_Tab
- **Progressive_Order**: The pedagogical ordering where each section may reference concepts from prior sections but not from later sections

---

## Requirements

### Requirement 1: Tab Block Structure

**User Story:** As a reader, I want every Mist code example to have a corresponding C# tab, so that I can understand what the compiler produces.

#### Acceptance Criteria

1. WHEN a Section contains a fenced code block with the `mist` language identifier that represents a compilable unit, THE Section SHALL wrap it in a Tab_Block where the Mist_Tab appears first and the CSharp_Tab appears second
2. THE CSharp_Tab SHALL contain C# code that is semantically equivalent to what the Mist compiler would emit for the corresponding Mist_Tab source
3. THE Mist_Tab SHALL use the `mist` language identifier in its fenced code block and the tab name "Mist"
4. THE CSharp_Tab SHALL use the `csharp` language identifier in its fenced code block and the tab name "Emitted C#"
5. IF a Section contains multiple fenced Mist code blocks that each introduce a different language feature or syntax form, THEN THE Section SHALL use a separate Tab_Block for each code block
6. IF a Section contains a Mist code fragment that is not a complete compilable unit, THEN THE Section SHALL present it as a standalone fenced code block without a Tab_Block

---

### Requirement 2: Progressive Content Structure

**User Story:** As a reader, I want the book to build concepts progressively, so that I can learn the language step by step without encountering unexplained concepts.

#### Acceptance Criteria

1. THE Book SHALL present sections in Progressive_Order following the part sequence (Getting Started, Basics, Object Model, Type System, Advanced Features, Standard Library, Tooling) where each Section only uses or mentions concepts that have been introduced in a prior section or the current section
2. WHEN a Section uses or mentions a concept introduced in a later section, THE Section SHALL include a forward-reference note that states the concept name and identifies the section where it is fully explained
3. THE Book SHALL begin with Getting Started content that assumes general programming experience in at least one language but requires no prior Mist knowledge
4. IF a Section introduces a new concept, THEN THE Section SHALL provide a definition or explanation of that concept before using it in code examples within the same section

---

### Requirement 3: Introduction Section

**User Story:** As a new reader, I want an introduction that explains what Mist is and why it exists, so that I can decide if the language is relevant to me.

#### Acceptance Criteria

1. THE Introduction Section SHALL state that Mist is a programming language that transpiles to C#, targeting the .NET runtime.
2. THE Introduction Section SHALL list the key design goals of Mist (ownership, traits, type unions, refinement types, pattern matching) and provide a one-sentence description of each goal.
3. THE Introduction Section SHALL state that the target audience is developers familiar with C# or similar statically-typed languages who want stronger compile-time safety guarantees.
4. THE Introduction Section SHALL list each part heading from the Table_of_Contents (Getting Started, Basics, Object Model, Type System, Advanced Features, Standard Library, Tooling) with a one-sentence summary of what that part covers.
5. THE Introduction Section SHALL include a Mist code example of no more than 15 lines within a Tab_Block using the `mdbook-tabs` syntax, with one tab labeled "Mist" and one tab labeled "Emitted C#" showing the corresponding transpiler output.
6. THE Introduction Section Tab_Block SHALL use the `{{#tabs}}`, `{{#tab name="Mist"}}`, and `{{#tab name="Emitted C#"}}` delimiters as defined in the mdbook-tabs convention.

---

### Requirement 4: Installation Section

**User Story:** As a new user, I want installation instructions, so that I can set up the Mist toolchain on my machine.

#### Acceptance Criteria

1. THE Installation Section SHALL list the prerequisites for installing Mist, including the minimum required .NET SDK version (10.0 or later)
2. THE Installation Section SHALL provide installation instructions covering at least one method (building from source or installing as a published tool), where each method is presented as a numbered sequence of commands the user can execute in a terminal
3. THE Installation Section SHALL include a verification command (e.g., `mist --version` or `mist build` on a sample project) and state the expected output that confirms successful installation
4. THE Installation Section SHALL document supported platforms by listing at least the operating systems and architectures on which Mist is tested (minimum: one platform)
5. IF the .NET SDK is not installed or is below the minimum required version, THEN THE Installation Section SHALL state the expected error behavior so the user can diagnose the issue

---

### Requirement 5: Hello World Section

**User Story:** As a new user, I want a Hello World tutorial, so that I can write and run my first Mist program.

#### Acceptance Criteria

1. THE Hello_World Section SHALL walk the reader through creating a new Mist project by showing the required directory structure: a project folder containing a `mist.toml` file and at least one `.mist` source file
2. THE Hello_World Section SHALL show the minimal Mist program (a class with a static Main method that calls `Console.WriteLine`) in a Tab_Block with the emitted C#
3. THE Hello_World Section SHALL show the `mist build` command for compilation and the `mist run` command for execution, including the expected console output from running the program
4. THE Hello_World Section SHALL explain each line of the Hello World program, covering: the `using` declaration, the `namespace` declaration, the class declaration, the Main method signature, and the `Console.WriteLine` call
5. THE Hello_World Section SHALL show the contents of the `mist.toml` file with at minimum the `name` field

---

### Requirement 6: Project Structure Section

**User Story:** As a new user, I want to understand how a Mist project is organized, so that I can navigate and create multi-file projects.

#### Acceptance Criteria

1. THE Project_Structure Section SHALL describe the `mist.toml` configuration file including the required `name` field, the optional `namespace_root` field (and its default behavior of falling back to the project name), and the optional `[[references]]` array with its `path` field
2. THE Project_Structure Section SHALL explain the directory layout of a Mist project, identifying that the minimum valid project consists of a `mist.toml` file and at least one `.mist` source file in the project directory
3. THE Project_Structure Section SHALL explain that the compiler discovers all files with the `.mist` extension recursively within the project directory
4. THE Project_Structure Section SHALL describe how multi-file projects are organized using the `[[references]]` array in `mist.toml`, including that each reference specifies a `path` (relative or absolute) pointing to another project directory containing its own `mist.toml`
5. IF the `mist.toml` file is missing or contains invalid TOML syntax, THEN THE Project_Structure Section SHALL document that the compiler reports a diagnostic error and halts compilation

---

### Requirement 7: Primitive Types Section

**User Story:** As a reader, I want to learn about Mist's primitive types, so that I can use basic data types in my programs.

#### Acceptance Criteria

1. THE Primitive_Types Section SHALL document all numeric types (int, uint, long, ulong, short, ushort, byte, sbyte, float, double, decimal)
2. THE Primitive_Types Section SHALL document the bool type
3. THE Primitive_Types Section SHALL document the char type
4. THE Primitive_Types Section SHALL document the string type including interpolated strings and escape sequences
5. THE Primitive_Types Section SHALL show each type in a Tab_Block with the emitted C#
6. THE Primitive_Types Section SHALL document numeric literal suffixes (u, L, UL, f, m)
7. THE Primitive_Types Section SHALL document hex literals (0x prefix)

---

### Requirement 8: Void and Unit Type Section

**User Story:** As a reader, I want to understand Mist's void type, so that I can use it as a first-class value type.

#### Acceptance Criteria

1. THE Void_Type Section SHALL explain that void is a first-class value type with a single instance (`Void.Void`) and state that the compiler emits a `MistVoid` struct in the generated C#.
2. THE Void_Type Section SHALL show void as a variable type in a Tab_Block, demonstrating a variable declared with type `void` and initialized to `Void.Void`.
3. THE Void_Type Section SHALL show void as a parameter type in a Tab_Block, demonstrating a method that accepts a `void` parameter.
4. THE Void_Type Section SHALL show void as a generic type argument in a Tab_Block, demonstrating a generic type instantiated with `void` (e.g., `Result<void>`).
5. THE Void_Type Section SHALL show the difference between fire-and-forget void methods and value-returning void methods in separate Tab_Blocks: one Tab_Block showing a void method called as a standalone statement (emitting standard C# `void`), and one Tab_Block showing a void method whose return value is assigned to a variable (emitting `MistVoid` return type with `return MistVoid.Instance`).
6. THE Void_Type Section SHALL document void equality semantics by stating that all `Void.Void` instances are equal (`==` returns `true`, `!=` returns `false`) and that `GetHashCode()` returns a constant value.

---

### Requirement 9: Variables Section

**User Story:** As a reader, I want to learn about variable declarations in Mist, so that I can store and manipulate values.

#### Acceptance Criteria

1. THE Variables Section SHALL explain explicit type annotations in variable declarations using the syntax `Type name = expression;`, shown in a Tab_Block with the emitted C#.
2. THE Variables Section SHALL explain type inference using `var` with the syntax `var name = expression;`, shown in a Tab_Block with the emitted C#.
3. THE Variables Section SHALL explain tuple deconstruction in variable declarations in both the `var` form (`var (a, b) = expr;`) and the typed form (`(Type1 a, Type2 b) = expr;`), each shown in a Tab_Block with the emitted C#.
4. THE Variables Section SHALL explain variable scoping rules, stating that variables are scoped to the enclosing block (delimited by `{` and `}`) and are not accessible outside that block.
5. THE Variables Section SHALL explain that variables declared with an explicit type or `var` are mutable by default and can be reassigned within their scope.

---

### Requirement 10: Namespaces Section

**User Story:** As a reader, I want to understand namespaces in Mist, so that I can organize code and avoid naming conflicts.

#### Acceptance Criteria

1. THE Namespaces Section SHALL explain file-scoped namespace declarations using the syntax `namespace Qualified.Name;` (semicolon-terminated, applies to the entire file), shown in a Tab_Block with the emitted C#.
2. THE Namespaces Section SHALL explain block namespace declarations using the syntax `namespace Qualified.Name { ... }` (brace-delimited, scoping enclosed types), shown in a Tab_Block with the emitted C#.
3. THE Namespaces Section SHALL explain namespace inference from the project name (NamespaceRoot), stating that files without an explicit namespace declaration are placed under the NamespaceRoot defined in `mist.toml` (defaulting to the project name).
4. THE Namespaces Section SHALL explain how nested namespaces map to directory structure, stating that a file in subdirectory `Models/Game/` under the project root receives the inferred namespace `{NamespaceRoot}.Models.Game`.
5. THE Namespaces Section SHALL explain `using` declarations for importing types from other namespaces using the syntax `using Qualified.Name;`, shown in a Tab_Block with the emitted C#.
6. THE Namespaces Section SHALL explain that types within the same namespace can reference each other without a `using` declaration.

---

### Requirement 11: Control Flow Section

**User Story:** As a reader, I want to learn about control flow constructs in Mist, so that I can write conditional and iterative logic.

#### Acceptance Criteria

1. THE Control_Flow Section SHALL document if/else if/else statements using the syntax `if (condition) { } else if (condition) { } else { }`, shown in a Tab_Block with the emitted C#.
2. THE Control_Flow Section SHALL document while loops using the syntax `while (condition) { }`, shown in a Tab_Block with the emitted C#.
3. THE Control_Flow Section SHALL document for loops using the syntax `for (initializer; condition; iterator) { }`, shown in a Tab_Block with the emitted C#.
4. THE Control_Flow Section SHALL document foreach loops using the syntax `foreach (Type name in collection) { }`, shown in a Tab_Block with the emitted C#.
5. THE Control_Flow Section SHALL document the break statement using the syntax `break;` within a loop body, shown in a Tab_Block with the emitted C#.
6. THE Control_Flow Section SHALL show at least one example of nested control flow (a loop containing a conditional, or a conditional containing a loop) in a Tab_Block with the emitted C#.

---

### Requirement 12: Functions Section

**User Story:** As a reader, I want to learn about functions in Mist, so that I can write reusable logic.

#### Acceptance Criteria

1. THE Functions Section SHALL document function declarations with parameters and return types using the syntax `ReturnType Name(ParamType param, ...) { }`, shown in a Tab_Block with the emitted C#.
2. THE Functions Section SHALL document static methods using the `static` modifier and instance methods (no `static` modifier), each shown in a Tab_Block with the emitted C#.
3. THE Functions Section SHALL document access modifiers `public` and `private` on method declarations, explaining that `public` methods are accessible from outside the declaring class and `private` methods are accessible only within the declaring class.
4. THE Functions Section SHALL document `void` return type for functions that do not return a value, shown in a Tab_Block with the emitted C#.
5. THE Functions Section SHALL explain recursion with an example of a function that calls itself, shown in a Tab_Block with the emitted C#.

---

### Requirement 13: Array Literals Section

**User Story:** As a reader, I want to learn about array literal syntax in Mist, so that I can create arrays concisely.

#### Acceptance Criteria

1. THE Array_Literals Section SHALL document the bracket syntax for array literals (`[1, 2, 3]`) with type inference via `var`, shown in a Tab_Block with the emitted C#.
2. THE Array_Literals Section SHALL document empty array literals with explicit type annotation (`Type[] name = [];`), shown in a Tab_Block with the emitted C#.
3. THE Array_Literals Section SHALL document nested array literals (`[[1, 2], [3, 4]]`), shown in a Tab_Block with the emitted C#.
4. THE Array_Literals Section SHALL document array literals used as method arguments (passed directly at the call site) and as return values (returned directly from a method with an array return type), each shown in a Tab_Block with the emitted C#.
5. THE Array_Literals Section SHALL document trailing comma support, stating that a comma after the last element is permitted (`[1, 2, 3,]`).
6. THE Array_Literals Section SHALL document array literals with explicit type annotation on the variable (`Type[] name = [1, 2, 3];`), shown in a Tab_Block with the emitted C#.

---

### Requirement 14: Tuples Section

**User Story:** As a reader, I want to learn about tuples in Mist, so that I can group multiple values without defining a class.

#### Acceptance Criteria

1. THE Tuples Section SHALL document positional tuple syntax using the form `(Type1, Type2) name = (val1, val2);` and element access via `name.Item1` through `name.ItemN`, shown in a Tab_Block with the emitted C#.
2. THE Tuples Section SHALL document named tuple elements using the form `(Type1 name1, Type2 name2) name = (name1: val1, name2: val2);` and access via the declared element names, shown in a Tab_Block with the emitted C#.
3. THE Tuples Section SHALL document tuple deconstruction in both the var form (`var (a, b) = tupleExpr;`) and the typed form (`(Type1 a, Type2 b) = tupleExpr;`), each shown in a Tab_Block with the emitted C#.
4. THE Tuples Section SHALL document nested tuples where a tuple element is itself a tuple (e.g., `((int, int), string)`), shown in a Tab_Block with the emitted C#.
5. THE Tuples Section SHALL state that tuples are value types that lower to C# `ValueTuple`.

---

### Requirement 15: Regex Type Section

**User Story:** As a reader, I want to learn about the regex type in Mist, so that I can use regular expressions.

#### Acceptance Criteria

1. THE Regex_Type Section SHALL document regex literal syntax using backtick delimiters (`` `pattern` ``), shown in a Tab_Block with the emitted C#.
2. THE Regex_Type Section SHALL document explicit `regex` type annotation on variable declarations (`regex name = `pattern`;`), shown in a Tab_Block with the emitted C#.
3. THE Regex_Type Section SHALL explain that regex literals are compiled to `System.Text.RegularExpressions.Regex` instances in the emitted C#.
4. THE Regex_Type Section SHALL document the `IsMatch` method (partial match) and the `IsFullMatch` method (entire string must match), each shown in a Tab_Block with the emitted C#.

---

### Requirement 16: Classes Section

**User Story:** As a reader, I want to learn about classes in Mist, so that I can define reference types with fields and methods.

#### Acceptance Criteria

1. THE Classes Section SHALL document class declaration syntax including the `class` keyword, class name, optional type parameters, optional base clause, and a body delimited by braces, with at least one Tab_Block showing a minimal class with fields
2. THE Classes Section SHALL document constructor declaration syntax including parameters, constructor chaining to another constructor via `: this(...)`, and usage of the `this` keyword to access instance members, with at least one Tab_Block per concept (constructor, chaining, `this` usage)
3. THE Classes Section SHALL document instance methods (non-static methods declared inside a class body) and static methods (methods with the `static` modifier), with at least one Tab_Block for each
4. THE Classes Section SHALL document static fields (fields with the `static` modifier) including initialization, with at least one Tab_Block
5. THE Classes Section SHALL document the `public` and `private` access modifiers on class members (fields, methods, constructors), explaining visibility rules, with at least one Tab_Block showing both modifiers
6. THE Classes Section SHALL present each distinct concept (field declaration, constructor, constructor chaining, `this` keyword, instance method, static method, static field, access modifiers) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 17: Inheritance Section

**User Story:** As a reader, I want to learn about class inheritance in Mist, so that I can create type hierarchies.

#### Acceptance Criteria

1. THE Inheritance Section SHALL document the syntax for extending a base class using the colon (`:`) followed by the base class name in the class declaration, with at least one Tab_Block showing a derived class extending a base class
2. THE Inheritance Section SHALL document constructor chaining to a base class constructor via `: base(...)`, showing how arguments are forwarded to the parent constructor, with at least one Tab_Block
3. THE Inheritance Section SHALL document method overriding, showing how a derived class provides a new implementation for a method defined in the base class, with at least one Tab_Block
4. THE Inheritance Section SHALL present each distinct concept (extending a base class, base constructor chaining, method overriding) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 18: Structs Section

**User Story:** As a reader, I want to learn about structs in Mist, so that I can define value types.

#### Acceptance Criteria

1. THE Structs Section SHALL document struct declaration syntax including the `struct` keyword, struct name, optional type parameters, optional interface list, and a body delimited by braces containing fields, constructors, and methods, with at least one Tab_Block showing a minimal struct
2. THE Structs Section SHALL explain the difference between structs (value types with copy semantics) and classes (reference types with reference semantics), with at least one Tab_Block demonstrating the behavioral difference
3. THE Structs Section SHALL document the `readonly` modifier on struct declarations, explaining that it prevents mutation of the struct's fields after construction, with at least one Tab_Block
4. THE Structs Section SHALL present each distinct concept (struct declaration, value-vs-reference semantics, readonly structs) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 19: Properties Section

**User Story:** As a reader, I want to learn about properties in Mist, so that I can use getter/setter syntax for encapsulated fields.

#### Acceptance Criteria

1. THE Properties Section SHALL document property declaration syntax with block-bodied `get` and `set` accessors, expression-bodied accessors (using `=>`), and the `init` accessor, with at least one Tab_Block for block-bodied and one for expression-bodied forms
2. THE Properties Section SHALL document auto-implemented properties (where `get` and `set` accessors use a semicolon body with no explicit backing field), with at least one Tab_Block
3. THE Properties Section SHALL document property initializers (assigning a default value to a property with `= expression;` after the accessor block), with at least one Tab_Block
4. THE Properties Section SHALL document accessor-level access modifiers (e.g., `private set`) that restrict visibility of individual accessors, with at least one Tab_Block
5. THE Properties Section SHALL present each distinct concept (block-bodied accessors, expression-bodied accessors, auto-implemented properties, property initializers, accessor modifiers) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 20: Interfaces Section

**User Story:** As a reader, I want to learn about interfaces in Mist, so that I can define contracts that classes implement.

#### Acceptance Criteria

1. THE Interfaces Section SHALL document interface declaration syntax including the `interface` keyword, interface name, optional type parameters, and a body containing method signatures and property signatures (without implementations), with at least one Tab_Block showing a minimal interface
2. THE Interfaces Section SHALL document implementing an interface on a class by listing the interface name in the class's base clause after the colon, showing that the class must provide implementations for all interface members, with at least one Tab_Block
3. THE Interfaces Section SHALL document using interface types as parameter types and variable types to achieve polymorphism, with at least one Tab_Block
4. THE Interfaces Section SHALL present each distinct concept (interface declaration, class implementation, polymorphic usage) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 21: Traits Section

**User Story:** As a reader, I want to learn about traits in Mist, so that I can use structural typing contracts.

#### Acceptance Criteria

1. THE Traits Section SHALL document trait declaration syntax including the `trait` keyword, trait name, optional type parameters, optional parent traits, and a body containing field signatures, method signatures, and property signatures, with at least one Tab_Block showing a minimal trait
2. THE Traits Section SHALL explain structural satisfaction: a type satisfies a trait when it has public members whose names and types match all members declared in the trait, without requiring an explicit `implements` clause, with at least one Tab_Block demonstrating a class that satisfies a trait without naming it
3. THE Traits Section SHALL document the `with` block syntax for explicit member mapping (using `with TypeName : TraitName { sourceMember as traitMember; }`) when a type's member names differ from the trait's expected names, with at least one Tab_Block
4. THE Traits Section SHALL document using trait types as parameter types and variable types to accept any structurally-compatible value, with at least one Tab_Block
5. THE Traits Section SHALL present each distinct concept (trait declaration, structural satisfaction, `with` mapping, polymorphic usage) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#
6. THE Traits Section SHALL explain the difference between traits (structural typing — no explicit declaration of conformance required) and interfaces (nominal typing — class must explicitly list the interface in its base clause)

---

### Requirement 22: Enums Section

**User Story:** As a reader, I want to learn about enums in Mist, so that I can define named constant sets.

#### Acceptance Criteria

1. THE Enums Section SHALL document enum declaration with an explicit underlying type annotation (e.g., `enum Direction : int { ... }`), with at least one Tab_Block
2. THE Enums Section SHALL document name-only enums where members have no explicit value and values are auto-assigned sequentially starting from 0, with at least one Tab_Block
3. THE Enums Section SHALL document enums with mixed explicit and implicit values where some members specify a value after the colon and subsequent unspecified members increment from the last explicit value, with at least one Tab_Block
4. THE Enums Section SHALL document union underlying types for enums (where the underlying type is a type union such as `int | string`), with at least one Tab_Block
5. THE Enums Section SHALL document enums with user-defined struct or class underlying types (where each member's value is an instance of the underlying type), with at least one Tab_Block
6. THE Enums Section SHALL document implicit conversion from an enum value to its underlying type, with at least one Tab_Block
7. THE Enums Section SHALL document pattern matching on enum values using the `is` keyword, with at least one Tab_Block
8. THE Enums Section SHALL present each distinct concept (typed enum, name-only enum, mixed values, union underlying type, user-defined underlying type, implicit conversion, pattern matching) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 23: Method Overloading Section

**User Story:** As a reader, I want to learn about method overloading in Mist, so that I can define multiple methods with the same name but different signatures.

#### Acceptance Criteria

1. THE Method_Overloading Section SHALL document overloading by parameter count (two or more methods with the same name but different numbers of parameters), with at least one Tab_Block showing at least two overloads that differ in arity
2. THE Method_Overloading Section SHALL document overloading by parameter type (two or more methods with the same name and same number of parameters but different parameter types), with at least one Tab_Block showing at least two overloads that differ in type
3. THE Method_Overloading Section SHALL present each distinct concept (overloading by count, overloading by type) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 24: Extension Methods Section

**User Story:** As a reader, I want to learn about extension methods in Mist, so that I can add methods to existing types without modifying them.

#### Acceptance Criteria

1. THE Extension_Methods Section SHALL document extension method declaration syntax using the `extension(ReceiverType receiverName)` block containing method declarations, explaining that methods inside the block are callable on instances of the receiver type, with at least one Tab_Block
2. THE Extension_Methods Section SHALL document calling extension methods using dot-notation on an instance of the receiver type, showing that the call syntax is identical to a regular instance method call, with at least one Tab_Block
3. THE Extension_Methods Section SHALL present each distinct concept (declaration syntax, calling convention) in its own Tab_Block with the Mist_Tab and CSharp_Tab showing the emitted C#

---

### Requirement 25: Generics Section

**User Story:** As a reader, I want to learn about generics in Mist, so that I can write type-safe reusable code.

#### Acceptance Criteria

1. THE Generics Section SHALL document generic class declarations with one or more type parameters using the `<T>` syntax
2. THE Generics Section SHALL document generic struct declarations
3. THE Generics Section SHALL document generic interface declarations
4. THE Generics Section SHALL document generic method declarations with type parameters on the method
5. THE Generics Section SHALL document type argument syntax at call sites (e.g., `new GenericBox<int>(42)`, `Method<string>(arg)`)
6. THE Generics Section SHALL document generic types with multiple type parameters (e.g., `<K, V>`)
7. THE Generics Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 26: Generic Constraints Section

**User Story:** As a reader, I want to learn about generic constraints in Mist, so that I can restrict type parameters to specific capabilities.

#### Acceptance Criteria

1. THE Generic_Constraints Section SHALL document the `where T : constraint` syntax for type parameters
2. THE Generic_Constraints Section SHALL document the `class` constraint (reference type)
3. THE Generic_Constraints Section SHALL document the `struct` constraint (value type)
4. THE Generic_Constraints Section SHALL document constraining to interfaces, traits, and base classes
5. THE Generic_Constraints Section SHALL document constructor constraints (`new()` and parameterized `new(int)`)
6. THE Generic_Constraints Section SHALL document multiple constraints on a single type parameter (e.g., `where T : class, IPrintable, new()`)
7. THE Generic_Constraints Section SHALL document multiple `where` clauses for multi-parameter generics (e.g., `where K : IOrderable where V : new()`)
8. THE Generic_Constraints Section SHALL document constraints on generic methods
9. THE Generic_Constraints Section SHALL show each constraint kind in a separate Tab_Block with the emitted C#

---

### Requirement 27: Type Unions Section

**User Story:** As a reader, I want to learn about type unions in Mist, so that I can represent values that may be one of several types.

#### Acceptance Criteria

1. THE Type_Unions Section SHALL document union type syntax using the `|` operator (e.g., `int | string`)
2. THE Type_Unions Section SHALL document union-typed variables, parameters, and return types
3. THE Type_Unions Section SHALL document narrowing union types via pattern matching with `is` and binding the narrowed value
4. THE Type_Unions Section SHALL document multi-type unions with three or more constituent types (e.g., `int | string | bool`)
5. THE Type_Unions Section SHALL document reassignment of a union-typed variable from one constituent type to another
6. THE Type_Unions Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 28: Type Aliases Section

**User Story:** As a reader, I want to learn about type aliases in Mist, so that I can create readable names for complex types.

#### Acceptance Criteria

1. THE Type_Aliases Section SHALL document simple type alias declaration syntax (`type Name = ExistingType;`)
2. THE Type_Aliases Section SHALL document union type aliases (`type Result = int | string | bool;`)
3. THE Type_Aliases Section SHALL document aliases to closed generic types (`type IntBox = GenericBox<int>;`)
4. THE Type_Aliases Section SHALL document generic type aliases with type parameters (`type Box<T> = GenericBox<T>;`)
5. THE Type_Aliases Section SHALL explain that aliases are transparent and interchangeable with the underlying type
6. THE Type_Aliases Section SHALL show each alias variant in a separate Tab_Block with the emitted C#

---

### Requirement 29: Newtypes Section

**User Story:** As a reader, I want to learn about newtypes in Mist, so that I can create distinct types that wrap existing types.

#### Acceptance Criteria

1. THE Newtypes Section SHALL document newtype declaration syntax (`type Name = new SourceType;`)
2. THE Newtypes Section SHALL document newtypes wrapping primitive types, structs, and classes
3. THE Newtypes Section SHALL explain the difference between newtypes and type aliases: newtypes create a distinct type that is not interchangeable with the source type, while aliases are transparent
4. THE Newtypes Section SHALL document member access on newtype instances (accessing the wrapped type's public members directly)
5. THE Newtypes Section SHALL document explicit casting between a newtype and its source type using `(NewType)value` and `(SourceType)newtypeValue`
6. THE Newtypes Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 30: Pattern Matching Section

**User Story:** As a reader, I want to learn about pattern matching in Mist, so that I can write expressive conditional logic.

#### Acceptance Criteria

1. THE Pattern_Matching Section SHALL document the `is` expression for type patterns with binding (e.g., `if (shape is Circle c)`)
2. THE Pattern_Matching Section SHALL document bare type patterns that perform a type check without binding a variable
3. THE Pattern_Matching Section SHALL document the `var` pattern for binding without a type check (e.g., `var x`)
4. THE Pattern_Matching Section SHALL document property patterns that match against object properties using subpatterns (e.g., `{ Radius: > 5 }`)
5. THE Pattern_Matching Section SHALL document relational patterns using comparison operators (`>`, `<`, `>=`, `<=`)
6. THE Pattern_Matching Section SHALL document logical pattern combinators (`and`, `or`, `not`)
7. THE Pattern_Matching Section SHALL document list patterns with element matching and slice patterns (`..`)
8. THE Pattern_Matching Section SHALL show each pattern kind in a separate Tab_Block with the emitted C#

---

### Requirement 31: Switch Expressions Section

**User Story:** As a reader, I want to learn about switch expressions in Mist, so that I can write concise multi-branch logic.

#### Acceptance Criteria

1. THE Switch_Expressions Section SHALL document switch expression syntax as a postfix operation (`value switch { pattern => result, ... }`)
2. THE Switch_Expressions Section SHALL document constant pattern arms (integer, string, boolean literals)
3. THE Switch_Expressions Section SHALL document type pattern arms for narrowing union types
4. THE Switch_Expressions Section SHALL document the discard pattern (`var _` or `_`) as a catch-all arm
5. THE Switch_Expressions Section SHALL document `when` guards on switch arms for additional conditions
6. THE Switch_Expressions Section SHALL document relational and logical patterns within switch arms
7. THE Switch_Expressions Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 32: Cast Operators Section

**User Story:** As a reader, I want to learn about cast operators in Mist, so that I can convert between types.

#### Acceptance Criteria

1. THE Cast_Operators Section SHALL document the `implicit operator` declaration syntax for automatic conversions
2. THE Cast_Operators Section SHALL document the `explicit operator` declaration syntax for conversions requiring a cast expression
3. THE Cast_Operators Section SHALL explain that implicit casts are applied automatically at assignment and argument sites, while explicit casts require the `(TargetType)expression` syntax
4. THE Cast_Operators Section SHALL explain that cast operators are resolved against the compile-time (static) type of the expression, not the runtime type
5. THE Cast_Operators Section SHALL show implicit and explicit cast examples in separate Tab_Blocks with the emitted C#

---

### Requirement 33: Refinement Types Section

**User Story:** As a reader, I want to learn about refinement types in Mist, so that I can express value constraints at the type level.

#### Acceptance Criteria

1. THE Refinement_Types Section SHALL document simple refinement type syntax with a single relational predicate (e.g., `type AdultAge = int >= 18;`)
2. THE Refinement_Types Section SHALL document compound refinement types using `and` combinators (e.g., `type Percentage = int >= 0 and <= 100;`)
3. THE Refinement_Types Section SHALL document property pattern refinements on class types (e.g., `type ValidRange = Range { Start: >= 0 };`)
4. THE Refinement_Types Section SHALL explain that refinement constraints are enforced at compile time for literal assignments and at runtime via pattern matching with `is`
5. THE Refinement_Types Section SHALL document refinement types as variable types, parameter types, return types, and field types
6. THE Refinement_Types Section SHALL document that arithmetic on refined values produces the base type
7. THE Refinement_Types Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 34: Dependent Types Section

**User Story:** As a reader, I want to learn about dependent types in Mist, so that I can express types that depend on values.

#### Acceptance Criteria

1. THE Dependent_Types Section SHALL document dependent type parameter syntax using `Name:PrimitiveType` in the type parameter list (e.g., `class FixedBuffer<Size:int>`)
2. THE Dependent_Types Section SHALL document the allowed value types for dependent parameters (int, uint, long, ulong, short, ushort, byte, sbyte, bool, char)
3. THE Dependent_Types Section SHALL document that dependent type parameters can be used as values within the type body (e.g., `return Size;`)
4. THE Dependent_Types Section SHALL document dependent types on classes, structs, interfaces, traits, and methods
5. THE Dependent_Types Section SHALL explain how dependent types relate to refinement types: refinement types constrain value ranges of existing types, while dependent types parameterize types by compile-time constant values
6. THE Dependent_Types Section SHALL document type identity semantics where different dependent values produce distinct types (e.g., `FixedBuffer<5>` is not assignable to `FixedBuffer<10>`)
7. THE Dependent_Types Section SHALL show each concept in a separate Tab_Block with the emitted C#

---

### Requirement 35: Ownership Section

**User Story:** As a reader, I want to learn about Mist's ownership system, so that I can control how references are shared and mutated.

#### Acceptance Criteria

1. THE Ownership Section SHALL explain the five ownership tiers in the following order: unrestricted (default, no annotation, standard reference semantics), owned (sole ownership, storable, full control), borrowed (exclusive mutable access, non-storable), frozen (immutable view that blocks owner mutation, non-storable), and readonly ref (live readonly view, non-storable).
2. THE Ownership Section SHALL document the `owned` keyword for declaring owned variables and owned type declarations (`owned class`), explaining that owned bindings require a uniquely-owned value from a constructor call, an owned-returning function, or a move from an existing owned binding.
3. THE Ownership Section SHALL document move semantics: assigning an owned value to another owned binding transfers ownership, the source variable enters a moved state and cannot be accessed afterward, and the compiler emits a diagnostic error on use-after-move.
4. THE Ownership Section SHALL document the lending rules: owned variables can be lent as readonly ref (owner transitions to LentReadonly state), as frozen (owner transitions to LentFrozen state and cannot mutate), or as borrowed (owner transitions to LentRef state and cannot be accessed until the borrow ends), with only one mutable borrow permitted at a time.
5. THE Ownership Section SHALL explain how value types (structs) satisfy all ownership tiers because they are copied rather than referenced.
6. THE Ownership Section SHALL show at least one Tab_Block per ownership tier demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 36: Ref Parameters Section

**User Story:** As a reader, I want to learn about ref parameters in Mist, so that I can pass values by reference.

#### Acceptance Criteria

1. THE Ref_Parameters Section SHALL document `readonly ref` parameter syntax (the `readonly ref` specifier before the type in a parameter declaration) and its semantics: the callee receives a live readonly view that is non-storable and cannot be used to mutate the value.
2. THE Ref_Parameters Section SHALL document `borrowed` parameter syntax (the `borrowed` specifier before the type in a parameter declaration) and its semantics: the callee receives exclusive mutable access for the duration of the call, the caller cannot access the lent variable until the borrow ends, and only one mutable borrow is permitted at a time.
3. THE Ref_Parameters Section SHALL document the `ref` parameter specifier and explain how it differs from `readonly ref` and `borrowed`.
4. THE Ref_Parameters Section SHALL show at least one Tab_Block per ref parameter kind (`readonly ref`, `borrowed`, `ref`) demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 37: Readonly Contexts Section

**User Story:** As a reader, I want to learn about readonly contexts in Mist, so that I can enforce immutability guarantees.

#### Acceptance Criteria

1. THE Readonly_Contexts Section SHALL document the `frozen` parameter modifier: its syntax (the `frozen` specifier before the type in a parameter declaration) and its semantics (the callee receives an immutable view that is non-storable).
2. THE Readonly_Contexts Section SHALL explain that while a frozen borrow is active, the owner transitions to the LentFrozen state and cannot mutate, move, or lend the variable as borrowed until the frozen borrow ends.
3. THE Readonly_Contexts Section SHALL document the `readonly` block statement syntax (`readonly { ... }`) and explain that all variables accessed within the block are treated as immutable for the duration of the block.
4. THE Readonly_Contexts Section SHALL show at least one Tab_Block per construct (`frozen` parameter, `readonly` block) demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 38: Structured Context Section

**User Story:** As a reader, I want to learn about structured context in Mist, so that I can use context-passing patterns.

#### Acceptance Criteria

1. THE Structured_Context Section SHALL document all context declaration forms: `with var identifier = expression`, `with TypeReference identifier = expression`, `with TypeReference _ = expression`, and `with expression`, explaining that each pushes a value onto a per-type context stack scoped to the declaring block.
2. THE Structured_Context Section SHALL document the context block syntax `with(expression) { ... }` and explain that the context value is pushed for the block duration only and automatically popped on block exit.
3. THE Structured_Context Section SHALL document the method `with` clause syntax (`with TypeReference identifier` after the parameter list) and explain that it binds the current context value for the specified type as a read-only local variable within the method body.
4. THE Structured_Context Section SHALL explain that context values are keyed by exact nominal type (no inheritance lookup), propagate through intermediate methods transparently, and that the compiler verifies context availability at compile time.
5. THE Structured_Context Section SHALL show at least one Tab_Block per context form (declaration statement, context block, method binding) demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 39: The Unknown Type Section

**User Story:** As a reader, I want to learn about the unknown type in Mist, so that I can work with values of indeterminate type safely.

#### Acceptance Criteria

1. THE Unknown_Type Section SHALL explain that `unknown` is the top type: any value of any type can be assigned to a variable of type `unknown`, but no operations (method calls, property access, arithmetic) are permitted on an `unknown` value without first narrowing it.
2. THE Unknown_Type Section SHALL document narrowing `unknown` values via pattern matching with the `is` keyword, explaining that after a successful `is` check the value is treated as the narrowed type within the guarded scope.
3. THE Unknown_Type Section SHALL document `unknown` as a tuple element type (e.g., `(unknown, int)`) and explain how individual elements can be narrowed independently.
4. THE Unknown_Type Section SHALL show at least one Tab_Block per usage pattern (assignment to unknown, narrowing via `is`, unknown in tuples) demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 40: Default Keyword Section

**User Story:** As a reader, I want to learn about the default keyword in Mist, so that I can obtain default values for types.

#### Acceptance Criteria

1. THE Default_Keyword Section SHALL document both `default` keyword forms: `default(TypeReference)` which produces the default value for the specified type, and the bare `default` literal which infers the type from context.
2. THE Default_Keyword Section SHALL explain what `default` produces for each type category: `0` for numeric types, `false` for bool, `'\0'` for char, `""` for string, `null` for reference types, and the zero-initialized struct for value types.
3. THE Default_Keyword Section SHALL document the `default` block declaration syntax within class and struct bodies (`default { fieldName = value ... }`) and explain that it specifies field-level default values used when no explicit initializer is provided.
4. THE Default_Keyword Section SHALL show at least one Tab_Block per form (`default(T)`, bare `default`, `default` block in a type declaration) demonstrating the Mist syntax and the emitted C# output.

---

### Requirement 41: Nested Scoping Section

**User Story:** As a reader, I want to learn about nested scoping in Mist, so that I can understand variable visibility in nested blocks.

#### Acceptance Criteria

1. THE Nested_Scoping Section SHALL document that each pair of braces (`{ }`) creates a new lexical scope, and variables declared within a block are not accessible outside that block.
2. THE Nested_Scoping Section SHALL explain variable shadowing rules: an inner scope may declare a variable with the same name as an outer scope variable, the inner declaration shadows the outer one for the duration of the inner scope, and the outer variable is restored when the inner scope exits.
3. THE Nested_Scoping Section SHALL explain how ownership and context declarations interact with nested scopes: context values and borrows are popped or released when their declaring scope exits.
4. THE Nested_Scoping Section SHALL show at least one Tab_Block demonstrating nested scoping with shadowing in Mist and the emitted C# output.

---

### Requirement 42: Standard Library Overview Section

**User Story:** As a reader, I want an overview of the Mist standard library, so that I can discover available built-in functionality.

#### Acceptance Criteria

1. THE Standard_Library_Overview Section SHALL describe the purpose of the standard library: providing foundational types and functions that are available to all Mist programs without explicit import.
2. THE Standard_Library_Overview Section SHALL list each available module or namespace in the standard library with a one-sentence description of its purpose.
3. THE Standard_Library_Overview Section SHALL provide brief descriptions (one to two sentences each) of key types and functions within each listed module, identifying their name, purpose, and which type category they belong to (class, struct, function, or trait).

---

### Requirement 43: CLI Usage Section

**User Story:** As a reader, I want to learn how to use the Mist CLI, so that I can compile and run projects from the command line.

#### Acceptance Criteria

1. THE CLI_Usage Section SHALL document the two available CLI commands: `build` (compiles the project and produces output artifacts) and `run` (compiles and then executes the resulting artifact).
2. THE CLI_Usage Section SHALL document all command-line flags and options: `--backend <id>` (selects the code generation backend, defaults to `csharp`), `--output <dir>` (sets the transpiled output directory, defaults to `<project>/out`), `--bin <dir>` (sets the compiled binary directory, defaults to `<project>/bin`), `--mode <compile|lex>` (selects compiler mode, defaults to `compile`), and `--quiet` / `-q` (suppresses progress output).
3. THE CLI_Usage Section SHALL document the positional argument: an optional project folder path (defaults to the current working directory), explaining that individual file paths are not accepted.
4. THE CLI_Usage Section SHALL provide usage examples for common workflows: building a project in the current directory, running a project from a specified folder, and using lex mode for debugging.

---

### Requirement 44: Editor Support Section

**User Story:** As a reader, I want to learn about editor support for Mist, so that I can set up syntax highlighting and tooling in my editor.

#### Acceptance Criteria

1. THE Editor_Support Section SHALL document the available VS Code extension (`mist-syntax-highlighting`) located in the `Editor/mist-syntax-highlighting/` directory of the repository.
2. THE Editor_Support Section SHALL provide installation instructions: opening the extension folder in VS Code and using the "Developer: Install Extension from Location" command or copying the folder into the VS Code extensions directory.
3. THE Editor_Support Section SHALL describe the features the extension provides: syntax highlighting via a TextMate grammar (`.tmLanguage.json`), language configuration (comment toggling, bracket matching, auto-closing pairs), and file association for the `.mist` extension.

---

### Requirement 45: Project Configuration Section

**User Story:** As a reader, I want a reference for the `mist.toml` configuration file, so that I can configure my projects correctly.

#### Acceptance Criteria

1. THE Project_Configuration Section SHALL document all available fields in `mist.toml`: `name` (required string, 1–128 characters, alphanumeric plus hyphens, underscores, and periods), `namespace_root` (optional string, same character constraints as `name`, defaults to the value of `name` when omitted), and `[[references]]` (optional array of tables, each containing a required `path` string pointing to a referenced project's directory or `mist.toml` file).
2. THE Project_Configuration Section SHALL provide at least two example configurations: a minimal single-project configuration (name only) and a multi-project configuration with `namespace_root` and one or more `[[references]]` entries.
3. THE Project_Configuration Section SHALL document project references: the `path` field accepts relative or absolute paths, relative paths are resolved from the project directory, paths pointing to a directory are automatically resolved to `<directory>/mist.toml`, and circular references are detected and reported as errors.
