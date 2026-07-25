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

**Why it has gone unnoticed:** nothing reads the scalar, and tree walkers are already
null-tolerant because the defect is pervasive. `SampleFingerprint.Collect` has an explicit
`if(child is not null)` guard, and `RecordSyntheticTypeFeatures` in the same tool already
documents the artifact by name: *"Rest is a non-nullable codegen artifact and is NOT a
valid union signal."* So the workaround is already load-bearing in tooling.

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
