# Implementation Plan: Pattern Matching (`is` Expressions)

## Overview

This plan implements C#-style `is` expression pattern matching in the Mist compiler. Changes follow the compiler pipeline order: grammar → codebase-wide rename → pretty-printer → semantic model → type checker → C# emitter → editor support → documentation → tests. Each task references specific files and requirements for traceability.

## Tasks

- [x] 1. Add pattern matching keywords and tokens to the grammar
  - [x] 1.1 Add `IS`, `AND`, `OR`, `NOT` keyword tokens to `Source/Mist/Grammar/keywords.storm`
    - Append four new keyword token declarations: `token IS = "is";`, `token AND = "and";`, `token OR = "or";`, `token NOT = "not";`
    - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - [x] 1.2 Add `DOTDOT` token and pattern grammar rules to `Source/Mist/Grammar/Mist.storm`
    - Add `token DOTDOT = "..";` **before** the existing `token DOT = ".";` declaration for maximal-munch correctness
    - Add all pattern grammar rules: `Pattern`, `OrPattern`, `AndPattern`, `UnaryPattern`, `PrimaryPattern`, `ConstantLiteral`, `PropertySubpatternList`, `PropertySubpattern`, `ListPatternElementList`, `ListPatternElement`
    - Modify the `Relational` rule to split into `IsExpression` and `RelationalComparison` alternatives as specified in the design
    - _Requirements: 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 2. Update all existing code for the RelationalNode split
  - [x] 2.1 Update `EmitRelational` and `ScanRelationalForValueReturningCalls` in `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - `EmitRelational` (line ~2065): Change from accessing `relational.Left`, `.Right`, `.Operator` directly to matching on `RelationalComparisonRelationalNode` (for existing comparison logic) and `IsExpressionRelationalNode` (stub returning `$"{left} is ..."` for now — full pattern emission in task 6)
    - `ScanRelationalForValueReturningCalls` (line ~537): Match on `RelationalComparisonRelationalNode` to access `.Left` and `.Right`; for `IsExpressionRelationalNode`, scan only the `.Left` additive (patterns contain no method calls)
    - _Requirements: 3.1, 3.2_
  - [x] 2.2 Update `ResolveRelational` in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - `ResolveRelational` (line ~7112): Match on `RelationalComparisonRelationalNode` for existing comparison logic; add `IsExpressionRelationalNode` case that resolves the left additive, resolves the pattern (stub for now — full pattern resolution in task 4), records `bool` as the expression type, and returns `bool`
    - _Requirements: 3.1, 6.1_
  - [x] 2.3 Update `CheckRelationalNode` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - `CheckRelationalNode` (line ~1935): Match on `RelationalComparisonRelationalNode` for existing comparison logic; add `IsExpressionRelationalNode` case (stub for now — full pattern checking in task 5)
    - _Requirements: 3.1_
  - [x] 2.4 Update `EmitRelational` in `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - The pretty-printer's `EmitRelational` method (in the expression emission chain around line ~1300): Match on `RelationalComparisonRelationalNode` for existing comparison formatting; add `IsExpressionRelationalNode` case (stub for now — full pattern printing in task 3)
    - _Requirements: 3.1_

- [x] 3. Checkpoint — Verify the build compiles with zero warnings
  - Build the solution with `dotnet build Mist.slnx` and ensure zero errors and zero warnings. The grammar changes trigger Storm source generation, producing new AST node types. All existing code referencing `RelationalNode` properties must compile against the new `RelationalComparisonRelationalNode` subtype. Ask the user if questions arise.

