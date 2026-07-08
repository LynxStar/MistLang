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
