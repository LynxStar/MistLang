# Implementation Plan: Ambient Extension Method Binding

## Overview

This plan implements ambient extension method binding — making C# extension methods from .NET assemblies available as instance methods when their namespace is imported via `using`. The implementation adds an `AmbientExtensionScanner` to detect extension methods, extends the `IAmbientTypeResolver` interface, modifies `ExtensionBlockInfo` for ambient support, registers discovered extensions in the `ExtensionRegistry`, and updates the emitter to produce static method calls.

## Tasks

- [x] 1. Create `AmbientExtensionMethodInfo` data class at `Source/Mist/Backends/CSharpTranspiler/AmbientExtensionMethodInfo.cs` with properties: Namespace, ContainingClassName, MethodName, ReceiverClrType, RemainingParameters, ReturnClrType, IsGenericMethod, MethodTypeParameters, MethodInfo
- [x] 2. Create `AmbientExtensionScanner` at `Source/Mist/Backends/CSharpTranspiler/AmbientExtensionScanner.cs` — scans loaded assemblies for static classes with `[ExtensionAttribute]`, detects extension methods (public static, has attribute, at least 1 param), records metadata, caches per namespace
- [x] 3. Add `GetExtensionMethodsInNamespace(string namespaceName)` to `IAmbientTypeResolver` interface and implement in `AssemblyTypeProvider` by delegating to `AmbientExtensionScanner`; update all test mock implementations to return empty list
- [x] 4. Modify `ExtensionBlockInfo`: make `DeclaringNode` nullable (`Storm.Runtime.SyntaxNode?`), add `IsAmbient` computed property, add `AmbientStaticClassName` property; update all existing construction sites
- [x] 5. Add `RegisterAmbientExtensions` method to `SemanticModelBuilder` — queries ambient resolver for extension methods in each imported namespace, groups by receiver type, projects receiver/params/return types via TypeProjector, creates ExtensionBlockInfo entries with ContainingNamespace and AmbientStaticClassName, registers in ExtensionRegistry
- [x] 6. Call `RegisterAmbientExtensions` during Pass 2e (Phase A) after Mist-native extension registration, passing all imported namespaces from using directives and global usings
- [x] 7. Update `CSharpEmitter` to detect ambient extension calls (via `ExtensionBlockInfo.IsAmbient`) and emit as static method invocations: `Namespace.ClassName.MethodName(receiver, args)` with proper type arguments and using directives
- [x] 8. Write unit tests for `AmbientExtensionScanner`: detection of LINQ methods, exclusion of non-extension methods, caching behavior, receiver type identification
- [x] 9. Write integration tests: extension methods available with `using System.Linq`, unavailable without it, ambiguity with Mist-native extensions, chained calls, generic type inference
- [x] 10. Create sample application at `samples/ExtensionMethodBinding/Program.mist` with proof code from requirements; add to AllFeatures orchestrator; verify compilation and expected output
- [x] 11. Add ambient extension methods section to `book/src/object-model/extension-methods.md` with snippet at `book/snippets/object-model/extension-methods/ambient-extensions.mist` and generated `.cs` file

## Task Dependencies

1: none
2: 1
3: 2
4: 1
5: 3, 4
6: 5
7: 6
8: 6
9: 7, 8
10: 9
11: 10

## Notes

- Tasks 1-4 are foundational data model and interface changes that must come first.
- Task 5 (registration logic) depends on both the scanner (Task 2-3) and the ExtensionBlockInfo changes (Task 4).
- Task 6 (calling the registration) depends on Task 5.
- Task 7 (emission) depends on Task 6 being in place so calls can be resolved.
- Tasks 8-9 (tests) depend on the full pipeline being functional (Tasks 1-7).
- Task 10 (sample) depends on the feature being complete and testable.
- Task 11 (book) depends on the sample compiling successfully.
- All process-spawning tests use the batched approach (1 compile, 1 run, N output lines) per the test process spawning policy.
