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
`GeneratedParserFidelityTests` (Tier 4) hold it; each of seven mutations across the two emitters
is caught. (Its `Children()` rows ran with memoization off and on; since 2026-09-27 they run on the
plain parser only, a memoized read returning the very node object the first read built.)

**Residue: the scalar stays.** Building the solution without it gives 239 compile errors in
`Mist.Core`, one per read, before any test project compiles: `TypeCheckerAnalyzer` 88,
`CSharpEmitter` 71, `SemanticModelBuilder` 39, `RefinementPredicate` 28, `MistPrettyPrinter` 6,
`SemanticModel` 2, and the v2 binders `OperatorBinder` 3 and `PatternBinder` 2 through `IsBare`.
Every one is a `.Right` on
`LogicalOr`, `LogicalAnd`, `VoidCoalescing`, `OrPattern` or `AndPattern`, read as the last operand;
the other eight rules' scalars have no reader there. Tests read it as well (`ParserRuleTests`) and
assign it (`RefinementComparisonEnginePropertyTests`). So it keeps its type, setter and value and is
only no longer a child. It held the list's last node when the loop stopped at a separator; null
when no round ran or the failing round's reference did not parse, so `(rest:X)*` always ended null;
and a discarded node when something after the reference failed (`(COMMA rest:X SEMI)*` on
`a,b;,c` held `c`, which is in neither the list nor `Children()`). Since 2026-09-27 a round that
fails puts back what it read (see "One recursive emitter" below), so it holds the list's last node,
or null when the list is empty: `a,b;,c` gives `b`, and `(rest:X)*` ends on its last node. Every
Mist rule in the table below has a separator before the label, whose round fails at the separator,
so no Mist value changed. Removing it means moving those
reads onto the list first -- v2's `GrammarRepetition` already reads the list alone -- and then
emitting no scalar for a label inside a repetition.

A reference nested deeper in the repeated body, in a nested group or an optional, was neither
collected into a list nor excluded from `Children()`. Since 2026-09-27 it is collected: see "One
recursive emitter" below.

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
channel to say so, and a predicate dropped there would leave its alternative unguarded. That last
refusal is lifted since 2026-09-27, those paths being gone ("One recursive emitter" below):
`held:(COMMA &IDENT)?` guards its optional as the unlabelled form does, and `held:(IDENT &SEMI)`
falls under the refusal of a label on a group with no modifier.

**Passes.** A predicate is nullable and its body is leftmost for `LeftRecursionPass` (`rule A =
&A B` and `rule A = &B A` are both left recursion); names inside it are resolved; a rule it names
is an edge of the memoization graph; and `ReachabilityPass` neither flags an alternative holding a
predicate nor counts its FIRST-k sequences against later ones, a guarded alternative accepting
strictly less than its first three tokens claim. That last point costs `STORM0006` its sight of
guarded alternatives, which is the price of the pass being a prefix approximation. Since
2026-09-27 the pass evaluates guards instead ("Reachability proves shadowing" below).

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

**All four resolved 2026-09-27** by "One recursive emitter" below: the first, third and fourth now
parse (`a,;)` to 4, `a<,b,c>` to 7 with children `a b c`, `a b,c` to 4); the second is refused at
lowering, the AST having no union type for a choice with a rule-reference arm.

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
  `name:IDENT " " other:IDENT` both yield no node on `a b`. Refused at lowering since 2026-09-27
  (`STORM0008`, "names the hidden token 'SP'"), in both spellings.
- `ReachabilityPass` keys a literal by its text and a token by its name, so `SEMI as A | ";" as B`
  draws no `STORM0006` although `SEMI as A | SEMI as B` does, and the two now lower alike.

## One recursive emitter -- 2026-09-27

The parser emitter wrote an element through one of several near-copies, chosen by where it stood:
an alternative's body, the body of an unlabelled optional or repetition, a labelled element, a
labelled optional, list or bare group, a labelled alternation, a struct group's field or structural
element. Each ended in a silent default and each had its own idea of which property a value went
to. Beyond the residue listed in the two entries above, an unlabelled optional reference
(`name:IDENT Args?`, `(COMMA Args)?`) parsed its node and dropped it -- the AST declared the
property and the parser never set it. Seventeen existing test grammars write that shape
(`IDENT TypeArgumentList?`) and none looked at the node. A labelled optional group starting with a
reference (`held:(Args COMMA)?`) never matched, and a struct group nested in another produced a
parser that did not compile.

