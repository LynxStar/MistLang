# Type Checker Constructor Chain Bugs — Bugfix Design

## Overview

The Mist compiler's `TypeCheckerAnalyzer` has two bugs that surface during constructor chain validation. First, `CheckConstructorChain` calls `FindEnclosingType` which walks the scope chain and returns the first class/struct it encounters without verifying scope ownership, causing false positive `MIST_TC_0013` errors when multiple types exist in the same namespace. Second, `MakeDiagnostic` hardcodes `CompilationUnitPath: null` and uses raw offsets as column numbers with line 0, so all type checker diagnostics lack file paths and have incorrect line/column information.

The fix reuses the existing `FindDeclaringType` method (which already performs correct scope matching) for constructor chain resolution, and enhances `MakeDiagnostic` to accept the `ISemanticModel` so it can resolve the source file path and convert offsets to proper 1-based line/column positions via `SourceText.ToLinePositionSpan`.

## Glossary

- **Bug_Condition (C)**: The conditions that trigger each bug — (1) `CheckConstructorChain` calling `FindEnclosingType` instead of `FindDeclaringType`, causing wrong type resolution; (2) `MakeDiagnostic` producing diagnostics with null file path and incorrect line/column
- **Property (P)**: The desired behavior — (1) constructor chain validation resolves the correct declaring type; (2) diagnostics include accurate file path and 1-based line/column positions
- **Preservation**: Existing behavior that must remain unchanged — genuine `MIST_TC_0013` errors for truly mismatched constructor chains, all other diagnostic rules (MIST_TC_0001–0018), `CheckObjectCreation` constructor matching, and `FindDeclaringType` usage in `CheckMethodBody`/`CheckConstructorBody`
- **`FindEnclosingType`**: The buggy static method at line 1671 in `TypeCheckerAnalyzer.cs` that walks the scope chain and returns the first class/struct without verifying `MemberScope` matches `ContainingScope`
- **`FindDeclaringType`**: The correct static method at line 175 in `TypeCheckerAnalyzer.cs` that walks the parent scope and verifies `MemberScope` matches the member's `ContainingScope` via `ReferenceEquals`
- **`MakeDiagnostic`**: The static helper at line 1689 in `TypeCheckerAnalyzer.cs` that creates `Diagnostic` records for all type checker errors
- **`SourceText.ToLinePositionSpan`**: Method on `SourceText` that converts an offset-based `SourceSpan` to a 1-based `LinePositionSpan` using binary search over cached line starts

## Bug Details

### Bug Condition

The bug manifests in two related ways:

**Bug 1 — Wrong enclosing type resolution**: When `CheckConstructorChain` (line 1077) calls `FindEnclosingType(ctorSymbol)` (line 1105), the method walks `scope.Members` at each scope level starting from the constructor's `ContainingScope` and returns the first symbol whose `Kind` is `Class` or `Struct`. In a namespace with multiple types (e.g., `Point` and `Color` in `AllFeatures`), the iteration order of `scope.Members` determines which type is returned. If `Color` appears before `Point` in the member list, `FindEnclosingType` returns `Color` even when checking `Point`'s constructor chain. The correct `FindDeclaringType` method (line 175) avoids this by checking `ReferenceEquals(memberScope, typeScope)` to verify the type's `MemberScope` matches the constructor's `ContainingScope`.

**Bug 2 — Missing file path and wrong location**: When `MakeDiagnostic` (line 1689) creates a `Diagnostic`, it always passes `CompilationUnitPath: null` and computes location as `new LinePosition(0, span.Start)` / `new LinePosition(0, span.End)`. This means line is always 0 and column is the raw byte offset, rather than the correct 1-based line and column. The `SourceText` class already provides `ToLinePositionSpan(SourceSpan)` for proper conversion, and the `ISemanticModel` exposes `Units` and `Asts` which can be used to find the source file containing a given syntax node.