- [x] 4. Implement pretty-printer support for all pattern kinds
  - [x] 4.1 Add `EmitPattern` and all pattern emission methods to `Source/Mist/Parsing/MistPrettyPrinter.cs`
    - Add `EmitPattern(PatternNode)` dispatching to `EmitOrPattern`
    - Add `EmitOrPattern(OrPatternNode)` — format as `left or right`
    - Add `EmitAndPattern(AndPatternNode)` — format as `left and right`
    - Add `EmitUnaryPattern(UnaryPatternNode)` — `not pattern` or direct dispatch
    - Add `EmitPrimaryPattern(PrimaryPatternNode)` — switch on all alternatives: `TypePatternPrimaryPatternNode` → `Type name`, `VarPatternPrimaryPatternNode` → `var name`, `ConstantPatternPrimaryPatternNode` → literal value, relational pattern variants → `op constant`, `PropertyPatternPrimaryPatternNode` → `TypeName? { Name: subpattern, ... } name?`, `ListPatternPrimaryPatternNode` → `[elem1, elem2, ..]`, `ParenthesizedPatternPrimaryPatternNode` → `(pattern)`
    - Add `EmitConstantLiteral(ConstantLiteralNode)` for constant pattern values
    - Add `EmitPropertySubpatternList` and `EmitListPatternElementList` helpers
    - Wire the `IsExpressionRelationalNode` case in `EmitRelational` to use `EmitPattern`
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 4.10, 4.11, 4.12_

- [x] 5. Implement semantic model changes for pattern variable scoping
  - [x] 5.1 Add `ResolvePattern` method to `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Add a `ResolvePattern(PatternNode, IType scrutineeType, ...)` method that recursively resolves type references within patterns (e.g., the type in a type pattern, the type prefix in a property pattern) and records expression types for constant sub-expressions
    - Wire the `IsExpressionRelationalNode` case in `ResolveRelational` to call `ResolvePattern`
    - _Requirements: 6.1_
  - [x] 5.2 Add `CollectPatternVariables` method and update `ResolveIfStatement` in `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
    - Add `CollectPatternVariables(ExpressionNode condition, Scope thenScope, SymbolTable symbolTable, ...)` that walks the condition expression tree looking for `IsExpressionRelationalNode` nodes and extracts variable bindings from their patterns
    - For type patterns (`Type name`): register `name` as `SymbolKind.LocalVariable` with the pattern's resolved type
    - For var patterns (`var name`): register `name` as `SymbolKind.LocalVariable` with the scrutinee's static type
    - For discard `_`: no variable registered
    - For property patterns with trailing identifier: register the identifier
    - Modify `ResolveIfStatement` (line ~6152) to: (1) resolve the condition, (2) create a child `Scope` for the then-branch, (3) call `CollectPatternVariables` to populate the child scope, (4) resolve the then-branch in the child scope instead of `blockScope`
    - The else branch does NOT get pattern variables
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

- [x] 6. Checkpoint — Verify the build compiles and existing tests pass
  - Build with `dotnet build Mist.slnx` (zero warnings). Run `dotnet test Mist.slnx` to verify no regressions. Ask the user if questions arise.

