# Requirements Document

## Introduction

This feature adds two related capabilities to the Mist programming language:

1. **Attributes** — C#-style attribute syntax (`[AttributeName]`, `[AttributeName(args)]`, or `[AttributeName<TypeArgs>]`) that can be applied to type declarations and methods. For v1, the scope is consuming CLR attributes (not defining custom attribute classes in Mist). The C# emitter outputs the corresponding `[Attribute]` annotations in generated C# code.

2. **`mist test` CLI command** — A new CLI command that compiles the project and runs tests via the backend's test runner. For the C# transpiler backend, this means running `dotnet test` on the emitted output. `mist test` can be used against any project — if there are no test-attributed methods, the runner simply finds no tests.

3. **Project output type** — A new `output_type` field in `mist.toml` that controls the kind of .csproj emitted (`executable` or `library`). This determines whether the project requires a Main entry point and whether `mist run` is available.

These features work together: attributes enable decorating methods with `[Fact]`, `[Test]`, etc., and the test command provides the workflow to compile and execute those tests regardless of project output type.

## Glossary

- **Attribute**: A metadata annotation applied to a declaration using bracket syntax `[Name(args)]`. Corresponds to a CLR attribute class.
- **Attribute_List**: A sequence of one or more attributes enclosed in square brackets, e.g., `[Fact]` or `[Fact, Trait("Category", "Unit")]`.
- **Attribute_Argument**: A positional or named argument passed to an attribute constructor, e.g., `"hello"` or `Skip = "reason"`.
- **Generic_Attribute**: An attribute with type arguments, e.g., `[JsonConverter<MyConverter>]`. The attribute name is followed by a TypeArgumentList.
- **Parser**: The Mist parser component that converts token streams into AST nodes.
- **Emitter**: The C# emitter component (`CSharpEmitter`) that translates Mist AST/semantic model into C# source files.
- **CLI**: The Mist command-line interface entry point (`mist` executable).
- **Argument_Parser**: The component that tokenizes and validates raw command-line arguments (`CliArgumentParser`).
- **Test_Command**: The CLI command verb `test` that compiles the project and runs tests via the backend test runner.
- **Backend_Test_Runner**: The backend-specific mechanism for executing tests. For the C# backend, this is `dotnet test`.
- **Compiler_Driver**: The orchestrating component that runs the full compilation pipeline.
- **Project_Configuration**: The parsed `mist.toml` file contents, including project name, packages, output type, and framework references.
- **Output_Type**: The `output_type` field in `mist.toml` that determines the kind of project emitted. Valid values: `"executable"` (default), `"library"`.
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.

## Requirements

### Requirement 1: Attribute Syntax Parsing

**User Story:** As a Mist developer, I want to annotate type declarations and methods with CLR attributes using bracket syntax, so that I can leverage the .NET attribute ecosystem.

#### Acceptance Criteria

1. WHEN one or more Attribute_Lists appear before a type declaration (class, struct, enum, or abstract), THE Parser SHALL parse the Attribute_Lists and attach them to the type declaration AST node.
2. WHEN one or more Attribute_Lists appear before a method declaration, THE Parser SHALL parse the Attribute_Lists and attach them to the method declaration AST node.
3. WHEN an Attribute_List contains multiple attributes separated by commas (e.g., `[A, B]`), THE Parser SHALL parse each attribute as a separate entry in the list.
4. WHEN an attribute has no arguments (e.g., `[Fact]`), THE Parser SHALL parse the attribute with an empty argument list and no parentheses node.
5. WHEN an attribute has positional arguments (e.g., `[InlineData(1, 2, 3)]`), THE Parser SHALL parse the arguments as expressions in source order.
6. WHEN an attribute has named arguments (e.g., `[Trait(Name = "value")]`), THE Parser SHALL parse the named argument as an identifier-equals-expression pair.
7. WHEN an attribute has both positional and named arguments (e.g., `[InlineData(1, Skip = "reason")]`), THE Parser SHALL accept positional arguments before named arguments in the same argument list.
8. WHEN an attribute name uses a dotted qualifier (e.g., `[System.Serializable]`), THE Parser SHALL parse the qualified name using the existing `QualifiedIdentifier` rule.
9. WHEN an attribute has type arguments (e.g., `[JsonConverter<MyConverter>]`), THE Parser SHALL parse the type arguments using the existing `TypeArgumentList` rule attached to the attribute name.
10. THE Parser SHALL recognize attribute syntax only in declaration position (before modifiers and type/method keyword tokens), not in expression position where `[` introduces array literals or index access.
11. IF an attribute's argument list is malformed (e.g., missing closing parenthesis, trailing comma with no argument), THEN THE Parser SHALL report a parse error with a diagnostic message indicating the location of the malformed attribute.

