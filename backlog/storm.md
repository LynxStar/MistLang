# Storm Backlog

## Delimited list syntax for grammar rules

The current pattern for comma-separated (or other delimiter) lists in Storm grammar is:

```storm
rule TypeArgumentList = LESS_THAN TypeArgument (COMMA TypeArgument)* GREATER_THAN;
```

This produces an AST node with `.TypeArgument` (first) and `.TypeArguments` (rest as `ImmutableArray`). Every consumer must handle the split:

```csharp
// This pattern repeats everywhere
var count = 1 + typeArgList.TypeArguments.Length;

// Or iteration:
Process(list.TypeArgument);
foreach(var item in list.TypeArguments)
{
    Process(item);
}
```

**Proposed:** A first-class delimited list construct in the Storm grammar language that emits a single `ImmutableArray<T>` containing all items:

```storm
// Strawman syntax — exact form TBD
rule TypeArgumentList = LESS_THAN TypeArgument[COMMA] GREATER_THAN;
// or
rule TypeArgumentList = LESS_THAN delimited(TypeArgument, COMMA) GREATER_THAN;
```

The generated node would have a single property:

```csharp
public ImmutableArray<TypeArgumentNode> TypeArguments { get; }
```

No separate `.First` / `.Rest` split. Consumers just iterate the one collection.

**Affected rules (non-exhaustive):**
- `TypeArgumentList = LESS_THAN TypeArgument (COMMA TypeArgument)* GREATER_THAN`
- `TypeParameterList = LESS_THAN TypeParameter (COMMA TypeParameter)* GREATER_THAN`
- `ParameterList = Parameter (COMMA Parameter)*`
- `BaseList = first:BaseTypeRef (COMMA rest:BaseTypeRef)*`
- `ConstraintList = Constraint (COMMA Constraint)*`
- `EnumMemberList = EnumMember (COMMA EnumMember)* COMMA?`
- `TupleTypeElement` lists in tuple type references
- `ArgumentList` in call expressions

**Why:**
- Eliminates a pervasive pattern of `1 + rest.Length` arity calculations
- Removes the need for "first + foreach rest" iteration everywhere
- Reduces surface area for off-by-one bugs
- Makes generated AST nodes more intuitive (one collection, not split head/tail)

**Considerations:**
- Need to decide how trailing delimiters are handled (optional trailing comma)
- Need to decide minimum count: `+` (one or more) vs `*` (zero or more)
- The PEG parser internally still needs the `first (sep rest)*` structure for parsing — this is a code generation concern, not a parsing concern
- Backward compatibility: existing consumers would need migration

## Labelled element inside a repetition emits a spurious scalar property

A label on a rule reference *inside* a `( ... )*` or `( ... )+` group makes the generator emit
**both** the list and a scalar. Each round of the loop that reaches the reference assigns the
scalar; each round that completes also adds the node to the list.

```storm
rule BaseList = first:BaseTypeRef (COMMA rest:BaseTypeRef)*;
```

```csharp
public BaseTypeRefNode First { get; set; } = default!;
public BaseTypeRefNode Rest { get; set; } = default!;   // the scalar
public ImmutableArray<BaseTypeRefNode> BaseTypeRefs { get; set; } = ImmutableArray<BaseTypeRefNode>.Empty;
```

**`Children()` fixed 2026-09-27.** It yielded the scalar unconditionally beside the list, so zero
rounds gave `[First, null]` -- every non-union `TypeReference` in every program had a null child
-- and `[1, 2, 3]` gave `[1, 3, 2, 3]`. It now yields the lead and the list: each real child once,
in source order. A reference repeated with no separator, `(rest:X)*`, and an unlabelled `X*` beside
a lead were worse off: the parser looked for the repeated reference only inside a sequence, so their
list stayed empty and every repeated child was lost. Both now fill the list.

Reproduced on a small independent grammar at `13b67c0e`, memoization off and on agreeing:

