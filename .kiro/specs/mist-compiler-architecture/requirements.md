# Requirements Document

## Introduction

Mist is a new programming language inspired by C#, Haskell, and TypeScript. This document specifies the architectural requirements for the Mist compiler and its supporting tooling. The compiler is implemented in C# 14 targeting .NET 10, and consumes a formal grammar definition written in a separate grammar language called Storm. The Storm grammar is processed at build time by a language-agnostic C# source generator (the Storm_Source_Generator) that produces an explicit lexer, parser, and AST node type definitions for whichever language's grammar it is supplied with. The Mist compiler is one consumer of the Storm_Source_Generator: it supplies the Storm grammar file(s) describing Mist and consumes the generated lexer, parser, and AST types. The compiler pipeline transforms Mist source code through a series of well-defined stages: lexing, parsing, semantic model construction, analysis, and backend code generation. The first supported backend is a C# transpiler that emits C# source code and invokes the .NET compiler to produce executable output.

This specification focuses on architectural concerns: pipeline stage boundaries, component contracts, extensibility points (pluggable analyzers and backends), Storm's high-level shape and contract with the source generator, the separation between the reusable Storm_Source_Generator and the Mist-specific compiler that consumes it, and tooling integration. The concrete syntax and semantics of Mist are intentionally deferred to a follow-up specification. Storm's shape is captured here at a contract level; a separate follow-up specification will pin down Storm's concrete grammar.

Reusability of the Storm_Source_Generator across languages is an explicit architectural goal for v1: the generator consumes a Storm grammar as input and emits a lexer, parser, and AST for whatever language that grammar describes, without containing Mist-specific logic, types, or knowledge. This keeps the generator a general-purpose tool that other .NET language projects could adopt, and prevents the v1 design from accidentally baking in Mist-specific assumptions.

This v1 specification is scoped for time-to-value: it captures the minimum architectural surface required to get a working end-to-end compiler from Mist source to an executable .NET assembly via the C# transpiler backend. A "Non-Goals" section at the end of this document enumerates deliberately deferred concerns that a later refactor pass is expected to address.

For the same time-to-value reason, v1 collapses the compiler into a single `Mist` .NET project that contains every pipeline stage (Lexer consumption, Parser consumption, Semantic_Model_Builder, Analyzer framework and built-in analyzers, Backend framework, CSharp_Transpiler_Backend, and Compiler_Driver), with internal stage boundaries enforced by folder and namespace organization rather than by separate projects. The only hard project splits in v1 are the `Storm.SourceGenerator` project (required by Roslyn: source generators must ship as their own `netstandard2.0` assembly) and a thin `Mist.Cli` executable project that hosts the Compiler_CLI surface on top of `Mist`. Splitting the `Mist` project further into per-stage projects is an explicit non-goal for v1 and is deferred to a later refactor.

## Glossary

