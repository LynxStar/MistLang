# Design Document

## Overview

This design turns the Requirements document for the Mist compiler architecture into a concrete, buildable v1 plan. The v1 goal is time-to-value: the minimum architectural surface that takes a Mist source file all the way to an executable .NET assembly via the C# transpiler backend, with clean enough stage boundaries that later refactors (incremental compilation, additional backends, LSP, parser error recovery, multi-stage projects) are not blocked.

The design pins down three deliverables:

1. **`Storm.SourceGenerator`** — a language-agnostic Roslyn source generator (`netstandard2.0`) that consumes Storm grammar files and emits a Lexer, Parser, and AST for whatever language the grammar describes. It contains zero Mist-specific code.
2. **`Mist`** — a single .NET 10 / C# 14 class library that contains every compiler stage: it supplies the Mist Storm grammar, consumes the generated Lexer/Parser/AST, then builds the semantic model, runs analyzers (including the type checker), and drives the C# transpiler backend. Stage boundaries are enforced by folder + namespace convention and by a dedicated contracts namespace per stage.
3. **`Mist.CLI`** — a thin executable that wires command-line arguments into `Mist`'s compiler driver.

All concrete Mist syntax and semantics are out of scope. Everything here is architecture: contracts, stage pipelines, determinism strategy, and testability seams. Concrete Storm syntax details (escape rules, exact identifier lexing, combinator precedence) are left to a follow-up spec; this design commits only to the Storm shape the Requirements document already pins down.

The design follows the Requirements document's numbering so that every architectural choice has a traceable requirement. Requirement references appear inline as `(R{n}.{m})`.

## Project and Namespace Layout

### Solution layout

```
Mist.slnx
├── Source/
│   ├── Storm.SourceGenerator/          # netstandard2.0, Roslyn source generator
│   ├── Mist/                           # net10.0, compiler library
│   └── Mist.CLI/                       # net10.0, executable
└── Test/
    ├── Storm.SourceGenerator.Tests/
    ├── Mist.Tests/
    └── Mist.CLI.Tests/
```

Exactly three non-test projects, per R1.4. Test projects are unconstrained in number.

### `Storm.SourceGenerator` layout

```
Storm.SourceGenerator/
├── Storm.SourceGenerator.csproj        # netstandard2.0, Roslyn analyzer packaging
├── Generator/
│   └── StormIncrementalGenerator.cs    # IIncrementalGenerator entry point
├── Frontend/                           # Storm parsing, hand-written
│   ├── StormLexer.cs
│   ├── StormParser.cs                  # recognizes `grammar`, `import`, `rule`, `token`, `fragment`, `hidden`
│   ├── StormSyntax.cs                  # Storm AST node types
│   └── StormSourceText.cs
├── Resolve/                            # Import resolution + name binding
│   ├── StormImportResolver.cs
│   └── StormGrammar.cs                 # "Effective grammar" aggregate; exposes GrammarName
├── Validate/                           # Semantic validation of Storm grammars
│   ├── NameResolutionPass.cs
│   ├── LeftRecursionPass.cs
│   ├── ReachabilityPass.cs
│   └── AmbiguityPass.cs
├── IR/                                 # Lowered grammar IR used by emitters
│   ├── GrammarIR.cs
│   ├── TokenIR.cs
│   ├── RuleIR.cs
│   └── ExpressionIR.cs
├── Emit/                               # C# code emitters
│   ├── LexerEmitter.cs
│   ├── ParserEmitter.cs
│   ├── AstEmitter.cs
│   ├── EmissionContext.cs
│   └── CodeWriter.cs                   # deterministic indentation + sorting
└── Diagnostics/
    ├── StormDiagnostic.cs              # Storm's own generator-build diagnostic
    └── StormDiagnosticIds.cs
```

Notes:

- The source generator has no dependency on `Mist` or `Mist.CLI` (R19.4). The runtime support types that the generated code references (`Token`, `TokenChannel`, `SourceSpan`, AST base classes) are **also emitted** by the source generator into the consumer's compilation under a `Storm.Runtime` namespace. This keeps `Storm.SourceGenerator` usable by any consumer (R19.5) without requiring a separate runtime NuGet package in v1.
- Every file under `Frontend/` is hand-written (R8.1, R8.2).

### `Mist` layout

The top-level folders mirror the stages called out by R19.1. Each stage has a public contracts namespace (`.Contracts` suffix) containing interfaces and data contracts, separate from the implementation namespace (R19.2). Downstream stages reference only upstream contracts (R19.3).