### Requirement 2: Attribute Grammar Definition

**User Story:** As a compiler maintainer, I want the attribute syntax formally defined in the Storm grammar file, so that the parser generator produces the correct AST nodes.

#### Acceptance Criteria

1. THE grammar SHALL define an `AttributeList` rule that matches `LEFT_BRACKET Attribute (COMMA Attribute)* RIGHT_BRACKET`.
2. THE grammar SHALL define an `Attribute` rule that matches `name:QualifiedIdentifier typeArgs:TypeArgumentList? arguments:(LEFT_PARENTHESIS AttributeArgumentList? RIGHT_PARENTHESIS)?`.
3. THE grammar SHALL define an `AttributeArgumentList` rule that matches `AttributeArgument (COMMA AttributeArgument)*`.
4. THE grammar SHALL define an `AttributeArgument` rule with two alternatives: `name:IDENTIFIER EQUAL value:Expression as NamedAttributeArgument` and `value:Expression as PositionalAttributeArgument`.
5. THE `ClassDeclaration`, `StructDeclaration`, `AbstractDeclaration`, and `EnumDeclaration` rules SHALL accept `attributes:AttributeList*` before their existing `modifiers:Modifier*` prefix.
6. THE `MethodDeclaration` and `ConstructorDeclaration` rules SHALL accept `attributes:AttributeList*` before their existing `modifiers:Modifier*` prefix.
7. THE `FieldDeclaration` rule SHALL accept `attributes:AttributeList*` before its existing `modifiers:Modifier*` prefix, enabling field-level attributes for future extensibility.

### Requirement 3: Attribute Emission to C#

**User Story:** As a Mist developer, I want attributes on my types and methods to appear in the generated C# code, so that the CLR runtime and test frameworks can discover them via reflection.

#### Acceptance Criteria

1. WHEN a type declaration has one or more Attribute_Lists, THE Emitter SHALL emit the corresponding C# attribute annotations on the line immediately preceding the type declaration keyword (e.g., `class`, `struct`), preserving the qualified name as written in source.
2. WHEN a method declaration has one or more Attribute_Lists, THE Emitter SHALL emit the corresponding C# attribute annotations on the line immediately preceding the method signature.
3. WHEN an attribute has positional arguments followed by named arguments, THE Emitter SHALL emit positional arguments first, then named arguments, separated by commas, within a single parenthesized list.
4. WHEN an attribute has named arguments, THE Emitter SHALL emit the named arguments using `Name = value` syntax within parentheses.
5. WHEN an attribute has no arguments and no parentheses in source, THE Emitter SHALL emit the attribute without parentheses (e.g., `[Fact]`).
6. WHEN multiple attributes appear in a single Attribute_List (e.g., `[A, B]`), THE Emitter SHALL emit them as a comma-separated list within one bracket pair (e.g., `[A, B]`).
7. WHEN multiple separate Attribute_Lists appear on a declaration (e.g., `[A]` then `[B]` on separate lines), THE Emitter SHALL emit each as a separate bracketed annotation on its own line, in the same order they appear in source.
8. WHEN an attribute has type arguments (e.g., `[JsonConverter<MyConverter>]`), THE Emitter SHALL emit the type arguments in the C# attribute (e.g., `[JsonConverter<MyConverter>]`).

### Requirement 4: Attribute Argument Expression Support

**User Story:** As a Mist developer, I want to pass string literals, numeric literals, boolean literals, and typeof expressions as attribute arguments, so that I can use common attribute patterns like `[InlineData(1, "hello", true)]`.

#### Acceptance Criteria