**Formal Specification:**
```
FUNCTION isBugCondition_WrongType(input)
  INPUT: input of type (ctorSymbol: ISymbol, model: ISemanticModel)
  OUTPUT: boolean

  // Bug triggers when CheckConstructorChain calls FindEnclosingType
  // and the constructor's declaring type is NOT the first class/struct
  // encountered when iterating scope.Members from ContainingScope upward
  LET enclosingType = FindEnclosingType(ctorSymbol)
  LET declaringType = FindDeclaringType(ctorSymbol)
  RETURN enclosingType != declaringType
         AND ctorSymbol has a constructor chain (: this(...))
END FUNCTION

FUNCTION isBugCondition_BadLocation(input)
  INPUT: input of type (diagnosticNode: SyntaxNode, model: ISemanticModel)
  OUTPUT: boolean

  // Bug triggers for ALL diagnostics emitted by MakeDiagnostic
  RETURN diagnosticNode.Span.Length > 0
         AND MakeDiagnostic is called for this node
END FUNCTION
```

### Examples

- **False positive MIST_TC_0013**: `Point() : this(0, 0)` — `FindEnclosingType` returns `Color` (first type in namespace scope), so the analyzer searches `Color`'s constructors for a match to `(int, int)`. `Color` has `Color(int, int, int)` which doesn't match, producing a false error: "No constructor on 'Color' matches the chained arguments". Expected: no error, because `Point(int, int)` exists and matches.
- **Correct MIST_TC_0013 (should still fire)**: If `Point` had `Point() : this(0, 0, 0)` but no 3-parameter constructor, the error should fire with "No constructor on 'Point' matches the chained arguments".
- **Missing file path**: Any type checker diagnostic (e.g., MIST_TC_0001 assignment error) reports `CompilationUnitPath: null` instead of the actual file path like `samples/AllFeatures/Point.mist`.
- **Wrong line/column**: A diagnostic for a node at offset 350 in a file reports `LinePosition(0, 350)` instead of the correct position like `LinePosition(15, 5)` (line 15, column 5).

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Genuine `MIST_TC_0013` errors must continue to fire when a constructor chain truly has no matching constructor on the declaring type
- `CheckObjectCreation` (MIST_TC_0011) must continue to correctly match constructors for `new Type(args)` expressions
- All other type checker diagnostics (MIST_TC_0001 through MIST_TC_0018) must continue to fire with unchanged logic
- `FindDeclaringType` usage in `CheckMethodBody` and `CheckConstructorBody` for setting `_currentEnclosingType` must remain unchanged
- Lexer and parser diagnostics with their own file paths and locations must pass through unchanged
- The `AnalyzerRunner` crash-handling diagnostic (which uses `CompilationUnitPath: null`) is unaffected

**Scope:**
All inputs that do NOT involve constructor chain checking or diagnostic location computation should be completely unaffected by this fix. This includes:
- Method body type checking
- Field declaration checking
- Entry point validation
- Object creation expression checking
- Operator type checking
- Assignment type checking

## Hypothesized Root Cause

Based on the bug description and code analysis, the root causes are:

1. **`FindEnclosingType` lacks scope verification (Bug 1)**: The method at line 1671 iterates `scope.Members` and returns the first `Class`/`Struct` symbol it finds. It does not check whether the found type's `MemberScope` is the same scope that contains the constructor. When multiple types exist in the same namespace scope, the iteration order is non-deterministic from the caller's perspective, and the wrong type can be returned. The correct `FindDeclaringType` method at line 175 already solves this by checking `ReferenceEquals(memberScope, typeScope)`.

2. **`CheckConstructorChain` calls the wrong method**: Line 1105 calls `FindEnclosingType(ctorSymbol)` instead of `FindDeclaringType(ctorSymbol)`. This is likely a copy-paste or oversight — `CheckConstructorBody` (line 244) correctly calls `FindDeclaringType`, but `CheckConstructorChain` uses the buggy variant.

3. **`MakeDiagnostic` has no access to source text (Bug 2)**: The method signature `MakeDiagnostic(string id, string message, SyntaxNode node)` does not accept the `ISemanticModel`, so it cannot look up which compilation unit contains the node or access the `SourceText` for offset-to-line conversion. It was written as a minimal helper that hardcodes `null` for the file path and uses raw offsets.

4. **Incorrect `LinePosition` construction**: Even for the location it does compute, `new LinePosition(0, span.Start)` treats the offset as a column on line 0. `LinePosition` is documented as 1-based, so line 0 is invalid. The correct approach is to use `SourceText.ToLinePositionSpan(sourceSpan)` which performs binary search over line starts.