| Rule, input | Before | After |
|---|---|---|
| `first:Elem (COMMA rest:Elem)*`, `a` | `[a, null]` | `[a]` |
| same, `a,b,c` | `[a, c, b, c]` | `[a, b, c]` |
| same, `a,b,` | `[a, null, b]` | `[a, b]` |
| `first:Elem (rest:Elem)*`, `a b c` | `[a, null]`, list empty | `[a, b, c]` |
| `first:Elem Elem*`, `a b c` | `[a]`, list empty | `[a, b, c]` |
| `LP first:Elem COMMA rest:Elem (COMMA additional:Elem)* RP`, `(a,b,c,d)` | `[a, b, d, c, d]` | `[a, b, c, d]` |

For Mist the generated parser, lexer and runtime are byte-identical, and the AST differs by exactly
the thirteen `yield return` lines of the rules below. `RepeatedRuleReferenceTests` (Tiers 1-2) and
`GeneratedParserFidelityTests` (Tier 4, memoization off and on) hold it; each of seven mutations
across the two emitters is caught.

**Residue: the scalar stays.** Building the solution without it gives 239 compile errors in
`Mist.Core`, one per read, before any test project compiles: `TypeCheckerAnalyzer` 88,
`CSharpEmitter` 71, `SemanticModelBuilder` 39, `RefinementPredicate` 28, `MistPrettyPrinter` 6,
`SemanticModel` 2, and the v2 binders `OperatorBinder` 3 and `PatternBinder` 2 through `IsBare`.
Every one is a `.Right` on
`LogicalOr`, `LogicalAnd`, `VoidCoalescing`, `OrPattern` or `AndPattern`, read as the last operand;
the other eight rules' scalars have no reader there. Tests read it as well (`ParserRuleTests`) and
assign it (`RefinementComparisonEnginePropertyTests`). So it keeps its type, setter and value and is
only no longer a child. It holds the list's last node when the loop stopped at a separator; null
when no round ran or the failing round's reference did not parse, so `(rest:X)*` always ends null;
and a discarded node when something after the reference failed (`(COMMA rest:X SEMI)*` on
`a,b;,c` holds `c`, which is in neither the list nor `Children()`). Removing it means moving those
reads onto the list first -- v2's `GrammarRepetition` already reads the list alone -- and then
emitting no scalar for a label inside a repetition.

A reference nested deeper in the repeated body, in a nested group or an optional, is neither
collected into a list nor excluded from `Children()`. No Mist rule has that shape.

**Scope — every rule with a rule-reference label inside a repetition:**

| Rule | Construct |
|---|---|
| `TypeReference` | `(PIPE rest:SingleTypeRef)*` |
| `RefinableTypeExpression` | `(PIPE rest:RefinableSingleTypeExpr)*` |
| `CompoundRefinementPredicate` | `(AND rest:UnaryPattern)*` |
| `BaseList` | `(COMMA rest:BaseTypeRef)*` |
| `VoidCoalescing` | `(QUESTION_QUESTION right:LogicalOr)*` |
| `LogicalOr` | `(LOGICAL_OR right:LogicalAnd)*` |
| `LogicalAnd` | `(LOGICAL_AND right:Equality)*` |
| `OrPattern` | `(OR right:AndPattern)*` |
| `AndPattern` | `(AND right:UnaryPattern)*` |
| `TupleSingleTypeRef` | `(COMMA additional:TupleTypeElement)*` |
| `TupleLiteralExpression` | `(COMMA additional:TupleLiteralElement)*` |
| `TuplePositionalPattern` | `(COMMA additional:Pattern)*` |
| `TypedTupleDeconstruction` | `(COMMA additional:TupleDeconstructionElement)*` |

`VarTupleDeconstruction`'s `(COMMA restNames:IDENTIFIER)*` labels a token, which becomes an
`ImmutableArray<Token>` and was never a child.

**Workaround (verified):** drop the label. `ArrayElementList` was changed from
`(COMMA rest:Expression)*` to `(COMMA Expression)*`; the scalar disappeared, the list kept
its `Expressions` name, and nothing needed re-threading because nothing read that scalar.
Storm names the list from the rule name regardless, which is why the unlabelled
`ArgumentList` and `TypeArgumentList` never had the defect.

