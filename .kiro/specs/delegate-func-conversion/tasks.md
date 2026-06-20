# Implementation Plan: Delegate-Func Conversion

## Overview

This plan implements the `DelegateType` and `BaseDelegateType` type representations, modifies the projection pipeline to preserve CLR delegate identity, adds assignability and overload resolution rules, and updates the C# emitter to produce correct delegate type names and constructor wrapping. Tasks are ordered so the type model is built first, then projection, then type checking, then emission, then documentation and sample. All property-based tests and the single integration test use in-process compilation except the approved single-spawn sample run.

## Tasks

- [ ] 1. Create DelegateType and BaseDelegateType type classes
  - [ ] 1.1 Implement `DelegateType` class at `Source/Mist/Semantics/Model/Types/DelegateType.cs`
    - Implement `IType` with `DisplayName`, `Kind => TypeKind.Class`
    - Add properties: `ClrFullName`, `ClrNamespace`, `ClrSimpleName`, `FuncSignature` (ConstructedType), `TypeParameters`, `TypeArguments`, `MemberScope`
    - Add equality based on `ClrFullName` + `TypeArguments` match
    - _Requirements: 1.1, 1.5, 1.6_
  - [ ] 1.2 Implement `BaseDelegateType` singleton at `Source/Mist/Semantics/Model/Types/BaseDelegateType.cs`
    - Implement `IType` with `DisplayName => "Delegate"`, `Kind => TypeKind.Class`
    - Singleton pattern via `public static readonly BaseDelegateType Instance`
    - Add settable `MemberScope` for System.Delegate's projected members
    - _Requirements: 3.1_
  - [ ]* 1.3 Write property test for DelegateType identity preservation (Property 1)
    - **Property 1: Projection preserves delegate identity**
    - Generate random DelegateType instances; verify ClrFullName, ClrNamespace, TypeArguments consistency and FuncSignature arity matches parameter count + 1
    - **Validates: Requirements 1.1, 1.3, 1.5, 1.6**

- [ ] 2. Modify TypeProjector for delegate projection
  - [ ] 2.1 Update `TypeProjector.TryProjectNamedDelegate` to return `DelegateType` instead of bare `ConstructedType`
    - When base type is `System.MulticastDelegate` and name is NOT in `System.Func`/`System.Action` family, produce a `DelegateType` preserving CLR identity and carrying the equivalent `func<...>` signature
    - Keep existing `System.Func<...>`/`System.Action<...>` → `ConstructedType` path unchanged
    - Handle generic delegates: open type parameters for definitions, concrete type arguments for constructed instances
    - _Requirements: 1.1, 1.2, 1.5_
  - [ ] 2.2 Add `System.Delegate`/`System.MulticastDelegate` dispatch in `TypeProjector.ProjectTypeReference`
    - When `clrType.FullName` is `"System.Delegate"` or `"System.MulticastDelegate"`, return `BaseDelegateType.Instance`
    - Place this check before existing delegate/func dispatch
    - _Requirements: 3.1_
  - [ ] 2.3 Ensure `AssemblyTypeProvider` uses `DelegateType` in projected method signatures
    - Verify that method parameters and return types referencing named delegates carry the `DelegateType` through projection rather than collapsing to bare `func<...>`
    - _Requirements: 1.3, 1.4_
  - [ ]* 2.4 Write unit tests for TypeProjector delegate projection
    - Test: `System.Func<int, string>` projects to ConstructedType, NOT DelegateType
    - Test: `System.Action` / `System.Action<int>` project to ConstructedType
    - Test: `System.Delegate` and `System.MulticastDelegate` project to `BaseDelegateType.Instance`
    - Test: `System.Predicate<int>` projects to DelegateType with correct ClrFullName and FuncSignature
    - Test: Generic delegate `Predicate<T>` → open DelegateType; `Predicate<int>` → constructed DelegateType
    - _Requirements: 1.1, 1.2, 1.5, 3.1_

