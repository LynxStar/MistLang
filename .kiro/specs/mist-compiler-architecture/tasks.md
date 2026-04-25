# Tasks

## Task 1: Solution and Project Scaffolding

- [x] 1.1 Create the `Mist.sln` solution file and the directory structure: `Source/Storm.SourceGenerator/`, `Source/Mist/`, `Source/Mist.CLI/`, `Test/Storm.SourceGenerator.Tests/`, `Test/Mist.Tests/`, `Test/Mist.CLI.Tests/`
- [x] 1.2 Create `Storm.SourceGenerator.csproj` targeting `netstandard2.0` with Roslyn analyzer/source-generator packaging (EnforceExtendedAnalyzerRules, IsRoslynComponent, Microsoft.CodeAnalysis.Analyzers and Microsoft.CodeAnalysis.CSharp references)
- [x] 1.3 Create `Mist.csproj` targeting `net10.0` with a project reference to `Storm.SourceGenerator` as an analyzer/source-generator dependency, and create the internal folder structure: `Grammar/`, `Common/Diagnostics/`, `Common/SourceText/`, `Common/Collections/`, `Lexing/Contracts/`, `Lexing/`, `Parsing/Contracts/`, `Parsing/`, `Semantics/Contracts/`, `Semantics/Model/`, `Semantics/Model/Types/`, `Analysis/Contracts/`, `Analysis/`, `Analysis/Builtin/`, `Backends/Contracts/`, `Backends/CSharpTranspiler/`, `Backends/CSharpTranspiler/Diagnostics/`, `Driver/Contracts/`, `Driver/`, `Generated/`
- [x] 1.4 Create `Mist.CLI.csproj` targeting `net10.0` with `OutputType=Exe` and a project reference to `Mist`, and create the internal folder structure: `Args/`, `Output/`
- [x] 1.5 Create `Storm.SourceGenerator.Tests.csproj` targeting `net10.0` with references to xUnit, FsCheck.Xunit (for property-based testing), and a project reference to `Storm.SourceGenerator`
- [x] 1.6 Create `Mist.Tests.csproj` targeting `net10.0` with references to xUnit, FsCheck.Xunit, and a project reference to `Mist`
- [x] 1.7 Create `Mist.CLI.Tests.csproj` targeting `net10.0` with references to xUnit, FsCheck.Xunit, and a project reference to `Mist.CLI`
- [x] 1.8 Verify the solution builds successfully with `dotnet build Mist.sln`

## Task 2: Storm.SourceGenerator — Diagnostics Infrastructure

- [x] 2.1 Create `StormDiagnosticIds.cs` in `Diagnostics/` with string constants for all Storm diagnostic IDs: `STORM_SYNTAX_ERROR`, `STORM_IMPORT_NOT_FOUND`, `STORM_DUPLICATE_NAME`, `STORM_UNDEFINED_REFERENCE`, `STORM_LEFT_RECURSION`, `STORM_UNREACHABLE_ALTERNATIVE`, `STORM_AMBIGUOUS_RULE`, `STORM_UNSUPPORTED_CONSTRUCT`
- [x] 2.2 Create `StormDiagnostic.cs` in `Diagnostics/` with a record type holding Id, Severity, Message, FilePath, Line, Column, and a helper to convert to Roslyn `DiagnosticDescriptor` / `Diagnostic` for build-time reporting

## Task 3: Storm.SourceGenerator — Hand-Written Storm Frontend (Lexer)

- [x] 3.1 Create `StormSourceText.cs` in `Frontend/` with a lightweight source-text wrapper holding file path, contents, and a method to compute line/column from offset
- [x] 3.2 Create `StormLexer.cs` in `Frontend/` implementing a hand-written lexer for the Storm language that tokenizes: keywords (`rule`, `token`, `fragment`, `hidden`, `import`, `as`), identifiers, string literals, character classes (`[...]`), operators (`=`, `|`, `;`, `(`, `)`, `?`, `*`, `+`, `:`, `.`), `//` line comments, whitespace, and produces a list of `StormToken` structs with Kind, Lexeme, Offset, Line, Column