- **Mist**: The programming language being designed, inspired by C#, Haskell, and TypeScript.
- **Storm**: A formal grammar description language, inspired by ANTLR but not required to be compatible with it, used to define the grammar of Mist (and potentially other languages).
- **Storm_Grammar_File**: A text file written in the Storm language that describes the lexical and syntactic structure of a target language.
- **Storm_Rule**: A Storm declaration introduced by the `rule` keyword that defines a parser production.
- **Storm_Token**: A Storm declaration introduced by the `token` keyword that defines a lexer token.
- **Storm_Fragment**: A Storm declaration introduced by the `fragment` keyword that defines a reusable sub-pattern for tokens. Fragments do not themselves produce tokens in the Token_Stream.
- **Storm_Hidden_Token**: A Storm_Token declared with the `hidden` modifier. Hidden tokens are emitted into the Token_Stream but are skipped by the Parser while remaining available to downstream tooling.
- **Source_Generator**: A C# source generator (as defined by the Roslyn source generator API) that runs at compile time and emits additional C# source code into a compilation.
- **Storm_Source_Generator**: A language-agnostic Source_Generator that consumes one or more Storm_Grammar_Files and emits C# source for the Lexer, Parser, and AST node types of the language described by those grammar files. The Storm_Source_Generator contains no knowledge of Mist or any other specific language; it is reusable across any consumer that supplies a Storm grammar. Shipped as the `Storm.SourceGenerator` .NET project targeting `netstandard2.0` as required by the Roslyn source generator API.
- **Mist_Project**: The `Mist` .NET project, which contains the entire Mist compiler implementation in v1: the consumers of the generated Lexer, Parser, and AST types; the Semantic_Model_Builder; the Analyzer framework and all built-in Analyzers (including the Type_Checker); the Backend framework; the CSharp_Transpiler_Backend; and the Compiler_Driver. Internal stage boundaries are organized by folder and namespace rather than by separate projects. The Mist_Project depends on the Storm_Source_Generator and supplies its own Storm_Grammar_File(s) describing Mist.
- **Mist_Cli_Project**: The `Mist.Cli` .NET executable project, a thin shell that references the Mist_Project and exposes the Compiler_CLI surface.
- **Mist_Compiler_Project**: The combined deliverable of the Mist_Project and the Mist_Cli_Project. References to the "Mist_Compiler_Project" in this document refer collectively to these two projects unless a specific one is named. The Mist_Compiler_Project depends on the Storm_Source_Generator and supplies its own Storm_Grammar_File(s) describing Mist.
- **Lexer**: The compiler component that converts a stream of Mist source characters into a stream of Tokens.
- **Token**: A single lexical unit produced by the Lexer, carrying a kind, source text span, channel (default or hidden), and position information.
- **Token_Stream**: An ordered sequence of Tokens produced by the Lexer from a single source input.
- **Parser**: The compiler component that consumes a Token_Stream and produces an Abstract Syntax Tree.
- **AST**: Abstract Syntax Tree — a tree data structure representing the syntactic structure of a parsed Mist source file.
- **Semantic_Model**: A representation of Mist source code enriched with resolved symbols, types, scopes, and other semantic information derived from one or more ASTs.
- **Semantic_Model_Builder**: The compiler component that converts one or more ASTs into a Semantic_Model.
- **Analyzer**: A pluggable component that inspects a Semantic_Model and reports Diagnostics without modifying the Semantic_Model.
- **Type_Checker**: A specific Analyzer that verifies Mist type rules against the Semantic_Model.
- **Diagnostic**: A structured message produced by a compiler stage, including severity, message, source location, and a stable diagnostic identifier.
- **Backend**: A pluggable component that consumes a Semantic_Model and produces a target output artifact.
- **CSharp_Transpiler_Backend**: The initial Backend implementation that translates a Semantic_Model into C# source code.
- **Compilation_Pipeline**: The end-to-end sequence of compiler stages: Lexer → Parser → Semantic_Model_Builder → Analyzers → Backend.
- **Compilation_Unit**: A single Mist source file passed through the Compilation_Pipeline.
- **Compilation**: A logical grouping of one or more Compilation_Units processed together, sharing a single Semantic_Model.
- **Compiler_Driver**: The top-level component that orchestrates the Compilation_Pipeline for a given Compilation.
- **Compiler_CLI**: The command-line interface executable that invokes the Compiler_Driver.

## Requirements

### Requirement 1: Compiler Implementation Platform

**User Story:** As a compiler developer, I want the compiler to be implemented on a modern, widely supported .NET platform, so that I can use current language features and tooling.

#### Acceptance Criteria

1. THE Mist_Compiler SHALL be implemented in C# targeting language version 14.
2. THE Mist_Compiler SHALL target the .NET 10 runtime.
3. THE Mist_Compiler SHALL follow idiomatic C# conventions for naming, nullability annotations, and async usage.
4. THE Mist_Compiler SHALL be organized as exactly three .NET projects: `Storm.SourceGenerator`, `Mist`, and `Mist.Cli`; additional .NET projects for automated tests are permitted and are not counted toward this three-project layout.
5. THE `Storm.SourceGenerator` project SHALL reside in its own .NET project independent of the `Mist` and `Mist.Cli` projects.
6. THE `Mist` project SHALL consume the `Storm.SourceGenerator` as an external dependency and supply its own Storm_Grammar_File(s) describing Mist.
7. THE `Storm.SourceGenerator` project SHALL NOT contain any Mist-specific logic, types, identifiers, or knowledge.
8. THE `Mist` project SHALL organize internal stage boundaries (Lexer consumption, Parser consumption, Semantic_Model_Builder, Analyzer framework and built-in analyzers, Backend framework, CSharp_Transpiler_Backend, Compiler_Driver) through folders and namespaces within the single project, rather than through separate .NET projects.
9. THE `Mist.Cli` project SHALL be a thin executable shell that references the `Mist` project and exposes the Compiler_CLI surface, and SHALL NOT contain pipeline-stage logic.