- [ ] 3. Update VisibilityFilter for delegate types
  - [ ] 3.1 Ensure named delegates are not hidden by `IsTypeExcluded`
    - Named delegates are public classes and should pass through existing visibility checks
    - Confirm `System.Delegate` and `System.MulticastDelegate` are never excluded
    - _Requirements: 5.1, 5.5_
  - [ ] 3.2 Add delegate-specific signature check in `IsTypeExcludedInSignature`
    - When a delegate's `Invoke` method references a pointer type, ref struct, or has a `ref readonly` return, mark the delegate as hidden
    - When a method parameter references a hidden delegate, hide that overload (consistent with per-overload filtering)
    - _Requirements: 5.2, 5.3, 5.4_
  - [ ]* 3.3 Write property test for visibility filter (Property 9)
    - **Property 9: Visibility filter — public delegates visible, excluded-signature delegates hidden**
    - Generate delegates with/without pointer params; verify visible/hidden classification
    - **Validates: Requirements 5.1, 5.2**
  - [ ]* 3.4 Write unit tests for delegate visibility
    - Test: Public delegate without excluded types → not hidden
    - Test: Delegate with pointer param in Invoke → hidden
    - Test: `System.Delegate` → never excluded
    - Test: Method overload referencing hidden delegate → overload hidden, sibling overloads survive
    - _Requirements: 5.1, 5.2, 5.5, 5.6_

- [ ] 4. Checkpoint — Projection pipeline complete
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 5. Implement assignability rules in TypeCheckerAnalyzer
  - [ ] 5.1 Add DelegateType and BaseDelegateType assignability logic to `TypeCheckerAnalyzer.IsAssignable`
    - `func<...>` → DelegateType: accept if signature-compatible
    - DelegateType → `func<...>`: accept if signature-compatible
    - DelegateType → BaseDelegateType: always accept
    - `func<...>` → BaseDelegateType: always accept
    - DelegateType(A) → DelegateType(B) where A ≠ B: reject (nominal distinctness)
    - BaseDelegateType → DelegateType: reject (cannot narrow)
    - BaseDelegateType → `func<...>`: reject (cannot narrow)
    - Add `IsSignatureCompatible` helper for pairwise parameter/return type comparison
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.2, 3.3, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_
  - [ ] 5.2 Add delegate invocation type checking
    - When a DelegateType-typed value has an invocation suffix, validate arguments against `DelegateType.FuncSignature`
    - Result type is the FuncSignature's return type
    - BaseDelegateType is NOT invocable — produce error diagnostic `MIST_TC_0093`
    - _Requirements: 8.1, 8.3, 3.6, 3.7_
  - [ ] 5.3 Add error diagnostics for delegate type mismatches
    - `MIST_TC_0090`: Signature mismatch (func/lambda incompatible with target DelegateType)
    - `MIST_TC_0091`: Nominal mismatch (two distinct named delegates)
    - `MIST_TC_0092`: Cannot narrow System.Delegate to specific type
    - `MIST_TC_0093`: System.Delegate not directly invocable
    - _Requirements: 2.5, 6.6, 6.7, 3.7_
  - [ ]* 5.4 Write property test for func-to-delegate assignability (Property 3)
    - **Property 3: func-to-delegate assignability iff signature-compatible**
    - Generate (DelegateType, ConstructedType) pairs; verify IsAssignable returns true iff signatures match
    - **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 6.2**
  - [ ]* 5.5 Write property test for delegate-to-func assignability (Property 4)
    - **Property 4: delegate-to-func assignability**
    - For any DelegateType D, verify IsAssignable(D, D.FuncSignature) returns true
    - **Validates: Requirements 1.4, 6.3, 8.4**
  - [ ]* 5.6 Write property test for BaseDelegateType assignability (Property 5)
    - **Property 5: Any delegate or func assignable to BaseDelegateType**
    - For any DelegateType D and any func type F, verify both are assignable to BaseDelegateType.Instance
    - **Validates: Requirements 3.2, 3.3, 6.4, 6.5**
  - [ ]* 5.7 Write property test for BaseDelegateType non-narrowing (Property 6)
    - **Property 6: BaseDelegateType cannot narrow and is not invocable**
    - Verify BaseDelegateType → DelegateType and BaseDelegateType → func both return false
    - **Validates: Requirements 3.6, 3.7, 6.7**
  - [ ]* 5.8 Write property test for nominal distinctness (Property 7)
    - **Property 7: Distinct named delegates are nominally non-convertible**
    - Generate pairs of DelegateTypes with different ClrFullName but same FuncSignature; verify IsAssignable returns false
    - **Validates: Requirements 6.1, 6.6**
  - [ ]* 5.9 Write property test for delegate invocation (Property 11)
    - **Property 11: Delegate invocation type-checks against FuncSignature**
    - For any DelegateType D with FuncSignature, verify invocation with correct args succeeds and wrong args produces error
    - **Validates: Requirements 8.1, 8.3**