## Task 4: Storm.SourceGenerator — Hand-Written Storm Frontend (AST)

- [x] 4.1 Create `StormSyntax.cs` in `Frontend/` defining the Storm AST node types as C# records: `StormFile` (imports + declarations), `StormImport` (path, optional namespace alias), `StormRuleDecl`, `StormTokenDecl`, `StormFragmentDecl`, `StormHiddenTokenDecl`, and expression nodes: `StormSequenceExpr`, `StormAlternationExpr`, `StormOptionalExpr`, `StormZeroOrMoreExpr`, `StormOneOrMoreExpr`, `StormGroupExpr`, `StormStringLiteralExpr`, `StormCharClassExpr`, `StormReferenceExpr`, `StormQualifiedReferenceExpr`, `StormLabeledExpr` (field label), `StormNamedAlternative` (with `as` name)

## Task 5: Storm.SourceGenerator — Hand-Written Storm Frontend (Parser)

- [x] 5.1 Create `StormParser.cs` in `Frontend/` implementing a hand-written recursive-descent parser for Storm grammar files that consumes the `StormToken` list from `StormLexer` and produces a `StormFile` AST node, supporting: import declarations, rule/token/fragment/hidden-token declarations, all expression combinators (sequence, alternation, grouping, `?`, `*`, `+`), string literals, character classes, references (plain and qualified), field labels (`name:element`), and named alternatives (`as Name`); reports syntax errors via `StormDiagnostic`
- [x] 5.2 Write unit tests in `Storm.SourceGenerator.Tests` for the Storm lexer: verify tokenization of keywords, identifiers, string literals, character classes, operators, comments, and whitespace
- [x] 5.3 Write unit tests in `Storm.SourceGenerator.Tests` for the Storm parser: verify parsing of import declarations, each declaration kind, expression combinators, field labels, named alternatives, and syntax error reporting

## Task 6: Storm.SourceGenerator — Import Resolution

- [x] 6.1 Create `StormImportResolver.cs` in `Resolve/` implementing the import resolution algorithm: starting from a root Storm file, recursively resolve imports using DFS with cycle detection (`visitedPaths` set), canonicalize relative paths against the importing file's directory, and collect all parsed `StormFile` ASTs keyed by canonical path
- [x] 6.2 Create `StormGrammar.cs` in `Resolve/` representing the effective grammar: merge unnamespaced imports' declarations into the root's declaration set in DFS-pre-order, store namespaced imports' declarations under qualified name tables (`<Namespace>.<Name>`), enforce duplicate-name detection for unnamespaced declarations, and establish the effective declaration order for token tiebreaking
- [x] 6.3 Write unit tests in `Storm.SourceGenerator.Tests` for import resolution: verify cycle handling, duplicate-name detection, namespaced vs unnamespaced merging, declaration order, and unresolved-import error reporting

## Task 7: Storm.SourceGenerator — Semantic Validation Passes

- [x] 7.1 Create `NameResolutionPass.cs` in `Validate/` that walks all expressions in the effective grammar and verifies every reference resolves to a declared rule/token/fragment (including qualified references for namespaced imports); rejects fragment references from rule expressions; emits `STORM_UNDEFINED_REFERENCE` diagnostics
- [x] 7.2 Create `LeftRecursionPass.cs` in `Validate/` that builds the rule-call graph from the effective grammar and detects direct and indirect left-recursion cycles; emits `STORM_LEFT_RECURSION` diagnostics with the cycle path
- [x] 7.3 Create `ReachabilityPass.cs` in `Validate/` that checks each rule's alternatives for unreachable alternatives under PEG ordered-choice semantics using FIRST/lookahead set analysis; emits `STORM_UNREACHABLE_ALTERNATIVE` warnings
- [x] 7.4 Create `AmbiguityPass.cs` in `Validate/` that checks each rule's alternatives for ambiguity that cannot be resolved within bounded lookahead; emits `STORM_AMBIGUOUS_RULE` warnings
- [x] 7.5 Write unit tests in `Storm.SourceGenerator.Tests` for each validation pass: undefined references, left-recursion detection, unreachable alternatives, and ambiguity detection

