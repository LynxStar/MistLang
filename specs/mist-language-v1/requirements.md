# Requirements Document

## Introduction

This document specifies the concrete language features for Mist v1, building on the compiler architecture defined in `specs/mist-compiler-architecture/`. The architecture spec defines the pipeline (lexer → parser → semantic model → analyzers → backend) and the Storm grammar system. This spec defines what the Mist language actually looks like and how each pipeline stage handles it.

Mist v1 mirrors a subset of C# syntax 1:1. The goal is a working end-to-end language: a developer writes Mist source files using familiar C#-like syntax, runs the `mist` CLI, and gets a working .NET executable. The language is intentionally minimal — no generics, no inheritance, no async, no properties — but complete enough to write real console applications that exercise every supported feature.

The semantic model does not include a standard library. Instead, the C# transpiler backend provides "ambient types" — types and namespaces that exist in a default .NET console app (e.g., `System.Console`, `System.String`, `System.Math`). User code references these as if they were defined in the program, and the transpiler emits corresponding `using` statements in the generated C#.

## Glossary

- **Mist_Source_File**: A text file with the `.mist` extension containing Mist source code.
- **Compilation_Unit**: A single Mist_Source_File processed through the compiler pipeline.
- **Storm_Grammar**: The Storm grammar file (`Mist.storm`) that defines the lexical and syntactic structure of Mist.
- **Lexer**: The Storm-generated lexer that tokenizes Mist source text into a Token_Stream.
- **Parser**: The Storm-generated parser that produces an AST from a Token_Stream.
- **Semantic_Model**: The immutable representation of resolved symbols, types, scopes, and semantic information built from one or more ASTs.
- **Semantic_Model_Builder**: The component that walks ASTs and constructs the Semantic_Model.
- **Type_Checker**: The built-in analyzer that verifies Mist type rules against the Semantic_Model.
- **CSharp_Transpiler**: The backend that emits C# source code from the Semantic_Model and invokes `dotnet build`.
- **Ambient_Type**: A type or namespace provided by the CSharp_Transpiler backend that is injected into the Semantic_Model without being defined in Mist source code. For the C# backend, these correspond to types available in a default .NET console app.
- **Namespace_Declaration**: A Mist declaration that groups types under a dotted name, either file-scoped (`namespace Foo.Bar;`) or block-scoped (`namespace Foo.Bar { ... }`).
- **Using_Declaration**: A `using Namespace;` statement that imports types from a namespace into the current scope.
- **Class_Declaration**: A `class` type declaration containing fields, constructors, and methods.
- **Struct_Declaration**: A `struct` type declaration containing fields, constructors, and methods.
- **Field_Declaration**: A member variable declaration within a Class_Declaration or Struct_Declaration, with an optional access modifier.
- **Constructor_Declaration**: A special method that initializes a Class_Declaration or Struct_Declaration instance, optionally chaining to another constructor via `this()`.
- **Method_Declaration**: A named function declared within a Class_Declaration or Struct_Declaration.
- **Access_Modifier**: One of `public`, `private`, or `static`, applied to fields, constructors, or methods.
- **Entry_Point**: A `static void Main()` or `static void Main(string[] args)` method that serves as the program's starting point.
- **Primitive_Type**: One of the built-in value types: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`, `void`.
- **String_Interpolation**: A string literal prefixed with `$` that contains embedded expressions in `{...}` placeholders, e.g., `$"Hello {name}"`.
- **Variable_Declaration**: A local variable introduced with an explicit type or with `var` for type inference.
- **Member_Access**: Dot-syntax access to a field or method on an object, e.g., `obj.Field` or `obj.Method()`.
- **Array_Type**: A type followed by `[]` denoting a single-dimensional array, e.g., `int[]`.
- **Sample_Application**: A Mist console application that exercises every implemented language feature and produces a working .NET executable.

## Requirements

### Requirement 1: Storm Grammar — Tokens

**User Story:** As a language designer, I want the Mist Storm grammar to define all lexical tokens needed by the Mist v1 language, so that the generated lexer correctly tokenizes Mist source files.

#### Acceptance Criteria

1. THE Storm_Grammar SHALL define hidden tokens for whitespace (spaces, tabs, carriage returns, newlines) and line comments (`// ...`).
2. THE Storm_Grammar SHALL define tokens for all keywords: `namespace`, `using`, `class`, `struct`, `public`, `private`, `static`, `void`, `new`, `return`, `if`, `else`, `while`, `for`, `foreach`, `in`, `break`, `var`, `true`, `false`, `this`.
3. THE Storm_Grammar SHALL define tokens for all Primitive_Type keywords: `int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`, `string`.
4. THE Storm_Grammar SHALL define tokens for all operators and punctuation: `+`, `-`, `*`, `/`, `%`, `=`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`, `.`, `,`, `;`, `:`, `(`, `)`, `{`, `}`, `[`, `]`, `$"`.
5. THE Storm_Grammar SHALL define tokens for identifiers matching the pattern `[a-zA-Z_][a-zA-Z0-9_]*`.
6. THE Storm_Grammar SHALL define tokens for integer literals including decimal (`42`), hexadecimal (`0xFF`, `0XFF`), and type suffixes (`L`, `l`, `U`, `u`, `UL`, `ul`, `Lu`, `lU`, and other valid C# combinations).
7. THE Storm_Grammar SHALL define tokens for floating-point literals including decimal notation (`3.14`) with type suffixes (`f`, `F`, `d`, `D`, `m`, `M`).
8. THE Storm_Grammar SHALL define tokens for string literals enclosed in double quotes (`"hello"`), supporting standard C# escape sequences (`\\`, `\"`, `\n`, `\r`, `\t`, `\0`).
9. THE Storm_Grammar SHALL define tokens for interpolated string start (`$"`), interpolated string text segments, interpolated expression holes (`{` and `}`), and interpolated string end (`"`).
10. THE Storm_Grammar SHALL define tokens for character literals enclosed in single quotes (`'a'`), supporting the same escape sequences as string literals.
11. WHEN the Lexer encounters a keyword token that matches the identifier pattern, THE Lexer SHALL produce the keyword token kind rather than an identifier token kind, using declaration-order priority in the Storm grammar.

### Requirement 2: Storm Grammar — Parser Rules

**User Story:** As a language designer, I want the Mist Storm grammar to define parser rules for all Mist v1 syntactic constructs, so that the generated parser produces a well-typed AST from valid Mist source.

#### Acceptance Criteria

1. THE Storm_Grammar SHALL define a `CompilationUnit` rule as the top-level entry point, consisting of zero or more Using_Declarations followed by an optional Namespace_Declaration and zero or more type declarations.
2. THE Storm_Grammar SHALL define rules for file-scoped namespace declarations (`namespace Dotted.Name;`) and block-scoped namespace declarations (`namespace Dotted.Name { ... }`).
3. THE Storm_Grammar SHALL define rules for Using_Declarations in the form `using Dotted.Name;`.
4. THE Storm_Grammar SHALL define rules for Class_Declarations in the form `[modifiers] class Name { members }`.
5. THE Storm_Grammar SHALL define rules for Struct_Declarations in the form `[modifiers] struct Name { members }`.
6. THE Storm_Grammar SHALL define rules for Field_Declarations in the form `[modifiers] Type name;` and `[modifiers] Type name = expression;`.
7. THE Storm_Grammar SHALL define rules for Constructor_Declarations in the form `[modifiers] TypeName(parameters) [: this(arguments)] { body }`.
8. THE Storm_Grammar SHALL define rules for Method_Declarations in the form `[modifiers] ReturnType Name(parameters) { body }`.
9. THE Storm_Grammar SHALL define rules for access modifiers (`public`, `private`, `static`) that may appear in any combination before a member declaration.
10. THE Storm_Grammar SHALL define rules for parameter lists as comma-separated `Type name` pairs enclosed in parentheses.
11. THE Storm_Grammar SHALL define rules for type references including Primitive_Types, user-defined type names, array types (`Type[]`), and `void`.

### Requirement 3: Storm Grammar — Statements

**User Story:** As a language designer, I want the Mist grammar to support all v1 statement forms, so that Mist programs can express control flow and variable manipulation.

#### Acceptance Criteria

1. THE Storm_Grammar SHALL define a rule for variable declaration statements in the form `Type name = expression;` and `var name = expression;`.
2. THE Storm_Grammar SHALL define a rule for expression statements in the form `expression;`.
3. THE Storm_Grammar SHALL define a rule for `return` statements in the form `return;` and `return expression;`.
4. THE Storm_Grammar SHALL define a rule for `if`/`else` statements in the form `if (condition) { body } [else { body }]` and `if (condition) { body } else if (condition) { body }`.
5. THE Storm_Grammar SHALL define a rule for `while` statements in the form `while (condition) { body }`.
6. THE Storm_Grammar SHALL define a rule for `for` statements in the form `for (initializer; condition; iterator) { body }`.
7. THE Storm_Grammar SHALL define a rule for `foreach` statements in the form `foreach (Type name in expression) { body }`.
8. THE Storm_Grammar SHALL define a rule for assignment statements in the form `target = expression;`.
9. THE Storm_Grammar SHALL define a rule for block statements as zero or more statements enclosed in `{ }`.

### Requirement 4: Storm Grammar — Expressions

**User Story:** As a language designer, I want the Mist grammar to support all v1 expression forms with correct operator precedence, so that Mist expressions evaluate as a C# developer would expect.

#### Acceptance Criteria

1. THE Storm_Grammar SHALL define expression rules that enforce C#-compatible operator precedence: unary (`!`, unary `-`) binds tightest, then multiplicative (`*`, `/`, `%`), then additive (`+`, `-`), then relational (`<`, `>`, `<=`, `>=`), then equality (`==`, `!=`), then logical AND (`&&`), then logical OR (`||`), with assignment (`=`) binding loosest.
2. THE Storm_Grammar SHALL define rules for primary expressions: literals (integer, float, string, interpolated string, char, boolean), identifiers, `this`, and parenthesized expressions.
3. THE Storm_Grammar SHALL define rules for method invocation expressions in the form `expression(arguments)` and `expression.Name(arguments)`.
4. THE Storm_Grammar SHALL define rules for member access expressions in the form `expression.Name`.
5. THE Storm_Grammar SHALL define rules for object creation expressions in the form `new TypeName(arguments)`.
6. THE Storm_Grammar SHALL define rules for array creation expressions in the form `new Type[size]`.
7. THE Storm_Grammar SHALL define rules for array indexing expressions in the form `expression[index]`.
8. THE Storm_Grammar SHALL define rules for interpolated string expressions in the form `$"text {expression} text"`, supporting multiple interpolation holes and nested expressions within holes.

### Requirement 5: Semantic Model — Symbol Kinds and Type Kinds

**User Story:** As a compiler developer, I want the semantic model to represent all Mist v1 language constructs as symbols and types, so that analyzers and the backend can reason about the program.

#### Acceptance Criteria

1. THE `SymbolKind` enum SHALL include the following members: `Namespace`, `Class`, `Struct`, `Field`, `Constructor`, `Method`, `Parameter`, `LocalVariable`.
2. THE `TypeKind` enum SHALL include exactly two members: `Class` (reference types) and `Struct` (value types). `TypeKind` classifies type constructors at the kind level (tier 3 in the value → type → kind hierarchy), not individual types.
3. THE Semantic_Model SHALL represent each Namespace_Declaration as a symbol with `SymbolKind.Namespace`.
4. THE Semantic_Model SHALL represent each Class_Declaration as a symbol with `SymbolKind.Class` and a type with `TypeKind.Class`.
5. THE Semantic_Model SHALL represent each Struct_Declaration as a symbol with `SymbolKind.Struct` and a type with `TypeKind.Struct`.
6. THE Semantic_Model SHALL represent each Field_Declaration as a symbol with `SymbolKind.Field`, associated with the declaring type's scope.
7. THE Semantic_Model SHALL represent each Constructor_Declaration as a symbol with `SymbolKind.Constructor`, associated with the declaring type's scope.
8. THE Semantic_Model SHALL represent each Method_Declaration as a symbol with `SymbolKind.Method`, associated with the declaring type's scope.
9. THE Semantic_Model SHALL represent each parameter in a parameter list as a symbol with `SymbolKind.Parameter`, associated with the containing method or constructor scope.
10. THE Semantic_Model SHALL represent each local variable declaration as a symbol with `SymbolKind.LocalVariable`, associated with the enclosing block scope.
11. THE Semantic_Model SHALL represent each Primitive_Type (`int`, `bool`, `char`, etc.) as a well-known type with `TypeKind.Struct`, since all C# primitives are value types.
12. THE Semantic_Model SHALL represent `string` as a well-known type with `TypeKind.Class`.
13. THE Semantic_Model SHALL represent array types as types with `TypeKind.Class`, referencing the element type.
14. THE Semantic_Model SHALL represent `void` as a built-in unit type with `TypeKind.Struct`.
15. THE Semantic_Model SHALL represent unresolved or erroneous types with a sentinel `Unknown` type that has no `TypeKind` — it exists only as an error marker during analysis and SHALL NOT propagate to the backend.

### Requirement 6: Semantic Model — Scope and Name Resolution

**User Story:** As a compiler developer, I want the semantic model to resolve names through nested scopes, so that identifiers in Mist code bind to the correct declarations.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL construct a scope hierarchy: global scope → namespace scope → type scope → method/constructor scope → block scope.
2. WHEN the Semantic_Model_Builder encounters a Using_Declaration, THE Semantic_Model_Builder SHALL make all types from the referenced namespace visible in the current file's scope.
3. WHEN the Semantic_Model_Builder resolves an identifier, THE Semantic_Model_Builder SHALL search the current scope first, then walk up the scope chain to parent scopes until a match is found.
4. IF the Semantic_Model_Builder encounters an identifier that cannot be resolved in any enclosing scope or imported namespace, THEN THE Semantic_Model_Builder SHALL emit a Diagnostic with the identifier's source span and the message indicating the name is undefined.
5. IF the Semantic_Model_Builder encounters a duplicate declaration within the same scope, THEN THE Semantic_Model_Builder SHALL emit a Diagnostic identifying both declarations.
6. THE Semantic_Model_Builder SHALL resolve `this` within instance methods and constructors to the enclosing type.
7. THE Semantic_Model_Builder SHALL resolve Member_Access expressions by looking up the member name in the target expression's resolved type.

### Requirement 7: Semantic Model — Ambient Types

**User Story:** As a Mist developer, I want to use .NET framework types like `Console.WriteLine` without defining them in Mist, so that I can write useful programs from day one.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL accept a set of Ambient_Type definitions provided by the backend before semantic model construction begins.
2. THE Semantic_Model_Builder SHALL register Ambient_Types in the global scope so that user code can reference them as if they were declared in the Mist program.
3. WHEN the CSharp_Transpiler is selected as the backend, THE CSharp_Transpiler SHALL provide Ambient_Type definitions for namespaces and types available in a default .NET 10 console application, including at minimum `System`, `System.Console`, `System.String`, `System.Math`, `System.Array`, and `System.Convert`.
4. THE Semantic_Model SHALL treat Ambient_Types identically to user-defined types for the purposes of name resolution and type checking.
5. WHEN the CSharp_Transpiler emits C# source, THE CSharp_Transpiler SHALL emit `using` statements corresponding to the namespaces of all referenced Ambient_Types.

### Requirement 8: Type Checker — Type Rules

**User Story:** As a Mist developer, I want the compiler to catch type errors at compile time, so that I get clear feedback before running my program.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that the right-hand side of every assignment is assignable to the declared type of the left-hand side.
2. THE Type_Checker SHALL verify that every argument in a method invocation is assignable to the corresponding parameter type.
3. THE Type_Checker SHALL verify that arithmetic operators (`+`, `-`, `*`, `/`, `%`) are applied only to numeric Primitive_Types (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`).
4. THE Type_Checker SHALL verify that comparison operators (`<`, `>`, `<=`, `>=`) are applied only to numeric Primitive_Types.
5. THE Type_Checker SHALL verify that equality operators (`==`, `!=`) are applied to operands of compatible types.
6. THE Type_Checker SHALL verify that logical operators (`&&`, `||`, `!`) are applied only to `bool` operands.
7. THE Type_Checker SHALL verify that conditions in `if`, `while`, and `for` statements are of type `bool`.
8. THE Type_Checker SHALL verify that `return` statements return a value assignable to the enclosing method's declared return type, or return no value when the return type is `void`.
9. WHEN a variable is declared with `var`, THE Type_Checker SHALL infer the variable's type from the initializer expression.
10. IF a `var` declaration has no initializer expression, THEN THE Type_Checker SHALL emit a Diagnostic indicating that `var` requires an initializer.
11. THE Type_Checker SHALL verify that array indexing expressions use an integer index and that the target expression is an array type.
12. THE Type_Checker SHALL verify that `new TypeName(arguments)` references a type with a matching constructor.
13. THE Type_Checker SHALL verify that `new Type[size]` uses an integer size expression and that `Type` is a valid element type.
14. THE Type_Checker SHALL verify that constructor chaining via `: this(arguments)` references a constructor on the same type with matching parameter types.

### Requirement 9: Type Checker — Access Modifiers

**User Story:** As a Mist developer, I want the compiler to enforce access modifiers, so that encapsulation is respected at compile time.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that `private` members are accessed only from within the declaring type.
2. THE Type_Checker SHALL verify that `public` members are accessible from any scope.
3. WHEN no access modifier is specified on a member, THE Type_Checker SHALL treat the member as `private` by default.
4. THE Type_Checker SHALL verify that `static` members are accessed through the type name, not through an instance.
5. THE Type_Checker SHALL verify that instance members are accessed through an instance, not through the type name.

### Requirement 10: Type Checker — Entry Point Validation

**User Story:** As a Mist developer, I want the compiler to validate that my program has a valid entry point, so that the compiled executable can run.

#### Acceptance Criteria

1. THE Type_Checker SHALL verify that exactly one class in the Compilation contains a method with the signature `static void Main()` or `static void Main(string[] args)`.
2. IF no Entry_Point is found in the Compilation, THEN THE Type_Checker SHALL emit a Diagnostic indicating that the program has no entry point.
3. IF multiple Entry_Points are found in the Compilation, THEN THE Type_Checker SHALL emit a Diagnostic identifying all Entry_Point locations.

### Requirement 11: C# Transpiler — Code Emission

**User Story:** As a Mist developer, I want the transpiler to produce correct, readable C# from my Mist source, so that the generated code compiles and runs on .NET.

#### Acceptance Criteria

1. THE CSharp_Transpiler SHALL emit one `.cs` file per Mist_Source_File in the Compilation.
2. THE CSharp_Transpiler SHALL emit `using` directives at the top of each generated `.cs` file for all namespaces referenced by the Mist source or required by Ambient_Types.
3. THE CSharp_Transpiler SHALL emit namespace declarations that mirror the Mist source's Namespace_Declarations.
4. THE CSharp_Transpiler SHALL emit class and struct declarations with their access modifiers, fields, constructors, and methods.
5. THE CSharp_Transpiler SHALL emit method bodies with all statements and expressions translated to syntactically valid C#.
6. THE CSharp_Transpiler SHALL emit `var` declarations as `var` in the generated C#, preserving type inference.
7. THE CSharp_Transpiler SHALL emit constructor chaining (`: this(args)`) in the generated C# constructor syntax.
8. THE CSharp_Transpiler SHALL emit array declarations, creation, and indexing using C# array syntax.
9. THE CSharp_Transpiler SHALL emit string interpolation expressions as C# interpolated strings (`$"...{expr}..."`).
10. THE CSharp_Transpiler SHALL emit a `.csproj` file targeting .NET 10 with `<OutputType>Exe</OutputType>` and `<Deterministic>true</Deterministic>`.
11. AFTER emitting all C# source files, THE CSharp_Transpiler SHALL invoke `dotnet build` on the emitted project and report the resulting assembly path on success.
12. IF `dotnet build` reports errors, THEN THE CSharp_Transpiler SHALL surface those errors as Diagnostics with `PipelineStage.Backend`.

### Requirement 12: Mist Grammar — Pretty Printer

**User Story:** As a compiler developer, I want a pretty printer that formats a Mist AST back into valid Mist source text, so that I can verify parser correctness through round-trip testing.

#### Acceptance Criteria

1. THE Mist compiler SHALL include a Pretty_Printer that accepts a Mist AST and produces a Mist source text string.
2. THE Pretty_Printer SHALL produce output that is syntactically valid Mist source — parseable by the Mist parser without errors.
3. THE Pretty_Printer SHALL preserve the semantic meaning of the AST: the printed source, when re-parsed and semantically analyzed, SHALL produce an equivalent Semantic_Model.
4. FOR ALL valid Mist ASTs, parsing the Pretty_Printer output and then pretty-printing the resulting AST SHALL produce identical output to the first pretty-print (idempotence: `print(parse(print(ast))) == print(ast)`).

### Requirement 13: Sample Application

**User Story:** As a Mist developer, I want a sample application that demonstrates every language feature, so that I can verify the compiler works end-to-end and use it as a reference.

#### Acceptance Criteria

1. THE Mist project SHALL include a sample Mist console application in a `samples/` directory.
2. THE Sample_Application SHALL use `namespace`, `using`, `class`, and `struct` declarations.
3. THE Sample_Application SHALL use `public`, `private`, and `static` access modifiers.
4. THE Sample_Application SHALL define and invoke constructors, including constructor chaining with `: this()`.
5. THE Sample_Application SHALL define and invoke methods with parameters and return values.
6. THE Sample_Application SHALL use all control flow statements: `if`/`else`, `while`, `for`, `foreach`, and `return`.
7. THE Sample_Application SHALL use all arithmetic operators (`+`, `-`, `*`, `/`, `%`), comparison operators (`<`, `>`, `<=`, `>=`, `==`, `!=`), and logical operators (`&&`, `||`, `!`).
8. THE Sample_Application SHALL use variable declarations with both explicit types and `var`.
9. THE Sample_Application SHALL use array declaration, creation (`new int[n]`), and indexing (`arr[i]`).
10. THE Sample_Application SHALL use object instantiation with `new`.
11. THE Sample_Application SHALL use string literals, string interpolation (`$"Hello {name}"`), char literals, integer literals (including hex), float literals, and boolean literals.
12. THE Sample_Application SHALL use `Console.WriteLine` for output via Ambient_Types.
13. THE Sample_Application SHALL define a valid Entry_Point (`static void Main()` or `static void Main(string[] args)`).
14. WHEN the `mist` CLI is invoked in the Sample_Application directory, THE compiler SHALL produce a working .NET executable that runs without errors.
