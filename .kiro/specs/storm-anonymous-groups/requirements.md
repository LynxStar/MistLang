# Requirements Document

## Introduction

The Storm source generator takes `.storm` grammar files and generates two C# artefacts: a file of AST node classes (`Mist.Ast.g.cs`) and a recursive-descent parser (`Mist.Parser.g.cs`). When a grammar rule contains an anonymous group `(...)` — that is, a parenthesised subexpression that carries no `as` alternative name — the generator currently flattens all labelled fields from inside that group directly onto the enclosing rule's node class. Under a repetition modifier (`*` or `+`), this is incorrect: the parser loop overwrites the same scalar fields on every iteration, so only the values from the last iteration survive.

The fix is to project every anonymous group as its own structural type — a generated `struct` — whose fields hold the labels declared inside that group. The parent node then holds a scalar or collection of that struct according to the group's modifier. This preserves every iteration's values in a `*` or `+` context, makes the shape of the grammar explicit in the type system, and eliminates the phantom "last-wins" fields from the generated AST.

Rules affected in the Mist grammar include `Additive`, `Multiplicative`, `Equality`, `LogicalOr`, `LogicalAnd`, `VoidCoalescing`, and the `RelationalComparison` alternative of `Relational`.

### Migration Strategy

This change affects hundreds of call sites across the Mist compiler. A naive shim approach (emitting both old and new properties without diagnostics) was attempted previously and failed — 793 call sites remained unmigrated because the build provided no signal about what still needed work. A flag-day approach (remove old properties, fix everything atomically) is impractical at this scale.

The chosen strategy uses `[Obsolete]`-attributed shim properties that forward to the new Group_Struct data. During the migration phase, both old and new properties exist, but every use of the old properties produces a CS0618 compiler warning. The warning count is the migration progress indicator. Once it reaches zero, the shims are removed and any remaining usage becomes a hard compile error — confirming nothing was missed.

## Glossary

### Components

- **AST_Emitter**: `Source/Storm.SourceGenerator/Emit/AstEmitter.cs` — generates the AST node class file from the lowered grammar IR.
- **Parser_Emitter**: `Source/Storm.SourceGenerator/Emit/ParserEmitter.cs` — generates the recursive-descent parser from the lowered grammar IR.
- **Grammar_IR**: The lowered intermediate representation in `Source/Storm.SourceGenerator/IR/` that the emitters consume. Key types: `RuleIR`, `RuleAlternativeIR`, `ExpressionIR`, `SequenceIR`, `AlternationIR`, `ZeroOrMoreIR`, `OneOrMoreIR`, `OptionalIR`, `FieldLabelIR`.
- **Consumer_Grammar**: A `.storm` file consumed by the generator — principally `Source/Mist/Grammar/Mist.storm` for the Mist project.
- **Generated_Code**: The C# files produced by the source generator, placed under `Source/Mist/obj/*/generated/...`.
- **Mist_Compiler**: The Mist compiler that reads the generated AST and parser code to compile `.mist` source files.

### Concepts

- **Anonymous_Group**: A parenthesised subexpression in a grammar rule that carries no `as` alternative name and no explicit rule-level name. Written as `(...)`, `(...)?`, `(...)*`, or `(...)+`.
- **Group_Struct**: The generated C# `struct` that represents one occurrence of an anonymous group. Named after the containing rule and the group's positional index within the rule's top-level alternative expression, e.g. `AdditiveNodeGroup0`.
- **Group_Index**: The zero-based positional index of an anonymous group among all anonymous groups in a single rule alternative. `Group0` is the first, `Group1` the second, and so on.
- **Modifier**: One of `?`, `*`, or `+` applied to an anonymous group, controlling the C# property type on the parent node.
- **Pure_Token_Alternation**: An alternation inside an anonymous group where every alternative is a single `TokenRefIR`. Example: `(PLUS | MINUS)`. This pattern may be optimised to a single `Token` field on the Group_Struct rather than generating a discriminated union struct.
- **Nested_Group**: An anonymous group that appears as a direct child of another anonymous group. Its Group_Struct is emitted as a nested type inside the outer group's Group_Struct.
- **Flat_Field_Antipattern**: The existing, incorrect behaviour where the AST_Emitter adds labelled fields from inside an anonymous group directly to the parent node, and the Parser_Emitter assigns those fields as scalar variables that are overwritten each loop iteration.
- **Obsolete_Shim**: A generated property on a node class marked with `[Obsolete]` that preserves the old flat-field API by forwarding reads to the new Group_Struct data. Produces CS0618 warnings at every call site, providing migration signal without breaking the build.