### Requirement 2: Storm Grammar Consumption

**User Story:** As a language designer, I want to describe Mist's grammar in a dedicated grammar language, so that the lexer, parser, and AST stay in sync with a single source of truth.

#### Acceptance Criteria

1. THE Mist_Compiler_Project SHALL define Mist's lexical and syntactic grammar in a Storm_Grammar_File rooted at a single entry Storm_Grammar_File.
2. THE Storm language SHALL be a distinct formal grammar language from Mist, inspired by ANTLR, without a requirement of ANTLR compatibility.
3. THE Mist_Compiler_Project build SHALL treat the root Storm_Grammar_File and all files it transitively imports as the authoritative source of Mist's grammar.
4. WHEN any Storm_Grammar_File in the transitive import closure of a consumer project's root Storm_Grammar_File changes, THE consumer project's build SHALL regenerate the Lexer, Parser, and AST node type sources during the next build by re-invoking the Storm_Source_Generator.
5. IF a Storm_Grammar_File contains a syntax error, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the file, line, and column of the error, and SHALL fail the build.

### Requirement 3: Storm Grammar Language — Declarations and Expressions

**User Story:** As a language designer, I want Storm to give me a clear, declarative way to describe tokens, parser rules, and reusable fragments, so that I can express Mist's grammar without embedding procedural code in the grammar file.

#### Acceptance Criteria

1. THE Storm language SHALL support three top-level declaration kinds introduced by the keywords `rule`, `token`, and `fragment`.
2. THE Storm language SHALL declare parser productions using the form `rule <name> = <expression>;`.
3. THE Storm language SHALL declare lexer tokens using the form `token <name> = <expression>;`.
4. THE Storm language SHALL declare reusable lexical sub-patterns using the form `fragment <name> = <expression>;`.
5. THE Storm language SHALL NOT permit embedded target-language code (semantic actions) within `rule`, `token`, or `fragment` declarations.
6. THE Storm_Rule, Storm_Token, and Storm_Fragment expression syntax SHALL support the following combinators: sequence (juxtaposition), alternation (`|`), grouping (`(` `)`), optional (`?`), zero-or-more (`*`), and one-or-more (`+`).
7. THE Storm_Token and Storm_Fragment expressions SHALL additionally support string literals, regular-expression-style character classes (including ranges such as `[a-zA-Z_]` and negated classes such as `[^"\\]`), and references to other Storm_Tokens and Storm_Fragments.
8. THE Storm_Rule expressions SHALL additionally support references to Storm_Tokens, references to other Storm_Rules, and anonymous string literals; an anonymous string literal appearing within a Storm_Rule SHALL be treated as an implicit reference to a token matching exactly that literal text.
9. THE Storm language SHALL support named alternatives within a Storm_Rule using the `as <Name>` syntax at the end of an alternative, where each top-level alternative of a rule MAY be labeled with `as <Name>` that the Storm_Source_Generator uses to name the corresponding AST subtype.
10. THE Storm language SHALL support field labels within a Storm_Rule using the `<name>:<element>` syntax, where the label names the corresponding property on the generated AST node type.
11. THE Storm language SHALL support `//` line comments.

### Requirement 4: Storm Grammar Language — Tokens, Hidden Channel, and Fragment Semantics

**User Story:** As a language designer, I want control over which lexical elements are visible to the parser and which are not, so that whitespace and comments can be ignored by the parser but remain available for other tooling.

#### Acceptance Criteria