1. THE Parser SHALL accept string literals as attribute arguments.
2. THE Parser SHALL accept integer literals (including hex literals, binary literals, and negated integer literals such as `-1`) as attribute arguments.
3. THE Parser SHALL accept floating-point literals and character literals as attribute arguments.
4. THE Parser SHALL accept boolean literals (`true`, `false`) as attribute arguments.
5. THE Parser SHALL accept type literals (primitive types like `int`, qualified names like `System.String`, and generic types like `List<int>`) as attribute arguments.
6. THE Emitter SHALL emit type literal attribute arguments as `typeof(T)` in C#, including generic type arguments (e.g., `List<int>` emits as `typeof(List<int>)`).

### Requirement 5: Test Command CLI Integration

**User Story:** As a Mist developer, I want to run `mist test` to compile and execute my test suite, so that I have a single-command workflow for running tests.

#### Acceptance Criteria

1. WHEN the first positional argument is `test`, THE Argument_Parser SHALL parse the remaining arguments and set the Command to Test_Command.
2. THE CLI SHALL accept the same flags as the `build` command (`--backend`, `--output`, `--bin`, `--quiet`, `--verbose`) when using the Test_Command.
3. WHEN the Command is Test_Command, THE CLI SHALL compile the project using the standard compilation pipeline (lex, parse, semantic analysis, type checking, backend emission). The compilation respects the project's `output_type` — the .csproj is emitted as Exe or Library per the normal rules.
4. IF the Command is Test_Command and compilation fails, THEN THE CLI SHALL print diagnostics to stderr and return exit code 1 without invoking the Backend_Test_Runner.
5. WHEN the Command is Test_Command and compilation succeeds, THE CLI SHALL invoke the Backend_Test_Runner to execute the compiled project and return the Backend_Test_Runner's exit code as its own exit code.
6. THE CLI usage message SHALL include `test` in the list of available commands with the description "Compile and run tests".
7. IF the Command is Test_Command and the `--mode` flag is set to `lex`, THEN THE CLI SHALL report a parse error indicating that the `test` command is not compatible with `--mode lex`.
8. `mist test` SHALL work against any project regardless of its Output_Type. If the project contains no test-attributed methods, the test runner will simply report zero tests found and exit with code 0.

### Requirement 6: C# Backend Test Execution

**User Story:** As a Mist developer, I want the C# backend to support test execution via `dotnet test`, so that test frameworks can discover and run my test methods.

#### Acceptance Criteria

1. WHEN `mist test` is invoked and compilation succeeds, THE CLI SHALL invoke `dotnet test` on the emitted .csproj in the output directory. The .csproj is emitted normally per the project's `output_type` — `dotnet test` operates on both Exe and Library projects.
2. THE Emitter SHALL include all packages declared in the project's `mist.toml` `[packages]` table as `<PackageReference>` elements in the emitted .csproj. This is the same behavior as `mist build` — no special test-mode package handling is needed.
3. IF a project has methods decorated with attributes whose unqualified name ends in `Fact`, `Theory`, `Test`, or `TestMethod`, but the `mist.toml` `[packages]` table does not declare any package whose name contains `Test` or `xunit` (case-insensitive), THEN THE CLI SHALL emit a warning diagnostic indicating that test framework packages are likely missing.

### Requirement 7: Test Runner Invocation

**User Story:** As a Mist developer, I want `mist test` to invoke `dotnet test` on the emitted project and report results, so that I see pass/fail output in my terminal.

#### Acceptance Criteria

1. WHEN the Command is Test_Command and emission succeeds, THE CLI SHALL invoke `dotnet test` on the emitted .csproj file path in the output directory.
2. THE CLI SHALL stream stdout from the `dotnet test` process to the CLI stdout in real time (not buffered until process completion).
3. THE CLI SHALL stream stderr from the `dotnet test` process to the CLI stderr in real time.
4. THE CLI SHALL return the exit code from the `dotnet test` process as the CLI exit code (0 for all tests passing, non-zero for failures).
5. IF the `dotnet` executable is not found on the system PATH, THEN THE CLI SHALL print `error: Failed to start 'dotnet' process.` to stderr and return exit code 1.
6. IF the `dotnet test` process starts but exits with a non-zero code, THEN THE CLI SHALL return that same non-zero exit code without printing additional error messages beyond what `dotnet test` already printed.

### Requirement 8: Run Command Output Type Validation

**User Story:** As a Mist developer, I want `mist run` to only work on executable projects, so that I don't accidentally try to run a library with no entry point.

#### Acceptance Criteria