## Requirements

---

### Requirement 1: Group Struct Generation

**User Story:** As a compiler author, I want anonymous groups in grammar rules to project as their own struct types, so that each structural unit of the grammar is represented as a distinct, correctly-typed C# value.

#### Acceptance Criteria

1. WHEN the AST_Emitter processes a rule alternative that contains an anonymous group `(...)`, THE AST_Emitter SHALL emit a `public struct {RuleName}NodeGroup{N}` type in the same namespace as the rule's node class, where `N` is the Group_Index of that anonymous group within the alternative.

2. THE Group_Struct SHALL contain one field per `label:Expression` declared directly inside the anonymous group, with the same type derivation rules used for labelled fields on ordinary node classes (rule references become `{Rule}Node`, token references become `Token`, and repetition or optional modifiers are applied accordingly).

3. WHEN a rule alternative contains multiple anonymous groups at the same nesting level, THE AST_Emitter SHALL assign each a distinct Group_Index in left-to-right source order starting from zero.

4. WHEN an anonymous group appears inside another anonymous group, THE AST_Emitter SHALL emit the inner Group_Struct as a nested `public struct` inside the outer Group_Struct, using the same Group_Index naming scheme relative to the outer group's body.

5. THE parent rule's node class SHALL contain one property for each anonymous group with a type determined by the group's Modifier:
   - No modifier: `{RuleName}NodeGroup{N}` (scalar, required)
   - `?`: `{RuleName}NodeGroup{N}?` (nullable, optional)
   - `*` or `+`: `ImmutableArray<{RuleName}NodeGroup{N}>` (list, defaulting to `ImmutableArray<{RuleName}NodeGroup{N}>.Empty`)

6. THE parent rule's node class SHALL NOT contain the individual labelled fields that exist inside an anonymous group; those fields SHALL appear only on the Group_Struct (except as Obsolete_Shim properties during migration — see Requirement 5).

---

### Requirement 2: Pure-Token Alternation Optimisation

**User Story:** As a compiler author, I want an anonymous group whose entire body is a token alternation to collapse to a single `Token` field, so that common patterns like `(PLUS | MINUS)` do not generate unnecessary discriminated structs.

#### Acceptance Criteria

1. WHEN an anonymous group's body is an `AlternationIR` where every alternative is a single `TokenRefIR` (a Pure_Token_Alternation), THE AST_Emitter SHALL NOT emit a Group_Struct for that group.

2. WHEN the Pure_Token_Alternation optimisation applies, THE parent node property for that group SHALL be `Token` (no modifier), `Token?` (`?` modifier), or `ImmutableArray<Token>` (`*` or `+` modifier) — the same types produced today for a single labelled token.

3. WHEN the Pure_Token_Alternation optimisation applies and the group carries a `label:` prefix, THE parent node property SHALL use the label name (PascalCase), matching the existing field-label promotion behaviour.

4. IF an anonymous group's body is an alternation where at least one alternative is not a single `TokenRefIR`, THEN the Pure_Token_Alternation optimisation SHALL NOT apply, and a Group_Struct SHALL be emitted.

---

### Requirement 3: Parser Emitter — Group Struct Construction

**User Story:** As a compiler author, I want the generated parser to construct Group_Struct instances during parsing, so that each iteration of a `*` or `+` loop stores its values in a separate struct value rather than overwriting shared scalar variables.

#### Acceptance Criteria

1. WHEN the Parser_Emitter encounters an anonymous group inside a `ZeroOrMoreIR` or `OneOrMoreIR`, THE Parser_Emitter SHALL emit code that, on each loop iteration, collects the parsed values for that group's labelled fields into a local `{RuleName}NodeGroup{N}` struct value and appends it to an `ImmutableArray.CreateBuilder<{RuleName}NodeGroup{N}>()` accumulator.

2. WHEN the Parser_Emitter encounters an anonymous group with a `?` modifier, THE Parser_Emitter SHALL emit code that sets a nullable `{RuleName}NodeGroup{N}?` local variable to a new struct value if the optional group matched, or leaves it `null` if the group was absent.

