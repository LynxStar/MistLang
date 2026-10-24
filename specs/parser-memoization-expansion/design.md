# Design Document: Parser Memoization Expansion

## Overview

This design expands the Storm parser memoization subsystem with two new candidate sources that feed the existing `MemoizationPlan` / `ParserEmitter` contract:

1. **Multi-caller alternation heuristic** — a static analysis that identifies rules attempted multiple times at the same position from different failing branches of a parent alternation.
2. **`@memo` annotation** — a grammar-author-controlled annotation that unconditionally nominates a rule for memoization.

Both mechanisms produce candidates that merge with the existing cycle-based analysis and pass through the `IsSpeculativeSafe` filter before entering the `MemoizationPlan`.

## Architecture

The pipeline remains linear:

```
StormLexer → StormParser → StormFile AST → GrammarIRLowering → GrammarIR
                                                                    ↓
                                                       MemoizationAnalysisPass
                                                                    ↓
                                                           MemoizationPlan
                                                                    ↓
                                                           ParserEmitter
```

Changes touch layers 1–6 of this pipeline. The `ParserEmitter` requires no modifications — it already consumes `MemoizationPlan` and generates correct memoized rule implementations.

## Components and Interfaces

### 1. Storm Lexer — Annotation Token

**File:** `Source/Storm.SourceGenerator/Frontend/StormLexer.cs`

Add a new `StormTokenKind.Annotation` enum value. The lexer recognizes `@` followed by identifier characters as a single token.

```csharp
// New enum value in StormTokenKind:
/// <summary>An annotation: <c>@</c> followed by an identifier (e.g., <c>@memo</c>).</summary>
Annotation,
```

In the `Tokenize` method, add a branch for `@` before the single-character operator dispatch:

```csharp
// --- Annotation ---
if (c == '@')
{
    var start = pos;
    pos++; // skip '@'
    if (pos < text.Length && IsIdentStart(text[pos]))
    {
        pos++;
        while (pos < text.Length && IsIdentPart(text[pos]))
        {
            pos++;
        }
        var lexeme = text.Substring(start, pos - start);
        tokens.Add(new StormToken(StormTokenKind.Annotation, lexeme, start, line, column));
    }
    else
    {
        // '@' not followed by identifier-start: emit error
        diagnostics.Add(new StormDiagnostic(
            StormDiagnosticIds.STORM_SYNTAX_ERROR,
            StormDiagnosticSeverity.Error,
            "Unrecognized character '@'",
            source.FilePath,
            line,
            column));
        tokens.Add(new StormToken(StormTokenKind.Error, "@", start, line, column));
    }
    continue;
}
```

### 2. Storm Parser — @memo Recognition

**File:** `Source/Storm.SourceGenerator/Frontend/StormParser.cs`

Modify `ParseDeclaration()` and `ParseFile()` to handle `Annotation` tokens:

```csharp
// In ParseDeclaration():
case StormTokenKind.Annotation:
    return ParseAnnotatedDeclaration();
```

New method `ParseAnnotatedDeclaration()`:

```csharp
private StormDeclaration? ParseAnnotatedDeclaration()
{
    var annotationTok = Advance(); // consume the annotation token

    // Only @memo is recognized
    if (annotationTok.Lexeme != "@memo")
    {
        Error($"Unrecognized annotation '{annotationTok.Lexeme}'");
        SynchronizeToDeclaration();
        return null;
    }

    // Check for duplicate @memo
    if (Current.Kind == StormTokenKind.Annotation)
    {
        Error("Duplicate '@memo' annotation on rule declaration");
        Advance(); // skip the duplicate
    }

    // @memo is only valid before 'rule'
    if (Current.Kind != StormTokenKind.RuleKeyword)
    {
        Error("'@memo' is only valid on rule declarations");
        SynchronizeToDeclaration();
        return null;
    }

    // Parse the rule declaration with memo flag
    var kw = Advance(); // consume 'rule'
    var nameTok = Expect(StormTokenKind.Identifier, "rule name");
    Expect(StormTokenKind.Equals, "'='");
    var body = ParseExpression();
    Expect(StormTokenKind.Semicolon, "';'");
    return new StormRuleDecl(nameTok.Lexeme, body, kw.Offset, kw.Line, kw.Column, isMemo: true);
}
```

Also add `StormTokenKind.Annotation` to `SynchronizeToDeclaration()` so recovery recognizes it as a declaration start.

### 3. StormRuleDecl — Memo Property

**File:** `Source/Storm.SourceGenerator/Frontend/StormSyntax.cs`

Extend `StormRuleDecl` with an `IsMemo` property:

```csharp
/// <summary>A <c>rule Name = ...;</c> declaration, optionally annotated with <c>@memo</c>.</summary>
public sealed record StormRuleDecl(
    string Name,
    StormExpression Body,
    int Offset,
    int Line,
    int Column,
    bool IsMemo = false)
    : StormDeclaration(Name, Body, Offset, Line, Column);
```

The default value `false` ensures all existing call sites remain source-compatible.

### 4. RuleIR — IsMemo Property

