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

A label applied to an element *inside* a `( ... )*` group makes the generator emit **both**
the expected list property and a bogus scalar property. The scalar is populated with the
last value parsed by the loop, and it is yielded from `Children()`.

```storm
rule ArrayElementList = first:Expression (COMMA rest:Expression)* COMMA?;
```

Generated AST — note `Rest` alongside `Expressions`:

```csharp
public ExpressionNode First { get; set; } = default!;
public ExpressionNode Rest { get; set; } = default!;   // spurious
public ImmutableArray<ExpressionNode> Expressions { get; set; } = ImmutableArray<ExpressionNode>.Empty;

public override IEnumerable<SyntaxNode> Children()
{
    yield return First;
    yield return Rest;                                 // unconditional
    foreach (var child in Expressions) { yield return child; }
}
```

Generated parser — one local declared outside the loop and reassigned each iteration, then
assigned to the property:

```csharp
ExpressionNode _Rest = null!;          // outside the while
while (!_hasError)
{
    ...
    _Rest = _ParseExpression();        // overwritten every pass
    ...
}
node.Rest = _Rest;                     // ends up as the LAST element, or null
```

**Symptoms:**

- Zero iterations — `[1]` yields `Children() == [First, null]`. Any walker that does not
  null-check throws.
- Multiple iterations — `[1, 2, 3]` yields `[1, 3, 2, 3]`. The last element is duplicated
  and out of order.

**Why it has gone unnoticed:** nothing reads the scalar, and every tree walker in the
repository is already null-tolerant because the defect is pervasive — a walker that
recurses over `Children()` has to guard each child or it throws on the zero-iteration
case. Union detection likewise cannot use `Rest` as a signal, because the scalar is a
non-nullable codegen artifact rather than a real parse result; the PIPE-separated tail
collection is the only trustworthy indicator. Both workarounds predate this note, which is
why the bug survived: the tooling that would have exposed it was written around it
instead.

**Scope — every rule with a label inside a repetition:**

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
| `VarTupleDeconstruction` | `(COMMA restNames:IDENTIFIER)*` |

`TypeReference` is the worst case: a non-union type reference leaves the scalar null, so
practically every type reference in every program yields a null child.

**Fix:** the generator should not emit a scalar property for a label inside a repetition —
the label should only contribute to the list, and `Children()` should not yield it.

**Workaround (verified):** drop the label. `ArrayElementList` was changed from
`(COMMA rest:Expression)*` to `(COMMA Expression)*`; the scalar disappeared, the list kept
its `Expressions` name, `Children()` became correct, and nothing needed re-threading
because nothing read the scalar. Storm names the list from the rule name regardless, which
is why the unlabelled `ArgumentList` and `TypeArgumentList` never had the defect.

**Before removing labels elsewhere,** check per rule whether any consumer reads the scalar
believing it meaningful — for the operator-chain rules a `.Right` read would compile and
silently mean "last operand", which would be a live bug rather than dead weight.

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