3. WHEN the Parser_Emitter encounters an anonymous group with no modifier, THE Parser_Emitter SHALL emit code that sets a `{RuleName}NodeGroup{N}` local variable to the single parsed struct value and assigns it to the corresponding property on the parent node.

4. WHEN the Pure_Token_Alternation optimisation applies (Requirement 2), THE Parser_Emitter SHALL emit the same token-parsing logic it emits today for a labelled token with the matching modifier, without constructing any Group_Struct.

5. THE Parser_Emitter SHALL assign the finalised `ImmutableArray<{RuleName}NodeGroup{N}>` (built with `.ToImmutable()`) to the corresponding list property on the parent node after the loop.

---

### Requirement 4: Mist Grammar — Affected Rules

**User Story:** As a Mist language developer, I want all arithmetic and logical binary-operator rules in the Mist grammar to use the corrected group projection, so that expressions like `a + b - c` preserve all intermediate operators and operands in the AST.

#### Acceptance Criteria

1. THE AST_Emitter SHALL apply group projection to at minimum the following rules from `Source/Mist/Grammar/Mist.storm`, each of which contains an anonymous group with a `*` modifier:
   - `VoidCoalescing`
   - `LogicalOr`
   - `LogicalAnd`
   - `Equality`
   - `Additive`
   - `Multiplicative`
   - The `RelationalComparison` alternative of `Relational`

2. FOR the `Additive` rule, the generated `AdditiveNode` class SHALL contain:
   - `MultiplicativeNode Left` (scalar, required)
   - `ImmutableArray<AdditiveNodeGroup0> Group0` (list, default empty)
   and (after shim removal) NOT contain scalar `Operator` or `Right` properties or the redundant `ImmutableArray<MultiplicativeNode> Multiplicatives` property.

3. FOR the `Additive` rule, `AdditiveNodeGroup0` SHALL contain:
   - `Token Operator`
   - `MultiplicativeNode Right`

4. FOR the `Equality` rule, the generated `EqualityNode` class SHALL contain:
   - `RelationalNode Left`
   - `ImmutableArray<EqualityNodeGroup0> Group0`
   and `EqualityNodeGroup0` SHALL contain `Token Operator` and `RelationalNode Right`.

5. WHEN the Pure_Token_Alternation optimisation applies to the `operator:(PLUS | MINUS)` sub-expression inside `Additive`'s anonymous group, THE `Operator` field on `AdditiveNodeGroup0` SHALL be of type `Token`.

---

### Requirement 5: Migration Strategy — Obsolete Shims

**User Story:** As a compiler author, I want the old flat-field properties to remain available during migration as `[Obsolete]`-marked shims that forward to the new Group_Struct data, so that the build stays green while CS0618 warnings provide an exhaustive worklist of unmigrated call sites.

#### Acceptance Criteria

1. WHEN shims are enabled, THE AST_Emitter SHALL emit, on each affected node class, the old scalar properties (e.g., `Operator`, `Right`, `Multiplicatives`) marked with `[Obsolete("Use Group0")]`.

2. EACH Obsolete_Shim property SHALL forward reads to the corresponding field(s) on the new Group_Struct data. For scalar shims on a `*`-modified group (e.g., `Operator`, `Right`), the shim SHALL return the value from the last element of the `ImmutableArray` (preserving current last-wins semantics for unmigrated consumers). For list shims (e.g., `Multiplicatives`), the shim SHALL project the relevant field from each element of the `ImmutableArray`.

3. THE Mist compiler SHALL build successfully with shims enabled, with zero errors and all existing tests passing. The only new diagnostics SHALL be CS0618 obsolete-usage warnings at unmigrated call sites.

4. THE CS0618 warnings SHALL NOT be suppressed globally or in `.editorconfig`. They are the migration signal and MUST remain visible.

5. WHEN all CS0618 warnings from Obsolete_Shim usage have been resolved (warning count is zero), THE shims SHALL be removable by a generator configuration change (or code deletion) without introducing any new compile errors.

6. AFTER shim removal, THE affected node classes SHALL contain only the new Group_Struct properties as specified in Requirements 1 and 4. Any remaining usage of the removed properties SHALL be a hard compile error, confirming the migration is complete.

---

### Requirement 6: Backward Compatibility — Unaffected Rules

**User Story:** As a compiler author, I want rules that contain no anonymous groups (or only Pure_Token_Alternation groups that optimise away) to continue generating the same code as before, so that the change does not break existing grammar consumers.