1. THE Storm language SHALL support declaring a Storm_Token as hidden by prefixing its declaration with the `hidden` keyword, in the form `hidden token <name> = <expression>;`.
2. THE Lexer SHALL emit all Tokens, including Tokens derived from Storm_Hidden_Tokens, into the Token_Stream with accurate source spans and positions.
3. THE Lexer SHALL mark Tokens derived from Storm_Hidden_Tokens with a distinct channel indicator that identifies them as hidden.
4. THE Parser SHALL skip Tokens marked as hidden when matching Storm_Rule expressions.
5. THE Storm_Source_Generator SHALL NOT generate a Token kind for a Storm_Fragment; fragments SHALL only appear as building blocks of Storm_Tokens.
6. WHEN the Lexer matches input against the set of Storm_Tokens, THE Lexer SHALL use maximal-munch: the longest matching Storm_Token is selected.
7. WHEN multiple Storm_Tokens match the same maximal-munch input length, THE Lexer SHALL resolve the tie using the declaration order of the Storm_Tokens within the effective grammar.

### Requirement 5: Storm Grammar Language — Parser Semantics

**User Story:** As a language designer, I want Storm's parser semantics to be powerful enough to describe real programming languages, so that Mist's grammar can be expressed naturally without contortions.

#### Acceptance Criteria

1. THE generated Parser SHALL implement LL(*) (predicated LL) parsing for Storm_Rule expressions.
2. THE generated Parser SHALL resolve alternation within a Storm_Rule using ordered choice (PEG semantics): when multiple alternatives of a Storm_Rule match at a given position, the first alternative in declaration order that matches SHALL be selected.
3. THE Storm_Source_Generator SHALL generate a public parser entry point for every Storm_Rule declared in the effective grammar.
4. THE Storm_Source_Generator SHALL NOT generate a parser entry point for any Storm_Token or Storm_Fragment.
5. THE selection of which Storm_Rule serves as the top-level entry point for a full Mist Compilation_Unit SHALL be a Mist_Compiler_Project concern, not a Storm or Storm_Source_Generator concern.

### Requirement 6: Storm Grammar Language — Imports and Namespaces

**User Story:** As a language designer, I want to split large grammars across files and avoid accidental name collisions, so that grammars are maintainable at scale.

#### Acceptance Criteria

1. THE Storm language SHALL support an `import "<path>";` declaration that references another Storm_Grammar_File.
2. THE Storm language SHALL support an `import "<path>" as <namespace>;` declaration that imports the referenced Storm_Grammar_File under a named namespace.
3. WHEN an unnamespaced `import "<path>";` is resolved, THE Storm_Source_Generator SHALL merge the imported file's declarations into the importing file's top-level declaration set.
4. WHEN a namespaced `import "<path>" as <namespace>;` is resolved, THE Storm_Source_Generator SHALL make the imported file's declarations accessible only via qualified references of the form `<namespace>.<name>` within the importing file.
5. IF an unnamespaced import introduces a Storm_Rule, Storm_Token, or Storm_Fragment name that is already defined in the importing file or in another unnamespaced imported file, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the duplicated name and the conflicting source locations, and SHALL fail the build.
6. IF an import path cannot be resolved, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the importing file, the import declaration's location, and the unresolved path, and SHALL fail the build.
7. WHEN the Storm_Source_Generator resolves imports, THE Storm_Source_Generator SHALL resolve each import path once per build regardless of how many times the same file is imported, so that cycles among imports are harmless.
8. THE effective declaration order used for token-match tiebreaking under Requirement 4.7 SHALL be: declarations in the root Storm_Grammar_File first, followed by declarations contributed by unnamespaced imports in the order those imports appear, followed by declarations reached only through namespaced imports.
9. WHEN an import path is relative, THE Storm_Source_Generator SHALL resolve the path relative to the directory of the importing Storm_Grammar_File.

### Requirement 7: Storm Grammar Language — Semantic Validation

**User Story:** As a language designer, I want Storm to catch obvious grammar bugs at build time, so that I don't discover them only when running the compiler on test inputs.

#### Acceptance Criteria