## Task 8: Storm.SourceGenerator — Grammar IR

- [x] 8.1 Create `GrammarIR.cs`, `TokenIR.cs`, `RuleIR.cs`, and `ExpressionIR.cs` in `IR/` defining the lowered grammar intermediate representation: `GrammarIR` holds ordered lists of `TokenIR` and `RuleIR`; `TokenIR` holds the token name, expression IR, hidden flag, and declaration-order index; `RuleIR` holds the rule name, alternatives with optional `as` names, field labels, and expression IR; `ExpressionIR` covers sequence, alternation, optional, zero-or-more, one-or-more, string-literal, char-class, token-ref, rule-ref, and inline-fragment (fragments inlined into token expressions)
- [x] 8.2 Implement lowering from the validated `StormGrammar` (effective grammar) to `GrammarIR`: inline fragment references into token expressions, materialize anonymous string literals in rules as synthetic token kinds, assign declaration-order indices, and resolve all references to IR-level pointers
- [x] 8.3 Write unit tests in `Storm.SourceGenerator.Tests` for IR lowering: verify fragment inlining, synthetic token creation for anonymous literals, declaration-order assignment, and reference resolution

## Task 9: Storm.SourceGenerator — Code Emitters Infrastructure

- [x] 9.1 Create `CodeWriter.cs` in `Emit/` providing a deterministic C# code-writing utility: manages indentation levels, writes lines/blocks, ensures sorted output where needed, produces no timestamps or non-deterministic content
- [x] 9.2 Create `EmissionContext.cs` in `Emit/` holding the consumer namespace, grammar name, and the `GrammarIR`, providing shared state for all emitters

## Task 10: Storm.SourceGenerator — Lexer Emitter

- [x] 10.1 Create `LexerEmitter.cs` in `Emit/` that generates the `TokenKind` enum (one member per token in declaration order, plus `EndOfFile` and `Error`), the DFA transition tables (NFA construction from token expressions → NFA union → determinization → equivalence-class compression), the accept-state table mapping to `TokenKind` ordinals with declaration-order tiebreaking, and the complete lexer class with a static `Lex` method that performs maximal-munch scanning, hidden-channel marking, and error recovery (emit Error token + advance one character)
- [x] 10.2 Write unit tests in `Storm.SourceGenerator.Tests` for the lexer emitter: verify generated `TokenKind` enum contents, DFA construction correctness for simple grammars, maximal-munch behavior, hidden-channel marking, and error-token generation

## Task 11: Storm.SourceGenerator — Parser Emitter

- [x] 11.1 Create `ParserEmitter.cs` in `Emit/` that generates a static parser class with one `Parse<RuleName>` method per rule: compute FIRST sets for LL(1) prediction, bounded lookahead chains for overlapping FIRST sets, and backtracking predicates as the LL(*) fallback; generate hidden-token attachment logic (leading/trailing trivia model); generate halt-on-first-error behavior returning null root
- [x] 11.2 Write unit tests in `Storm.SourceGenerator.Tests` for the parser emitter: verify generated parse methods for simple grammars, LL(1) vs lookahead vs backtracking selection, hidden-token attachment, and error-halt behavior

## Task 12: Storm.SourceGenerator — AST Emitter

- [x] 12.1 Create `AstEmitter.cs` in `Emit/` that generates AST node types: one `sealed record <Name>Node : SyntaxNode` per single-alternative rule, `abstract record <Name>Node : SyntaxNode` with `sealed record <AltName><Name>Node : <Name>Node` subtypes for rules with named alternatives; map `?` to nullable, `*`/`+` to `ImmutableArray<T>`, token references to `Token` properties; generate field-label-based property names; generate `Children()` override yielding child nodes in source order; generate the `SyntaxNode` base record and `Storm.Runtime` types (`Token`, `TokenChannel`, `SourceSpan`)
- [x] 12.2 Write unit tests in `Storm.SourceGenerator.Tests` for the AST emitter: verify generated type hierarchy for single-alternative rules, named-alternative rules, optional/list properties, field labels, and `Children()` enumeration

