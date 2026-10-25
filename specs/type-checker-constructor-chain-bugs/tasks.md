# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Constructor Chain Wrong Type Resolution and Missing Diagnostic Location
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate both bugs exist
  - **Scoped PBT Approach**: Scope the property to concrete failing cases:
    - Compile `Point.mist` and `Color.mist` together (multi-type namespace with constructor chaining)
    - Assert `MIST_TC_0013` is NOT emitted for `Point() : this(0, 0)` (bug 1: false positive fires because `FindEnclosingType` returns `Color`)
    - Assert any emitted type checker diagnostic has non-null `CompilationUnitPath` (bug 2: always null)
    - Assert any emitted type checker diagnostic has `Location.Start.Line >= 1` (bug 2: line is always 0)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bugs exist)
  - Document counterexamples found:
    - `MIST_TC_0013` fires with "No constructor on 'Color' matches the chained arguments" for Point's constructor chain
    - All type checker diagnostics have `CompilationUnitPath == null` and `Location.Start.Line == 0`
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Genuine Constructor Chain Errors and Non-Chain Diagnostics Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - **Observe on UNFIXED code**:
    - Compile a program where constructor chain genuinely has no match (e.g., `Foo() : this(1, 2, 3)` with only `Foo(int)`) — observe `MIST_TC_0013` fires with correct type name
    - Compile a single-type program with valid constructor chaining — observe no false positive
    - Trigger non-constructor-chain diagnostics (MIST_TC_0001 assignment mismatch, MIST_TC_0008 return type, MIST_TC_0011 object creation) — observe same diagnostic IDs and messages
    - Verify `CheckObjectCreation` correctly emits `MIST_TC_0011` for mismatched `new Type(args)` — observe behavior
  - **Write property-based tests capturing observed behavior**:
    - For all constructor chains where declaring type genuinely has no matching constructor, `MIST_TC_0013` fires with the correct type name
    - For all single-type compilations with valid constructor chaining, no `MIST_TC_0013` is emitted
    - For all non-constructor-chain type checking (method bodies, field declarations, object creation, operators, assignments, entry points), the same set of diagnostic IDs and messages are produced
    - `FindDeclaringType` usage in `CheckMethodBody`/`CheckConstructorBody` continues to resolve correctly
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 3. Fix for constructor chain wrong type resolution and missing diagnostic location

  - [x] 3.1 Replace `FindEnclosingType` with `FindDeclaringType` in `CheckConstructorChain`
    - In `CheckConstructorChain` (line 1105), replace `FindEnclosingType(ctorSymbol)` with `FindDeclaringType(ctorSymbol)`
    - `FindDeclaringType` already exists at line 175 and verifies `ReferenceEquals(memberScope, typeScope)`
    - This is a single-line change that reuses proven logic
    - _Bug_Condition: isBugCondition_WrongType(input) where FindEnclosingType(ctorSymbol) != FindDeclaringType(ctorSymbol) AND ctorSymbol has a constructor chain_
    - _Expected_Behavior: enclosingType == FindDeclaringType(ctorSymbol) AND no false MIST_TC_0013 emitted_
    - _Preservation: Genuine MIST_TC_0013 errors still fire for truly mismatched constructor chains_
    - _Requirements: 2.1, 2.2_

  - [x] 3.2 Remove the buggy `FindEnclosingType` method
    - Delete the `FindEnclosingType` method (lines 1671–1683) since it is no longer called anywhere
    - This eliminates the buggy code path entirely
    - _Requirements: 2.1_

  - [x] 3.3 Update `MakeDiagnostic` signature to accept `ISemanticModel`
    - Change signature from `MakeDiagnostic(string id, string message, SyntaxNode node)` to `MakeDiagnostic(string id, string message, SyntaxNode node, ISemanticModel model)`
    - The model provides access to `Units` (compilation units with `SourceText`) and `Asts` (AST roots)
    - _Bug_Condition: isBugCondition_BadLocation(input) where MakeDiagnostic produces null path and line 0_
    - _Expected_Behavior: diagnostic.CompilationUnitPath IS NOT NULL AND diagnostic.Location.Start.Line >= 1_
    - _Requirements: 2.3, 2.4_

  - [x] 3.4 Implement file path resolution in `MakeDiagnostic`
    - Use the node's span to determine which AST root contains it by checking span containment against each AST in `model.Asts`
    - Once the AST index is found, retrieve the corresponding `CompilationUnit` from `model.Units` and get `Source.Path`
    - Pass this path as `CompilationUnitPath` in the `Diagnostic` constructor
    - _Expected_Behavior: diagnostic.CompilationUnitPath == sourceFile.Path for the file containing the node_
    - _Requirements: 2.3_

  - [x] 3.5 Implement proper line/column conversion in `MakeDiagnostic`
    - Convert the Storm `SyntaxNode.Span` to a Mist `SourceSpan`
    - Call `sourceText.ToLinePositionSpan(sourceSpan)` to get a proper 1-based `LinePositionSpan`
    - Use this as the `Location` in the `Diagnostic` constructor instead of the hardcoded `LinePosition(0, offset)` values
    - _Expected_Behavior: diagnostic.Location == sourceText.ToLinePositionSpan(nodeSpan) with 1-based line/column_
    - _Requirements: 2.4_

  - [x] 3.6 Update all `MakeDiagnostic` call sites to pass the `model` parameter
    - Every call to `MakeDiagnostic` throughout the file (approximately 20+ call sites) must pass the `model` parameter
    - The `model` is already available at every call site since it is passed through the checking methods
    - _Requirements: 2.3, 2.4_

  - [x] 3.7 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Constructor Chain Resolves Correct Type and Diagnostics Have Location
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms:
      - `CheckConstructorChain` resolves the correct declaring type via `FindDeclaringType`
      - No false `MIST_TC_0013` for `Point() : this(0, 0)` in multi-type namespace
      - Type checker diagnostics include non-null `CompilationUnitPath`
      - Type checker diagnostics have correct 1-based line/column
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bugs are fixed)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 3.8 Verify preservation tests still pass
    - **Property 2: Preservation** - Genuine Errors and Non-Chain Diagnostics Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix:
      - Genuine `MIST_TC_0013` still fires for truly mismatched constructor chains
      - `CheckObjectCreation` still correctly matches constructors
      - All other type checker diagnostics (MIST_TC_0001–0018) unchanged
      - `FindDeclaringType` in `CheckMethodBody`/`CheckConstructorBody` unaffected
      - Lexer/parser diagnostics pass through unchanged

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.
