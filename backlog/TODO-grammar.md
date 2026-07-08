# Grammar Backlog

## Unify DependentTypeParameter alternatives into a single rule with TypeReference

The `TypeParameter` rule currently splits dependent parameters into two alternatives based on whether the constraint is a primitive keyword or an identifier:

```storm
rule TypeParameter
    = name:IDENTIFIER COLON valueType:PrimitiveType           as DependentTypeParameter
    | name:IDENTIFIER COLON constraintName:IDENTIFIER         as NamedConstraintDependentTypeParameter
    | name:IDENTIFIER                                          as RegularTypeParameter
    ;
```

This is a grammar-level workaround for a problem that should be solved semantically. Both alternatives represent the same concept: a dependent type parameter with a type constraint. The distinction between a primitive keyword and a user-defined refinement type is a resolution concern, not a parsing concern.

**Proposed change:**

```storm
rule TypeParameter
    = name:IDENTIFIER COLON constraint:TypeReference   as DependentTypeParameter
    | name:IDENTIFIER                                   as RegularTypeParameter
    ;
```

**Why:**
- Eliminates a redundant AST node variant (`NamedConstraintDependentTypeParameter`)
- Allows qualified names, generics, or any valid type reference shape as a constraint
- Moves validation to the semantic layer where it belongs
- Simplifies downstream pattern matching (one dependent variant instead of two)

**Impact:**
- Grammar change (removes one alternative from `TypeParameter`)
- `SemanticModelBuilder` constraint resolution needs adjustment (already does type lookup)
- `ProgramModel` construction simplifies (`AddTypeParameter` handles one dependent case)
- Pretty-printer, emitter, and tests that match on the old node type need updating