```
Mist/
├── Mist.csproj                         # net10.0, references Storm.SourceGenerator
├── Grammar/
│   └── Mist.storm                      # root Storm_Grammar_File for Mist
├── Common/                             # cross-cutting primitives
│   ├── Diagnostics/                    # Mist.Common.Diagnostics
│   │   ├── Diagnostic.cs
│   │   ├── DiagnosticSeverity.cs
│   │   ├── DiagnosticId.cs
│   │   ├── PipelineStage.cs
│   │   └── DiagnosticBag.cs
│   ├── SourceText/                     # Mist.Common.Text
│   │   ├── SourceText.cs
│   │   ├── SourceSpan.cs
│   │   ├── LinePosition.cs
│   │   └── LinePositionSpan.cs
│   └── Collections/
│       └── ImmutableOrderedDictionary.cs # deterministic ordered dictionary
├── Lexing/
│   ├── Contracts/                      # Mist.Lexing.Contracts
│   │   ├── ILexer.cs
│   │   └── TokenStream.cs
│   └── MistLexer.cs                    # wraps Storm-generated lexer
├── Parsing/
│   ├── Contracts/                      # Mist.Parsing.Contracts
│   │   ├── IParser.cs
│   │   └── ParseResult.cs
│   └── MistParser.cs                   # wraps Storm-generated parser, picks entry rule
├── Semantics/
│   ├── Contracts/                      # Mist.Semantics.Contracts
│   │   ├── ISemanticModelBuilder.cs
│   │   ├── ISemanticModel.cs
│   │   ├── ISymbol.cs
│   │   ├── IType.cs
│   │   └── IScope.cs
│   └── Model/
│       ├── SemanticModelBuilder.cs
│       ├── SemanticModel.cs
│       ├── SymbolTable.cs
│       ├── Scope.cs
│       └── Types/
├── Analysis/
│   ├── Contracts/                      # Mist.Analysis.Contracts
│   │   ├── IAnalyzer.cs
│   │   └── AnalyzerContext.cs
│   ├── AnalyzerRunner.cs
│   └── Builtin/
│       └── TypeCheckerAnalyzer.cs
├── Backends/
│   ├── Contracts/                      # Mist.Backends.Contracts
│   │   ├── IBackend.cs
│   │   ├── BackendContext.cs
│   │   ├── BackendResult.cs
│   │   └── BackendId.cs
│   └── CSharpTranspiler/
│       ├── CSharpTranspilerBackend.cs
│       ├── CSharpEmitter.cs
│       ├── CSharpCompilationDriver.cs
│       └── Diagnostics/
│           └── RoslynDiagnosticMapper.cs
├── Driver/
│   ├── Contracts/                      # Mist.Driver.Contracts
│   │   ├── ICompilerDriver.cs
│   │   ├── Compilation.cs              # MistCompilation type
│   │   ├── CompilationUnit.cs
│   │   ├── CompilerOptions.cs
│   │   └── CompilerResult.cs
│   └── CompilerDriver.cs
└── Generated/                          # owned by Storm.SourceGenerator output
    └── (Mist.Generated.*)              # Lexer, Parser, AST node types, Storm.Runtime
```

Namespace convention:

- Public contract types live in `Mist.<Stage>.Contracts`.
- Implementation types live in `Mist.<Stage>` or `Mist.<Stage>.<Subarea>`.
- Shared primitives (Diagnostic, SourceSpan, etc.) live in `Mist.Common.*`.
- Generator output lives in `Mist.Generated.*` plus a `Storm.Runtime` namespace for emitted runtime support.

### `Mist.CLI` layout

```
Mist.CLI/
├── Mist.CLI.csproj                     # net10.0, OutputType=Exe
├── Program.cs                          # Main, wires args → CompilerDriver
├── Args/
│   ├── CliArguments.cs
│   └── CliArgumentParser.cs
└── Output/
    └── DiagnosticFormatter.cs          # human-readable diagnostic rendering
```

`Mist.CLI` contains no pipeline-stage logic (R1.9). `Program.cs` constructs `CompilerOptions` from parsed arguments, calls `ICompilerDriver.Compile`, formats diagnostics to stderr, and maps the aggregated result to an exit code.

## Core Domain Types

Signatures below are illustrative C#, not final code. Fields use C# 14 / .NET 10 idioms: `readonly struct` for small value-like types, `record` for immutable reference data, nullable annotations throughout.

### Diagnostics and severity

```csharp
namespace Mist.Common.Diagnostics;

public enum DiagnosticSeverity
{
    Warning,
    Error,
    // Information / Hint intentionally omitted for v1 (Non-Goal 5).
}

public readonly record struct DiagnosticId(string Value);

public enum PipelineStage
{
    StormGeneration, // build-time, reported via Roslyn
    Lexing,
    Parsing,
    SemanticModelConstruction,
    Analysis,
    Backend,
    Driver,
}

public sealed record Diagnostic(
    DiagnosticId Id,
    DiagnosticSeverity Severity,
    PipelineStage Stage,
    string Message,
    LinePositionSpan? Location,     // null when not tied to source
    string? CompilationUnitPath,    // path of the CompilationUnit when relevant
    string? AnalyzerName);          // populated only for analyzer-origin diagnostics

public sealed class DiagnosticBag
{
    public void Add(Diagnostic diagnostic);
    public bool HasErrors { get; }
    public ImmutableArray<Diagnostic> ToImmutableArray();
}
```

`DiagnosticBag` is **append-only** per compilation unit and is drained into an immutable array at stage boundaries. `Mist.Common.Diagnostics.Diagnostic` is the single diagnostic type used across every stage (R17.1). Requirement R17.5 requires start/end line/column; `LinePositionSpan` below carries those.

### Source location

```csharp
namespace Mist.Common.Text;

public readonly record struct LinePosition(int Line, int Column); // both 1-based

public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End);

public readonly record struct SourceSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed class SourceText
{
    public string Path { get; }
    public string Contents { get; }
    public ReadOnlySpan<char> GetSpan(SourceSpan span);
    public LinePositionSpan ToLinePositionSpan(SourceSpan span);
    // Internally caches a line-start offset array built lazily.
}
```