- [ ] 6. Implement overload resolution preference rules
  - [ ] 6.1 Add delegate-aware preference tiebreaker in `SemanticModelBuilder`
    - DelegateType-typed argument → prefer matching DelegateType overload (exact nominal match)
    - `func<...>` argument → prefer `func<...>` overload over DelegateType overload
    - Lambda argument → prefer DelegateType overload over `func<...>` overload (target-type inference favors nominal)
    - DelegateType overload vs BaseDelegateType overload → prefer DelegateType (more specific)
    - Ambiguity across multiple DelegateTypes → error diagnostic `MIST_TC_0094`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_
  - [ ] 6.2 Add target-type inference for lambdas targeting DelegateType
    - When a lambda with implicit parameter types targets a DelegateType, infer parameter types from the DelegateType's FuncSignature
    - Apply same inference rules as when target is a `func<...>`
    - _Requirements: 2.8_
  - [ ] 6.3 Add method group resolution for DelegateType targets
    - Single compatible overload in method group → select it
    - Zero or multiple compatible overloads → error diagnostic (`MIST_TC_0095` / `MIST_TC_0096`)
    - _Requirements: 2.7, 2.9_
  - [ ]* 6.4 Write property test for overload resolution preference (Property 10)
    - **Property 10: Overload resolution prefers more specific delegate type**
    - Generate overload sets with DelegateType vs BaseDelegateType; verify more specific wins
    - **Validates: Requirements 7.1, 7.2, 7.4**

- [ ] 7. Checkpoint — Type checking complete
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 8. Update CSharpTypeMap for delegate type name emission
  - [ ] 8.1 Add DelegateType and BaseDelegateType arms in `CSharpTypeMap.GetTypeName`
    - DelegateType with TypeArguments → `{ClrNamespace}.{ClrSimpleName}<{TypeArg1}, ...>`
    - DelegateType without TypeArguments → `{ClrNamespace}.{ClrSimpleName}`
    - BaseDelegateType → `"System.Delegate"`
    - Handle generic type arguments in nested positions (e.g., `List<RequestDelegate>`)
    - _Requirements: 4.1, 4.2, 4.5, 4.6_
  - [ ]* 8.2 Write property test for type map emission (Property 2)
    - **Property 2: Type map emits correct CLR delegate name**
    - Generate DelegateType instances; verify GetTypeName output starts with ClrNamespace, contains ClrSimpleName, includes type args in angle brackets when present, never contains "System.Func" or "System.Action"
    - **Validates: Requirements 1.8, 4.1, 4.2, 4.6**

