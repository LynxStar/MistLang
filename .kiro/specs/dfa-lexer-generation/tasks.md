# Tasks

## Task 1: Core NFA Data Structures
- [x] 1.1 Create `CharRange` struct in `Source/Storm.SourceGenerator/Emit/CharRange.cs` with `Lo`, `Hi` fields, `Contains(char)` method, and equality/hashcode overrides
- [x] 1.2 Create `NfaState` class in `Source/Storm.SourceGenerator/Emit/NfaState.cs` with `Id`, `Transitions` list, and `AcceptingTokenIndex` (-1 for non-accepting)
- [x] 1.3 Create `NfaTransition` class in `Source/Storm.SourceGenerator/Emit/NfaTransition.cs` with `Ranges` (CharRange[]), `IsEpsilon` flag, and `Target` (NfaState)
- [x] 1.4 Create `NfaFragment` class in `Source/Storm.SourceGenerator/Emit/NfaFragment.cs` with `Start` and `Accept` NfaState properties
- [x] 1.5 Create `Nfa` class in `Source/Storm.SourceGenerator/Emit/Nfa.cs` with `StartState`, `States` list, and helper to collect all states reachable from start

## Task 2: Thompson's Construction (NfaBuilder)
- [x] 2.1 Create `NfaBuilder` static class in `Source/Storm.SourceGenerator/Emit/NfaBuilder.cs` with a state ID counter and `NewState()` helper
- [x] 2.2 Implement `BuildFragment(StringLiteralIR)` — chain of states connected by single-char transitions for each character in the literal
- [x] 2.3 Implement `BuildFragment(CharClassIR)` — parse the char class text (reuse LexerEmitter's `ParseCharClass` logic) and create a single transition with the appropriate `CharRange[]`
- [x] 2.4 Implement `BuildFragment(SequenceIR)` — concatenate sub-fragments by connecting each accept state to the next start state via epsilon
- [x] 2.5 Implement `BuildFragment(AlternationIR)` — new start state with epsilon transitions to each alternative's start, each alternative's accept with epsilon to new accept state
- [x] 2.6 Implement `BuildFragment(OptionalIR)` — build inner fragment, add epsilon from start to accept (allowing empty match)
- [x] 2.7 Implement `BuildFragment(ZeroOrMoreIR)` — build inner fragment, add epsilon from accept back to start and from new start to new accept
- [x] 2.8 Implement `BuildFragment(OneOrMoreIR)` — build inner fragment, add epsilon from accept back to start (but no skip epsilon)
- [x] 2.9 Implement `BuildFragment(InlineFragmentIR)` — delegate to `BuildFragment(fragment.Body)`
- [x] 2.10 Implement `BuildFragment(TokenRefIR)` — delegate to `BuildFragment(tokenRef.Token.Expression)`
- [x] 2.11 Implement top-level `Build(ExpressionIR)` dispatch method that switches on expression type
- [x] 2.12 Implement `BuildCombined(IReadOnlyList<TokenIR>)` — create shared start state with epsilon transitions to each token's NFA, annotate each token NFA's accept state with declaration index, return combined `Nfa`

## Task 3: Regex Pattern Parser
- [x] 3.1 Create `RegexParser` static class in `Source/Storm.SourceGenerator/Emit/RegexParser.cs` with `Parse(string pattern)` returning `ExpressionIR`
- [x] 3.2 Implement character class parsing (`[a-z]`, `[^abc]`, `[a-zA-Z0-9_]`) producing `CharClassIR`
- [x] 3.3 Implement escape sequence expansion (`\s` → `[ \t\n\r\f\v]`, `\d` → `[0-9]`, `\w` → `[a-zA-Z0-9_]`, `\n`, `\t`, `\r`, `\0`, `\\`, literal escapes)
- [x] 3.4 Implement dot metacharacter (`.`) as `CharClassIR` matching any char except `\n`
- [x] 3.5 Implement quantifiers (`+`, `*`, `?`) wrapping the preceding expression in `OneOrMoreIR`, `ZeroOrMoreIR`, `OptionalIR`
- [x] 3.6 Implement grouping with parentheses producing nested expression trees
- [x] 3.7 Implement alternation (`|`) producing `AlternationIR`
- [x] 3.8 Implement concatenation of adjacent elements producing `SequenceIR`
- [x] 3.9 Wire `NfaBuilder.BuildFragment(RegexLiteralIR)` to call `RegexParser.Parse` then `BuildFragment` on the result

## Task 4: Subset Construction (NFA → DFA)
- [x] 4.1 Create `DfaState` class in `Source/Storm.SourceGenerator/Emit/DfaState.cs` with `Id`, `Transitions` dictionary (CharRange → target state ID), and `AcceptingTokenIndex`
- [x] 4.2 Create `Dfa` class in `Source/Storm.SourceGenerator/Emit/Dfa.cs` with `StartStateId`, `States` list
- [x] 4.3 Create `SubsetConstructor` static class in `Source/Storm.SourceGenerator/Emit/SubsetConstructor.cs`
- [x] 4.4 Implement epsilon closure computation — given a set of NFA states, compute all states reachable via epsilon transitions (BFS/DFS with visited set)
- [x] 4.5 Implement alphabet partitioning — collect all `CharRange` boundaries from NFA transitions and split into non-overlapping intervals
- [x] 4.6 Implement the main subset construction loop — worklist algorithm: start with epsilon closure of NFA start state, for each unprocessed DFA state and each alphabet partition, compute the target NFA state set, create or reuse DFA states
- [x] 4.7 Implement accepting state resolution — when a DFA state's NFA state set contains multiple accepting states, assign the one with the lowest declaration index

## Task 5: DFA Minimization (Hopcroft's Algorithm)
- [x] 5.1 Create `DfaMinimizer` static class in `Source/Storm.SourceGenerator/Emit/DfaMinimizer.cs`
- [x] 5.2 Implement initial partition — separate accepting states by token index, group all non-accepting states together
- [x] 5.3 Implement partition refinement loop — for each partition and each input symbol, split partitions where transitions lead to different target partitions
- [x] 5.4 Implement minimized DFA construction — create one DFA state per final partition, map transitions to partition representatives, preserve accepting annotations
- [x] 5.5 Implement top-level `Minimize(Dfa)` method that runs the full algorithm and returns a new minimized `Dfa`

## Task 6: Equivalence Class Computation
- [x] 6.1 Create `EquivalenceClassComputer` static class in `Source/Storm.SourceGenerator/Emit/EquivalenceClassComputer.cs`
- [x] 6.2 Implement `Compute(Dfa)` — for each character 0–65535, determine its transition signature across all DFA states, group characters with identical signatures into the same class, return `(int[] charToClass, int classCount)`
- [x] 6.3 Optimize: use the alphabet partition intervals from subset construction as a starting point to avoid iterating all 65536 characters naively

## Task 7: Updated LexerEmitter — Table Emission
- [x] 7.1 Add a new method `EmitDfaLexerClass` to `LexerEmitter` (or a new `DfaLexerEmitter` class) that orchestrates the full pipeline: NFA build → subset construction → minimization → equivalence classes → code emission
- [x] 7.2 Emit the equivalence class mapping array (`_charClass`) as a static `byte[]` or `int[]` of size 65536
- [x] 7.3 Emit the transition table per mode as a static 2D array `short[stateCount, classCount]` where -1 means no transition
- [x] 7.4 Emit the accepting state table per mode as a static `short[]` array where -1 means non-accepting
- [x] 7.5 Preserve the existing `EmitTokenKindEnum` logic unchanged
- [x] 7.6 Preserve the existing `EmitHiddenTokenKinds` logic unchanged
- [x] 7.7 Preserve the existing mode constants and `TokenModes` array emission

## Task 8: Updated LexerEmitter — Lex Loop Emission
- [x] 8.1 Emit the table-driven `Lex` method: loop over input, look up equivalence class, look up transition, track last accepting state and position for maximal munch
- [x] 8.2 Emit backtracking logic: when no transition exists, rewind to last accepting state position, emit the token, and restart from there
- [x] 8.3 Emit error token handling: when no transition and no accepting state seen, emit Error token for current char and advance by one
- [x] 8.4 Emit line/column tracking: increment line counter on `\n`, reset column; otherwise increment column
- [x] 8.5 Emit EndOfFile token after the main loop
- [x] 8.6 Emit mode stack logic: initialize with `Mode_default`, switch transition/accepting tables based on `modeStack.Peek()`
- [x] 8.7 Emit push/pop mode actions after token emission (switch on token kind, push target mode or pop stack)

## Task 9: Preserve Public API and Remove Old Code
- [x] 9.1 Ensure emitted `Lex` method signature is `public static TokenStream Lex(SourceText source, DiagnosticBag diagnostics)`
- [x] 9.2 Ensure emitted `IsHidden` method signature is `public static bool IsHidden(int tokenKind)`
- [x] 9.3 Ensure emitted `GetTokenMode` method signature is `public static int GetTokenMode(int tokenKind)`
- [x] 9.4 Remove all per-token `TryMatch` method emission code
- [x] 9.5 Remove all `System.Text.RegularExpressions.Regex` field emission code
- [x] 9.6 Remove the `_regexPatterns` thread-static field and `CollectRegexPatterns` methods
- [x] 9.7 Update the `Emit` entry point to call the new DFA pipeline instead of the old per-token pipeline

## Task 10: Property-Based Tests
- [x] 10.1 Create `ExpressionIRGenerator` in test project — FsCheck `Arbitrary<ExpressionIR>` that generates depth-limited random expression trees (StringLiteralIR, CharClassIR, SequenceIR, AlternationIR, OptionalIR, ZeroOrMoreIR, OneOrMoreIR)
- [x] 10.2 Write property test P1 (NFA Round-Trip): for any ExpressionIR and any string, NFA acceptance matches TryMatch oracle
- [x] 10.3 Write property test P2 (Combined NFA Union): for any set of token expressions and any string, combined NFA accepts iff any individual NFA accepts
- [x] 10.4 Write property test P3 (Subset Construction): for any combined NFA and any string, DFA accepts iff NFA accepts, with correct priority
- [x] 10.5 Write property test P4 (Minimization Correctness): for any DFA and any string, minimized DFA accepts iff original DFA accepts with same token
- [x] 10.6 Write property test P5 (Minimization Idempotence): for any DFA, minimize(minimize(dfa)).StateCount == minimize(dfa).StateCount
- [x] 10.7 Write property test P10 (Full Lexer Equivalence): for any input string, DFA lexer produces same token stream as TryMatch lexer

## Task 11: Unit and Integration Tests
- [x] 11.1 Write unit tests for `RegexParser`: verify specific patterns (`\s+`, `[^\n]*`, `[a-zA-Z_][a-zA-Z0-9_]*`, `.`, `\d+`) produce correct ExpressionIR trees
- [x] 11.2 Write unit tests for structural emission: verify emitted code contains transition table arrays, no `Regex` usage, correct method signatures
- [x] 11.3 Write unit tests for edge cases: empty input, single char, all-error input, deeply nested mode push/pop
- [x] 11.4 Write integration test: lex the Mist grammar test corpus with both old and new emitters, compare token streams
- [x] 11.5 Write integration test: verify interpolated string `$"text{expr}text"` produces correct token sequence with mode transitions

## Task 12: Build Verification and Cleanup
- [x] 12.1 Run `dotnet build` on the full solution and fix any compilation errors
- [x] 12.2 Run `dotnet test` on `Storm.SourceGenerator.Tests` and fix any test failures
- [x] 12.3 Run `dotnet test` on `Mist.Tests` to verify the Mist compiler still works end-to-end with the new lexer
- [x] 12.4 Remove any temporary scaffolding or dead code from the implementation