1. WHEN the Command is Run, THE CLI SHALL parse the project's `mist.toml` to determine the Output_Type before starting the compilation pipeline.
2. IF the Command is Run and the project's Output_Type is `"library"`, THEN THE CLI SHALL emit an error message indicating that `mist run` can only be used with executable projects and return exit code 1 without compiling.
3. WHEN the Command is Run and the project's Output_Type is `"executable"` (or absent, defaulting to executable), THE CLI SHALL proceed with compilation and execution as it does today.

### Requirement 9: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want attribute syntax to be highlighted correctly, so that I get visual distinction for attribute annotations.

#### Acceptance Criteria

1. WHEN a `[` token appears at the beginning of a line (ignoring leading whitespace) or immediately after a modifier keyword, THE TextMate_Grammar SHALL treat the region from `[` to the matching `]` as an attribute annotation and apply the scope `meta.attribute.mist` to the entire region, including the brackets.
2. THE TextMate_Grammar SHALL highlight the identifier immediately following the opening `[` (the attribute name) with scope `entity.other.attribute-name.mist`.
3. WHEN an attribute contains a parenthesized argument list (e.g., `[Attr(arg1, arg2)]`), THE TextMate_Grammar SHALL highlight string arguments with scope `string.quoted.double.mist`, numeric arguments with their corresponding `constant.numeric.*` scopes, and boolean arguments (`true`/`false`) with scope `constant.language.boolean.mist`.
4. THE TextMate_Grammar SHALL scope the opening `[` and closing `]` brackets of an attribute annotation as `punctuation.definition.attribute.begin.mist` and `punctuation.definition.attribute.end.mist` respectively.
5. IF a `[` token does not appear in a declaration position (i.e., it is not at the beginning of a line ignoring whitespace, nor immediately after a modifier keyword), THEN THE TextMate_Grammar SHALL NOT apply the `meta.attribute.mist` scope to it.
6. WHEN an attribute has type arguments (e.g., `[JsonConverter<MyConverter>]`), THE TextMate_Grammar SHALL highlight the type arguments using the existing type-parameter highlighting patterns within the `meta.attribute.mist` scope.

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating attributes, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Attributes/Program.mist` with a corresponding `samples/Attributes/mist.toml` project file.
2. The sample SHALL demonstrate attributes on class declarations, struct declarations, enum declarations, abstract declarations, and method declarations.
3. The sample SHALL demonstrate attributes with no arguments, positional arguments, named arguments, multiple attributes in one list, and multiple attribute lists on a single declaration.
4. The sample SHALL demonstrate type literal arguments in attributes.
5. The sample SHALL demonstrate generic attribute type arguments (e.g., `[JsonConverter<MyConverter>]`).
6. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable by matching against the expected output.
7. `samples/AllFeatures/Program.mist` SHALL call `Attributes.Program.RunDemo()` to include the feature in the full-suite sample run, and `samples/AllFeatures/mist.toml` SHALL include the Attributes project as a reference.
8. The sample SHALL compile and run without errors, warnings, or informational diagnostics using the Mist compiler.

#### Proof Code

```mist
namespace Attributes;

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

// PROOF: Attribute on a class declaration with no arguments
[Serializable]
public class Config
{
    string name;

    public Config(string name)
    {
        this.name = name;
    }
}

// PROOF: Multiple attributes in separate lists on a class
[Serializable]
[Obsolete("Use NewService instead")]
public class OldService
{
}

// PROOF: Attribute with positional arguments on a method
// PROOF: Attribute with named argument on a method
public class MyTests
{
    [Obsolete("use TestAdditionV2")]
    public void TestAddition()
    {
        var result = 1 + 1;
        Console.WriteLine($"TestAddition: {result}");
    }

    [Range(0, 100)]
    public int Score;

    [Obsolete("use TestSumV2")]
    public void TestSum(int a, int b, int expected)
    {
        var sum = a + b;
        Console.WriteLine($"TestSum({a},{b}): {sum} == {expected}");
    }
}

// PROOF: Attribute on a struct
[Serializable]
public struct Point
{
    public int x;
    public int y;

