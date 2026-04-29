# Requirements Document

## Introduction

This feature replaces the current per-token `TryMatch` lexer generation strategy in Storm's `LexerEmitter` with a DFA-based (Deterministic Finite Automaton) lexer. The current approach generates individual matching methods for each token and iterates over all active tokens in the current mode to find the longest match. This is O(tokens × input) per character. A DFA-based lexer processes each input character with a single table lookup, achieving O(input) performance regardless of token count. The DFA approach is the standard technique used by production lexer generators (flex, re2c, etc.) and will significantly improve lexing throughput for grammars with many tokens like Mist.

## Glossary

- **DFA**: Deterministic Finite Automaton — a state machine where each state has exactly one transition per input symbol. Used to recognize regular languages in linear time.
- **NFA**: Nondeterministic Finite Automaton — a state machine that may have multiple transitions per input symbol or epsilon (empty) transitions. Easier to construct from regular expressions but requires conversion to DFA for efficient execution.
- **Thompson_Construction**: An algorithm that converts a regular expression (or expression IR) into an equivalent NFA by composing small NFA fragments for each operator (concatenation, alternation, repetition).
- **Subset_Construction**: An algorithm (also called the powerset construction) that converts an NFA to an equivalent DFA by treating sets of NFA states as single DFA states.
- **DFA_Minimization**: An algorithm (such as Hopcroft's) that reduces a DFA to the smallest equivalent DFA by merging indistinguishable states.
- **Transition_Table**: A two-dimensional array indexed by (state, input character) that encodes all DFA transitions. The generated lexer uses this table to drive state changes.
- **Maximal_Munch**: The lexing strategy where the longest possible match is always preferred. The DFA tracks the last accepting state seen during a scan and backtracks to it when no further transitions are possible.
- **Declaration_Order_Priority**: When two tokens match the same input with the same length, the token declared earlier in the grammar wins.
- **Lexer_Mode**: A named set of active tokens. The lexer maintains a mode stack; tokens can push or pop modes. Each mode has its own DFA.
- **Accepting_State**: A DFA state that represents a complete match for one or more tokens. Each accepting state records the token kind and declaration index.
- **ExpressionIR**: The intermediate representation for token expressions in Storm, including StringLiteralIR, CharClassIR, RegexLiteralIR, SequenceIR, AlternationIR, OptionalIR, ZeroOrMoreIR, OneOrMoreIR, and InlineFragmentIR.
- **LexerEmitter**: The Storm source generator component responsible for emitting C# lexer source code from GrammarIR.
- **GrammarIR**: The complete lowered grammar intermediate representation containing all tokens, rules, and mode declarations.
- **TokenIR**: The lowered representation of a single token declaration, including its expression, mode, hidden flag, and mode action.

## Requirements

### Requirement 1: NFA Construction from ExpressionIR

**User Story:** As a Storm developer, I want token expressions to be converted into NFAs, so that the DFA construction pipeline has a well-defined input.

#### Acceptance Criteria

1. WHEN a StringLiteralIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts exactly the literal string.
2. WHEN a CharClassIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts exactly the characters in the class, respecting negation and ranges.
3. WHEN a RegexLiteralIR expression is provided, THE Thompson_Construction SHALL parse the regex pattern and produce an equivalent NFA supporting character classes, quantifiers, escapes, and the dot metacharacter.
4. WHEN a SequenceIR expression is provided, THE Thompson_Construction SHALL produce an NFA that concatenates the NFAs of each element in order.
5. WHEN an AlternationIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts any of the alternative sub-expressions.
6. WHEN an OptionalIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts either the inner expression or the empty string.
7. WHEN a ZeroOrMoreIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts zero or more repetitions of the inner expression.
8. WHEN a OneOrMoreIR expression is provided, THE Thompson_Construction SHALL produce an NFA that accepts one or more repetitions of the inner expression.
9. WHEN an InlineFragmentIR expression is provided, THE Thompson_Construction SHALL produce an NFA equivalent to the fragment body expression.
10. FOR ALL valid ExpressionIR trees, THE Thompson_Construction SHALL produce an NFA where the set of accepted strings equals the language defined by the expression (round-trip property: an NFA constructed from an expression accepts a string if and only if the expression matches that string).

### Requirement 2: Combined NFA per Lexer Mode

**User Story:** As a Storm developer, I want all tokens in a lexer mode to be combined into a single NFA, so that subset construction can produce one DFA per mode.

#### Acceptance Criteria

1. WHEN multiple tokens belong to the same Lexer_Mode, THE Thompson_Construction SHALL combine their individual NFAs into a single NFA with a shared start state and epsilon transitions to each token NFA.
2. THE Thompson_Construction SHALL annotate each token NFA's accepting state with the token kind and declaration index.
3. WHEN two token NFAs in the same mode share an accepting state after combination, THE Thompson_Construction SHALL preserve both token annotations so that Declaration_Order_Priority can resolve the conflict during DFA construction.

### Requirement 3: NFA to DFA Conversion via Subset Construction

**User Story:** As a Storm developer, I want the combined NFA to be converted to a DFA, so that the lexer can process each input character with a single state transition.

#### Acceptance Criteria

1. WHEN a combined NFA for a Lexer_Mode is provided, THE Subset_Construction SHALL produce a DFA that accepts exactly the same language as the NFA.
2. THE Subset_Construction SHALL compute epsilon closures correctly, including transitive epsilon transitions.
3. WHEN a DFA state corresponds to multiple NFA accepting states, THE Subset_Construction SHALL assign the DFA state the token kind with the lowest declaration index (Declaration_Order_Priority).
4. THE Subset_Construction SHALL produce a DFA for each Lexer_Mode defined in the GrammarIR.

### Requirement 4: DFA Minimization

**User Story:** As a Storm developer, I want the DFA to be minimized, so that the generated transition table is as compact as possible.

#### Acceptance Criteria

1. WHEN a DFA is provided, THE DFA_Minimization SHALL produce an equivalent DFA with the fewest possible states.
2. THE DFA_Minimization SHALL preserve accepting state token annotations: two states that accept different token kinds are distinguishable and are not merged.
3. THE DFA_Minimization SHALL preserve the language recognized by the DFA (the minimized DFA accepts exactly the same set of strings as the original).
4. FOR ALL valid DFAs, minimizing twice SHALL produce the same result as minimizing once (idempotence property).

### Requirement 5: Transition Table Emission

**User Story:** As a Storm developer, I want the minimized DFA to be emitted as a C# transition table, so that the generated lexer uses table-driven state transitions instead of per-token matching methods.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit a Transition_Table as a static two-dimensional array indexed by (state, input character equivalence class).
2. THE LexerEmitter SHALL emit an accepting state table that maps each DFA state to its accepted token kind, or a sentinel value for non-accepting states.
3. THE LexerEmitter SHALL emit one Transition_Table and one accepting state table per Lexer_Mode defined in the GrammarIR.
4. THE LexerEmitter SHALL emit equivalence class mapping that groups input characters with identical transition behavior into the same class, reducing table width.
5. WHEN the GrammarIR contains a single mode, THE LexerEmitter SHALL emit a single pair of tables without mode-indexing overhead.

### Requirement 6: Table-Driven Lexer Loop Emission

**User Story:** As a Storm developer, I want the generated lexer to use a table-driven loop with maximal munch, so that lexing runs in linear time over the input.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit a Lex method that processes input using the Transition_Table for the current Lexer_Mode.
2. THE LexerEmitter SHALL implement Maximal_Munch by tracking the last Accepting_State and its position during each scan, and backtracking to it when no further transitions are available.
3. WHEN two tokens match the same input with the same length, THE LexerEmitter SHALL select the token with the lower declaration index (Declaration_Order_Priority).
4. WHEN no transition exists from the current state for the current input character, and no Accepting_State has been seen, THE LexerEmitter SHALL emit an error token for the current character and advance by one position.
5. THE LexerEmitter SHALL track line and column positions during lexing, incrementing the line counter on newline characters.
6. THE LexerEmitter SHALL emit an EndOfFile token when the end of input is reached.

### Requirement 7: Lexer Mode Support in DFA Lexer

**User Story:** As a Storm developer, I want the DFA lexer to support lexer modes with push and pop actions, so that features like interpolated strings continue to work.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit a mode stack that starts with the default mode.
2. WHEN a token with a PushModeIR action is matched, THE LexerEmitter SHALL push the target mode onto the mode stack and switch to the corresponding Transition_Table.
3. WHEN a token with a PopModeIR action is matched, THE LexerEmitter SHALL pop the mode stack (if more than one mode remains) and switch to the Transition_Table of the new top mode.
4. THE LexerEmitter SHALL select the Transition_Table corresponding to the current top of the mode stack for each lexing iteration.

### Requirement 8: Hidden Token Support

**User Story:** As a Storm developer, I want hidden tokens (whitespace, comments) to be handled correctly in the DFA lexer, so that the parser receives a token stream with proper channel assignments.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit a set of hidden token kinds derived from tokens marked as hidden in the GrammarIR.
2. WHEN a hidden token is matched, THE LexerEmitter SHALL assign it to the Hidden channel in the emitted token.
3. WHEN a non-hidden token is matched, THE LexerEmitter SHALL assign it to the Default channel.

### Requirement 9: TokenKind Enum Compatibility

**User Story:** As a Storm developer, I want the TokenKind enum to remain unchanged, so that the parser and all downstream consumers continue to work without modification.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit the same TokenKind enum as the current implementation, with identical member names and declaration order.
2. THE LexerEmitter SHALL include the EndOfFile and Error members in the TokenKind enum.
3. THE LexerEmitter SHALL use the same SanitizeIdentifier logic for token names containing special characters.

### Requirement 10: Public API Compatibility

**User Story:** As a Storm developer, I want the generated lexer class to maintain the same public API, so that consumers like MistLexer.cs require no changes.

#### Acceptance Criteria

1. THE LexerEmitter SHALL emit a static Lex method with the signature `public static TokenStream Lex(SourceText source, DiagnosticBag diagnostics)`.
2. THE LexerEmitter SHALL emit a static IsHidden method with the signature `public static bool IsHidden(int tokenKind)`.
3. THE LexerEmitter SHALL emit a static GetTokenMode method with the signature `public static int GetTokenMode(int tokenKind)`.
4. THE LexerEmitter SHALL emit the same mode constants (e.g., `Mode_default`, `Mode_Interpolation`) as the current implementation.

### Requirement 11: RegexLiteralIR Handling

**User Story:** As a Storm developer, I want regex literal patterns to be compiled into the DFA rather than using runtime Regex objects, so that all token matching is handled uniformly by the transition table.

#### Acceptance Criteria

1. WHEN a RegexLiteralIR contains character class patterns (e.g., `\s+`, `[^\n]*`), THE Thompson_Construction SHALL parse the regex and produce an equivalent NFA.
2. WHEN a RegexLiteralIR contains the dot metacharacter, THE Thompson_Construction SHALL treat it as matching any character except newline.
3. WHEN a RegexLiteralIR contains escape sequences (e.g., `\s`, `\d`, `\w`, `\n`, `\t`), THE Thompson_Construction SHALL expand them to their equivalent character classes.
4. THE LexerEmitter SHALL eliminate all runtime `System.Text.RegularExpressions.Regex` usage from the generated lexer, replacing it with DFA table lookups.

### Requirement 12: Correctness Equivalence

**User Story:** As a Storm developer, I want the DFA lexer to produce identical token streams to the current TryMatch lexer for all valid inputs, so that the replacement is safe and non-breaking.

#### Acceptance Criteria

1. FOR ALL valid Mist source inputs, THE DFA-based lexer SHALL produce the same sequence of token kinds as the current TryMatch-based lexer.
2. FOR ALL valid Mist source inputs, THE DFA-based lexer SHALL produce tokens with the same lexeme strings as the current TryMatch-based lexer.
3. FOR ALL valid Mist source inputs, THE DFA-based lexer SHALL produce tokens with the same source spans as the current TryMatch-based lexer.
4. FOR ALL valid Mist source inputs, THE DFA-based lexer SHALL produce tokens with the same channel assignments (Hidden or Default) as the current TryMatch-based lexer.
5. FOR ALL inputs containing unrecognized characters, THE DFA-based lexer SHALL produce the same error tokens as the current TryMatch-based lexer.