**Before removing labels elsewhere,** check per rule whether any consumer reads the scalar
believing it meaningful — for the operator-chain rules every read above means "last operand",
so removing the label would break them rather than drop dead weight.

**Related:** the `storm-parser-repetition-bugs` spec fixed `OptionalIR(SequenceIR(...))`
generating a `Token?` instead of a node property. This is the same family — a labelled
element in a repetition — and was not covered.

## Syntactic predicates (`&E`, `!E`) -- added 2026-09-19

Storm had no lookahead, so an alternative could only be told from another by its own tokens. That
is not enough where two readings share every token and differ in what FOLLOWS: an ordered choice
commits to the first alternative that succeeds locally, and a failure later in the enclosing
sequence never re-tries the others. `Api.Same<int>` as a value against `Use(a < b, c > d)` is the
case that needed it.

`&E` succeeds where `E` matches and `!E` where it does not; neither consumes input nor yields an
AST child. A predicate is a prefix on one element (`&A*` is `&(A*)`) and cannot carry a label.

**What a body may be.** A rule reference, or a fixed-width expression over tokens -- a token, a
sequence of them, or an alternation whose arms are the same width. The token form is a test over
`Peek` offsets with no state touched; the rule form saves and restores the position, the error
flag and the diagnostic count around one call. Anything else is `STORM0008` at lowering, as is a
label inside a predicate and a predicate inside a LABELLED element: the emitter writes a labelled
sequence, optional or list through paths that pass over element kinds they do not know and have no
channel to say so, and a predicate dropped there would leave its alternative unguarded.

**Passes.** A predicate is nullable and its body is leftmost for `LeftRecursionPass` (`rule A =
&A B` and `rule A = &B A` are both left recursion); names inside it are resolved; a rule it names
is an edge of the memoization graph; and `ReachabilityPass` neither flags an alternative holding a
predicate nor counts its FIRST-k sequences against later ones, a guarded alternative accepting
strictly less than its first three tokens claim. That last point costs `STORM0006` its sight of
guarded alternatives, which is the price of the pass being a prefix approximation.

**Memoization** stays sound: a predicate reads past the rule's end, but the answer is still a pure
function of position in a fixed token stream. `SyntacticPredicateTests` runs each behaviour with
the plan on and off. Twenty-three mutations, each caught by name.

## An unlabelled token alternation in a rule sequence emits no code

Found 2026-09-19 while sizing the predicate, measured on an unmodified generator: for

```storm
rule Item = name:IDENT typeArgs:TypeArgs (SEMI | COMMA) as Grouped | ...;
```

the generated alternative goes from the `_ParseTypeArgs()` check straight to node construction.
The follower is neither checked nor consumed: `ParserEmitter.EmitFullExprParse` has no
`AlternationIR` case and its switch ends in a silent `default: break`. A single unlabelled token
is matched, the `TokenRefIR` case existing. `Mist.storm` has no such shape today, so nothing is
wrong in the shipped parser, but a grammar author who writes one gets an alternative that accepts
anything. The same silent default is why a predicate needed emission of its own rather than a
reuse of "emit this expression". Either give the emitter the arm or refuse the shape at lowering.

**Both, 2026-09-27.** Revalidated first at `13b67c0e` on independent grammars, memoization off and
on agreeing: `a<b>;`, `a<b>(` and `a<b>` all parsed as `Grouped` to position 4, the follower unread.
The same silent default sat in the body of an unlabelled `?`, `*` and `+` -- `(SEMI | COMMA)?` never
consumed, and `(SEMI | COMMA)+` could never match, its loop making no progress -- and the structural
elements of a struct-projected group skipped any alternation.