- [ ] 9. Update CSharpEmitter for call-site delegate wrapping
  - [ ] 9.1 Add delegate constructor wrapping at call sites
    - When argument type is func/lambda/method-group and parameter type is DelegateType: emit `new {DelegateName}({argument})`
    - When target is BaseDelegateType: emit lambda as-is (C# 11+ implicit conversion)
    - When DelegateType value is passed where `func<...>` expected: emit as-is (C# handles implicitly)
    - _Requirements: 4.3, 4.4, 4.7, 4.8_
  - [ ] 9.2 Add delegate invocation emission
    - When DelegateType-typed value is invoked: emit standard C# delegate invocation `variable(args)`
    - _Requirements: 8.2_
  - [ ]* 9.3 Write property test for emitter wrapping (Property 8)
    - **Property 8: Emitter wraps func-to-delegate conversion**
    - For any DelegateType D and signature-compatible expression E, verify emitted output contains `new {GetTypeName(D)}({emitted E})`
    - **Validates: Requirements 4.3, 4.7**
  - [ ]* 9.4 Write unit tests for emission
    - Test: Lambda → DelegateType emits `new RequestDelegate(ctx => ...)`
    - Test: Lambda → BaseDelegateType emits lambda as-is
    - Test: DelegateType → func assignment emits value as-is
    - Test: Method group → DelegateType emits `new DelegateName(methodGroup)`
    - _Requirements: 4.3, 4.4, 4.7, 4.8_

- [ ] 10. Checkpoint — Emission complete
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 11. Sample application and integration test
  - [ ] 11.1 Create `samples/DelegateFuncConversion/mist.toml` with `name = "DelegateFuncConversion"` and `[framework_references] "Microsoft.AspNetCore.App" = true`
    - _Requirements: 10.1, 10.2_
  - [ ] 11.2 Create `samples/DelegateFuncConversion/Program.mist` with the Requirement 10 proof code verbatim
    - Include all 8 conversion patterns: lambda→Predicate, method group→Predicate, lambda→Comparison, func→DelegateType variable, DelegateType→func variable, func→System.Delegate, invoke DelegateType, lambda→System.Delegate (MapGet)
    - Allman braces, `var` per policy
    - Do NOT wire into `samples/AllFeatures` (requires ASP.NET Core framework reference)
    - _Requirements: 10.1, 10.3, 10.4, 10.5, 10.6, 10.8, 10.9_
  - [ ] 11.3 Add dedicated integration test for the sample (APPROVED single-spawn exception)
    - Compile in-process using `CompilerDriver`
    - Resolve `Microsoft.AspNetCore.App` framework reference
    - Spawn `dotnet` once to run the compiled DLL
    - Assert exit code 0 and stdout matches expected output byte-for-byte
    - Configure UTF-8 stdout/stderr encoding per Test UTF-8 Encoding Policy
    - _Requirements: 10.7, 10.9_

- [ ] 12. Book documentation
  - [ ] 12.1 Update `book/src/advanced/dotnet-interop.md` with delegate-func conversion section
    - Explain named CLR delegates as distinct types implicitly convertible from signature-compatible `func<...>` values and lambdas
    - Document `func<...>` → named delegate, named delegate → `func<...>`, any → `System.Delegate` conversions
    - Explain nominal distinctness between different named delegates
    - Include ASP.NET Core `MapGet("/", () => "Hello!")` example for System.Delegate conversion
    - Document that DelegateType-typed values are directly invocable
    - All code examples as compiled snippets under `book/snippets/advanced/dotnet-interop/`
    - No new SUMMARY.md entry (content within existing chapter)
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8_

- [ ] 13. Distributed steering file updates
  - [ ] 13.1 Update `distribute/steering/mist-interop.md`
    - Document named delegates projected as DelegateType preserving CLR identity
    - Document implicit conversions: func→delegate, lambda→delegate, method group→delegate, delegate→func, any→System.Delegate
    - Document that System.Func/Action project directly as `func<...>` without DelegateType wrapper
    - Document nominal distinctness between named delegates
    - Remove/correct any content stating named delegates are collapsed to bare `func<...>`
    - Preserve `MIST-MANAGED-STEERING` marker and front matter
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_
  - [ ] 13.2 Update any other contradicting steering files for consistency
    - Check `distribute/steering/mist-language.md` and other files for contradictions
    - _Requirements: 11.6_

- [ ] 14. Final checkpoint — Full verification
  - Build the solution with zero warnings; run the full test suite (in-process tests + the single approved sample-spawn test); report the duration of the sample integration test and flag if >5s.
  - _Requirements: 1.1, 1.8, 2.1, 3.1, 4.1, 5.1, 6.1, 7.1, 8.1, 9.1, 10.1, 11.1_

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP.
- Each task references specific requirements for traceability.
- Checkpoints ensure incremental validation between major phases.
- Property tests validate universal correctness properties from the design document.
- Unit tests validate specific examples and edge cases.
- **Zero-warnings policy:** all changes must build with zero warnings. If a warning appears, stop and report.
- **Process spawning:** only task 11.3 spawns a process (approved single-spawn exception). All other tests use in-process compilation via a shared `CompilerDriver`.
- **Proof code is immutable:** the Requirement 10 proof code must compile and run verbatim; if it cannot, stop and report the gap rather than weakening the sample.
- **No AllFeatures integration:** the sample requires `Microsoft.AspNetCore.App` and is excluded from the full-suite baseline run.
- **Diagnostic IDs:** use `MIST_TC_0090`–`MIST_TC_0096` as specified in the design document's Error Handling section.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "2.1", "2.2"] },
    { "id": 2, "tasks": ["2.3", "2.4", "3.1", "3.2"] },
    { "id": 3, "tasks": ["3.3", "3.4", "5.1"] },
    { "id": 4, "tasks": ["5.2", "5.3", "5.4", "5.5", "5.6", "5.7", "5.8"] },
    { "id": 5, "tasks": ["5.9", "6.1", "6.2", "6.3"] },
    { "id": 6, "tasks": ["6.4", "8.1"] },
    { "id": 7, "tasks": ["8.2", "9.1", "9.2"] },
    { "id": 8, "tasks": ["9.3", "9.4"] },
    { "id": 9, "tasks": ["11.1", "11.2"] },
    { "id": 10, "tasks": ["11.3", "12.1"] },
    { "id": 11, "tasks": ["13.1", "13.2"] }
  ]
}
```
