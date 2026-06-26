# Implementation Plan

## Overview

Fix the type resolution unification bug in two phases: Phase A interns extension receiver types (single-line change in `RegisterAmbientExtensions`), Phase B introduces a unified `ResolveMethodMember` function replacing the fragmented lookup API. A cleanup phase removes structural equality band-aids after Phase A is verified.

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1", "2"] },
    { "id": 1, "tasks": ["3.1"] },
    { "id": 2, "tasks": ["3.2", "3.3"] },
    { "id": 3, "tasks": ["3.4"] },
    { "id": 4, "tasks": ["4.1"] },
    { "id": 5, "tasks": ["4.2"] },
    { "id": 6, "tasks": ["4.3"] },
    { "id": 7, "tasks": ["4.4", "4.5"] },
    { "id": 8, "tasks": ["4.6"] },
    { "id": 9, "tasks": ["5.1"] },
    { "id": 10, "tasks": ["5.2"] },
    { "id": 11, "tasks": ["6"] },
    { "id": 12, "tasks": ["7"] }
  ]
}
```

## Tasks

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Extension Receiver Type Identity Failure
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the type interning bug exists
  - **Scoped PBT Approach**: Scope the property to CLR interface types projected as extension receivers (e.g., `IServiceProvider`) — verify that after `RegisterAmbientExtensions`, the extension block's `ReceiverType` is `ReferenceEquals` to the instance returned by `AssemblyTypeProvider.ResolveType` for the same namespace/name
  - Test that for any CLR interface type registered as an extension receiver, the receiver type instance on the `ExtensionBlockInfo` is reference-equal to the canonical type instance from the type provider cache
  - Additionally test that a generic extension method call with explicit type arguments (e.g., `GetRequiredService<MigrationRunner>()`) resolves to the concrete substituted return type, not `unknown`
  - Use in-process compilation via shared `CompilerDriver` — do NOT spawn a process per iteration
  - If the test needs to compile and run a Mist program, batch all scenarios into a single program (1 compile, 1 run) per the process-spawning policy
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bug exists: receiver type is a disconnected shell, not the canonical instance; generic extension resolution returns `unknown`)
  - Document counterexamples found to understand root cause
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Non-Interface Extension and Own-Member Resolution
  - **IMPORTANT**: Follow observation-first methodology
  - Observe behavior on UNFIXED code for non-buggy inputs (cases where the bug condition does NOT apply):
    - Non-generic extension methods on class-typed receivers (e.g., `string.Contains()`) resolve correctly
    - Non-generic extension methods on struct-typed receivers resolve correctly
    - Extension methods on generic constructed types (e.g., `List<int>.Select()`) resolve correctly
    - Own members on class/struct/interface types resolve without consulting extensions
    - Non-generic method calls (no type arguments) return the first matching overload's declared type
    - Universal extensions (receiver type = unknown) are included in applicability results
    - Extension method namespace filtering excludes non-imported extensions
    - Mist-defined types resolve through scope chain without involving ambient type projection
    - Field/property invocation as method emits diagnostic error
    - Type argument arity mismatch emits diagnostic error
    - Lambda-compatible overload selection still works for Func/Action/delegate parameters
  - Write property-based tests capturing observed behavior patterns from Preservation Requirements in design
  - Use in-process compilation via shared `CompilerDriver` — batch scenarios into a single program where process spawning is needed (1 compile, 1 run)
  - Property-based testing generates many test cases for stronger guarantees that existing behavior is preserved
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11_

- [x] 3. Phase A fix — Extension Receiver Type Interning

  - [x] 3.1 Implement Phase A: Replace `ProjectTypeReference` with `ProjectMemberType` for extension receivers
    - In `SemanticModelBuilder.RegisterAmbientExtensions`, change `projector.ProjectTypeReference(group[0].ReceiverClrType, stdlib)` to `projector.ProjectMemberType(group[0].ReceiverClrType, stdlib)`
    - This ensures extension receiver types go through the full resolver on cache miss, returning the canonical interned instance instead of a disconnected shell
    - Verify zero warnings after change (zero-warnings policy)
    - _Bug_Condition: isBugCondition(input) where input is a CLR interface type projected as an extension receiver via the cache-only path, creating a disconnected shell_
    - _Expected_Behavior: After RegisterAmbientExtensions, ReceiverType on ExtensionBlockInfo is ReferenceEquals to AssemblyTypeProvider.ResolveType(namespace, name) for the same logical type_
    - _Preservation: Non-generic extension methods on class/struct receivers, own-member resolution, namespace filtering, Mist-defined type resolution all continue to work unchanged_
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.2 Verify bug condition exploration test now passes (Phase A portion)
    - **Property 1: Expected Behavior** - Extension Receiver Type Identity
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The receiver type reference-equality assertion should now pass
    - The generic extension method resolution (`GetRequiredService<MigrationRunner>()` returning concrete type) may or may not pass yet — if it still fails, Phase B will address it
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Reference identity portion PASSES (confirms type interning fix works)
    - _Requirements: 2.1, 2.2_

  - [x] 3.3 Verify preservation tests still pass after Phase A
    - **Property 2: Preservation** - Non-Interface Extension and Own-Member Resolution
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions from the single-line change)
    - Confirm all tests still pass after fix (no regressions)

  - [x] 3.4 Run full test suite to confirm no regressions
    - Run `dotnet test` on the full solution (use `cwd` parameter, not `cd`)
    - Verify all existing tests pass (3867+ tests)
    - Verify zero warnings in build output

- [x] 4. Phase B fix — Unified Method Resolution

  - [x] 4.1 Define `MethodResolutionHints` and `MethodResolutionResult` types
    - Add `private readonly record struct MethodResolutionHints(int? TypeArgCount, int? ArgumentCount, bool PreferLambdaCompatible)` to `SemanticModelBuilder`
    - Add `private readonly record struct MethodResolutionResult(ISymbol Symbol, IType ReturnType, bool IsExtension, ExtensionBlockInfo? ExtensionBlock, IReadOnlyList<TypeParameterType>? ExtensionTypeParams)` to `SemanticModelBuilder`
    - Verify zero warnings after addition
    - _Requirements: 2.5, 2.6, 2.7_

  - [x] 4.2 Implement `ResolveMethodMember` function
    - Implement the unified search in priority order: own member scope (walk base chain for classes) → ambient members via resolver → default methods on abstracts → extension registry (applicable, namespace-filtered)
    - Apply consistent overload selection based on `MethodResolutionHints`: type argument arity filtering, parameter count filtering, lambda-compatible parameter preference
    - Return `MethodResolutionResult` with both the resolved symbol and return type in one pass
    - Return null if no matching method is found (no silent fallthrough)
    - Verify zero warnings
    - _Bug_Condition: Fragmented lookups disagree on overload selection when type args are provided_
    - _Expected_Behavior: Single function returns arity-matching overload with correct substituted return type_
    - _Preservation: Own members preferred over extensions, namespace filtering applied, kind validation maintained_
    - _Requirements: 2.5, 2.6, 2.7_

  - [x] 4.3 Migrate `MethodCallSuffix` handler to use `ResolveMethodMember`
    - Replace the `LookupMemberType` + `LookupGenericMemberSymbol` pair in the method call suffix handler with a single `ResolveMethodMember` call
    - Deprecate (but do not delete) old lookup functions until all callers are migrated
    - Ensure kind validation (field/property detection) is handled within the unified path
    - Verify zero warnings
    - _Requirements: 2.5, 2.6, 2.7_

  - [x] 4.4 Verify bug condition exploration test fully passes (Phase B)
    - **Property 1: Expected Behavior** - Extension Receiver Type Identity and Overload Resolution
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - Both the reference-identity assertion AND the generic extension method resolution assertion should now pass
    - `GetRequiredService<MigrationRunner>()` should resolve to `MigrationRunner`, not `unknown`
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES fully (confirms both type interning and overload resolution are fixed)
    - _Requirements: 2.3, 2.4, 2.5_

  - [x] 4.5 Verify preservation tests still pass after Phase B
    - **Property 2: Preservation** - Non-Interface Extension and Own-Member Resolution
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms unified resolution doesn't regress existing behavior)
    - Confirm all tests still pass after fix (no regressions)

  - [x] 4.6 Run full test suite to confirm no regressions
    - Run `dotnet test` on the full solution (use `cwd` parameter, not `cd`)
    - Verify all existing tests pass
    - Verify zero warnings in build output

- [x] 5. Cleanup — Remove structural equality band-aids

  - [x] 5.1 Remove `AbstractType` structural equality fallback in `ExtensionRegistry.IsApplicable`
    - After Phase A ensures type interning, the structural equality check for `AbstractType` in `ExtensionRegistry` is redundant
    - Remove the fallback and verify extension resolution still works via reference equality alone
    - Verify zero warnings
    - _Requirements: 2.1, 2.2_

  - [x] 5.2 Verify all tests pass after cleanup
    - Run full test suite to confirm the structural equality removal causes no regressions
    - If any tests fail, the type interning from Phase A is incomplete — investigate and fix rather than reverting the cleanup

- [x] 6. Integration validation — Pendragon sample app
  - Build the Pendragon sample project which uses `GetRequiredService<MigrationRunner>()` on `IServiceProvider`
  - Verify it compiles with zero errors, zero warnings, and zero informational diagnostics
  - Verify the resolved type for the `GetRequiredService<MigrationRunner>()` call is `MigrationRunner` (not `unknown` or `object`)
  - This exercises the full end-to-end path: type projection → extension registration → receiver matching → overload selection → type argument substitution
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 7. Checkpoint - Ensure all tests pass
  - Run the full test suite one final time
  - Confirm Pendragon sample builds cleanly
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Phase A is a single-line change with outsized impact — it eliminates the disconnected shell problem entirely for extension receivers.
- Phase B is a larger structural change but scoped to `SemanticModelBuilder` — old lookup functions are deprecated, not deleted, to allow incremental migration.
- Tests that spawn processes must batch scenarios (1 compile, 1 run) per workspace policy. Use shared `CompilerDriver` for in-process compilation where possible.
- All shell commands use `cwd` parameter, never `cd`.
- Zero warnings policy applies to all changes — stop and ask if a warning is introduced.