#### Acceptance Criteria

1. WHEN a rule alternative contains no anonymous groups that require a Group_Struct (i.e., all anonymous groups are Pure_Token_Alternations or there are no anonymous groups at all), THE AST_Emitter SHALL produce the same node class as it does today.

2. WHEN the Parser_Emitter processes a rule that contains no anonymous groups requiring Group_Struct construction, THE Parser_Emitter SHALL produce the same parser method as it does today.

---

### Requirement 7: Mist Compiler Consumers — AST Visitor Updates

**User Story:** As a compiler author, I want all existing uses of the affected AST nodes (such as `AdditiveNode`, `MultiplicativeNode`, `EqualityNode`) to be updated to traverse the new group list properties, so that semantic analysis and code emission continue to work correctly.

#### Acceptance Criteria

1. WHEN the generated AST node classes change shape (Requirement 4), THE Mist_Compiler's analysis and backend stages that currently read the Obsolete_Shim properties SHALL be updated to use the corresponding `Group0` `ImmutableArray` property instead, eliminating the CS0618 warning at each call site.

2. THE `Children()` override on each affected node class SHALL yield all `SyntaxNode` children contained in the Group_Struct list, by iterating the list and yielding each node-typed field from each struct element.

3. THE Mist_Compiler SHALL compile, analyse, and emit correct C# for a Mist program containing a multi-term additive expression such as `a + b - c`, producing a C# expression with the correct number of operands.

4. AFTER all consumer migrations are complete, the build SHALL have zero CS0618 warnings from Obsolete_Shim usage, and the shims SHALL be removed per Requirement 5.6.

---

### Requirement 8: Sample Application

**User Story:** As a Mist developer, I want a sample application demonstrating that multi-term binary expressions are parsed and evaluated correctly, so that I can verify the fix is complete and working end-to-end.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/StormAnonymousGroups/Program.mist`.

2. The sample SHALL demonstrate multi-term additive, multiplicative, and logical-or expressions using three or more operands, printing a labeled result for each expression.

3. Each demonstrated expression SHALL print labeled output to stdout so correctness is observable from the expected value.

4. `samples/AllFeatures/Program.mist` SHALL call `StormAnonymousGroups.Program.RunDemo()` to include the sample in the full-suite run.

5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// PROOF: AdditiveNode.Group0 list preserves all operators and operands
// PROOF: a + b - c produces two Group0 entries, not last-wins scalar
// PROOF: Multiplicative group projection works in the same way
var x = 10 + 3 - 5;
var y = 2 * 4 * 3;
var z = 1 + 2 + 3 + 4;

Console.WriteLine($"x={x}");
Console.WriteLine($"y={y}");
Console.WriteLine($"z={z}");
```

**Expected output:**
```
x=8
y=24
z=10
```

---

### Requirement 9: Generator Test Coverage

**User Story:** As a compiler author, I want automated tests that verify the group-projection logic in the AST_Emitter and Parser_Emitter, so that regressions are caught without running the full Mist compiler pipeline.

#### Acceptance Criteria

1. THE test suite SHALL include a test that, given a minimal grammar rule of the form `rule Additive = left:Multiplicative (operator:(PLUS | MINUS) right:Multiplicative)*;`, verifies that the AST_Emitter produces an `AdditiveNode` class with an `ImmutableArray<AdditiveNodeGroup0> Group0` property and no scalar `Operator` or `Right` properties (when shims are disabled).

2. THE test suite SHALL include a test that verifies the Parser_Emitter, given the same rule, emits a `while` loop body that constructs an `AdditiveNodeGroup0` struct value per iteration and appends it to a builder, rather than overwriting scalar variables.

3. THE test suite SHALL include a test for the Pure_Token_Alternation optimisation: a rule whose anonymous group contains only a token alternation SHALL produce a `Token` (or `ImmutableArray<Token>`) property on the parent node rather than a Group_Struct.

4. THE test suite SHALL include a test for a required (no-modifier) anonymous group: the parent node SHALL have a scalar `{Rule}NodeGroup0` property with no nullable or list wrapping.

5. THE test suite SHALL include a test for a `?`-modified anonymous group: the parent node SHALL have a `{Rule}NodeGroup0?` nullable property.

6. THE test suite SHALL include a test that, with shims enabled, the Obsolete_Shim properties are present, marked `[Obsolete]`, and return correct values forwarded from the Group_Struct data.