1. IF a Storm_Rule, Storm_Token, or Storm_Fragment references an undefined name, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the reference's source location and the undefined name, and SHALL fail the build.
2. IF a Storm_Rule contains an alternative that is statically unreachable given ordered-choice semantics, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the unreachable alternative.
3. IF the Storm_Source_Generator detects direct or indirect left-recursion that it cannot lower to LL(*), THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic identifying the offending Storm_Rule and its recursion path, and SHALL fail the build.
4. IF the Storm_Source_Generator detects a grammar ambiguity it cannot resolve via lookahead, THEN THE Storm_Source_Generator SHALL emit a build-time Diagnostic of at least Warning severity identifying the ambiguous Storm_Rule and the ambiguous alternatives.

### Requirement 8: Storm Bootstrap

**User Story:** As a compiler developer, I want Storm itself to be parsed by simple, hand-written code, so that there is no bootstrap problem when building the Storm_Source_Generator.

#### Acceptance Criteria

1. THE Storm_Source_Generator SHALL parse Storm_Grammar_Files using a hand-written C# parser for the Storm language.
2. THE Storm_Source_Generator SHALL NOT depend on a previously generated Storm-in-Storm parser to parse Storm_Grammar_Files.

### Requirement 9: Source-Generated Lexer, Parser, and AST

**User Story:** As a compiler developer, I want the lexer, parser, and AST types to be generated from the Storm grammar by a C# source generator, so that grammar changes flow automatically into compiled code without a separate build step.

#### Acceptance Criteria

1. THE Storm_Source_Generator SHALL be implemented as a C# source generator using the Roslyn source generator API supported by .NET 10.
2. WHEN the Storm_Source_Generator is invoked with a root Storm_Grammar_File as input, THE Storm_Source_Generator SHALL emit C# source code for an explicit Lexer, an explicit Parser, and AST node types for the described language.
3. THE generated Lexer and Parser SHALL NOT rely on runtime reflection to perform lexing or parsing.
4. THE generated Lexer SHALL expose an API that consumes source text in the described language and produces a Token_Stream.
5. THE generated Parser SHALL expose an API that consumes a Token_Stream and produces an AST, with one public entry point per Storm_Rule as specified in Requirement 5.3.
6. THE Storm_Source_Generator SHALL generate exactly one AST node type per Storm_Rule, with the generated type's name and shape determined by the Storm_Rule's name, its expression structure, and any named alternatives.
7. THE generated Lexer, Parser, and AST node type source code SHALL be deterministic for a given transitive closure of Storm_Grammar_File inputs, producing byte-identical output across repeated builds with unchanged inputs.
8. WHERE the Storm_Source_Generator encounters a grammar rule it cannot lower to C#, THE Storm_Source_Generator SHALL emit a build-time Diagnostic describing the unsupported construct and SHALL fail the build.

### Requirement 10: Lexing Stage

**User Story:** As a compiler developer, I want a well-defined lexing stage, so that downstream stages operate on a consistent stream of tokens.

#### Acceptance Criteria

1. THE Lexer SHALL consume Mist source text as input and produce a Token_Stream as output.
2. THE Lexer SHALL associate each Token with a source span expressed as a start offset and length within the input.
3. THE Lexer SHALL associate each Token with a line and column position within the input.
4. WHEN the Lexer encounters an input sequence that does not match any Storm_Token, THE Lexer SHALL emit a Diagnostic identifying the offending source span and SHALL continue lexing from the next character.
5. THE Lexer SHALL preserve the original source text for each Token sufficient to reconstruct the token's lexeme.
6. THE Lexer SHALL NOT perform syntactic or semantic analysis.

### Requirement 11: Parsing Stage

**User Story:** As a compiler developer, I want a well-defined parsing stage, so that syntax is separated from semantic concerns.

#### Acceptance Criteria

1. THE Parser SHALL consume a Token_Stream as input and produce an AST as output.
2. THE AST SHALL be strongly typed, with a distinct node type for each Storm_Rule as specified in Requirement 9.6.
3. THE AST SHALL associate each node with a source span covering the tokens from which the node was parsed.
4. WHEN the Parser encounters a Token_Stream that does not conform to the grammar, THE Parser SHALL emit a Diagnostic identifying the source span of the syntax error and SHALL halt parsing of the current Compilation_Unit.
5. THE Parser SHALL NOT perform name resolution, type checking, or other semantic analysis.