`SourceSpan` is the compact offset-based form the Lexer and Parser attach to every token / AST node (R10.2, R11.3). `LinePositionSpan` is the human-facing form used in diagnostics (R17.5). `SourceText` converts between the two so that stages do not each re-scan newlines.

### Tokens and token streams

Because `Storm.SourceGenerator` is language-agnostic, `Token` and `TokenStream` are emitted by the generator into the consumer under `Storm.Runtime`. `Mist.Lexing.Contracts` re-exports type aliases / thin wrappers so Mist-side code uses Mist-owned contract types.

```csharp
namespace Storm.Runtime; // emitted by Storm.SourceGenerator

public enum TokenChannel { Default, Hidden }

public readonly record struct Token(
    int Kind,            // discriminator; matched to a generated TokenKind enum
    SourceSpan Span,
    LinePosition Start,
    LinePosition End,
    TokenChannel Channel,
    string SourceTextRef); // reference into owning SourceText; no copy
```

```csharp
namespace Mist.Lexing.Contracts;

public sealed class TokenStream
{
    public SourceText Source { get; }
    public ImmutableArray<Token> AllTokens { get; }        // including hidden
    public ImmutableArray<Token> DefaultChannelTokens { get; } // parser's view
    public TokenStream(SourceText source, ImmutableArray<Token> tokens);
}

public interface ILexer
{
    TokenStream Lex(SourceText source, DiagnosticBag diagnostics);
}
```

The two arrays are **views into the same underlying `ImmutableArray<Token>`**; `DefaultChannelTokens` is computed once at construction and cached. R4.2 requires all tokens (including hidden) to be available; R4.4 requires the parser to skip hidden tokens; exposing both views satisfies both without double-lexing.

### AST base types

The source generator emits one concrete AST node type per Storm rule (R9.6), with named alternatives becoming subtypes. All node types derive from a common emitted base:

```csharp
namespace Storm.Runtime; // emitted

public abstract record SyntaxNode
{
    public SourceSpan Span { get; init; }
    public ImmutableArray<Token> LeadingHiddenTokens { get; init; }
    public ImmutableArray<Token> TrailingHiddenTokens { get; init; }

    // Generated concrete nodes override to enumerate child nodes in source order.
    public abstract IEnumerable<SyntaxNode> Children();
}
```

Mist code never names `SyntaxNode` directly in contracts; `Mist.Parsing.Contracts.ParseResult` carries the concrete generated root type.

```csharp
namespace Mist.Parsing.Contracts;

public sealed record ParseResult(
    SourceText Source,
    TokenStream Tokens,
    SyntaxNode? Root,              // null iff parser halted on first error (R11.4)
    ImmutableArray<Diagnostic> Diagnostics);
```

### Semantic model shape

The semantic model is exposed through interfaces so that test doubles can construct `ISemanticModel` instances without going through the builder (R21.4, R21.5, R21.6). Mist-specific symbol/type implementations live in `Mist.Semantics.Model`; the contract namespace stays intentionally shallow.

```csharp
namespace Mist.Semantics.Contracts;

public interface ISemanticModel
{
    MistCompilation Compilation { get; }
    IScope GlobalScope { get; }
    IReadOnlyList<CompilationUnit> Units { get; }

    // Every semantic entity traces back to an AST node (R12.3).
    SyntaxNode GetDeclaringNode(ISymbol symbol);

    // Reverse lookups used by analyzers and the backend.
    ISymbol? GetDeclaredSymbol(SyntaxNode node);
    IType? GetTypeOf(SyntaxNode node);

    ImmutableArray<ISymbol> AllSymbols { get; }
}

public interface ISymbol
{
    string Name { get; }
    SymbolKind Kind { get; }
    IScope ContainingScope { get; }
    IType? DeclaredType { get; }
    SourceSpan? DeclarationSpan { get; }
}

public interface IType
{
    string DisplayName { get; }
    TypeKind Kind { get; }
    // Concrete type hierarchy is Mist-specific and is intentionally deferred
    // to the follow-up Mist-language spec. At the architectural level we
    // only commit to identity-by-reference and a display name.
}

public interface IScope
{
    IScope? Parent { get; }
    IReadOnlyList<ISymbol> Members { get; }
    ISymbol? Lookup(string name);
}

public enum SymbolKind { /* deferred to Mist-language spec */ }
public enum TypeKind { /* deferred to Mist-language spec */ }
```

Immutability (R12.4) is enforced by exposing only `IReadOnly*` / `ImmutableArray` surfaces and by constructing the entire model in one `SemanticModelBuilder.Build` call that freezes internal mutable builders before returning.

### MistCompilation and CompilationUnit

```csharp
namespace Mist.Driver.Contracts;

public sealed record CompilationUnit(SourceText Source);

public sealed record MistCompilation(ImmutableArray<CompilationUnit> Units)
{
    public string DisplayName { get; init; } = "";
}

public sealed record CompilerOptions(
    ImmutableArray<string> SourcePaths,
    BackendId Backend,
    string OutputDirectory,
    ImmutableArray<string> EnabledAnalyzerIds); // empty = run all registered

public sealed record CompilerResult(
    bool Succeeded,
    string? PrimaryOutputArtifactPath,       // null on failure
    ImmutableArray<Diagnostic> Diagnostics); // aggregated, in stage order (R16.2, R17.4)
```

