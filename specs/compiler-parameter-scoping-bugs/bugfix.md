# Bugfix Requirements Document

## Introduction

Three related compiler bugs were discovered while testing the AllFeatures sample app. All three cause silent failures or incorrect scoping during semantic model construction, resulting in misleading downstream diagnostics rather than clear error messages at the point of failure. These bugs block compilation of valid Mist programs that use multiple method parameters, field initializers, or `string.Length()`.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a method declares two or more parameters THEN the system only registers the first parameter in the method body scope, causing subsequent parameters to produce `MIST_UNDEFINED_IDENTIFIER (historical)` errors when referenced

1.2 WHEN a class or struct field declaration includes an initializer expression (e.g., `private int value = 0;`) THEN the system silently drops the entire file's AST during parsing, causing the type declared in that file to be unregistered in the semantic model

1.3 WHEN a file's AST is dropped due to a field initializer THEN the system produces misleading `MIST_UNDEFINED_TYPE (historical)` errors in other files that reference the type, and `MIST_TC_0017 (historical)` (no entry point) if the entry point class is in the affected file, with no parse error diagnostic emitted

1.4 WHEN `string.Length()` is called on a string variable THEN the system silently crashes during semantic model construction, dropping the entire file's AST — even though `Length` is registered as a method symbol in `CSharpAmbientTypeResolver`

1.5 WHEN a file's AST is dropped due to the `string.Length()` crash THEN the system produces the same misleading downstream errors as Bug 2 (undefined types, missing entry point) with no diagnostic pointing to the actual failure

### Expected Behavior (Correct)

2.1 WHEN a method declares two or more parameters THEN the system SHALL register all parameters in the method body scope so that every parameter is accessible by name within the method body

2.2 WHEN a class or struct field declaration includes an initializer expression THEN the system SHALL parse the field declaration successfully and include the initializer in the AST

2.3 WHEN a file contains a field with an initializer THEN the system SHALL register the type declared in that file in the semantic model, and other files referencing that type SHALL resolve it without errors

2.4 WHEN `string.Length()` is called on a string variable THEN the system SHALL resolve the `Length` member from the ambient type resolver and type-check the call without crashing

2.5 WHEN the semantic model builder encounters any ambient type member access THEN the system SHALL either resolve it successfully or emit a clear diagnostic — never silently drop the file's AST

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a method declares exactly one parameter THEN the system SHALL CONTINUE TO register that parameter in the method body scope correctly

3.2 WHEN a class or struct field declaration has no initializer (e.g., `private int value;`) THEN the system SHALL CONTINUE TO parse and register the field correctly

3.3 WHEN `string.Contains()` or `string.Substring()` is called on a string variable THEN the system SHALL CONTINUE TO resolve those members and type-check the calls correctly

3.4 WHEN `Array.Length` is accessed on an array variable THEN the system SHALL CONTINUE TO resolve the `Length` member correctly

3.5 WHEN a file parses successfully THEN the system SHALL CONTINUE TO register all types from that file in the semantic model and make them available to other compilation units

3.6 WHEN a method has parameters of array types (e.g., `int[] values`) THEN the system SHALL CONTINUE TO register those parameters correctly in the method body scope