**Now.** `AstEmitter.NodeSlots` records, for each property, the element that fills it and whether it
assigns or appends. The parser declares one local per property and walks the alternative with a
single recursive `ElementWriter`. Every element kind at every depth is matched against the flag of
what holds it, and a matched value goes straight into the locals of the properties it fills. A
failed optional or round of a repetition puts back the position, error flag and diagnostics, the
lengths of the lists it appended to and the scalars it set, so no node from an abandoned attempt
is kept. The AST's walk now reaches inside optionals and repetitions. A reference nested there gets
a property of its own, nullable under optionals and a list under a repetition. That list is flat
across rounds, so `(A (COMMA B)*)*` gives `As` and `Bs`; structure between them needs labels,
which make a struct group. A label under a repetition that is not the repetition's collected
reference is a list. Every shape that had a property before keeps its name and type. Lowering joins
an unlabelled group written inside a sequence to it, so `(COMMA (LP Elem RP))*` collects `Elem` as
`(COMMA LP Elem RP)*` does. Nested struct groups are built, and the refusal of a predicate inside a
labelled element is lifted (see the predicates entry).

`UnemittableShapes` is what stays refused, each as `STORM0008` at lowering naming its rule:

- an unlabelled choice with an arm that is not a single token;
- a labelled one -- the AST has no union type;
- a label on a group with no modifier;
- an unlabelled reference inside a struct group's unlabelled optional or repetition, where no field
  keeps it -- labelling it makes a nested group;
- a reference to a hidden token;
- a character class, regular expression, fragment or inline block inside a rule body.

Reproduced on independent grammars at `37facf27` and on this change, memoization off and on
agreeing:

| Rule, input | Before | After |
|---|---|---|
| `name:IDENT held:(COMMA (SEMI \| LP) RP)?`, `a,;)` | stops at 1 | 4, `Held` = `,` |
| `first:Elem (LT (COMMA rest:Elem)* GT)?`, `a<,b,c>` | stops at 1 | 7, children `a b c` |
| `head:IDENT (key:IDENT COMMA? value:IDENT)*`, `a b,c` | stops at 1 | 4 |
| `first:Elem (COMMA (LP Elem RP))*`, `a,(b),(c)` | children `a` | children `a b c` |
| `name:IDENT Args?`, `a<b>` | `Args` null, no child | `Args` kept, a child |
| `head:IDENT (key:IDENT (LT value:IDENT GT)? SEMI)*` | does not compile | `a b<c>;d;` to 8 |
| `first:Elem (COMMA rest:Elem SEMI)*`, `a,b;,c` | `Rest` = `c`, a discarded node | `Rest` = `b` |
| `name:IDENT choice:(SEMI \| Args)` | never matches `Args` | refused |

For Mist the generated AST, lexer and runtime are byte-identical, and the parser is rewritten.
This holds for the grammar before and after the purity keywords landed. Every one of the
repository's 605 `.mist` files parses with both the old and the new generated parser. Their
trees, compared property by property with every `Children()` sequence, span and hidden token, are
identical in 573 files. The other 32 differ only in four token properties the AST declared and
the old parser never set, which now hold their token:

- a trailing `COMMA?`, in `AnonymousPropertyList`, `ArrayElementList`, `EnumMemberList`,
  `PropertyAssignmentList` and `SwitchArmList`;
- `ForStatement.SEMICOLON2`;
- `RIGHT_BRACE` in `DefaultTypeBinding`'s default field initializer;
- `IDENTIFIERs` in `VarTupleDeconstruction`.

Over the 596 files before purity landed, the new parser was about 12% faster: 2,039 ms against
2,323 ms for five warm passes. `ParserEmitter` went from 2,787 lines to 894 plus the 467-line `ParserEmitter.Elements.cs`.

The following hold it:

- `NodeSlotTests` (Tier 1, 8 cases);
- `UnemittableShapeTests` (Tier 1, 5 refused shapes and 5 shapes that used to be dropped; Tier 3,
  1 case for the join);
- `GeneratedParserFidelityTests` (Tier 4, 7 rows and 3 cases, one of them an inline block whose
  prefix, parsed once, must reach whichever arm follows).