`MistCompilation` is the unit of work the driver operates on (R12.1, R16.1). `CompilationUnit` is one Mist source file (glossary; R16.3–R16.5). `CompilerResult` is the single structured artifact returned by `ICompilerDriver.Compile` (R16.6).

## Storm Language Design (Contract Level)

This section describes Storm at the architectural contract level. Concrete syntax (escape rules, exact identifier regex, combinator precedence when a human writes an ambiguous expression) is deferred to a follow-up Storm-syntax spec per Non-Goal 12.

### File structure

A Storm grammar file is a sequence of:

1. An optional `grammar <Name>;` declaration (must be the first declaration if present, before imports).
2. Zero or more `import` declarations.
3. One or more top-level declarations: `rule`, `token`, `hidden token`, or `fragment`.
4. `//` line comments, which may appear anywhere.

There is no file header (other than the optional `grammar` declaration), no module declaration, no semantic-action blocks (R3.5).

Only one file in the transitive import closure may contain a `grammar` declaration — that file is the root Storm_Grammar_File. The grammar name drives generated class names (e.g., `MistLexer`, `MistParser`) and the consumer namespace (`<Name>.Generated`). If no file declares a `grammar` directive, the Storm_Source_Generator emits `STORM_NO_ROOT_GRAMMAR` (STORM0009) and fails the build. If multiple files declare a `grammar` directive, the Storm_Source_Generator emits `STORM_MULTIPLE_ROOT_GRAMMARS` (STORM0010) and fails the build.

### Declarations

Four top-level declaration forms (R3.1, R3.12):

```
grammar   <Name>                  ;   // optional, at most one per import closure
rule      <Name> = <RuleExpr>     ;
token     <Name> = <TokenExpr>    ;
fragment  <Name> = <TokenExpr>    ;
hidden token <Name> = <TokenExpr> ;
```

Named alternatives (R3.9) are part of `RuleExpr`. The `as` keyword at the end of an alternative names the alternative; the `:` operator between a name and an element labels the field:

```
rule Expr = lhs:Expr op:('+' | '-') rhs:Expr as Binary
          | value:NUMBER                      as Literal
          ;
```

Each top-level alternative of a `rule` may be labeled with `as <Name>`. If any alternative in a rule is labeled, all labeled alternatives become AST subtypes of that rule's AST type (see "AST generation strategy" below). Field labels use the `<name>:<element>` syntax and become the property names on the generated AST node type. Without a field label, the generator auto-names the property from the referenced rule or token name.

### Expressions

`RuleExpr` and `TokenExpr` share the core combinators (R3.6): sequence (juxtaposition), alternation (`|`), grouping, `?`, `*`, `+`.

`TokenExpr` additionally supports (R3.7):

- String literals: `"fn"`, `"->"`.
- Character classes: `[a-zA-Z_]`, `[^"\\]`.
- References to other tokens and fragments.

`RuleExpr` additionally supports (R3.8):

- References to tokens and to other rules.
- Anonymous string literals, which desugar to implicit token references matching that exact literal text. The generator materializes these as synthetic, hidden-from-the-grammar-author token kinds during lowering.

### Imports

Two forms (R6.1, R6.2):

```
import "<path>";
import "<path>" as <Namespace>;
```

Path resolution (R6.9): relative paths resolve against the importing file's directory. Absolute paths are not required in v1; if supported, they resolve literally.

Resolution algorithm:

1. Start from the root Storm grammar file — the file containing the `grammar <Name>;` declaration. The Storm_Source_Generator scans all Storm grammar files in the consumer project's import closure to locate the file with the `grammar` declaration. If no file declares a `grammar` directive, emit `STORM_NO_ROOT_GRAMMAR` (STORM0009) and fail. If multiple files declare a `grammar` directive, emit `STORM_MULTIPLE_ROOT_GRAMMARS` (STORM0010) and fail.
2. Maintain `visitedPaths: Set<CanonicalPath>` and `byPath: OrderedDict<CanonicalPath, ParsedStormFile>` keyed by canonicalized absolute path.
3. For each file being processed:
   - Parse it (hand-written parser, R8.1).
   - For each `import` declaration in source order:
     - Canonicalize the path relative to the current file's directory.
     - If `canonical ∈ visitedPaths`, skip (cycles harmless per R6.7).
     - Otherwise add to `visitedPaths`, parse, and recursively resolve its imports.
     - If the file cannot be read/resolved → emit `STORM_IMPORT_NOT_FOUND` and fail (R6.6).
4. Build the **effective grammar** (represented by `StormGrammar`, which exposes a `GrammarName` property derived from the `grammar` declaration):
   - Start with the root file's declarations in declaration order.
   - Append, in DFS-pre-order of first visit, declarations contributed by each unnamespaced import's file (recursively merged).
   - Namespaced imports' declarations are stored under a qualified name table keyed by `<Namespace>.<Name>` and are accessible only via qualified reference (R6.4).