    public Point(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}

// PROOF: Attribute with typeof argument (type literal)
[DebuggerTypeProxy(List<int>)]
public class CustomCollection
{
}

// PROOF: Multiple attributes in a single list
[Serializable, Obsolete("combined")]
public class MultiAttr
{
}

// PROOF: Generic attribute type arguments
[JsonConverter<int>]
public class TypedConfig
{
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("--- Attributes Demo ---");
        var config = new Config("test");
        Console.WriteLine($"Config created: {config}");
        var p = new Point(1, 2);
        Console.WriteLine($"Point created: ({p.x}, {p.y})");
        var tests = new MyTests();
        tests.TestAddition();
        tests.TestSum(1, 2, 3);

        // PROOF: Attribute on a parameter - the code compiles and runs
        var fmt = new Formatter();
        var formatted = fmt.Format("Hello", 42);
        Console.WriteLine($"Formatter result: {formatted}");

        // PROOF: Attribute on a property - the code compiles and runs
        var settings = new Settings(100);
        Console.WriteLine($"Settings timeout: {settings.Timeout}");

        // PROOF: Attribute on an indexer - the code compiles and runs
        var table = new LookupTable(3);
        table[0] = 99;
        Console.WriteLine($"LookupTable[0]: {table[0]}");

        Console.WriteLine("--- Attributes Demo End ---");
    }

    public static void Main()
    {
        RunDemo();
    }
}

// PROOF: Attribute on a parameter - uses a custom attribute targeting parameters
public class ParamTagAttribute : Attribute
{
    public string Tag;

    ParamTagAttribute(string tag)
    {
        this.Tag = tag;
    }
}

public class Formatter
{
    public string Format([ParamTag("format-input")] string template, int value)
    {
        return $"{template}: {value}";
    }
}

// PROOF: Attribute on a property declaration
public class Settings
{
    [Obsolete("Use Timeout instead")]
    public int Delay { get; }

    public int Timeout { get; }

    Settings(int timeout)
    {
        this.Timeout = timeout;
    }
}

// PROOF: Attribute on an indexer declaration
public class LookupTable
{
    int[] data;

    LookupTable(int size)
    {
        this.data = new int[size];
    }