- [x] 7. Implement type checker validation for patterns
  - [x] 7.1 Add pattern matching diagnostic IDs to `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Add static `DiagnosticId` fields: `MIST_TC_0050` (undefined type), `MIST_TC_0051` (constant type mismatch), `MIST_TC_0052` (non-comparable relational), `MIST_TC_0053` (undefined property), `MIST_TC_0054` (list requires array), `MIST_TC_0055` (list too many elements — warning), `MIST_TC_0056` (or inconsistent variables), `MIST_TC_0057` (not cannot bind variable)
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  - [x] 7.2 Add `CheckPattern` method and wire into `CheckRelationalNode` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
    - Add `CheckPattern(PatternNode, IType scrutineeType, ISemanticModel, DiagnosticBag)` that recursively validates each pattern kind:
      - **Type pattern**: verify type exists in scope → `MIST_TC_0050` if not
      - **Constant pattern**: verify constant type compatible with scrutinee → `MIST_TC_0051` if not
      - **Relational pattern**: verify scrutinee supports comparison → `MIST_TC_0052`; verify constant type compatible → `MIST_TC_0051`
      - **Property pattern**: verify each property name exists on scrutinee type (or pattern type prefix) → `MIST_TC_0053`; recursively validate subpatterns
      - **List pattern**: verify scrutinee is array type → `MIST_TC_0054`; warn if too many fixed elements → `MIST_TC_0055`; recursively validate element patterns against element type
      - **Or pattern**: validate each alternative; verify all alternatives introduce same variables with same types → `MIST_TC_0056`
      - **Not pattern**: validate operand; verify operand introduces no variables → `MIST_TC_0057`
      - **And pattern**: validate each operand against scrutinee type
      - **Var pattern**: always valid
      - **Discard pattern**: always valid
      - **Parenthesized pattern**: recursively validate inner pattern
    - Skip validation when scrutinee type is `UnknownType` to suppress cascading errors
    - Wire the `IsExpressionRelationalNode` case in `CheckRelationalNode` to call `CheckPattern`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 7.1, 7.2, 7.3, 7.4_

- [x] 8. Implement C# emitter support for all pattern kinds
  - [x] 8.1 Add `EmitPattern` and all pattern emission methods to `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
    - Add `EmitPattern(PatternNode)` dispatching to `EmitOrPattern`
    - Add `EmitOrPattern` → `pattern1 or pattern2`
    - Add `EmitAndPattern` → `pattern1 and pattern2`
    - Add `EmitUnaryPattern` → `not pattern` or direct dispatch
    - Add `EmitPrimaryPattern` switching on all alternatives:
      - `TypePatternPrimaryPatternNode` → `Type name`
      - `VarPatternPrimaryPatternNode` → `var name`
      - `ConstantPatternPrimaryPatternNode` → literal value
      - Relational pattern variants → `op constant`
      - `PropertyPatternPrimaryPatternNode` → `TypeName? { Name: subpattern, ... } name?`
      - `ListPatternPrimaryPatternNode` → `[elem1, elem2, ..]`; for slice elements with an identifier, emit `.. var name` (insert `var` keyword for C# compatibility)
      - `ParenthesizedPatternPrimaryPatternNode` → `(pattern)`
    - Wire the `IsExpressionRelationalNode` case in `EmitRelational` to use `EmitPattern`
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9, 9.10, 9.11, 9.12, 9.13_

- [x] 9. Checkpoint — Verify the build compiles and existing tests pass
  - Build with `dotnet build Mist.slnx` (zero warnings). Run `dotnet test Mist.slnx` to verify no regressions. Ask the user if questions arise.

- [x] 10. Update syntax highlighting and documentation
  - [x] 10.1 Update `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
    - Add `is` to the `keyword.operator.comparison.mist` pattern (or create a new `keyword.operator.mist` pattern): `\\b(is)\\b`
    - Add `and`, `or`, `not` to the `keyword.operator.logical.mist` pattern: update the match to `\\b(and|or|not)\\b|&&|\\|\\||!`
    - Add `..` to the operators or punctuation patterns
    - _Requirements: 11.1, 11.2, 11.3_
  - [x] 10.2 Update `CSHARP_FEATURES.md`
    - In the `Pattern Matching` section: mark Type patterns, Constant patterns, `var` patterns, Property patterns, Relational patterns, Logical patterns, and List patterns as ✅; leave Positional patterns as 🔲
    - In the `Expressions — Other` section: mark `is` pattern matching and `Discard (_)` as ✅
    - Update the `Summary` table counts to reflect the new implementations
    - _Requirements: 12.1, 12.2, 12.3_
  - [x] 10.3 Create sample application in `samples/PatternMatching/`
    - Create `samples/PatternMatching/mist.toml` with project configuration
    - Create `samples/PatternMatching/Main.mist` with a `static void Main` entry point demonstrating:
      - Type pattern in an `if` condition with variable binding
      - Property pattern matching against object properties
      - Relational and logical patterns (e.g., `is > 0 and < 100`)
      - List pattern matching against arrays
      - Each pattern kind printing observable output via `Console.WriteLine`
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7_

- [x] 11. Implement property-based and example-based tests
  - [x] 11.1 Create `MistPatternGenerator` in `Test/Mist.Tests/Generators/MistPatternGenerator.cs`
    - Build an FsCheck generator that produces random valid Mist source programs containing `is` expressions with various pattern kinds (type, constant, var, discard, property, relational, logical, list, parenthesized)
    - Generate complete compilable programs (class with Main method, variable declarations for scrutinees) so the full pipeline can process them
    - _Requirements: 2.8, 3.3_
  - [x] 11.2 Write property test for round-trip parsing (Property 1) in `Test/Mist.Tests/Parsing/PatternMatchingRoundTripPropertyTests.cs`
    - **Property 1: Pattern Matching Round-Trip**
    - Use `MistPatternGenerator` to generate source programs, parse → pretty-print → parse, and verify AST equivalence
    - All operations are in-process (MistLexer, MistParser, MistPrettyPrinter) — no process spawning
    - Minimum 100 iterations
    - **Validates: Requirements 2.8, 3.3, 4.1–4.12**
  - [x] 11.3 Write property test for pattern variable scoping (Property 2) in `Test/Mist.Tests/Semantics/PatternVariableScopingPropertyTests.cs`
    - **Property 2: Pattern Variable Scoping in Then Branch**
    - Generate programs with `if (x is Type name)` conditions, build semantic model in-process, verify pattern variable is in then-branch scope with correct `SymbolKind.LocalVariable` and correct type
    - **Validates: Requirements 5.1, 5.2, 5.3**
  - [x] 11.4 Write property test for discard non-binding (Property 3) in `Test/Mist.Tests/Semantics/PatternDiscardNonBindingPropertyTests.cs`
    - **Property 3: Discard Pattern Non-Binding**
    - Generate programs with `if (x is _)` conditions, build semantic model in-process, verify no new variable introduced in then-branch scope
    - **Validates: Requirements 5.4**
  - [x] 11.5 Write property test for is-expression bool type (Property 4) in `Test/Mist.Tests/Semantics/IsExpressionBoolTypePropertyTests.cs`
    - **Property 4: Is-Expression Evaluates to Bool**
    - Generate programs with various `is` expressions, build semantic model in-process, verify expression type recorded as `bool`
    - **Validates: Requirements 6.1**
  - [x] 11.6 Write property test for undefined type diagnostic (Property 5) in `Test/Mist.Tests/Analysis/PatternUndefinedTypeDiagnosticPropertyTests.cs`
    - **Property 5: Undefined Type in Pattern Produces Diagnostic**
    - Generate programs with type patterns referencing non-existent types, run type checker in-process, verify `MIST_TC_0050` diagnostic emitted
    - **Validates: Requirements 6.2, 10.1**
  - [x] 11.7 Write property test for or-pattern variable consistency (Property 6) in `Test/Mist.Tests/Analysis/OrPatternVariableConsistencyPropertyTests.cs`
    - **Property 6: Or-Pattern Variable Consistency**
    - Generate programs with `or` patterns having inconsistent variable bindings, run type checker in-process, verify `MIST_TC_0056` diagnostic emitted
    - **Validates: Requirements 7.1, 10.6**
  - [x] 11.8 Write property test for not-pattern variable restriction (Property 7) in `Test/Mist.Tests/Analysis/NotPatternVariableRestrictionPropertyTests.cs`
    - **Property 7: Not-Pattern Variable Restriction**
    - Generate programs with `not` patterns containing variable bindings, run type checker in-process, verify `MIST_TC_0057` diagnostic emitted
    - **Validates: Requirements 7.2, 10.7**
  - [x] 11.9 Write property test for emission structural correctness (Property 8) in `Test/Mist.Tests/Backend/PatternEmissionPropertyTests.cs`
    - **Property 8: Emission Structural Correctness**
    - Generate valid programs with `is` expressions, compile in-process through the full pipeline, verify emitted C# source contains expected pattern syntax substrings (no process spawning — structural string verification only)
    - **Validates: Requirements 8.1–8.8, 9.1–9.13**

- [x] 12. Final checkpoint — Ensure all tests pass
  - Build with `dotnet build Mist.slnx` (zero warnings). Run `dotnet test Mist.slnx` to verify all tests pass. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- All property tests operate in-process using compiler internal APIs — no external process spawning per iteration
- The `RelationalNode` split (task 2) is the highest-risk change since it touches every pipeline stage; the checkpoint at task 3 validates this before proceeding
- The `DOTDOT` token must be declared before `DOT` in `Mist.storm` for maximal-munch correctness
- Discard `_` uses the existing `IDENTIFIER` token — no new token needed
- Slice variables in list patterns need `var` keyword inserted in C# output (`.. var rest`)