5. Duplicate-name check (R6.5): if two unnamespaced declarations share the same name across the unnamespaced namespace, emit `STORM_DUPLICATE_NAME` with both source locations and fail.
6. The resulting **declaration order** used for token tiebreaking (R4.7) is exactly the order produced by step 4: root first, unnamespaced imports in import-appearance order, then namespaced-only declarations last (R6.8).

### Semantic validation passes

Over the effective grammar (R7):

1. **Name resolution** — every reference to a rule/token/fragment resolves to a declaration, including via qualified names for namespaced imports. Unresolved → `STORM_UNDEFINED_REFERENCE` (R7.1). Fragments referenced from `RuleExpr` is rejected here too (fragments are lexer-only; R4.5).
2. **Left-recursion check** — build the rule-call graph. Detect direct and indirect left-recursion. If found and not expressible as the generator's supported left-recursion-elimination form (see "Generated parser architecture"), emit `STORM_LEFT_RECURSION` with the cycle path and fail (R7.3).
3. **Reachability / unreachable-alternative check** — for each `rule`, statically check that no alternative is shadowed by an earlier alternative under PEG ordered-choice semantics (R7.2, R5.2). This uses the LL(*) lookahead sets computed for code generation; if alternative *k*'s lookahead set is a subset of the union of alternatives *1..k-1*'s lookahead sets, alternative *k* is unreachable. Emit `STORM_UNREACHABLE_ALTERNATIVE` (Warning severity).
4. **Ambiguity check** — for each rule, check that alternatives' LL(*) lookahead sets are distinguishable within the bounded lookahead the generator uses. If not, emit `STORM_AMBIGUOUS_RULE` with at least Warning severity (R7.4). Not a build failure; v1 resolves ambiguity via PEG order.

### Diagnostic ID summary

| ID | Constant | Trigger |
|---|---|---|
| STORM0001 | `STORM_IMPORT_NOT_FOUND` | Import path cannot be resolved (R6.6) |
| STORM0002 | `STORM_DUPLICATE_NAME` | Duplicate unnamespaced declaration name (R6.5) |
| STORM0003 | `STORM_UNDEFINED_REFERENCE` | Reference to undefined rule/token/fragment (R7.1) |
| STORM0004 | `STORM_LEFT_RECURSION` | Unresolvable left-recursion detected (R7.3) |
| STORM0005 | `STORM_UNREACHABLE_ALTERNATIVE` | Alternative shadowed by earlier alternative (R7.2) |
| STORM0006 | `STORM_AMBIGUOUS_RULE` | Ambiguous alternatives within bounded lookahead (R7.4) |
| STORM0009 | `STORM_NO_ROOT_GRAMMAR` | No file declares a `grammar` directive |
| STORM0010 | `STORM_MULTIPLE_ROOT_GRAMMARS` | Multiple files declare a `grammar` directive |

Validation passes run in the order listed. Later passes depend on earlier passes succeeding.


## Generated Lexer Architecture

The `LexerEmitter` in `Storm.SourceGenerator` emits a self-contained C# lexer class into the consumer's compilation. The generated lexer is a table-driven maximal-munch scanner.

### Emission strategy

1. **Token kind enum.** The emitter generates a `TokenKind` enum with one member per `Storm_Token` in the effective grammar, plus a synthetic `EndOfFile` member and an `Error` member for unrecognized input. `Storm_Fragment` declarations do not appear in `TokenKind` (R4.5).

2. **DFA construction.** Each `Storm_Token`'s expression is lowered to an NFA, then the union of all token NFAs is determinized into a single DFA. The DFA accepts the longest match (maximal-munch, R4.6). When multiple tokens match at the same length, the DFA's accept states carry the `TokenKind` with the lowest declaration-order index (R4.7). `Storm_Fragment` expressions are inlined into the tokens that reference them before NFA construction.

3. **DFA encoding.** The DFA is emitted as a pair of flat arrays:
   - `ReadOnlySpan<int> Transitions` — a flattened transition table indexed by `(state * alphabetSize) + inputClass`.
   - `ReadOnlySpan<short> Accept` — per-state accept info: `-1` for non-accepting, otherwise the `TokenKind` ordinal.
   
   Input characters are mapped to equivalence classes to compress the table. The equivalence-class map is emitted as a `ReadOnlySpan<byte>` lookup.

4. **Hidden channel.** The emitter marks each `TokenKind` that originated from a `hidden token` declaration. The generated lexer sets `TokenChannel.Hidden` on tokens of those kinds (R4.3).

5. **Error recovery.** When the DFA cannot advance from the current position, the lexer emits a `Diagnostic` for the offending character, produces an `Error` token spanning one character, and advances by one character (R10.4).

### Generated lexer API

```csharp
namespace <ConsumerNamespace>.Generated; // e.g. Mist.Generated (driven by `grammar Mist;`)

public sealed class MistLexer // name derived from grammar declaration (e.g., `grammar Mist;` → MistLexer)
{
    public static TokenStream Lex(SourceText source, DiagnosticBag diagnostics);
}
```

The method is static and allocation-light: tokens are accumulated into an `ImmutableArray<Token>.Builder` and frozen at the end. The `TokenStream` constructor splits the array into `AllTokens` and `DefaultChannelTokens` views.

### Determinism

The emitter sorts all internal data structures by declaration order before encoding. The `CodeWriter` utility emits deterministic whitespace and no timestamps (R9.7, R20.2).

## Generated Parser Architecture

