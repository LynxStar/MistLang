# Bugfix Requirements Document

## Introduction

The Mist parser exhibits exponential backtracking when parsing `TypeArgumentList` nodes due to the `TypeReference` rule trying `UnionTypeRef` as its first alternative. For every non-union type reference (the vast majority of cases), the parser speculatively parses a full `SingleTypeRef` including nested `TypeArgumentList`s, fails to find a `PIPE` token, discards the entire parse, then re-parses the same content via later alternatives. Since `TypeArgument` contains `TypeReference` recursively, this doubling compounds exponentially (~2.3x–3.5x per additional type argument), making it impossible to define types with more than ~10 type arguments within reasonable time.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a source file contains type references with many type arguments (e.g., `Func<T1, T2, ..., T16, TResult>`) THEN the parser exhibits exponential time growth (~2.3x–3.5x per additional type argument) due to speculative `UnionTypeRef` parsing and full backtracking

1.2 WHEN parsing a non-union `TypeReference` that contains nested `TypeArgumentList`s THEN the parser parses the full type as a `SingleTypeRef` inside `UnionTypeRef`, fails to find a `PIPE` token, discards the entire parse result, and re-parses the same content from scratch via later alternatives

1.3 WHEN a source file contains arity-overloaded type declarations with corresponding type aliases (e.g., 16 `Func` variants + 16 `Action` aliases) THEN parsing takes exponentially longer with each additional arity level (33 seconds at N=11, estimated ~5 hours at N=16)

### Expected Behavior (Correct)

2.1 WHEN a source file contains type references with many type arguments (e.g., `Func<T1, T2, ..., T16, TResult>`) THEN the parser SHALL parse them in linear time relative to the number of type arguments, completing N=0 through N=16 in under 10 seconds total

2.2 WHEN parsing a non-union `TypeReference` THEN the parser SHALL parse exactly one `SingleTypeRef` and then optionally consume `PIPE`-separated continuations without any speculative backtracking

2.3 WHEN a source file contains arity-overloaded type declarations with corresponding type aliases THEN the parser SHALL handle all arity levels (0 through 16) without exponential time growth

### Unchanged Behavior (Regression Prevention)

3.1 WHEN parsing a union type reference (e.g., `int | string`) THEN the system SHALL CONTINUE TO produce a correct parse tree representing the union of constituent types

3.2 WHEN parsing a simple named type reference (e.g., `List<int>`) THEN the system SHALL CONTINUE TO produce a correct parse tree with the type name and type arguments

3.3 WHEN parsing a primitive type reference (e.g., `int`, `string[]`, `bool?`) THEN the system SHALL CONTINUE TO produce a correct parse tree with array rank and optional suffixes

3.4 WHEN parsing a void type reference THEN the system SHALL CONTINUE TO produce a correct `VoidTypeRef` node

3.5 WHEN parsing an unknown type reference THEN the system SHALL CONTINUE TO produce a correct `UnknownTypeRef` node

3.6 WHEN parsing a tuple type reference (e.g., `(int, string)`) THEN the system SHALL CONTINUE TO produce a correct parse tree with all tuple elements

3.7 WHEN parsing a parenthesized type reference (e.g., `(int | string)[]`) THEN the system SHALL CONTINUE TO produce a correct parse tree with the inner type and array rank

3.8 WHEN parsing type references with type parameter declarations (e.g., `class Foo<T1, T2>`) THEN the system SHALL CONTINUE TO parse type parameter lists correctly without exponential growth

3.9 WHEN all existing 2979 tests are run after the fix THEN the system SHALL CONTINUE TO pass all tests (accounting for updated node type names in test assertions)

3.10 WHEN the AllFeatures sample is compiled and run after the fix THEN the system SHALL CONTINUE TO produce correct output
