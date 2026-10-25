# Compiler Parameter Scoping Bugs — Bugfix Design

## Overview

Three related bugs in `SemanticModelBuilder.cs` cause silent failures during semantic model construction. Bug 1: only the first method parameter is registered in scope. Bug 2: field initializers cause the entire file's AST to be silently dropped. Bug 3: calling `string.Length()` crashes the semantic model builder. All three produce misleading downstream diagnostics instead of clear errors at the point of failure. The fix targets `SemanticModelBuilder.cs` with minimal, surgical changes to the parameter registration loop, field initializer resolution, and ambient method call resolution.

## Glossary

- **Bug_Condition (C)**: The set of inputs that trigger one of the three bugs — multi-parameter methods, field initializers, or `string.Length()` calls
- **Property (P)**: The desired behavior — all parameters in scope, field initializers parsed correctly, `Length` resolved without crash
- **Preservation**: Existing behavior for single-parameter methods, fields without initializers, and other string methods (`Contains`, `Substring`) that must remain unchanged
- **SemanticModelBuilder**: The class in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` that walks ASTs to build symbols, scopes, and types across two passes
- **Pass 1**: Registers user-defined types, fields, constructors, and methods in the symbol table
- **Pass 2**: Resolves type references, registers parameters and locals, and computes expression types
- **RegisterParameters**: The method in Pass 2 that iterates `ParameterListNode` children to register parameter symbols in the method scope
- **ResolveFieldDeclaration**: The method in Pass 2 that resolves a field's type reference and updates the field symbol
- **LookupMemberType**: The method that resolves member access (e.g., `.Length()`) on a target type by searching member scopes and the ambient type resolver

## Bug Details

### Bug Condition

The bugs manifest in three distinct scenarios during Pass 2 of semantic model construction. Each causes either incorrect scoping, silent AST loss, or a crash.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type MistSourceFile
  OUTPUT: boolean

  // Bug 1: Multi-parameter methods
  IF input contains a method or constructor with ParameterList
     AND ParameterList has more than one Parameter
     AND method body references any parameter beyond the first
  THEN RETURN true

  // Bug 2: Field with initializer
  IF input contains a FieldDeclaration
     AND FieldDeclaration has an initializer (EQUAL Expression)?
     AND the initializer is present (HasValue is true)
  THEN RETURN true

  // Bug 3: string.Length() call
  IF input contains a method call expression
     AND the target type is StringType
     AND the member name is "Length"
  THEN RETURN true

  RETURN false
END FUNCTION
```

### Examples

- **Bug 1**: `public Point(int x, int y) { this.x = x; this.y = y; }` — parameter `y` produces `MIST_UNDEFINED_IDENTIFIER` because only `x` is registered in the method scope
- **Bug 1**: `public int Add(int a, int b) { return a + b; }` — parameter `b` is not in scope, causing a type resolution failure on `b`
- **Bug 2**: `private int value = 0;` — the entire file's AST is silently dropped during Pass 2, causing all types in that file to be unregistered
- **Bug 2**: `private static int totalCreated;` (no initializer) — works correctly, confirming the bug is specific to the optional initializer group
- **Bug 3**: `int len = input.Length();` — the semantic model builder crashes silently when resolving `Length` on a string, dropping the file's AST
- **Bug 3**: `bool has = input.Contains("x");` — works correctly, confirming the bug is specific to `Length` and not all string methods

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Methods with exactly one parameter must continue to register that parameter correctly in the method body scope
- Field declarations without initializers (e.g., `private int value;`) must continue to parse and register correctly
- `string.Contains()` and `string.Substring()` calls must continue to resolve correctly via the ambient type resolver
- `Array.Length` access on array variables must continue to resolve correctly
- All types from successfully parsed files must continue to be registered in the semantic model
- Parameters with array types (e.g., `int[] values`) must continue to be registered correctly

**Scope:**
All inputs that do NOT involve multi-parameter methods, field initializers, or `string.Length()` calls should be completely unaffected by this fix. This includes:
- Single-parameter methods and constructors
- Fields without initializers
- Other ambient type method calls (`Console.WriteLine`, `Math.Abs`, `Convert.ToInt32`)
- All expression types, control flow, and variable declarations

## Hypothesized Root Cause

Based on code analysis of `SemanticModelBuilder.cs`:

1. **Bug 1 — Parameter Registration Loop**: The `RegisterParameters` method (line ~590) accesses `paramList.Parameter` for the first parameter, then iterates `paramList.Children()` looking for additional `ParameterNode` instances using `ReferenceEquals` to skip the first. The Storm source generator for the grammar rule `ParameterList = Parameter (COMMA Parameter)*` likely flattens the repeated `(COMMA Parameter)*` into the `Children()` collection, but the `paramList.Parameter` property only returns the first `Parameter`. The `Children()` iteration with `ReferenceEquals` check should work in theory, but the generated code may return the same node reference or the children may not include the repeated parameters as direct `ParameterNode` children — they may be wrapped in intermediate nodes from the `(COMMA Parameter)*` repetition.

2. **Bug 2 — Field Initializer Resolution**: The `ResolveFieldDeclaration` method (line ~516) resolves the field's type reference but does NOT resolve the initializer expression. The grammar defines `initializer:(EQUAL Expression)?` as an optional group. When the initializer is present, the `FieldDeclarationNode.Initializer` property has `HasValue = true`, but `ResolveFieldDeclaration` never calls `ResolveExpression` on the initializer's expression. The crash likely occurs because the expression within the optional group is visited by some other part of the resolution pass that doesn't expect it, or the `Initializer` property access itself triggers an issue when the optional group is present. The CSharpEmitter and PrettyPrinter both use `FindFieldInitializerExpression` (walking `Children()` to find `ExpressionNode`) to handle this — the semantic model builder needs the same approach.

3. **Bug 3 — string.Length() Resolution**: The `LookupMemberType` method correctly resolves `Length` on `StringType` via the ambient resolver. However, the crash occurs in `ResolvePostfixSuffix` for `MethodCallSuffixPostfixSuffixNode` — after looking up the member type, it calls `ResolveArgumentList` on `methodCall.Arguments`. For `Length()` which has no arguments, `methodCall.Arguments` is `null`. But the issue is that `Length` is registered as a method symbol with no parameters in `CSharpAmbientTypeResolver.BuildString()`. The crash may occur because the `ArgumentListNode` handling assumes at least one argument exists (the `ArgumentList = Expression (COMMA Expression)*` rule requires at least one expression), or there's a null reference when the argument list is null but the code path still tries to access it. Looking at the code, `ResolvePostfixSuffix` does check `if (methodCall.Arguments is not null)` before calling `ResolveArgumentList`, so the crash is likely elsewhere — possibly in how `LookupMemberType` interacts with the scope when `Length` is looked up, since `Length` is semantically a property (no parameters) but modeled as a method.

## Correctness Properties

Property 1: Bug Condition — All Method Parameters Registered in Scope

_For any_ method or constructor declaration with N parameters (N ≥ 2), the fixed `RegisterParameters` function SHALL register all N parameters in the method body scope, such that each parameter name is resolvable via `scope.Lookup(name)` and no `MIST_UNDEFINED_IDENTIFIER` diagnostic is produced when the parameter is referenced in the method body.

**Validates: Requirements 2.1**

Property 2: Bug Condition — Field Initializers Preserve AST

_For any_ field declaration that includes an initializer expression (e.g., `private int value = 0;`), the fixed `ResolveFieldDeclaration` function SHALL resolve the field type and initializer expression without crashing, and the file's AST SHALL remain in the semantic model with all types from that file registered and resolvable.

**Validates: Requirements 2.2, 2.3**

Property 3: Bug Condition — string.Length() Resolves Without Crash

_For any_ method call expression where the target type is `string` and the member name is `Length`, the fixed semantic model builder SHALL resolve the member type to `int` without crashing, and the file's AST SHALL remain in the semantic model.

**Validates: Requirements 2.4, 2.5**

Property 4: Preservation — Single-Parameter Methods Unchanged

_For any_ method or constructor with exactly one parameter, the fixed code SHALL produce the same scoping behavior as the original code, preserving correct parameter registration and resolution.

**Validates: Requirements 3.1, 3.6**

Property 5: Preservation — Fields Without Initializers Unchanged

_For any_ field declaration without an initializer, the fixed code SHALL produce the same behavior as the original code, preserving correct field type resolution and symbol registration.

**Validates: Requirements 3.2, 3.5**

Property 6: Preservation — Other String Methods Unchanged

_For any_ method call on a string variable where the member name is NOT `Length` (e.g., `Contains`, `Substring`), the fixed code SHALL produce the same resolution behavior as the original code.