Each of seventeen mutations is caught. The inline-prefix one survived until that last case was added.

**Residue**, each reproduced on this change. The first four are deferred until classic is sunset
(decided 2026-09-27): over a hundred classic and analysis walkers iterate `Children()` or read these
properties, and a fix changes what they see.

- `Children()` yields null for a labelled reference inside a labelled optional group that is
  absent. `baseClause:(COLON baseList:BaseList)?` gives every type declaration without a base
  clause a null child, and `typeAnnotation:(COLON type:TypeReference)?` does the same for every
  enum without one. The property is declared non-nullable (`BaseListNode BaseList`). Mist's
  walkers skip null children, which is why nothing fails. Guarding such properties in `Children()`
  is the fix.
- The outer label of such a group holds nothing: `BaseClause` and `TypeAnnotation` are always
  null, the node going to the inner label alone. This is kept deliberately, since filling it would
  give `Children()` the node twice. `MistPrettyPrinter` reads `enumDecl.TypeAnnotation`, so it
  never prints an enum's underlying type. `SemanticModelBuilder` already reads `Type` instead, and
  says why.
- `ConstraintClause`, a label forwarded out of a struct group and read by v2's `TypeSlotBuilder`,
  is the same node as the group's `Constraints` field, so `Children()` yields it twice.
- `Children()` does not descend into a nested group struct's node fields.
- `GeneratedParserFidelityTests` holds 21 cases (27 parses) at the generated-parser boundary, and
  `RuleLiteralTokenReuseTests` another 2. That is at the top of the pyramid's ~10-20 per boundary,
  so adding a row there should come with a case against removing one.

**The first four resolved 2026-10-03** by "Children() yields each node a node holds once" below: an
outer label holds the node of the capture inside it, which alone yields it, guarded where it may be
absent, and a group struct is walked to every depth.

## Reachability proves shadowing -- 2026-09-27

`ReachabilityPass` reported an alternative when every FIRST-3 token sequence it could begin with
was one an earlier alternative could begin with. A sequence cut at three tokens stood for whatever
followed it, so on `666c596c`, `rule R = A B C D | A B C E;` reported alternative 2, which the
parser reaches on `a b c e`: the first alternative fails at `e` and ordered choice backtracks. A
reference cut by recursion then carried on as if matched: with `rule P = A P B | C;`, `P B D` claimed
`A B B` and so covered `A B B D`, which it never matches. Raising k moves the cut, it does not remove
it. The example in `backlog/general.md` names a `MemberDeclaration` production that has since
changed, so these were reproduced on fresh grammars.

**Now.** `AlternativeShadowing` reports a later alternative only on proof. It lists the token
prefixes the later alternative can begin with, each marked complete, cut at the current depth, or
stopped where the pass does not follow it -- a recursive reference, an inline block's arms, a
character class, a fragment. For each prefix it runs the earlier alternatives as the generated
parser does, over three outcomes -- succeeds, fails, or cannot tell. That covers ordered choice,
greedy optionals and repetitions, a `+` whose first round must consume, and predicates. A prefix is
covered when an earlier alternative succeeds on it whatever follows, and the alternative is reported
when every prefix is covered. A prefix left uncovered because it was cut, where an earlier
alternative read past its end, is tried one token deeper, up to 16 tokens and 256 prefixes. Past
either bound nothing is reported.

A token is taken to differ from another only when both are declared tokens of different names. A
literal may be the declared token that spells it (`";"` lowers to `SEMI` since the rule-literal
fix), and assuming otherwise can prove a guard holds where it fails. So `SEMI !";" | SEMI SEMI`
reports nothing. A guarded alternative covers a later one only where the later one's own tokens
decide the guard, and a guarded alternative is itself reported when covered, so the predicates
entry's "neither flags ... nor counts" no longer holds.

| Rule `R` | Before | After |
|---|---|---|
| `A B C D \| A B C E` | 2 | nothing |
| `A B C D E F \| A B C D E G` | 2 | nothing |
| `P B D \| A B B D`, `P = A P B \| C` | 2 | nothing |
| `Q D \| A B C E`, `Q = A B C` | 2 | nothing |
| `A B? C D E \| A B C D F` | 2 | nothing |
| `A B* B \| A B B` | 2 | nothing |
| `A C \| A B C \| A B* C` | 3 | nothing |
| `IDENT LT GT LP \| IDENT LT GT \| IDENT` | 2 | nothing |
| `A B \| A B C` | nothing | 2 |
| `A B? \| A C`, `A B? C \| A C D` | nothing | 2 |
| `A? \| B \| C` | nothing | 2, 3 |
| `A* \| B` | nothing | 2 |
| `A B C &D \| A B C D` | nothing | 2 |
| `A !B \| A C` | nothing | 2 |