## Correctness Properties

Property 1: Bug Condition — Constructor Chain Resolves Correct Declaring Type

_For any_ constructor symbol with a constructor chain (`: this(...)`) in a compilation with multiple types in the same namespace, the fixed `CheckConstructorChain` method SHALL resolve the enclosing type by verifying that the type's `MemberScope` matches the constructor's `ContainingScope`, ensuring the correct declaring type is used for constructor matching.

**Validates: Requirements 2.1, 2.2**

Property 2: Bug Condition — Diagnostics Include Correct File Path and Location

_For any_ diagnostic emitted by `MakeDiagnostic` where the syntax node belongs to a compilation unit with a known source file, the fixed method SHALL populate `CompilationUnitPath` with the source file path and SHALL compute `Location` as a 1-based `LinePositionSpan` using `SourceText.ToLinePositionSpan`.

**Validates: Requirements 2.3, 2.4**

Property 3: Preservation — Genuine Constructor Chain Errors Still Fire

_For any_ constructor chain where the declaring type genuinely has no matching constructor for the chained arguments, the fixed code SHALL produce the same `MIST_TC_0013` diagnostic as the original code (with the correct type name), preserving error detection for truly invalid constructor chains.

**Validates: Requirements 3.1**

Property 4: Preservation — Non-Constructor-Chain Diagnostics Unchanged

_For any_ input that does NOT involve constructor chain checking (method bodies, field declarations, object creation, operators, assignments, entry points), the fixed code SHALL produce exactly the same set of diagnostics as the original code, preserving all existing type checking behavior.