**Validates: Requirements 3.3, 3.4**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`

**1. Fix `RegisterParameters` — iterate all parameters from ParameterList**

The current code accesses `paramList.Parameter` for the first parameter and then iterates `paramList.Children()` for additional ones. The fix should iterate ALL `ParameterNode` children uniformly, without relying on the `Parameter` property:

- Replace the current two-step approach (first parameter + children loop) with a single loop over `paramList.Children()` that registers every `ParameterNode` found
- Remove the `ReferenceEquals` check that skips the first parameter, since we'll process all parameters uniformly
- Alternatively, if `Children()` doesn't yield the first parameter, keep the explicit first-parameter registration but fix the children iteration to correctly find repeated parameters (they may be nested inside intermediate repetition nodes)

**2. Fix `ResolveFieldDeclaration` — handle optional initializer expression**

The current code resolves the field type but ignores the initializer. The fix should:

- After resolving the field type, check if the field has an initializer expression
- Use the same `FindFieldInitializerExpression` pattern used by `CSharpEmitter` and `MistPrettyPrinter` — walk `fieldDecl.Children()` to find an `ExpressionNode`
- If an initializer expression is found, call `ResolveExpression` on it to resolve its types
- This prevents the crash that occurs when the unresolved expression is encountered later in the pipeline

**3. Fix `LookupMemberType` or `ResolvePostfixSuffix` — handle `string.Length()` without crash**

The crash when resolving `string.Length()` needs investigation. The fix should:

- Ensure that `LookupMemberType` for `StringType` correctly returns the member type without triggering a crash
- If the issue is in how the ambient type's member scope is populated, ensure `RegisterAmbientTypeInScope` correctly adds `Length` to the string type's member scope
- If the issue is a null reference in the resolution chain, add appropriate null checks
- Verify that the `Length` method symbol (which has no parameters) doesn't cause issues when resolved via `MethodCallSuffixPostfixSuffixNode` with a null argument list

**4. Add defensive error handling**

- Ensure that any unhandled exception during field or expression resolution is caught and reported as a diagnostic rather than silently dropping the file's AST
- The existing try/catch in `Build()` around `Pass2_ResolveTypes` already catches exceptions, but the catch block should preserve the AST rather than discarding it

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that construct ASTs with multi-parameter methods, field initializers, and `string.Length()` calls, then run them through the semantic model builder on the UNFIXED code to observe failures.

**Test Cases**:
1. **Multi-Parameter Method Test**: Build a method with two parameters and verify both are in scope (will fail on unfixed code — second parameter missing)
2. **Field Initializer Test**: Build a class with `private int value = 0;` and verify the type is registered (will fail on unfixed code — AST dropped)
3. **string.Length() Test**: Build a method calling `input.Length()` on a string and verify resolution (will fail on unfixed code — crash)
4. **Three-Parameter Constructor Test**: Build a constructor with three parameters and verify all are in scope (will fail on unfixed code)

**Expected Counterexamples**:
- Bug 1: Second parameter produces `MIST_UNDEFINED_IDENTIFIER` diagnostic
- Bug 2: Type from file with field initializer produces `MIST_UNDEFINED_TYPE` in other files
- Bug 3: Semantic model builder throws exception or produces `MIST_SEMANTIC_ERROR`

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := SemanticModelBuilder_fixed.Build(input)
  ASSERT expectedBehavior(result)
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT SemanticModelBuilder_original.Build(input) = SemanticModelBuilder_fixed.Build(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-buggy inputs

**Test Plan**: Observe behavior on UNFIXED code first for single-parameter methods, fields without initializers, and other string methods, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Single-Parameter Preservation**: Verify methods with one parameter continue to register it correctly
2. **No-Initializer Field Preservation**: Verify fields without initializers continue to resolve correctly
3. **Other String Methods Preservation**: Verify `Contains` and `Substring` continue to resolve correctly
4. **Array.Length Preservation**: Verify array length access continues to work

### Unit Tests

- Test `RegisterParameters` with 1, 2, 3, and 5 parameters
- Test `ResolveFieldDeclaration` with and without initializer expressions
- Test `LookupMemberType` for `string.Length`, `string.Contains`, `string.Substring`
- Test that `string.Length()` call with no arguments resolves to `int`
- Test multi-file compilation where one file has a field initializer

### Property-Based Tests

- Generate methods with random parameter counts (1–10) and verify all parameters are registered in scope
- Generate field declarations with and without initializers and verify correct resolution
- Generate random ambient type member accesses and verify no crashes occur
- Generate single-parameter methods and verify preservation of existing behavior

### Integration Tests

- Compile the `AllFeatures` sample (which uses multi-parameter methods, field initializers, and string operations) end-to-end
- Verify that `Point(int x, int y)` constructor registers both parameters
- Verify that `Counter` class with `private int value = 0;` field initializer compiles correctly
- Verify that `StringUtils.IsGreeting` using `string.Contains()` continues to work