### Requirement 12: Semantic Model Construction

**User Story:** As a compiler developer, I want a single Semantic Model that unifies information from all compilation units, so that analyzers and backends can reason about the whole program.

#### Acceptance Criteria

1. THE Semantic_Model_Builder SHALL consume one or more ASTs belonging to a single Compilation and produce a single Semantic_Model.
2. THE Semantic_Model SHALL expose resolved symbols, scopes, and type information for the Compilation.
3. THE Semantic_Model SHALL associate each semantic entity with the AST node from which the entity was derived.
4. THE Semantic_Model SHALL be immutable once construction completes.
5. WHEN the Semantic_Model_Builder detects a semantic construction error that prevents producing a well-formed Semantic_Model, THE Semantic_Model_Builder SHALL emit a Diagnostic describing the error.
6. THE Semantic_Model_Builder SHALL produce a Semantic_Model even when Diagnostics of Error severity are reported, provided construction can proceed, so that downstream Analyzers can report additional Diagnostics.

### Requirement 13: Pluggable Analyzer Framework

**User Story:** As a compiler developer, I want analyzers to be pluggable, so that new language rules and lints can be added without modifying core pipeline stages.

#### Acceptance Criteria

1. THE Mist_Compiler SHALL define an Analyzer contract that accepts a Semantic_Model and produces a collection of Diagnostics.
2. THE Mist_Compiler SHALL allow zero or more Analyzers to be registered with the Compiler_Driver for a given Compilation.
3. THE Compiler_Driver SHALL invoke every registered Analyzer against the Semantic_Model for each Compilation.
4. THE Analyzer contract SHALL NOT permit an Analyzer to mutate the Semantic_Model.
5. THE Mist_Compiler SHALL include a Type_Checker Analyzer that verifies Mist type rules.
6. IF any registered Analyzer produces a Diagnostic of Error severity, THEN THE Compiler_Driver SHALL NOT invoke any Backend for that Compilation.
7. WHERE an Analyzer throws an unhandled exception, THE Compiler_Driver SHALL emit a Diagnostic identifying the Analyzer and SHALL continue invoking remaining Analyzers.

### Requirement 14: Pluggable Backend Framework

**User Story:** As a compiler developer, I want backends to be pluggable, so that Mist can target multiple output formats over time.

#### Acceptance Criteria

1. THE Mist_Compiler SHALL define a Backend contract that accepts a Semantic_Model and produces a target output artifact.
2. THE Mist_Compiler SHALL allow one Backend to be selected per Compilation.
3. THE Backend contract SHALL NOT permit a Backend to mutate the Semantic_Model.
4. THE Backend contract SHALL allow a Backend to emit Diagnostics describing backend-specific errors or warnings.
5. IF a Backend produces a Diagnostic of Error severity, THEN THE Compiler_Driver SHALL report the Diagnostic and SHALL treat the Compilation as failed.
6. WHERE multiple Backends are available, THE Compiler_CLI SHALL allow the caller to select which Backend to invoke.

### Requirement 15: C# Transpiler Backend

**User Story:** As a Mist user, I want an initial backend that produces a runnable program, so that I can execute Mist code on the .NET platform.

#### Acceptance Criteria

1. THE CSharp_Transpiler_Backend SHALL implement the Backend contract.
2. WHEN invoked with a Semantic_Model, THE CSharp_Transpiler_Backend SHALL emit C# source code that represents the semantics of the input Compilation.
3. THE CSharp_Transpiler_Backend SHALL write emitted C# source to a configurable output directory.
4. AFTER emitting C# source, THE CSharp_Transpiler_Backend SHALL invoke the .NET compiler to compile the emitted C# into a .NET assembly.
5. IF the .NET compiler reports errors when compiling emitted C#, THEN THE CSharp_Transpiler_Backend SHALL surface those errors as Diagnostics.

### Requirement 16: Compilation Pipeline Orchestration

**User Story:** As a compiler developer, I want a single driver that orchestrates the pipeline, so that stages are composed consistently across entry points.

#### Acceptance Criteria