**File:** `Source/Storm.SourceGenerator/IR/RuleIR.cs`

Add an `IsMemo` property to `RuleIR`:

```csharp
public sealed class RuleIR
{
    public string Name { get; }
    public IReadOnlyList<RuleAlternativeIR> Alternatives { get; }

    /// <summary>
    /// Whether this rule was annotated with <c>@memo</c> in the source grammar.
    /// </summary>
    public bool IsMemo { get; }

    public RuleIR(string name, IReadOnlyList<RuleAlternativeIR> alternatives, bool isMemo = false)
    {
        Name = name;
        Alternatives = alternatives;
        IsMemo = isMemo;
    }
}
```

### 5. GrammarIRLowering — Propagate Memo Flag

**File:** `Source/Storm.SourceGenerator/IR/GrammarIRLowering.cs`

When creating `RuleIR` shells in step 3, propagate `IsMemo` from `StormRuleDecl`:

```csharp
// In step 3 — collecting rule declarations:
if (decl is StormRuleDecl ruleDecl)
{
    var isMemo = ruleDecl.IsMemo;
    var shell = new RuleIR(decl.Name, new List<RuleAlternativeIR>(), isMemo);
    ruleShells[decl.Name] = shell;
    ruleOrder.Add((decl.Name, decl));
}
```

### 6. MemoizationAnalysisPass — Multi-Caller Heuristic + Union

**File:** `Source/Storm.SourceGenerator/Analyze/MemoizationAnalysisPass.cs`

The `Run` method is restructured to compute three candidate sets and union them before applying `IsSpeculativeSafe`:

```csharp
public static MemoizationPlan Run(GrammarIR grammar, List<StormDiagnostic>? diagnostics = null)
{
    // Phase 1 — build full rule-reference graph
    var graph = BuildReferenceGraph(grammar);

    // Phase 2 — existing cycle-based candidates
    var cycleCandidates = FindCycleCandidates(grammar, graph);

    // Phase 3 — multi-caller alternation heuristic
    var multiCallerCandidates = FindMultiCallerCandidates(grammar);

    // Phase 4 — @memo-annotated rules
    var memoCandidates = new HashSet<string>();
    foreach (var rule in grammar.Rules)
    {
        if (rule.IsMemo)
        {
            memoCandidates.Add(rule.Name);
        }
    }

    // Phase 5 — union all candidate sources
    var allCandidates = new HashSet<string>(cycleCandidates);
    allCandidates.UnionWith(multiCallerCandidates);
    allCandidates.UnionWith(memoCandidates);

    // Phase 6 — apply IsSpeculativeSafe filter, emit warnings for @memo failures
    var rulesByName = new Dictionary<string, RuleIR>(grammar.Rules.Count);
    foreach (var rule in grammar.Rules)
    {
        rulesByName[rule.Name] = rule;
    }

    var marked = new List<string>();
    foreach (var rule in grammar.Rules)
    {
        if (allCandidates.Contains(rule.Name))
        {
            if (IsSpeculativeSafe(rule))
            {
                marked.Add(rule.Name);
            }
            else if (rule.IsMemo && diagnostics != null)
            {
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_MEMO_IGNORED,
                    StormDiagnosticSeverity.Warning,
                    $"'@memo' on rule '{rule.Name}' ignored: rule is not speculative-safe",
                    null, 0, 0));
            }
        }
    }

    return marked.Count == 0 ? MemoizationPlan.Empty : new MemoizationPlan(marked);
}
```

#### Multi-Caller Heuristic Implementation

```csharp
/// <summary>
/// Identifies rules that are referenced from two or more distinct alternatives
/// of any parent rule's top-level alternation. Such rules are attempted multiple
/// times at the same position when earlier alternatives fail.
/// </summary>
private static HashSet<string> FindMultiCallerCandidates(GrammarIR grammar)
{
    var candidates = new HashSet<string>();

    foreach (var parentRule in grammar.Rules)
    {
        if (parentRule.Alternatives.Count < 2)
        {
            continue; // single-alternative rules can't trigger redundant calls
        }

        // For each alternative, collect the set of rules reachable from it
        // through direct references or single-path sequences.
        var perAlternativeRefs = new List<HashSet<string>>();
        foreach (var alt in parentRule.Alternatives)
        {
            var refs = new HashSet<string>();
            CollectRuleRefs(alt.Expression, refs);
            perAlternativeRefs.Add(refs);
        }

        // A rule appearing in 2+ alternative ref-sets is a multi-caller candidate
        var seen = new Dictionary<string, int>();
        foreach (var refSet in perAlternativeRefs)
        {
            foreach (var ruleName in refSet)
            {
                seen.TryGetValue(ruleName, out var count);
                seen[ruleName] = count + 1;
            }
        }

        foreach (var (ruleName, count) in seen)
        {
            if (count >= 2)
            {
                candidates.Add(ruleName);
            }
        }
    }

    return candidates;
}
```

### 7. Mist Grammar — @memo Annotations

**File:** `grammars/Mist.storm`

Add `@memo` before the seven hot-path rule declarations:

```storm
@memo
rule Expression = ...;

@memo
rule Statement = ...;

@memo
rule Primary = ...;

@memo
rule Postfix = ...;

@memo
rule Unary = ...;

@memo
rule TypeReference = ...;

@memo
rule TopLevelItem = ...;
```

## Data Models

### New/Modified Types

| Type | Change | Description |
|------|--------|-------------|
| `StormTokenKind` | Add `Annotation` | New enum value for `@identifier` tokens |
| `StormRuleDecl` | Add `IsMemo` param | Boolean defaulting to `false` |
| `RuleIR` | Add `IsMemo` param | Boolean defaulting to `false` |
| `MemoizationAnalysisPass.Run` | Add optional `diagnostics` param | Receives warnings for ignored @memo |
| `StormDiagnosticIds` | Add `STORM_MEMO_IGNORED` | New warning diagnostic id |

### Data Flow

```
@memo token (lexer)
  → StormRuleDecl.IsMemo = true (parser)
    → RuleIR.IsMemo = true (lowering)
      → memoCandidates set (analysis pass)
        → union with cycle + multi-caller candidates
          → IsSpeculativeSafe filter
            → MemoizationPlan (dense ids, declaration order)
```

## Error Handling

| Scenario | Diagnostic | Severity |
|----------|-----------|----------|
| `@` not followed by identifier | STORM_SYNTAX_ERROR | Error |
| Unrecognized annotation (not `@memo`) | STORM_SYNTAX_ERROR | Error |
| `@memo` on token/fragment/hidden | STORM_SYNTAX_ERROR | Error |
| Duplicate `@memo` on same rule | STORM_SYNTAX_ERROR | Error |
| `@memo` rule fails IsSpeculativeSafe | STORM_MEMO_IGNORED | Warning |

## Testing Strategy

### Unit Tests
- Lexer: verify `@memo`, `@unknown`, `@` alone, `@123` produce correct token kinds
- Parser: verify `@memo rule X = A;` produces `StormRuleDecl` with `IsMemo=true`; verify `@memo token X = ...;` produces error; verify duplicate `@memo @memo` produces error
- Lowering: verify `IsMemo` propagates from `StormRuleDecl` to `RuleIR`
- Multi-caller heuristic: small hand-crafted grammars with known multi-caller rules
- IsSpeculativeSafe filter: verify unsafe @memo rules are excluded with warning

### Property Tests
- Annotation token lexing: random valid identifiers after `@` always produce Annotation token
- Memo propagation round-trip: random grammars with @memo flags, lowered RuleIR.IsMemo matches source
- Multi-caller detection: random grammar structures, rules in 2+ alternatives always appear in candidates
- Memoization transparency: random input strings, AST with memoization equals AST without

### Integration Tests
- Parse Mist.storm with @memo annotations, verify 7 rules in MemoizationPlan
- Parse AllFeatures corpus, verify wall-clock time under 2 seconds

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Annotation token lexing

*For any* string of the form `@` followed by a valid identifier (`[a-zA-Z_][a-zA-Z0-9_]*`), the Storm lexer SHALL produce exactly one token of kind `Annotation` whose lexeme equals the full input string including the `@` prefix.

**Validates: Requirements 1.1, 1.3**

### Property 2: Unrecognized annotation rejection

*For any* annotation token whose lexeme is not `@memo`, the Storm parser SHALL emit a diagnostic error and not produce a valid declaration node.

**Validates: Requirements 2.3**

### Property 3: Memo annotation propagation

*For any* Storm grammar source containing rule declarations, the `IsMemo` flag on each `RuleIR` produced by `GrammarIRLowering` SHALL equal `true` if and only if the corresponding `StormRuleDecl` in the source AST had `IsMemo = true`.

**Validates: Requirements 3.1, 3.2**

### Property 4: Multi-caller candidate detection

*For any* grammar IR where a rule `R` is referenced from two or more distinct alternatives of any parent rule's alternation, the `MemoizationAnalysisPass` SHALL include `R` in the candidate set (before the IsSpeculativeSafe filter), regardless of whether `R` participates in a recursion cycle.

**Validates: Requirements 4.1, 4.2**

### Property 5: Candidate union with speculative-safety filter

*For any* grammar IR, the final `MemoizationPlan` SHALL contain exactly those rules that (a) appear in the union of cycle-based candidates, multi-caller candidates, and `@memo`-annotated rules, AND (b) pass the `IsSpeculativeSafe` check.

**Validates: Requirements 5.1, 5.2**

### Property 6: Dense declaration-order id assignment

*For any* non-empty `MemoizationPlan`, the assigned ids SHALL be a dense sequence `0..N-1` where N is the plan count, and the ordering SHALL match grammar declaration order.

**Validates: Requirements 5.4**

### Property 7: Memoization transparency

*For any* input string (valid or invalid), parsing with a memoized rule implementation SHALL produce an identical AST structure and diagnostic sequence as parsing with the non-memoized implementation of the same rule.

**Validates: Requirements 7.1, 7.2, 7.3**