A choice between single tokens is now matched in all four places against the flag around it,
`if (ok && (Match((int)TokenKind.SEMI) ?? Match((int)TokenKind.COMMA)) == null) ok = false;`. It
consumes the token it chose, consumes nothing when none fits, and leaves the restore to the
alternative, optional or loop that holds it: `a<b>;` and `a<b>,` parse as `Grouped` to position 5,
and `a<b>(` and `a<b>` fall back to `Plain` at 1, also when a memoized read replays them. Any other
unlabelled alternation -- a rule-reference arm, a sequence arm, a nested alternation -- is
`STORM0008` at lowering, naming its rule, since the emitter can match only tokens there. A labelled
alternation and a predicate body keep their own paths, but an alternation *inside* a labelled
optional or repetition is checked like any other, so `held:(SEMI | Args)?`, which never matched,
is now refused. `Mist.storm` has no unlabelled alternation, and the generated Mist parser is
byte-identical. `UnlabelledTokenChoiceTests` (Tiers 1-3) and `GeneratedParserFidelityTests`
(Tier 4) hold it; each of eleven mutations is caught.

**Residue -- the generator-wide audit this does not do.** Paths that still pass over element kinds
they do not know, each reproduced on the fixed generator:

- A labelled optional sequence (`EmitAtomicSequenceBody`) skips a choice inside it:
  `held:(COMMA (SEMI | LP) RP)?` reads the optional as absent on `a,;)` and stops at 1.
- A labelled alternation (`EmitAlternationExprParse`) never matches a rule-reference arm:
  `held:(SEMI | Args)` reads `a<b>` as the next alternative -- the known limitation noted in
  `AstEmitter.GetPropertyInfo`.
- An unlabelled optional or repetition nested in the body of another
  (`EmitFullExprParseAssignOnly`) is dropped: `first:Elem (LT (COMMA rest:Elem)* GT)?` reads `a<>`
  but stops at 1 on `a<,b,c>`.
- An unlabelled optional among a struct-projected group's elements (`EmitConsumeStructuralElement`)
  is skipped: `head:IDENT (key:IDENT COMMA? value:IDENT)*` stops at 1 on `a b,c`.

## A rule literal that repeats a declared token's text never matches

Found 2026-09-27: with `token SEMI = ";";` declared, `rule Item = name:IDENT ";";` fails on `a;`.
Lowering gives the literal a synthetic token, `__SYNTH_SEMI`, because it looks for an existing
token by that synthetic name rather than by its text. The lexer then produces the declared `SEMI`,
which wins the tie on declaration order, so the synthetic token never appears. `Mist.storm` writes
no literal in a rule, so the shipped parser is unaffected.

**Fixed, 2026-09-27.** Lowering now resolves a literal by its text: the first declared default-mode
token whose pattern is exactly that string, also through a fragment, is the token the literal names,
and no synthetic token is made for it. A literal no declared token spells keeps its synthetic token.
A hidden token counts, being what the lexer produces for the text: the literal names it just as
writing its name would. A token of another mode does not: the synthetic token is lexed in the
default mode, where that token is no rival. `a;` now parses as `Item` to position 2.
`RuleLiteralTokenReuseTests` holds it -- Tier 1 on `DeclaredTokenSpelling` (8 cases), Tier 3 on
lowering from grammar text (3), Tier 4 on a compiled parser (2) -- and each of seven mutations is
caught. The generated Mist sources are byte-identical.

**Residue**, each reproduced on the fixed generator:

- A literal that a pattern token also admits still never matches, its synthetic token losing the
  tie. With `token IDENT = [a-z]+;`, `rule Cond = "if" name:IDENT;` yields no node on `if a`; with
  `token SEMI = [;];`, `name:IDENT ";"` yields none on `a;`.
- A rule element naming a hidden token never matches, the parser reading only the default channel,
  and nothing refuses it: with `hidden token SP = " ";`, `name:IDENT SP other:IDENT` and
  `name:IDENT " " other:IDENT` both yield no node on `a b`.
- `ReachabilityPass` keys a literal by its text and a token by its name, so `SEMI as A | ";" as B`
  draws no `STORM0006` although `SEMI as A | SEMI as B` does, and the two now lower alike.