Each sound report the old pass made in these tests is still made: duplicates,
`A C | A D | A (C | D)`, `A B* C | A B C D` and `Q | A B C D E` with `Q = A B C D`. The unsound
inline-block reports below are kept too. On Mist's grammar (135 rules, 364 alternatives) neither
pass reports anything; the new one takes about 300 ms where the old took about 6 s.

`AlternativeShadowingTests` holds it at Tier 1, over grammars built directly:

- 30 rows, each asserting the reported alternatives, the severity and the owning rule's line;
- a property of 300 cases checking every report against an interpreter of the generated parser's
  semantics, over every input of up to 7 tokens drawn from three;
- a property of 300 cases checking that alternatives of plain tokens are reported exactly when an
  earlier one is a prefix. The old pass fails it on its second case.

`SyntacticPredicateTests` asserted the old false positive: `IDENT LT GT LP | IDENT LT GT | IDENT`
reported alternative 2, which the parser reaches on `a<>;`. That row is now
`IDENT LT GT | IDENT LT GT LP | IDENT`, which the guarded form `IDENT LT GT &SEMI | ...` beside it
does not report.

Each of seventeen mutations of the pass is caught, as is putting the old pass back. Three -- an
undecided optional or choice given a known end, an undecided choice taken to fail -- survived until
the three rows where a literal may be the token after it were added.

**Unresolved, left as the specs have it:**

- Inline-rule-prefix requirement 5.3 compares arm tails by FIRST-k and is unchanged, so it keeps
  the truncation defect: arms whose tails share three tokens and differ at the fourth are reported.
- Requirement 5.4 treats an inline block, against its siblings, as its prefix clause. The pass
  keeps that, taking a block to win wherever its prefix matches, which is not proof: in
  `inline Prefix: { Arm1 as Alt1 } | A` with `Prefix = A` and `Arm1 = Prefix B`, the sibling `A` is
  reached on `a c` and reported. `InlineBlockReachabilityTests` asserts this report twice.
- `mist-compiler-architecture` requirement 7.2 says an unreachable alternative is an Error that
  fails the build. The pass emits a Warning, as before; the zero-warning policy is what fails a
  build today.

**Limits:** a proof needing more than 16 tokens is not found, so a duplicate of 17 tokens is not
reported. A later alternative with more than 256 prefixes at some depth is not reported. A literal
and a declared token never match each other, so `SEMI as A | ";" as B` still draws nothing. An
element after one whose end is unknown is taken to succeed only if it is written as an optional, a
repetition or a group of them, never through a rule reference.

## Left recursion behind a rule that can succeed empty -- 2026-09-28

`LeftRecursionPass` walked a sequence past an element only when the element was itself written to be
skippable -- `?`, `*` or a predicate -- and never through a rule reference. `rule Empty = A?;
rule R = Empty R | B;` drew nothing, and the emitted `_ParseR` calls `_ParseEmpty()` and then
`_ParseR()` with no position check: on `b` the first returns having consumed nothing, and a memo entry
is written only after a body returns, so nothing stops the second call. The pass also skipped inline
blocks, and a qualified reference became an edge to a name that was never a node, so a cycle through
one never closed.

**Now.** `LeftCallGraph` finds the rules that may succeed without consuming, as a least fixed point
over the rule declarations, and walks each body only that far: a sequence while its elements may be
empty, every alternative of a choice, and the operand of `?`, `*`, `+` and a predicate. `E+` itself is
never empty, because the emitted loop counts only rounds that consume. A token consumes whatever its
pattern or mode action. An inline block is walked as the emitter writes it: the prefix rule's body,
then, if that may be empty, each arm rule's body with the prefix stripped as lowering strips it.
Neither rule is called, so neither is an edge. A block or arm `InlinePrefixVerification` would reject
contributes nothing, since that pass runs after this one, and a block met again while it is being
walked ends the walk. References resolve through `StormGrammar.LookupByName`, rules are compared by
reference, and a namespaced rule appears in a cycle path under its qualified name.