The `ParserEmitter` emits a recursive-descent parser with LL(\*) prediction.

### Prediction strategy

For each `Storm_Rule` alternation:

1. Compute the FIRST set of each alternative. If FIRST sets are disjoint, emit a simple token-kind switch (LL(1)).
2. If FIRST sets overlap, compute bounded lookahead up to depth *k* (configurable, default 4). If lookahead distinguishes all alternatives within *k* tokens, emit a lookahead chain.
3. If bounded lookahead is insufficient, emit a **backtracking predicate**: try each alternative in declaration order, memoize the result at the current position, and commit to the first that succeeds (PEG ordered-choice, R5.2). This is the LL(\*) fallback.

The emitter prefers (1) over (2) over (3) for performance. Validation pass 4 (ambiguity check) warns when (3) is needed so the grammar author can refactor if desired.

### Entry points

One `public static` parse method per `Storm_Rule` (R5.3, R9.5):

```csharp
namespace <ConsumerNamespace>.Generated; // driven by `grammar <Name>;` declaration

public static class MistParser
{
    // One per rule. Returns the concrete AST node type for that rule.
    public static CompilationUnitNode ParseCompilationUnit(TokenStream tokens, DiagnosticBag diagnostics);
    public static ExprNode ParseExpr(TokenStream tokens, DiagnosticBag diagnostics);
    // ... etc.
}
```

No entry points for tokens or fragments (R5.4). The Mist compiler chooses which entry point to call for a full compilation unit (R5.5) — typically `ParseCompilationUnit`.

### Error handling

v1 halts on the first parse error (R11.4, Non-Goal 3). The parser emits a `Diagnostic` with the offending token's span and returns a `ParseResult` with `Root = null`.

### Hidden token attachment

The parser attaches hidden tokens to AST nodes using a leading/trailing trivia model: hidden tokens before the first default-channel token of a node become `LeadingHiddenTokens`; hidden tokens after the last default-channel token become `TrailingHiddenTokens`. This preserves all source text for potential future formatting/round-trip needs without affecting parse logic.

## AST Generation Strategy

The `AstEmitter` generates one C# type per `Storm_Rule` (R9.6).

### Mapping rules

| Storm construct | Generated C# type |
|---|---|
| `rule Foo = a:A b:B c:C;` (single alternative, no `as` label) | `sealed record FooNode(ANode A, BNode B, CNode C) : SyntaxNode` |
| `rule Foo = ... as Bar \| ... as Baz;` (named alternatives with `as`) | `abstract record FooNode : SyntaxNode` with `sealed record BarFooNode(...) : FooNode` and `sealed record BazFooNode(...) : FooNode` |
| Element with `?` | Property type becomes nullable: `ANode?` |
| Element with `*` | Property type becomes `ImmutableArray<ANode>` |
| Element with `+` | Property type becomes `ImmutableArray<ANode>` (guaranteed non-empty by parser) |
| Token reference in a rule | Property of type `Token` |
| Anonymous string literal in a rule | Property of type `Token` (synthetic token kind) |
| `name:Element` (field label) | Property named `Name` instead of auto-generated name |

All generated node types are `record` types inheriting from `SyntaxNode`, giving structural equality and `with`-expression support for free.

### Children enumeration

Each concrete node overrides `SyntaxNode.Children()` to yield child nodes in source order. Token-typed properties are not yielded (they are leaves). This supports generic tree walkers without reflection (R9.3).

### Naming

- Rule `Foo` → `FooNode`.
- Named alternative `as Bar` inside rule `Foo` → `BarFooNode` at the same namespace level. Collision avoidance appends a numeric suffix if needed.
- Field label `name:Element` → property `Name` on the generated node type.

## Mist Pipeline Stage Contracts

Each stage in the `Mist` project exposes a contract interface in its `Contracts` namespace. The `CompilerDriver` depends only on these interfaces.

### ILexer

```csharp
namespace Mist.Lexing.Contracts;

public interface ILexer
{
    TokenStream Lex(SourceText source, DiagnosticBag diagnostics);
}
```

`MistLexer` wraps the Storm-generated static `Lex` method behind this interface for testability and DI.

### IParser

```csharp
namespace Mist.Parsing.Contracts;

public interface IParser
{
    ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics);
}
```

`MistParser` wraps the Storm-generated static parse method for the chosen entry rule.

### ISemanticModelBuilder

```csharp
namespace Mist.Semantics.Contracts;

public interface ISemanticModelBuilder
{
    ISemanticModel Build(MistCompilation compilation, ImmutableArray<SyntaxNode> asts, DiagnosticBag diagnostics);
}
```

Accepts the ASTs that survived parsing without errors (R16.4). Returns an `ISemanticModel` even when errors are reported, provided construction can proceed (R12.6).

### IAnalyzer

```csharp
namespace Mist.Analysis.Contracts;

public interface IAnalyzer
{
    string AnalyzerId { get; }
    void Analyze(ISemanticModel model, DiagnosticBag diagnostics);
}
```

Analyzers are stateless. The `AnalyzerId` is used for diagnostic attribution (R13.7) and for the `EnabledAnalyzerIds` filter in `CompilerOptions`.

### AnalyzerRunner

