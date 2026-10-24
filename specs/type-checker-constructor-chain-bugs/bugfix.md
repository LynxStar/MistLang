# Bugfix Requirements Document

## Introduction

The Mist compiler's type checker (`TypeCheckerAnalyzer`) has two related bugs that surface when compiling the AllFeatures sample project. The first bug causes a false positive `MIST_TC_0013` error that incorrectly reports "No constructor on 'Color' matches the chained arguments" when checking Point's constructor chain `Point() : this(0, 0)`. The second bug causes all type checker diagnostics to lack file path and accurate line/column information, making it difficult for users to locate errors in multi-file projects.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a constructor uses chaining (e.g., `Point() : this(0, 0)`) and the `CheckConstructorChain` method calls `FindEnclosingType(ctorSymbol)` THEN the system returns the first class/struct type found by iterating `scope.Members` at each scope level, which may be a different type (e.g., `Color`) rather than the actual declaring type (`Point`), because `FindEnclosingType` does not verify that the found type's `MemberScope` matches the constructor symbol's `ContainingScope`

1.2 WHEN the wrong enclosing type is resolved in `CheckConstructorChain` THEN the system emits a false positive `MIST_TC_0013` error (e.g., "No constructor on 'Color' matches the chained arguments") because it searches for matching constructors on the wrong type's member scope

1.3 WHEN the type checker emits any diagnostic via `MakeDiagnostic` THEN the system always passes `CompilationUnitPath: null` in the resulting `Diagnostic` record, so the error output cannot indicate which source file contains the error

1.4 WHEN the type checker emits any diagnostic via `MakeDiagnostic` THEN the system calculates the location as `new LinePosition(0, span.Start)` and `new LinePosition(0, span.End)`, which always reports line 0 and uses the raw character offset as the column, instead of computing the correct 1-based line and column from the source text

### Expected Behavior (Correct)

2.1 WHEN a constructor uses chaining (e.g., `Point() : this(0, 0)`) and the `CheckConstructorChain` method resolves the enclosing type THEN the system SHALL return the type whose `MemberScope` matches the constructor symbol's `ContainingScope`, ensuring the correct declaring type is identified (e.g., `Point` for Point's constructor)

2.2 WHEN `CheckConstructorChain` resolves the correct enclosing type and searches for matching constructors THEN the system SHALL NOT emit `MIST_TC_0013` if a matching constructor exists on the actual declaring type (e.g., `Point(int, int)` matches the chained arguments `(0, 0)`)

2.3 WHEN the type checker emits a diagnostic via `MakeDiagnostic` THEN the system SHALL include the correct source file path in the `CompilationUnitPath` field of the `Diagnostic` record

2.4 WHEN the type checker emits a diagnostic via `MakeDiagnostic` THEN the system SHALL compute the correct 1-based line and column numbers from the node's span using the source text's offset-to-line-position conversion, rather than hardcoding line 0

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a constructor chain genuinely has no matching constructor on the declaring type THEN the system SHALL CONTINUE TO emit `MIST_TC_0013` with the correct type name and error message

3.2 WHEN `new Type(args)` object creation expressions are type-checked via `CheckObjectCreation` THEN the system SHALL CONTINUE TO correctly match constructors and emit `MIST_TC_0011` when no match is found

3.3 WHEN the type checker checks method bodies, return types, operator types, access modifiers, and other non-constructor-chain rules THEN the system SHALL CONTINUE TO emit the correct diagnostics (MIST_TC_0001 through MIST_TC_0018) with unchanged behavior

3.4 WHEN the lexer or parser emits diagnostics with file paths and locations THEN the system SHALL CONTINUE TO pass those diagnostics through unchanged

3.5 WHEN `FindDeclaringType` is called from `CheckMethodBody` or `CheckConstructorBody` to set `_currentEnclosingType` THEN the system SHALL CONTINUE TO correctly resolve the declaring type using its existing scope-matching logic
