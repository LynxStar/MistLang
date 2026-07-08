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


## Unify BaseTypeRef into TypeReference

The `BaseList` rule currently uses a bespoke `BaseTypeRef` node that only accepts `IDENTIFIER typeArgs?`:

```storm
rule BaseList = first:BaseTypeRef (COMMA rest:BaseTypeRef)*;
rule BaseTypeRef = name:IDENTIFIER typeArgs:TypeArgumentList?;
```

This forces `BaseTypeResolver` to do its own arity-aware lookup path, duplicating logic that `TypeReferenceResolver.ResolveNamed` already handles for member type references. It also prevents qualified names in base clauses without extra work.

**Proposed change:**

```storm
rule BaseList = first:TypeReference (COMMA rest:TypeReference)*;
```

Drop `BaseTypeRef` entirely. Base clauses accept a full `TypeReference` and invalid forms are rejected semantically:

- Union types in base position → error: "cannot inherit from a union type"
- Tuple types in base position → error: "cannot inherit from a tuple type"
- Array types in base position → error: "cannot inherit from an array type"
- Primitives in base position → error: "cannot inherit from a primitive type"

**Why:**
- Eliminates the parallel resolution path in `BaseTypeResolver` (arity lookup, type arg resolution)
- Base clauses get qualified names, generics, and all type reference features uniformly
- Semantic errors are more informative than syntax errors ("cannot inherit from `int | string`" vs "expected identifier")
- Simplifies `BaseTypeResolver` to just call `typeResolver.Resolve(baseTypeRef, scope)` and validate the result

**Impact:**
- Grammar change (replaces `BaseTypeRef` with `TypeReference` in `BaseList`)
- `BaseTypeResolver` simplifies significantly (delegates to `TypeReferenceResolver`)
- Tests that construct `BaseTypeRefNode` directly need updating
- New semantic diagnostics for invalid base type forms


## Unify RefinementPredicate into AndPattern

The `RefinementPredicate` rule on type alias declarations and the `AndPattern` used in adhoc `where` clauses represent the same semantic structure (a list of `UnaryPattern` nodes joined by `and`) but parse into different node types:

```storm
rule RefinementPredicate
    = AND first:UnaryPattern (AND rest:UnaryPattern)*   as CompoundRefinementPredicate
    | predicate:AndPattern                              as SimpleRefinementPredicate
    ;

rule WhereClause
    = WHERE name:IDENTIFIER COLON constraints:ConstraintList  as GenericConstraintClause
    | WHERE name:IDENTIFIER predicate:AndPattern              as AdhocRefinementConstraint
    ;
```

In practice `SimpleRefinementPredicate` just wraps an `AndPattern` — they carry the same data. The `CompoundRefinementPredicate` variant (with a leading `AND` token) is unclear when it fires given normal syntax like `type Foo = int >= 0 and <= 100;` (the `>=` is not an `AND` token, so it falls into `SimpleRefinementPredicate`).

**Proposed change:**

```storm
rule RefinableSingleTypeExpr = type:SingleTypeRef refinement:AndPattern?;
```

Drop `RefinementPredicate` entirely. The refinement slot on a type alias accepts a direct `AndPattern`, identical to what adhoc where clauses use.

**Why:**
- One node type for predicates everywhere (no unwrapping `SimpleRefinementPredicate` to get to `AndPattern`)
- Simplifies ProgramModel code — `RefinedType.PredicateNode` can be typed as `AndPatternNode` instead of `SyntaxNode`
- `TypeParameter.RefinementNode` and `Parameter.RefinementNode` already use `AndPatternNode` — this makes them all uniform
- Eliminates a seemingly unreachable grammar alternative (`CompoundRefinementPredicate`)

**Impact:**
- Grammar change (removes `RefinementPredicate` rule, replaces with `AndPattern?` on `RefinableSingleTypeExpr`)
- Need to verify `CompoundRefinementPredicate` is truly unreachable or identify what syntax triggers it
- `TypeAliasBuilder`, `MistPrettyPrinter`, and tests that match on `RefinementPredicateNode` need updating
- `RefinedType.PredicateNode` tightens from `SyntaxNode` to `AndPatternNode`
