# Tasks

## Task 1: Fix parameter registration to include all parameters
- [x] 1.1 Fix `RegisterParameters` in `SemanticModelBuilder.cs` to iterate all `ParameterNode` children from the `ParameterListNode`, ensuring every parameter (not just the first) is registered in the method body scope
- [x] 1.2 Write unit tests verifying methods with 2, 3, and 5 parameters all have every parameter resolvable in the method body scope
- [x] 1.3 Write a property-based test (Property 1) generating methods with random parameter counts (2–8) and asserting all parameters are registered without `MIST_UNDEFINED_IDENTIFIER (historical)` diagnostics
- [x] 1.4 Write a preservation test (Property 4) verifying single-parameter methods continue to register the parameter correctly after the fix

## Task 2: Fix field initializer resolution to prevent silent AST loss
- [x] 2.1 Fix `ResolveFieldDeclaration` in `SemanticModelBuilder.cs` to resolve the optional initializer expression by walking `fieldDecl.Children()` for an `ExpressionNode` (matching the pattern used by `CSharpEmitter` and `MistPrettyPrinter`) and calling `ResolveExpression` on it
- [x] 2.2 Write unit tests verifying that a class with `private int value = 0;` parses successfully and the type is registered in the semantic model
- [x] 2.3 Write a property-based test (Property 2) generating field declarations with initializer expressions and asserting the file's AST is preserved and types are registered
- [x] 2.4 Write a preservation test (Property 5) verifying fields without initializers continue to resolve correctly after the fix

## Task 3: Fix string.Length() resolution crash
- [x] 3.1 Investigate and fix the crash in `SemanticModelBuilder.cs` when resolving `string.Length()` — likely in `LookupMemberType` or `ResolvePostfixSuffix` — ensuring the `Length` member resolves to `int` without exception
- [x] 3.2 Write unit tests verifying `string.Length()` resolves to type `int` without crashing or producing error diagnostics
- [x] 3.3 Write a property-based test (Property 3) constructing string method calls including `Length` and asserting no crash and correct type resolution
- [x] 3.4 Write a preservation test (Property 6) verifying `string.Contains()`, `string.Substring()`, and `Array.Length` continue to resolve correctly after the fix

## Task 4: Integration verification
- [x] 4.1 Write an integration test compiling a multi-file program that exercises all three bug scenarios: multi-parameter constructors, field initializers, and `string.Length()` calls
- [x] 4.2 Verify the existing `AllFeatures` sample programs (`Point.mist`, `Counter.mist`, `StringUtils.mist`) compile without errors after all fixes are applied