1. THE Compiler_Driver SHALL execute the Compilation_Pipeline stages in the order: Lexer, Parser, Semantic_Model_Builder, Analyzers, Backend.
2. THE Compiler_Driver SHALL collect Diagnostics from every stage and expose them in a single aggregated result.
3. IF the Lexer produces a Diagnostic of Error severity for a Compilation_Unit, THEN THE Compiler_Driver SHALL skip the Parser stage for that Compilation_Unit.
4. IF the Parser produces a Diagnostic of Error severity for a Compilation_Unit, THEN THE Compiler_Driver SHALL exclude that Compilation_Unit's AST from Semantic_Model construction.
5. IF the Semantic_Model_Builder fails to produce a Semantic_Model, THEN THE Compiler_Driver SHALL skip the Analyzers and Backend stages.
6. THE Compiler_Driver SHALL return a structured result indicating overall success or failure, the selected Backend's output artifact location on success, and all aggregated Diagnostics.

### Requirement 17: Diagnostic Reporting

**User Story:** As a Mist user, I want consistent, actionable diagnostics from every stage, so that I can understand and fix problems in my code.

#### Acceptance Criteria

1. THE Mist_Compiler SHALL define a single Diagnostic type used by every pipeline stage.
2. THE Diagnostic type SHALL include severity, a stable diagnostic identifier, a human-readable message, and a source location where applicable.
3. THE Diagnostic type SHALL support at minimum the severities: Error and Warning.
4. THE Compiler_Driver SHALL preserve the originating pipeline stage for each Diagnostic in the aggregated result.
5. WHEN a Diagnostic refers to a source location, THE Diagnostic SHALL identify the Compilation_Unit, start line, start column, end line, and end column of the referenced span.

### Requirement 18: Command-Line Interface

**User Story:** As a Mist user, I want a command-line tool to compile Mist code, so that I can integrate the compiler into my build process.

#### Acceptance Criteria

1. THE Compiler_CLI SHALL be invoked as the `mist` command.
2. THE Compiler_CLI SHALL accept zero or more Mist source file paths as input.
3. WHEN no source file paths are provided, THE Compiler_CLI SHALL recursively discover all files with the `.mist` extension in the current working directory and its subdirectories, and SHALL use those as input.
4. THE Compiler_CLI SHALL accept an option specifying the Backend to invoke.
5. THE Compiler_CLI SHALL accept an option specifying the output directory for Backend artifacts.
6. WHEN compilation succeeds, THE Compiler_CLI SHALL exit with status code 0.
7. IF any Diagnostic of Error severity is produced, THEN THE Compiler_CLI SHALL exit with a non-zero status code.
8. THE Compiler_CLI SHALL print each Diagnostic to standard error in a human-readable format that includes severity, identifier, source location, and message.

### Requirement 19: Extensibility and Component Boundaries

**User Story:** As a compiler developer, I want strict component boundaries, so that stages can evolve independently and be tested in isolation.

#### Acceptance Criteria

1. THE `Mist` project SHALL organize each pipeline stage (Lexer consumption, Parser consumption, Semantic_Model_Builder, Analyzer framework, Backend framework) under a distinct top-level folder and a correspondingly distinct namespace.
2. Each pipeline stage SHALL expose its contract through public interfaces or abstract types declared in a dedicated contract namespace, separate from the namespace containing that stage's implementation types.
3. Within the `Mist` project, downstream stages SHALL depend only on the public contract namespaces of upstream stages, and SHALL NOT take dependencies on implementation namespaces of upstream stages. This is a review-enforced convention for v1; it is not required to be verified by compile-time project-reference checks.
4. THE `Storm.SourceGenerator` project SHALL have no source-code or project-reference dependency on the `Mist` project or the `Mist.Cli` project.
5. THE `Storm.SourceGenerator` project SHALL be consumable by any .NET project that supplies a Storm_Grammar_File, independent of Mist.

### Requirement 20: Determinism and Reproducibility

**User Story:** As a Mist user, I want deterministic builds, so that identical inputs produce identical outputs.

#### Acceptance Criteria