## Task 13: Storm.SourceGenerator — Generator Entry Point and Determinism

- [x] 13.1 Create `StormIncrementalGenerator.cs` in `Generator/` implementing `IIncrementalGenerator`: register for additional files matching `*.storm`, find the root Storm grammar file, orchestrate the full pipeline (parse → resolve imports → validate → lower to IR → emit lexer + parser + AST + runtime types), report diagnostics via `SourceProductionContext`, and add generated source files via `context.AddSource`
- [x] 13.2 Ensure all emitters produce deterministic output: sorted declarations, sorted enum members by declaration order, sorted type definitions, no timestamps, no process IDs; verify by running the generator twice on the same input and asserting byte-identical output
- [x] 13.3 Write unit tests in `Storm.SourceGenerator.Tests` for the full generator pipeline: provide a simple Storm grammar as an additional file, run the generator, and verify that expected source files are produced with correct content

## Task 14: Mist — Common Types (Diagnostics)

- [x] 14.1 Create `DiagnosticSeverity.cs` in `Common/Diagnostics/` with the `DiagnosticSeverity` enum: `Warning`, `Error`
- [x] 14.2 Create `DiagnosticId.cs` in `Common/Diagnostics/` with the `DiagnosticId` readonly record struct
- [x] 14.3 Create `PipelineStage.cs` in `Common/Diagnostics/` with the `PipelineStage` enum: `StormGeneration`, `Lexing`, `Parsing`, `SemanticModelConstruction`, `Analysis`, `Backend`, `Driver`
- [x] 14.4 Create `Diagnostic.cs` in `Common/Diagnostics/` with the `Diagnostic` sealed record containing Id, Severity, Stage, Message, Location (nullable `LinePositionSpan`), CompilationUnitPath (nullable string), and AnalyzerName (nullable string)
- [x] 14.5 Create `DiagnosticBag.cs` in `Common/Diagnostics/` with the append-only `DiagnosticBag` class: `Add(Diagnostic)`, `HasErrors` property, `ToImmutableArray()` method

## Task 15: Mist — Common Types (SourceText and Collections)

- [x] 15.1 Create `LinePosition.cs` in `Common/SourceText/` with the `LinePosition` readonly record struct (1-based Line and Column)
- [x] 15.2 Create `LinePositionSpan.cs` in `Common/SourceText/` with the `LinePositionSpan` readonly record struct (Start and End `LinePosition`)
- [x] 15.3 Create `SourceSpan.cs` in `Common/SourceText/` with the `SourceSpan` readonly record struct (Start offset, Length, computed End property)
- [x] 15.4 Create `SourceText.cs` in `Common/SourceText/` with the `SourceText` sealed class: Path, Contents, `GetSpan(SourceSpan)` returning `ReadOnlySpan<char>`, `ToLinePositionSpan(SourceSpan)` converting offsets to line/column using a lazily-cached line-start offset array
- [x] 15.5 Create `ImmutableOrderedMap.cs` in `Common/Collections/` with a deterministic ordered dictionary implementation preserving insertion order
- [x] 15.6 Write unit tests in `Mist.Tests` for `SourceText`: verify `GetSpan` returns correct slices, `ToLinePositionSpan` computes correct line/column for single-line and multi-line inputs, and edge cases (empty input, span at end of file)

## Task 16: Mist — Lexing Stage

- [x] 16.1 Create `ILexer.cs` in `Lexing/Contracts/` with the `ILexer` interface: `TokenStream Lex(SourceText source, DiagnosticBag diagnostics)`
- [x] 16.2 Create `TokenStream.cs` in `Lexing/Contracts/` with the `TokenStream` sealed class: `Source` property, `AllTokens` (`ImmutableArray<Token>` including hidden), `DefaultChannelTokens` (`ImmutableArray<Token>` excluding hidden, computed once at construction)
- [x] 16.3 Create `MistLexer.cs` in `Lexing/` implementing `ILexer` by wrapping the Storm-generated static `Lex` method behind the interface for testability and DI