```csharp
namespace Mist.Analysis;

public sealed class AnalyzerRunner
{
    public AnalyzerRunner(ImmutableArray<IAnalyzer> analyzers);
    
    public ImmutableArray<Diagnostic> RunAll(ISemanticModel model)
    {
        // For each analyzer:
        //   try { analyzer.Analyze(model, bag); }
        //   catch (Exception ex) { bag.Add(driver-level diagnostic identifying the analyzer, R13.7); }
        // Returns aggregated diagnostics.
    }
}
```

### IBackend

```csharp
namespace Mist.Backends.Contracts;

public readonly record struct BackendId(string Value);

public sealed record BackendResult(
    bool Succeeded,
    string? PrimaryArtifactPath,
    ImmutableArray<Diagnostic> Diagnostics);

public interface IBackend
{
    BackendId Id { get; }
    BackendResult Emit(ISemanticModel model, string outputDirectory, DiagnosticBag diagnostics);
}
```

### ICompilerDriver

```csharp
namespace Mist.Driver.Contracts;

public interface ICompilerDriver
{
    CompilerResult Compile(CompilerOptions options);
}
```

## Compiler Driver Pipeline

`CompilerDriver.Compile` orchestrates the full pipeline (R16.1–R16.6):

```
CompilerOptions
  │
  ▼
┌─────────────────────────────────────────────┐
│ 1. Load source files → CompilationUnits     │
│    Create MistCompilation                    │
└─────────────────────────────────────────────┘
  │
  ▼  (per CompilationUnit)
┌─────────────────────────────────────────────┐
│ 2. Lex: ILexer.Lex(source) → TokenStream   │
│    If errors → skip parse for this unit     │  (R16.3)
└─────────────────────────────────────────────┘
  │
  ▼  (per CompilationUnit, if lex succeeded)
┌─────────────────────────────────────────────┐
│ 3. Parse: IParser.Parse(tokens) → AST      │
│    If errors → exclude AST from sem model   │  (R16.4)
└─────────────────────────────────────────────┘
  │
  ▼  (all surviving ASTs)
┌─────────────────────────────────────────────┐
│ 4. Build semantic model                     │
│    ISemanticModelBuilder.Build(asts)        │
│    If builder fails entirely → skip rest    │  (R16.5)
└─────────────────────────────────────────────┘
  │
  ▼
┌─────────────────────────────────────────────┐
│ 5. Run analyzers                            │
│    AnalyzerRunner.RunAll(model)             │
│    If any Error diagnostic → skip backend   │  (R13.6)
└─────────────────────────────────────────────┘
  │
  ▼  (if no analyzer errors)
┌─────────────────────────────────────────────┐
│ 6. Emit via backend                         │
│    IBackend.Emit(model, outputDir)          │
│    If backend errors → mark failed          │  (R14.5)
└─────────────────────────────────────────────┘
  │
  ▼
CompilerResult { Succeeded, ArtifactPath, Diagnostics }
```

All diagnostics from every stage are aggregated into a single `ImmutableArray<Diagnostic>` in stage order (R16.2, R17.4). The driver never swallows diagnostics.

### Backend resolution

The driver maintains a registry of `IBackend` instances (v1: just `CSharpTranspilerBackend`). `CompilerOptions.Backend` selects which one to invoke (R14.6). If the requested backend ID is not found, the driver emits an Error diagnostic and returns immediately.

## C# Transpiler Backend

`CSharpTranspilerBackend` implements `IBackend` (R15.1).

### Emission

`CSharpEmitter` walks the `ISemanticModel` and emits C# source files into the configured output directory (R15.3). The emitter:

1. Creates a temporary output directory structure.
2. Walks `ISemanticModel.AllSymbols` in a deterministic order (sorted by fully-qualified name) to ensure byte-identical output (R20.2).
3. Emits one `.cs` file per top-level Mist declaration (or per logical grouping — exact mapping deferred to the Mist-language spec).
4. Emits a `.csproj` file targeting the same .NET version as the Mist compiler.

### Compilation

`CSharpCompilationDriver` invokes `dotnet build` on the emitted project (R15.4):

```csharp
public sealed class CSharpCompilationDriver
{
    public BackendResult Compile(string projectDirectory, DiagnosticBag diagnostics)
    {
        // 1. Shell out to `dotnet build` with structured output.
        // 2. Parse stdout/stderr for MSBuild diagnostic lines.
        // 3. Map each .NET compiler diagnostic to a Mist Diagnostic (R15.5).
        //    v1 does NOT attempt source-mapping back to Mist spans (Non-Goal 6);
        //    diagnostics carry the emitted C# file path and line.
        // 4. Return BackendResult with the assembly path on success.
    }
}
```

`RoslynDiagnosticMapper` parses MSBuild-format diagnostic lines (`file(line,col): severity code: message`) and converts them to `Mist.Common.Diagnostics.Diagnostic` instances with `PipelineStage.Backend`.

### Determinism

The emitter sorts all output by symbol name, uses no timestamps, and writes files in sorted order. The emitted `.csproj` uses deterministic build settings (`<Deterministic>true</Deterministic>`). This satisfies R20.2.

## Command-Line Interface

`Mist.CLI` is a thin shell (R1.9, R18).

### Argument parsing

```
mist [source-files...] [--backend <id>] [--output <dir>]
```