**Validates: Requirements 3.2, 3.3, 3.4, 3.5**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`

**Change 1 — Replace `FindEnclosingType` call with `FindDeclaringType` in `CheckConstructorChain`**:
- In `CheckConstructorChain` (line 1105), replace `FindEnclosingType(ctorSymbol)` with `FindDeclaringType(ctorSymbol)`
- `FindDeclaringType` already exists at line 175 and correctly verifies `ReferenceEquals(memberScope, typeScope)`
- This is a single-line change that reuses proven logic

**Change 2 — Remove `FindEnclosingType` method**:
- Delete the `FindEnclosingType` method (lines 1671–1683) since it is no longer called anywhere
- This eliminates the buggy code path entirely

**Change 3 — Update `MakeDiagnostic` signature to accept `ISemanticModel`**:
- Change the signature from `MakeDiagnostic(string id, string message, SyntaxNode node)` to `MakeDiagnostic(string id, string message, SyntaxNode node, ISemanticModel model)`
- The model provides access to `Units` (compilation units with `SourceText`) and `Asts` (AST roots)

**Change 4 — Implement file path resolution in `MakeDiagnostic`**:
- Use the node's span to determine which AST root contains it by checking span containment against each AST in `model.Asts`
- Once the AST index is found, retrieve the corresponding `CompilationUnit` from `model.Units` and get `Source.Path`
- Pass this path as `CompilationUnitPath` in the `Diagnostic` constructor

**Change 5 — Implement proper line/column conversion in `MakeDiagnostic`**:
- Convert the Storm `SyntaxNode.Span` to a Mist `SourceSpan`
- Call `sourceText.ToLinePositionSpan(sourceSpan)` to get a proper 1-based `LinePositionSpan`
- Use this as the `Location` in the `Diagnostic` constructor instead of the hardcoded `LinePosition(0, offset)` values

**Change 6 — Update all `MakeDiagnostic` call sites**:
- Every call to `MakeDiagnostic` throughout the file (approximately 20+ call sites) must pass the `model` parameter
- The `model` is already available at every call site since it is passed through the checking methods

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that compile multi-type Mist programs with constructor chaining and inspect the emitted diagnostics. Run these tests on the UNFIXED code to observe false positives and missing location info.

**Test Cases**:
1. **False Positive Test**: Compile `Point.mist` and `Color.mist` together; assert that `MIST_TC_0013` is NOT emitted for `Point() : this(0, 0)` (will fail on unfixed code — false positive fires)
2. **Wrong Type Name Test**: Compile the same files; if `MIST_TC_0013` fires, check that the error message references `Color` instead of `Point` (confirms wrong type resolution on unfixed code)
3. **Missing File Path Test**: Trigger any type checker diagnostic and assert `CompilationUnitPath` is non-null (will fail on unfixed code — always null)
4. **Wrong Line Number Test**: Trigger a type checker diagnostic and assert `Location.Start.Line > 0` (will fail on unfixed code — line is always 0)

**Expected Counterexamples**:
- `MIST_TC_0013` fires with message "No constructor on 'Color' matches the chained arguments" for `Point`'s constructor chain
- All diagnostics have `CompilationUnitPath == null` and `Location.Start.Line == 0`
- Possible causes confirmed: `FindEnclosingType` returns wrong type; `MakeDiagnostic` hardcodes null path and line 0

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition_WrongType(input) DO
  result := CheckConstructorChain_fixed(input)
  ASSERT enclosingType == FindDeclaringType(ctorSymbol)
  ASSERT no false MIST_TC_0013 emitted
END FOR

FOR ALL input WHERE isBugCondition_BadLocation(input) DO
  diagnostic := MakeDiagnostic_fixed(id, message, node, model)
  ASSERT diagnostic.CompilationUnitPath IS NOT NULL
  ASSERT diagnostic.Location.Start.Line >= 1
  ASSERT diagnostic.Location.Start.Column >= 1
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition_WrongType(input) DO
  // Single-type compilations, or constructor chains that genuinely fail
  ASSERT CheckConstructorChain_original(input).diagnosticIds
      == CheckConstructorChain_fixed(input).diagnosticIds
END FOR

FOR ALL input WHERE NOT isBugCondition_BadLocation(input) DO
  // Non-type-checker diagnostics (lexer, parser, semantic model builder)
  ASSERT originalDiagnostic.Id == fixedDiagnostic.Id
  ASSERT originalDiagnostic.Message == fixedDiagnostic.Message
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain (various type configurations, constructor signatures, argument lists)
- It catches edge cases that manual unit tests might miss (e.g., single-type namespaces, types with no constructors, empty argument lists)
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for single-type compilations and genuine constructor mismatches, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Genuine MIST_TC_0013 Preservation**: Compile a program where a constructor chain truly has no match (e.g., `Foo() : this(1, 2, 3)` with only `Foo(int)`) and verify the error still fires with the correct type name after the fix
2. **Single-Type Namespace Preservation**: Compile a program with only one type that has constructor chaining and verify behavior is identical before and after the fix
3. **Other Diagnostic Preservation**: Trigger various non-constructor-chain diagnostics (MIST_TC_0001 assignment, MIST_TC_0008 return type, MIST_TC_0011 object creation) and verify the same diagnostic IDs and messages are produced
4. **CheckObjectCreation Preservation**: Verify `new Type(args)` constructor matching continues to work correctly

### Unit Tests

- Test `FindDeclaringType` returns the correct type when multiple types exist in the same namespace scope
- Test `MakeDiagnostic` produces non-null `CompilationUnitPath` when a source file is available
- Test `MakeDiagnostic` produces correct 1-based line/column from `SourceText.ToLinePositionSpan`
- Test `MakeDiagnostic` gracefully handles nodes with zero-length spans (location should be null)
- Test `CheckConstructorChain` with matching constructor on correct type produces no diagnostic
- Test `CheckConstructorChain` with genuinely mismatched arguments produces `MIST_TC_0013`

### Property-Based Tests

- Generate random namespace configurations with 1–5 types, each with 0–3 constructors, and verify `FindDeclaringType` always returns the type whose `MemberScope` matches the constructor's `ContainingScope`
- Generate random constructor chains with varying argument counts and types, and verify `MIST_TC_0013` fires if and only if no matching constructor exists on the declaring type
- Generate random source text with known line breaks and verify `SourceText.ToLinePositionSpan` produces correct 1-based positions for arbitrary offsets

### Integration Tests

- Compile the full `AllFeatures` sample project (`Point.mist`, `Color.mist`, etc.) and verify zero false positive diagnostics from the type checker
- Compile a multi-file project with intentional type errors and verify each diagnostic includes the correct file path and line/column pointing to the actual error location
- Compile a single-file project with constructor chaining and verify identical behavior before and after the fix