| Grammar (tokens `A`, `B`) | Before | After |
|---|---|---|
| `Empty = A?; R = Empty R \| B`, in either order | nothing | `R -> R` |
| `R = inline H: { x:X as Alt } \| B; H = A?; X = H R` | nothing | `R -> R` |
| `R = Ns.Wrap \| B`, with `Wrap = R` imported as `Ns` | nothing | `R -> Ns.Wrap -> R` |
| `Foo = B; R = Ns.Foo`, with `Foo = Foo A` imported as `Ns` | `Foo -> Foo` | nothing |

Each row was run through `StormGrammar.Build` on `43d164d0`, before and after. On Mist's grammar (135
rules, 157 calls made before consuming, two inline blocks) the pass reports nothing, in about 3 ms.

`NullableLeftRecursionTests` holds it at Tier 2, over declarations built by hand and a lookup callback:
37 cases. They refuse recursion behind one or several empty rules in either declaration order, mutual
recursion, rules empty only through each other, every operand wrapper and label, qualified references,
inline prefixes, stripped and spelled-out arm tails and nested blocks. They accept recursion after a
consumed token, after `+` of an empty rule, and after a token with a mode action whose pattern matches
empty. Each of nineteen mutations of the pass is caught; removing the re-entry guard is caught by the
test host dying of a stack overflow.

**Unresolved:**

- The last row. `LookupByName` sends the namespaced file's unqualified `Foo` to the root rule, which
  consumes, so the pass sees no cycle. Lowering keys rules by simple name, so it merges both bodies into
  one `Foo` and lists that rule twice; the old pass keyed rules by name too, which is why it reported.
  Neither version yields a compilable parser: every namespaced rule is lowered under both its simple
  and its qualified name, and the parser emits `_ParseNs.Wrap()` (run on `43d164d0`).
- The arm test copies `InlinePrefixVerification`'s: a first element naming the prefix, or leading
  elements shaped like the prefix body's over references, literals, `?`, `*` and `+`. Should that pass
  come to accept more, the arms it adds are skipped here, which misses a call rather than inventing one.
- `mist-compiler-architecture`'s design table gives left recursion `STORM0004`. The code has
  `STORM_LEFT_RECURSION` as `STORM0005`, and `STORM0004` is an undefined reference. Unchanged.

**Limits:** the walk ignores what ordered choice and an inline block's prediction rule out, so a
recursive alternative hidden by an earlier one that always succeeds is still refused, as it was
before. A predicate is taken to succeed.

## Children() yields each node a node holds once -- 2026-10-03

The four residues "One recursive emitter" deferred until classic was sunset, and a fifth found while
closing them, each reproduced on `4848cb01` with Mist's grammar unchanged:

- A labelled reference under an optional was yielded without a guard, so a type declaration without
  `baseClause:(COLON baseList:BaseList)?` and an enum without `typeAnnotation:(COLON type:TypeReference)?`
  each had a null child: 1,027 over the 592 of the repository's 606 `.mist` files that parse.
- The outer label held nothing when the reference inside it had a label of its own: `class Derived :
  Plain, IOther { }` had its `BaseList` and a null `BaseClause`, and `enum Sized : byte { A, B }`
  printed as `enum Sized {`. An unlabelled `(COLON inner:X)?` left its own property null the same way.
- Where an outer label did forward the node, `Children()` yielded it twice: `ConstraintClause` beside
  `Group0.Constraints` on an associated type slot (20 times over the same files), and `held:(inner:X)?`.
- A group struct was walked only for its scalar node fields. `type Bound<TA, TB> : IStringable where
  TA : struct where TB : class;` never yielded its `where` clauses, which had no `Parent` and no
  `Origin`; nor were the nodes of a list or nullable field, or of a nested group.
- A rule whose alternatives carry no names builds one node type with the first alternative's
  properties, and a later alternative fills only those it declares under the same name and type. One
  it left unset was yielded anyway: `R = a:Elem COMMA | SEMI | LP (COMMA a:Elem)*` gave a null child
  on `;` and on `(`. Mist's grammar has no such rule.