## Task 17: Mist — Parsing Stage

- [x] 17.1 Create `IParser.cs` in `Parsing/Contracts/` with the `IParser` interface: `ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)`
- [x] 17.2 Create `ParseResult.cs` in `Parsing/Contracts/` with the `ParseResult` sealed record: Source (`SourceText`), Tokens (`TokenStream`), Root (nullable `SyntaxNode`), Diagnostics (`ImmutableArray<Diagnostic>`)
- [x] 17.3 Create `MistParser.cs` in `Parsing/` implementing `IParser` by wrapping the Storm-generated static parse method for the chosen entry rule

## Task 18: Mist — Semantics Stage

- [x] 18.1 Create `ISymbol.cs`, `IType.cs`, `IScope.cs` in `Semantics/Contracts/` with the interfaces as specified in the design: `ISymbol` (Name, Kind, ContainingScope, DeclaredType, DeclarationSpan), `IType` (DisplayName, Kind), `IScope` (Parent, Members, Lookup), plus `SymbolKind` and `TypeKind` enums
- [x] 18.2 Create `ISemanticModel.cs` in `Semantics/Contracts/` with the `ISemanticModel` interface: Compilation, GlobalScope, Units, GetDeclaringNode, GetDeclaredSymbol, GetTypeOf, AllSymbols
- [x] 18.3 Create `ISemanticModelBuilder.cs` in `Semantics/Contracts/` with the `ISemanticModelBuilder` interface: `ISemanticModel Build(Compilation compilation, ImmutableArray<SyntaxNode> asts, DiagnosticBag diagnostics)`
- [x] 18.4 Create `SemanticModel.cs` in `Semantics/Model/` implementing `ISemanticModel` as an immutable type: all collections exposed as `IReadOnly*` or `ImmutableArray`, no public mutation API after construction
- [x] 18.5 Create `SymbolTable.cs` and `Scope.cs` in `Semantics/Model/` implementing `IScope` with parent-chain lookup and `ISymbol` implementations
- [x] 18.6 Create `SemanticModelBuilder.cs` in `Semantics/Model/` implementing `ISemanticModelBuilder`: accepts ASTs and a Compilation, walks ASTs to build symbols/scopes/types, freezes all mutable builders before returning the immutable `SemanticModel`; reports diagnostics for semantic construction errors

## Task 19: Mist — Analysis Framework

- [x] 19.1 Create `IAnalyzer.cs` in `Analysis/Contracts/` with the `IAnalyzer` interface: `string AnalyzerId { get; }`, `void Analyze(ISemanticModel model, DiagnosticBag diagnostics)`
- [x] 19.2 Create `AnalyzerContext.cs` in `Analysis/Contracts/` (reserved for future per-analyzer configuration)
- [x] 19.3 Create `AnalyzerRunner.cs` in `Analysis/` that accepts an `ImmutableArray<IAnalyzer>`, runs each analyzer against the semantic model, catches unhandled exceptions per analyzer and emits a driver-level diagnostic identifying the failing analyzer, and returns aggregated diagnostics
- [x] 19.4 Create `TypeCheckerAnalyzer.cs` in `Analysis/Builtin/` implementing `IAnalyzer` with `AnalyzerId = "TypeChecker"`: a stub that walks `ISemanticModel.AllSymbols` and reports type-checking diagnostics (concrete type rules deferred to Mist-language spec; the architectural skeleton and contract are established here)

## Task 20: Mist — Backends Framework

- [x] 20.1 Create `BackendId.cs` in `Backends/Contracts/` with the `BackendId` readonly record struct
- [x] 20.2 Create `BackendResult.cs` in `Backends/Contracts/` with the `BackendResult` sealed record: Succeeded, PrimaryArtifactPath (nullable string), Diagnostics (`ImmutableArray<Diagnostic>`)
- [x] 20.3 Create `BackendContext.cs` in `Backends/Contracts/` (reserved for future backend configuration)
- [x] 20.4 Create `IBackend.cs` in `Backends/Contracts/` with the `IBackend` interface: `BackendId Id { get; }`, `BackendResult Emit(ISemanticModel model, string outputDirectory, DiagnosticBag diagnostics)`