    [Obsolete("Use Get method instead")]
    public int this[int index]
    {
        get { return data[index]; }
        set { data[index] = value; }
    }
}
```

**Expected output:**
```
--- Attributes Demo ---
Config created: Attributes.Config
Point created: (1, 2)
TestAddition: 2
TestSum(1,2): 3 == 3
Formatter result: Hello: 42
Settings timeout: 100
LookupTable[0]: 99
--- Attributes Demo End ---
```

### Requirement 11: Test Workflow Sample

**User Story:** As a Mist developer, I want a sample project demonstrating the `mist test` workflow end-to-end, so that I can see how to set up a test project with xUnit.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Testing/Tests.mist` with a corresponding `samples/Testing/mist.toml` project file.
2. The `mist.toml` SHALL declare `output_type = "library"` and SHALL include xUnit packages in its `[packages]` table (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`).
3. The sample SHALL contain at least two test methods decorated with `[Fact]` attributes.
4. The sample SHALL contain at least one test method decorated with `[Theory]` and `[InlineData(...)]` attributes demonstrating parameterized tests.
5. The sample SHALL compile successfully with `mist build` and all tests SHALL pass when run with `mist test`.
6. The sample SHALL NOT be included in the AllFeatures orchestrator (it is a test project, not an executable demo).

#### Proof Code

```mist
namespace Testing;

using Xunit;

public class MathTests
{
    [Fact]
    public void Addition()
    {
        var result = 2 + 2;
        Assert.Equal(4, result);
    }

    [Fact]
    public void Subtraction()
    {
        var result = 10 - 3;
        Assert.Equal(7, result);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 1, 0)]
    public void ParameterizedAdd(int a, int b, int expected)
    {
        var result = a + b;
        Assert.Equal(expected, result);
    }
}
```

### Requirement 12: Book Documentation

**User Story:** As a Mist developer, I want the language book to document attributes, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section at `book/src/advanced/attributes.md` covering attribute syntax, valid declaration targets (types and methods), and supported argument types (string, integer, float, char, bool, type literal).
2. THE book chapter SHALL demonstrate attributes on types and methods with various argument forms (no args, positional, named, type literals, generic type arguments) using at least 4 compiled snippets.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy, located under `book/snippets/attributes/`.
4. THE book's `SUMMARY.md` SHALL reference the new section in the Advanced Features part, after the existing ".NET Interop" entry.

### Requirement 13: Test Runner Gap TODO

**User Story:** As a Mist project maintainer, I want a documented TODO noting the gap around testing infrastructure, so that the team tracks the eventual goal of a Mist-native test runner.

#### Acceptance Criteria

1. A file SHALL exist at `project/TODO-test-runner.md` documenting that the current `mist test` command delegates to the backend's test runner (`dotnet test` for the C# transpiler backend).
2. The TODO SHALL note that a future Mist-native test runner is desired but out of scope for v1, and SHALL include a brief rationale explaining why (no annotation system was available until attributes were added; a native runner requires assertion APIs and result formatting).
3. The TODO SHALL outline what a native test runner would need, including at minimum: test discovery mechanism, assertion library (e.g., `Assert.Equal`, `Assert.Throws`), result reporting format (e.g., TRX, console summary), and integration with IDE test explorers.

### Requirement 14: Project Output Type Configuration

**User Story:** As a Mist developer, I want to configure the kind of project my `mist.toml` describes via an `output_type` field, so that the compiler emits the correct .csproj structure for executables or libraries.

#### Acceptance Criteria

1. THE Project_Configuration parser SHALL recognize an optional `output_type` field in `mist.toml` that accepts the string values `"executable"` or `"library"` (case-sensitive, lowercase).
2. WHEN the `output_type` field is absent from `mist.toml`, THE Project_Configuration parser SHALL default the Output_Type to `"executable"`.
3. IF the `output_type` field is present but its value is not one of `"executable"` or `"library"`, THEN THE Project_Configuration parser SHALL emit a diagnostic error indicating the invalid value and listing the valid options.
4. WHEN the Output_Type is `"executable"`, THE Emitter SHALL emit the .csproj with `<OutputType>Exe</OutputType>` in the PropertyGroup.
5. WHEN the Output_Type is `"library"`, THE Emitter SHALL emit the .csproj with `<OutputType>Library</OutputType>` in the PropertyGroup. The compiled artifact SHALL be a .dll file.
6. WHEN the Output_Type is `"executable"`, THE Compiler_Driver SHALL enforce that the project contains a Main entry point (existing EntryPointAnalyzer behavior).
7. WHEN the Output_Type is `"library"`, THE Compiler_Driver SHALL suppress the Main entry point requirement, allowing the project to compile without a `static void Main` method.
8. IF the Output_Type is `"library"` and the project contains top-level statements, THEN THE Compiler_Driver SHALL emit a diagnostic error indicating that library projects cannot have top-level statements (top-level statements imply an entry point, which contradicts library output).

### Requirement 15: Steering Distribution Update

**User Story:** As a Mist developer, I want the distributed steering files to document `mist test` and testing workflows, so that AI-assisted tools understand the new capabilities.

#### Acceptance Criteria

1. WHEN a developer runs `mist steering`, THE CLI SHALL install an updated main steering file that mentions `mist test` at a high level (command exists, compiles and runs tests via backend runner) and documents the `output_type` field in `mist.toml` with both valid values and their effects.
2. THE CLI SHALL ALSO install a dedicated testing steering file (e.g., `testing.md`) that documents the full testing workflow in detail: how to set up a test project with packages in `mist.toml`, how to write test methods with attributes (`[Fact]`, `[Theory]`, `[InlineData]`), how `mist test` invokes `dotnet test`, and that `mist test` works on any project regardless of output type.
3. THE dedicated testing steering file SHALL be a separate file from the main project steering, so that agents can selectively pull in testing context only when relevant.

### Requirement 16: highlight.js Grammar Update

**User Story:** As a Mist book reader, I want code examples in the book to have correct syntax highlighting for attributes, so that the visual presentation matches what I see in my editor.

#### Acceptance Criteria

1. THE highlight.js grammar used for the book's code highlighting SHALL recognize attribute syntax (bracket-enclosed annotations before declarations) and apply appropriate CSS classes for highlighting.
2. THE highlight.js grammar SHALL highlight attribute names, string arguments, numeric arguments, boolean arguments, and type arguments within attribute brackets.
3. THE highlight.js grammar SHALL use the same syntactic patterns as the TextMate grammar (adapted to highlight.js format) to ensure consistent highlighting between the book and VS Code.