**Now.** A label on an optional or repeated group forwards the node of the capture inside it -- the
reference's own label or a group struct's field -- and only that capture yields it, so `BaseClause`,
`TypeAnnotation` and `ConstraintClause` hold their node without being children; an unlabelled optional's
property around a labelled reference forwards it the same way. A labelled reference a repetition
overwrites keeps its list as the capture that holds every node, as before. A property an alternative may
leave unset keeps its non-nullable type, and `Children()` guards it: its element lies under an optional or
a `*` that may run no rounds, or a later alternative of a shared node does not certainly fill it. A later
alternative fills one of the first alternative's children unless its own owner of that node fills a child
too, and a forwarding label only beside the same owner, so the node it builds yields each node once;
`NodeSlots` and the guard read those fills from one place, `FilledProperties`. `Children()` walks a group
struct to every depth in the group's grammar order -- scalar, nullable and list node fields, and nested
groups by their modifier, a list only when it is not default -- from `AstEmitter.Children.cs`. No
generated property changes name or type.

For Mist only three `Children()` bodies change (`TypeBodyDeclarationNode`,
`AssociatedTypeSlotDeclarationNode`, `EnumDeclarationNode`), and the parser only fills `BaseClause` and
`TypeAnnotation`, putting them back with their optional; the lexer and runtime are byte-identical. Over
the 592 files the walk reaches the same 325,492 nodes with no null and no duplicate, every node a node
holds among its children once, in source order, with its parent and origin set. No tracked file writes a
slot's `where` clause, so that count cannot see the fourth residue; the tests below do. Those clauses now
carry an origin, so Stage 5 judges the type arguments written in them, which `IsInSource` skipped before.
`MistPrettyPrinter` prints `enum Sized : byte` with no change of its own.

The following hold it:

- `NodeSlotTests` (Tier 1, 19 cases), including eight rows on what a later unnamed alternative fills,
  which nothing held before;
- `NodeChildrenTests` (Tier 2, 19 rows), the emitted `Children()` of each shape;
- `GeneratedParserFidelityTests` (Tier 4), still 21 cases (27 parses): `Held` now carries an inner label,
  and `Nest`'s nested group a node beside its token, which its fact walks and parent-links;
- Mist's `GeneratedAstFidelityTests` (Tier 3, 3 cases), over Mist's grammar: every node a node holds,
  read off its properties and group structs, is one of its children once and in source order, with its
  parent and origin set; the outer labels hold their inner label's node; an enum's underlying type
  survives printing.

Each of twenty-four mutations is caught. Storm `56945a87`, `116211dc` and `e818f145`, the last after a
review found the shared-node roles, `(inner:X)?` and the default array; StormLang `main` at `fe7ddcd1`.

**Residue:**

- A forwarding label is not walked: a node built by hand with only `BaseClause`, `TypeAnnotation` or
  `ConstraintClause` set yields nothing for it. Mist's hand-built test trees set the inner label, alone
  or with the outer one.
- `Children()` follows the properties' declaration order -- labels, then groups, then unlabelled
  elements -- so `R = A x:B` yields `B` first, and `x:X (a:Y b:Z)* y:W` yields `y` before the group's
  nodes. A group's order is the grammar's, and every node in the 592 files is in source order.
- A later unnamed alternative fills only what the first declares, and only where each node stays yielded
  once: on `(,x`, `R = SEMI held:(COMMA inner:Elem)? | LP held:(COMMA Elem)?` parses `x` and drops it, as
  it drops a capture the first alternative does not declare. Where two alternatives' anonymous groups
  share a `Group{N}` name but not their fields, the parser does not compile (CS0117 on
  `R = (a:Elem b:Elem)* SEMI | LP (c:Elem d:Elem)*`); that predates this change.
- An inline block's arm builds its node from the prefix rule's elements, which the guard does not see. An
  arm rule that names the prefix rule (`Arm1 = head:Prefix tail:C`) or labels its elements differently
  (`T = b:Elem SEMI c:Elem` under `inline P:` with `P = a:Elem SEMI`) leaves that property unset, so
  `Children()` yields null and the prefix's node is held by nothing. That predates this change; Mist's
  two inline blocks repeat the prefix's labels.
- A labelled optional of two references nested in another optional is not projected as a struct. Its
  label now forwards the first reference, where it held the last unlabelled one, and the other gets a
  property of its own.