## Task 21: Mist — C# Transpiler Backend

- [x] 21.1 Create `CSharpTranspilerBackend.cs` in `Backends/CSharpTranspiler/` implementing `IBackend` with `Id = new BackendId("csharp")`: orchestrates emission via `CSharpEmitter` then compilation via `CSharpCompilationDriver`, returns `BackendResult`
- [x] 21.2 Create `CSharpEmitter.cs` in `Backends/CSharpTranspiler/` that walks `ISemanticModel.AllSymbols` in deterministic order (sorted by fully-qualified name), emits one `.cs` file per top-level declaration, emits a `.csproj` with `<Deterministic>true</Deterministic>`, and writes all files to the output directory in sorted order
- [x] 21.3 Create `CSharpCompilationDriver.cs` in `Backends/CSharpTranspiler/` that invokes `dotnet build` on the emitted project directory, captures stdout/stderr, and delegates diagnostic parsing to `RoslynDiagnosticMapper`
- [x] 21.4 Create `RoslynDiagnosticMapper.cs` in `Backends/CSharpTranspiler/Diagnostics/` that parses MSBuild-format diagnostic lines (`file(line,col): severity code: message`) and converts them to `Mist.Common.Diagnostics.Diagnostic` instances with `PipelineStage.Backend`

## Task 22: Mist — Compiler Driver

- [x] 22.1 Create `CompilationUnit.cs` and `Compilation.cs` in `Driver/Contracts/` as sealed records per the design
- [x] 22.2 Create `CompilerOptions.cs` in `Driver/Contracts/` with the `CompilerOptions` sealed record: SourcePaths, Backend (`BackendId`), OutputDirectory, EnabledAnalyzerIds
- [x] 22.3 Create `CompilerResult.cs` in `Driver/Contracts/` with the `CompilerResult` sealed record: Succeeded, PrimaryOutputArtifactPath (nullable), Diagnostics (`ImmutableArray<Diagnostic>`)
- [x] 22.4 Create `ICompilerDriver.cs` in `Driver/Contracts/` with the `ICompilerDriver` interface: `CompilerResult Compile(CompilerOptions options)`
- [x] 22.5 Create `CompilerDriver.cs` in `Driver/` implementing `ICompilerDriver`: load source files into `CompilationUnit`s, create `Compilation`, execute the pipeline (Lex → Parse → Build semantic model → Run analyzers → Emit via backend) with short-circuit logic (skip parse on lex error, exclude AST on parse error, skip analyzers+backend if semantic model fails, skip backend if analyzer errors), aggregate all diagnostics in stage order, resolve the backend from a registry, and return `CompilerResult`

## Task 23: Mist.CLI — Argument Parsing

- [x] 23.1 Create `CliArguments.cs` in `Args/` with a record holding: SourceFiles (list of strings), BackendId (string, default `"csharp"`), OutputDirectory (string, default `"./out"`)
- [x] 23.2 Create `CliArgumentParser.cs` in `Args/` with a hand-rolled argument parser: parse `mist [source-files...] [--backend <id>] [--output <dir>]`, return `CliArguments` or report errors
- [x] 23.3 Write unit tests in `Mist.CLI.Tests` for `CliArgumentParser`: verify parsing of source files, `--backend` flag, `--output` flag, defaults when flags are omitted, and error cases (missing flag values)

## Task 24: Mist.CLI — Diagnostic Formatting and Program Entry Point

- [x] 24.1 Create `DiagnosticFormatter.cs` in `Output/` that formats a `Diagnostic` to the MSBuild-compatible format: `<path>(<startLine>,<startCol>): <severity> <id>: <message>`, handling diagnostics with and without source locations
- [x] 24.2 Create `Program.cs` with the `Main` entry point: parse CLI arguments via `CliArgumentParser`, discover `.mist` files recursively if no source files provided, map `CliArguments` to `CompilerOptions`, construct and invoke `ICompilerDriver.Compile`, format each diagnostic to stderr via `DiagnosticFormatter`, exit with code 0 on success or 1 on any Error-severity diagnostic
- [x] 24.3 Write unit tests in `Mist.CLI.Tests` for `DiagnosticFormatter`: verify formatting of diagnostics with source locations, without source locations, and with different severities
- [x] 24.4 Write unit tests in `Mist.CLI.Tests` for `.mist` file discovery logic: verify recursive discovery in a temp directory structure