- `[source-files...]` — zero or more `.mist` file paths (R18.1). If no files are provided, the CLI recursively discovers all `*.mist` files in the current working directory and its children (R18.7).
- `--backend <id>` — backend to invoke; defaults to `csharp` (R18.2).
- `--output <dir>` — output directory; defaults to `./out` (R18.3).

`CliArgumentParser` is a simple hand-rolled parser (no third-party dependency for v1). It produces a `CliArguments` record that `Program.cs` maps to `CompilerOptions`.

### Exit codes

- `0` on success (R18.4).
- `1` if any Error-severity diagnostic was produced (R18.5).

### Diagnostic rendering

`DiagnosticFormatter` writes each diagnostic to stderr (R18.6) in the format:

```
<path>(<startLine>,<startCol>): <severity> <id>: <message>
```

This matches the MSBuild diagnostic format so that editors and CI tools can parse it natively.

## Determinism Strategy

Determinism (R20) is enforced at three levels:

1. **Storm.SourceGenerator** — `CodeWriter` emits code with sorted declarations, no timestamps, no process IDs. DFA tables are built from sorted token lists. Output is byte-identical for identical grammar input (R9.7).

2. **Mist pipeline** — `DiagnosticBag` preserves insertion order. The driver processes compilation units in the order they appear in `CompilerOptions.SourcePaths`. The semantic model builder processes ASTs in the same order. Analyzers run in registration order. No stage uses `DateTime.Now`, `Environment.ProcessId`, or unseeded `Random` (R20.3).

3. **C# transpiler backend** — emitted files are sorted by symbol name. The emitted `.csproj` enables `<Deterministic>true</Deterministic>`. `dotnet build` with deterministic settings produces reproducible assemblies.

## Testability Seams

Each stage is independently testable (R21) through its contract interface:

| Stage | Test seam | How |
|---|---|---|
| Lexer (R21.1) | `ILexer.Lex(source)` | Construct a `SourceText` from a string, call `Lex`, assert on `TokenStream` |
| Parser (R21.2) | `IParser.Parse(tokens)` | Build a `TokenStream` manually from a `Token[]`, call `Parse`, assert on AST |
| Parser entry points (R21.3) | Generated static methods | Call any `Parse<RuleName>` directly with a token stream to test sub-grammar fragments |
| Semantic model (R21.4) | `ISemanticModelBuilder.Build(asts)` | Construct AST nodes manually, call `Build`, assert on `ISemanticModel` |
| Analyzers (R21.5) | `IAnalyzer.Analyze(model)` | Construct a mock/stub `ISemanticModel`, call `Analyze`, assert on diagnostics |
| Backends (R21.6) | `IBackend.Emit(model, dir)` | Construct a mock/stub `ISemanticModel`, call `Emit`, assert on emitted files |

Integration tests can also wire the full pipeline through `ICompilerDriver.Compile` with real source files.

### Test project conventions

- `Storm.SourceGenerator.Tests` — tests the hand-written Storm parser, import resolution, validation passes, and emitted code (by compiling emitted source in-memory and asserting on the resulting types).
- `Mist.Tests` — tests each Mist pipeline stage in isolation via contract interfaces, plus integration tests through the driver.
- `Mist.CLI.Tests` — tests argument parsing and diagnostic formatting; end-to-end tests invoke the CLI as a subprocess.

## Correctness Properties

The following properties should hold across the architecture and are candidates for property-based testing:

1. **Lexer round-trip.** For any valid Mist source text, concatenating the lexemes of all tokens in the `TokenStream` (including hidden tokens) reproduces the original source text exactly.

2. **Lexer completeness.** For any input string, the lexer produces a token stream where the union of all token spans covers every character in the input exactly once (no gaps, no overlaps).

3. **Lexer determinism.** For any input string, repeated calls to `ILexer.Lex` produce identical `TokenStream` instances (same token count, same kinds, same spans).

4. **Parser span coverage.** For any successfully parsed AST, the root node's `Span` covers the entire default-channel token range, and every token in the default channel is reachable through exactly one leaf of the AST.

5. **AST children consistency.** For any AST node, `node.Children()` returns the same sequence on repeated calls, and every child's `Span` is contained within the parent's `Span`.

6. **Semantic model immutability.** After `ISemanticModelBuilder.Build` returns, no public API on `ISemanticModel` or any reachable `ISymbol`, `IType`, or `IScope` permits mutation. (Testable by attempting writes through reflection or by verifying all public surfaces return `IReadOnly*` / `Immutable*` types.)

7. **Diagnostic completeness.** For any compilation, every diagnostic in `CompilerResult.Diagnostics` has a non-null `Id`, a valid `Severity`, and a `Stage`. Diagnostics with a source location have a valid `CompilationUnitPath` and a `Location` where `Start` ≤ `End`.

8. **Pipeline short-circuit.** If the lexer reports an Error for a compilation unit, no parser diagnostic is produced for that unit. If the parser reports an Error, no semantic-model diagnostic references that unit's AST. If any analyzer reports an Error, no backend diagnostic is produced.

9. **Backend determinism.** For any `ISemanticModel`, repeated calls to `IBackend.Emit` with the same output directory produce byte-identical file sets.

10. **Storm generator determinism.** For any set of Storm grammar files, repeated invocations of the source generator produce byte-identical C# output.