1. FOR a given transitive closure of Storm_Grammar_File inputs, Mist source inputs, Analyzer set, and Backend configuration, THE Compiler_Driver SHALL produce the same aggregated Diagnostics across repeated invocations.
2. FOR a given Semantic_Model, THE CSharp_Transpiler_Backend SHALL produce byte-identical emitted C# source across repeated invocations.
3. THE Compiler_Driver SHALL NOT depend on wall-clock time, process identifiers, or unseeded random sources for any output that is exposed to the user.

### Requirement 21: Testability

**User Story:** As a compiler developer, I want each stage to be independently testable, so that I can write focused unit tests and property-based tests for individual stages.

#### Acceptance Criteria

1. THE Lexer SHALL be invocable without the Parser, Semantic_Model_Builder, Analyzers, or any Backend.
2. THE Parser SHALL be invocable with a Token_Stream constructed by a test without invoking the Lexer.
3. THE Parser SHALL expose a public entry point for every Storm_Rule so that tests can parse arbitrary sub-language fragments directly.
4. THE Semantic_Model_Builder SHALL be invocable with ASTs constructed by a test without invoking the Parser.
5. Each Analyzer SHALL be invocable with a Semantic_Model constructed by a test without invoking prior pipeline stages.
6. Each Backend SHALL be invocable with a Semantic_Model constructed by a test without invoking prior pipeline stages.

## Non-Goals (Deferred to Later Refactor Pass)

The following concerns are intentionally out of scope for v1 of the Mist compiler architecture, and are expected to be addressed in a subsequent refactor pass once an end-to-end working compiler exists. They are listed here so that v1 design choices do not preclude them, but no v1 requirement depends on them.

1. **Incremental compilation and caching.** v1 recompiles every Compilation_Unit on every invocation. Caching lexer, parser, semantic model, or backend outputs across invocations is deferred.
2. **Language server / IDE integration.** v1 exposes no LSP server, no editor integration, and no incremental semantic-model APIs for tooling.
3. **Parser error recovery.** v1 parsers halt on the first syntax error in a Compilation_Unit. Reporting multiple syntax errors per file via error recovery is deferred. (Lexer error recovery is retained because it is cheap and already specified in Requirement 10.4.)
4. **Multi-backend fan-out.** v1 invokes exactly one Backend per Compilation. Running multiple Backends over the same Semantic_Model in a single invocation is deferred.
5. **Information-severity diagnostics.** v1 supports only Error and Warning severities. Informational and Hint severities (typical of IDE scenarios) are deferred.
6. **Source mapping from emitted C# back to Mist.** v1's CSharp_Transpiler_Backend emits C# and surfaces .NET compiler diagnostics as-is. Attributing emitted C# constructs back to originating Mist source spans (e.g., via `#line` directives or source maps) is deferred.
7. **Round-trip pretty-printer seam.** v1 does not require a Mist pretty-printer or a parse → print → parse round-trip testability hook. This is valuable for property-based testing but is deferred until the Mist syntax stabilizes.
8. **Diagnostic suggestions / "did you mean" hints.** v1's Storm and Mist Diagnostics identify errors precisely but do not offer corrective suggestions.
9. **Self-hosted Storm.** v1's Storm_Source_Generator parses Storm with a hand-written C# parser (Requirement 8). Expressing Storm's grammar in Storm and bootstrapping the generator from its own output is deferred.
10. **Self-hosted Mist.** v1 is implemented in C#. Rewriting the Mist compiler in Mist is a long-term north star, not a v1 concern.
11. **Concrete Mist syntax and semantics.** Deferred to a follow-up specification. This document pins down only the architectural contracts that downstream stages depend on.
12. **Concrete Storm syntax fine-tuning.** This document captures Storm's declarations, expressions, semantics, imports, and validation rules at a contract level. A follow-up specification will pin down the exact concrete syntax (precedence of combinators, escape rules in string literals and character classes, exact lexical rules for Storm identifiers, etc.).
13. **Splitting the Mist compiler into per-stage projects.** v1 keeps all pipeline stages inside a single `Mist` project with internal folder and namespace organization (see Requirements 1 and 19). Splitting stages into dedicated .NET projects — e.g., separate Lexer, Parser, Semantic_Model_Builder, Analyzer, and Backend projects — is deferred to a future refactor once scale or plugin distribution justifies the overhead.