## Task 25: Mist — Stub Storm Grammar File

- [x] 25.1 Create a minimal `Mist.storm` file in `Source/Mist/Grammar/` with a placeholder grammar containing at least one `token` declaration (e.g., whitespace as hidden, a simple identifier token) and one `rule` declaration (e.g., a compilation-unit rule), sufficient for the generated lexer/parser/AST to compile and for end-to-end pipeline tests to exercise all stages

## Task 26: Property-Based Tests — Lexer Properties

- [x] 26.1 (**PBT**) Write property test "Lexer round-trip" in `Mist.Tests`: for any generated Mist source string, concatenating all token lexemes (including hidden tokens) from `ILexer.Lex` reproduces the original source text exactly. **Validates: Correctness Property 1**
- [x] 26.2 (**PBT**) Write property test "Lexer completeness" in `Mist.Tests`: for any input string, the token spans from `ILexer.Lex` cover every character in the input exactly once (no gaps, no overlaps). **Validates: Correctness Property 2**
- [x] 26.3 (**PBT**) Write property test "Lexer determinism" in `Mist.Tests`: for any input string, repeated calls to `ILexer.Lex` produce identical `TokenStream` instances (same count, kinds, spans). **Validates: Correctness Property 3**

## Task 27: Property-Based Tests — Parser and AST Properties

- [x] 27.1 (**PBT**) Write property test "Parser span coverage" in `Mist.Tests`: for any successfully parsed AST, the root node's `Span` covers the entire default-channel token range. **Validates: Correctness Property 4**
- [x] 27.2 (**PBT**) Write property test "AST children consistency" in `Mist.Tests`: for any AST node, `Children()` returns the same sequence on repeated calls, and every child's `Span` is contained within the parent's `Span`. **Validates: Correctness Property 5**

## Task 28: Property-Based Tests — Semantic Model and Diagnostics Properties

- [x] 28.1 (**PBT**) Write property test "Semantic model immutability" in `Mist.Tests`: after `ISemanticModelBuilder.Build` returns, verify via reflection that no public API on `ISemanticModel` or any reachable `ISymbol`, `IType`, or `IScope` permits mutation (all public surfaces return `IReadOnly*` / `Immutable*` types, no public setters). **Validates: Correctness Property 6**
- [x] 28.2 (**PBT**) Write property test "Diagnostic completeness" in `Mist.Tests`: for any compilation (generated source inputs), every diagnostic in `CompilerResult.Diagnostics` has a non-null `Id`, a valid `Severity`, and a `Stage`; diagnostics with a source location have a valid `CompilationUnitPath` and `Start ≤ End`. **Validates: Correctness Property 7**

## Task 29: Property-Based Tests — Pipeline and Backend Properties

- [x] 29.1 (**PBT**) Write property test "Pipeline short-circuit" in `Mist.Tests`: if the lexer reports an Error for a compilation unit, no parser diagnostic is produced for that unit; if the parser reports an Error, no semantic-model diagnostic references that unit; if any analyzer reports an Error, no backend diagnostic is produced. **Validates: Correctness Property 8**
- [x] 29.2 (**PBT**) Write property test "Backend determinism" in `Mist.Tests`: for any `ISemanticModel`, repeated calls to `IBackend.Emit` with the same output directory produce byte-identical file sets. **Validates: Correctness Property 9**

## Task 30: Property-Based Tests — Storm Generator Determinism

- [x] 30.1 (**PBT**) Write property test "Storm generator determinism" in `Storm.SourceGenerator.Tests`: for any set of Storm grammar files (generated via a grammar generator), repeated invocations of the source generator produce byte-identical C# output. **Validates: Correctness Property 10**
