# Implementation Plan: Storm Trait Keyword

## Overview

This plan implements the `trait` keyword in the Storm grammar definition language. The implementation proceeds bottom-up through the pipeline: lexer → syntax types → parser → pretty printer → grammar resolution → validation → IR building → emission → generator wiring → TextMate grammar → Mist.storm integration. Each step builds on the previous, ensuring no orphaned code.

## Tasks

- [x] 1. Lexer and syntax foundations
  - [x] 1.1 Add `TraitKeyword` to `StormTokenKind` and `ClassifyKeyword`
    - Add `TraitKeyword` variant to the `StormTokenKind` enum in `StormLexer.cs`
    - Add `"trait" => StormTokenKind.TraitKeyword` to `ClassifyKeyword` method
    - Add `StormTokenKind.TraitKeyword` case to `SynchronizeToDeclaration` in the parser
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Add `StormFieldCardinality`, `StormTraitField`, and `StormTraitDecl` to `StormSyntax.cs`
    - Add `StormFieldCardinality` enum with `One`, `Optional`, `ZeroOrMore`, `OneOrMore`
    - Add `StormTraitField` record with `FieldName`, `TypeName`, `Cardinality`
    - Add `StormTraitDecl` record extending `StormDeclaration` with `Fields`, `ComposedTraits`
    - _Requirements: 2.1, 2.2, 2.9, 3.6_

  - [x] 1.3 Extend `StormRuleDecl` with optional `TraitBindings` property
    - Add `IReadOnlyList<string>? TraitBindings = null` parameter to `StormRuleDecl` record
    - Ensure backward compatibility (null/empty means no bindings)
    - _Requirements: 3.1, 3.2_

  - [x] 1.4 Write unit tests for lexer `trait` keyword recognition
    - Test `trait` → `TraitKeyword`, `traits` → `Identifier`, `Trait` → `Identifier`
    - Add tests to `StormLexerTests.cs`
    - _Requirements: 1.1, 1.2, 1.3_

- [x] 2. Parser implementation
  - [x] 2.1 Implement `ParseTraitDecl` in `StormParser.cs`
    - Add `StormTokenKind.TraitKeyword` case to `ParseDeclaration` dispatch
    - Implement `ParseTraitDecl` method handling trait name, optional composition (`: Trait1, Trait2`), equals sign, space-separated fields, and semicolon
    - Implement `ParseTraitField` helper method parsing `fieldName:TypeName` with optional cardinality suffix
    - Report diagnostic for zero fields or missing semicolon
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7, 2.8, 3.6_

  - [x] 2.2 Implement rule trait binding parsing in `ParseRuleDecl`
    - Modify `ParseRuleDecl` to detect colon after rule name and parse comma-separated trait name identifiers
    - Ensure `@memo` annotated rules also pass through trait binding parsing
    - Report diagnostic if colon is present but no valid identifier follows
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 2.3 Write unit tests for trait declaration parsing
    - Test parsing with 1, 2, N fields; all cardinality variants; trait composition
    - Test error cases: missing semicolon, zero fields, missing colon in field
    - Add tests to `StormParserTests.cs`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 2.4 Write unit tests for rule trait binding parsing
    - Test 1, 2, N trait bindings; `@memo` + trait binding; no binding backward compat
    - Test error case: colon present but no identifier
    - Add tests to `StormParserTests.cs`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 3. Pretty printer and round-trip
  - [x] 3.1 Implement trait declaration and binding formatting in `StormPrettyPrinter.cs`
    - Add `StormTraitDecl` case to `PrintDeclaration` dispatch
    - Implement `PrintTraitDecl` method: `trait Name : Composed = field1:Type1 field2:Type2?;`
    - Modify `StormRuleDecl` printing to include `: Trait1, Trait2` when `TraitBindings` is non-empty
    - Handle `@memo` prefix case for rules with trait bindings
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 3.2 Write property test for trait declaration round-trip
    - **Property 1: Trait declaration round-trip**
    - **Validates: Requirements 1, 2, 10**

  - [x] 3.3 Write property test for rule trait binding round-trip
    - **Property 2: Rule trait binding round-trip**
    - **Validates: Requirements 3, 10**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Diagnostic IDs and validation pass
  - [x] 5.1 Add diagnostic IDs `STORM0018`–`STORM0024` to `StormDiagnosticIds.cs`
    - Add constants for all seven new diagnostic codes with message templates
    - _Requirements: 4.3, 4.5, 4.6, 5.1, 5.2, 5.3_

  - [x] 5.2 Update `NameResolutionPass` to skip `StormTraitDecl`
    - Add early `continue` for `StormTraitDecl` in the declaration walk loop (traits have no body expressions to resolve)
    - _Requirements: 4.8_

  - [x] 5.3 Implement `TraitValidationPass.cs` in `Source/Storm.SourceGenerator/Validate/`
    - Validate trait field type references resolve to tokens or rules (not fragments) → `STORM0018`
    - Validate composed trait references resolve to declared traits → `STORM0023`
    - Validate rule trait binding names resolve to declared traits → `STORM0019`
    - Validate structural satisfaction: field name, type, and cardinality matching → `STORM0020`, `STORM0021`
    - Validate multi-alternative rules cannot bind traits → `STORM0022`
    - Validate inline block arm targets satisfy propagated traits → `STORM0024`
    - _Requirements: 3.5, 4.3, 4.5, 5.1, 5.2, 5.3, 5.4, 5.5, 8.1, 8.3_

  - [x] 5.4 Write unit tests for `TraitValidationPass` diagnostics
    - Test each diagnostic (`STORM0018`–`STORM0024`) fires for corresponding error condition
    - Test success case (valid trait binding with correct fields)
    - Add tests to new `TraitValidationPassTests.cs`
    - _Requirements: 4.3, 4.5, 5.1, 5.2, 5.3, 5.4, 5.5_

  - [x] 5.5 Write property test for trait field type resolution
    - **Property 3: Trait field type resolution**
    - **Validates: Requirements 4, 5**

  - [x] 5.6 Write property test for structural satisfaction
    - **Property 4: Structural satisfaction**
    - **Validates: Requirement 5**

  - [x] 5.7 Write property test for multi-alternative rejection
    - **Property 8: Multi-alternative rejection**
    - **Validates: Requirement 3.5**

- [x] 6. Trait IR types and builder
  - [x] 6.1 Create `TraitIR.cs` and `TraitFieldIR.cs` in `Source/Storm.SourceGenerator/IR/`
    - Implement `TraitIR` class with `Name`, `Fields`, `ComposedTraits`, and `AllFields()` method
    - Implement `TraitFieldIR` class with `FieldName`, `TypeName`, `IsToken`, `Cardinality`
    - _Requirements: 4.1, 4.4_

  - [x] 6.2 Create `TraitIRBuilder.cs` in `Source/Storm.SourceGenerator/IR/`
    - Implement `BuildResult` class with `Traits` and `RuleTraitBindings` properties
    - Implement `Build(StormGrammar)` method that:
      - Builds `TraitIR` for each `StormTraitDecl` (resolving field types to `IsToken` flag)
      - Builds rule→traits mapping for `StormRuleDecl` nodes with `TraitBindings`
      - Resolves inline block propagation (arm target rules inherit parent traits, deduplicated)
    - _Requirements: 4.1, 4.2, 4.4, 4.7, 4.8, 8.2, 8.4, 8.5_

- [x] 7. Emission integration
  - [x] 7.1 Extend `EmissionContext` with trait data
    - Add `IReadOnlyList<TraitIR> Traits` property
    - Add `IReadOnlyDictionary<string, IReadOnlyList<TraitIR>> RuleTraitBindings` property
    - Update constructor to accept trait parameters
    - _Requirements: 4.4, 6.1, 7.1_

  - [x] 7.2 Implement trait interface emission in `AstEmitter.cs`
    - Emit `I{TraitName}Node` interface per `TraitIR` with get-only properties
    - Map field cardinality and IsToken to correct C# property types per the design table
    - Handle trait composition: emitted interface extends composed trait interfaces
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9_

  - [x] 7.3 Add trait interfaces to node class implements lists in `AstEmitter.cs`
    - When emitting sealed node classes, check `RuleTraitBindings` for the rule name
    - Add all corresponding `I{TraitName}Node` interfaces to the implements list
    - Deduplicate interfaces (same trait from own binding + propagation appears once)
    - _Requirements: 7.1, 7.2, 7.3, 8.2, 8.5, 9.1, 9.2_

  - [x] 7.4 Write unit tests for trait interface emission
    - Verify generated C# contains correct interface declarations with proper property types
    - Verify node classes include trait interfaces in implements lists
    - Verify trait composition produces interface inheritance
    - Add tests to `EmitterTests.cs` or new `TraitEmitterTests.cs`
    - _Requirements: 6.1, 6.2, 7.1, 7.2_

  - [x] 7.5 Write property test for interface emission correctness
    - **Property 5: Interface emission correctness**
    - **Validates: Requirements 6, 7**

  - [x] 7.6 Write property test for trait composition inheritance
    - **Property 6: Trait composition inheritance**
    - **Validates: Requirements 3.6, 6.9, 7.3**

  - [x] 7.7 Write property test for inline block propagation
    - **Property 7: Inline block propagation**
    - **Validates: Requirement 8**

- [x] 8. Generator wiring
  - [x] 8.1 Wire `TraitValidationPass` and `TraitIRBuilder` into `StormIncrementalGenerator.cs`
    - Insert `TraitValidationPass.Run(grammar)` after name resolution, before lowering
    - Report trait validation diagnostics; bail on error
    - Call `TraitIRBuilder.Build(grammar)` after lowering succeeds
    - Pass `TraitBuildResult` into `EmissionContext` constructor
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

- [x] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. TextMate grammar update
  - [x] 10.1 Update `Editor/storm-syntax-highlighting/syntaxes/storm.tmLanguage.json`
    - Add `trait` to `definition-names` pattern for declaration keyword highlighting
    - Add `trait` to `storage-types` pattern
    - Add `trait-bindings` pattern for `rule Name : Trait1, Trait2` syntax highlighting
    - Ensure `trait-bindings` pattern takes priority over plain `definition-names`
    - _Requirements: 11.1, 11.2, 11.3_

- [x] 11. Mist.storm integration
  - [x] 11.1 Add `CallableDeclaration` trait to `Source/Mist/Grammar/Mist.storm`
    - Declare `trait CallableDeclaration = attributes:AttributeList* modifiers:Modifier* parameters:ParameterList? withClauses:WithClause* constraints:WhereClause* body:MethodBody;`
    - Add `: CallableDeclaration` binding to `ConstructorDeclaration` rule
    - Add `: CallableDeclaration` binding to `MethodDeclaration` rule
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 11.2 Write integration test verifying `ICallableDeclarationNode` emission
    - Verify `ICallableDeclarationNode` interface is generated with expected properties
    - Verify `ConstructorDeclarationNode` and `MethodDeclarationNode` implement it
    - _Requirements: 12.4_

- [x] 12. Zero downstream impact verification
  - [x] 12.1 Verify Mist compiler and CLI build without changes
    - Build `Source/Mist/` and `Source/Mist.CLI/` with no modifications
    - Run all tests in `Test/Mist.Tests/` and `Test/Mist.CLI.Tests/`
    - Confirm zero new warnings or errors
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

  - [x] 12.2 Write property test for zero downstream impact
    - **Property 9: Zero downstream impact**
    - **Validates: Requirement 14**

- [x] 13. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- The implementation language is C# (matching the Storm source generator codebase)
- All new code goes under `Source/Storm.SourceGenerator/` and `Test/Storm.SourceGenerator.Tests/`

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2", "5.1"] },
    { "id": 1, "tasks": ["1.3", "1.4", "10.1"] },
    { "id": 2, "tasks": ["2.1", "2.2"] },
    { "id": 3, "tasks": ["2.3", "2.4", "3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "5.2"] },
    { "id": 5, "tasks": ["5.3"] },
    { "id": 6, "tasks": ["5.4", "5.5", "5.6", "5.7", "6.1"] },
    { "id": 7, "tasks": ["6.2"] },
    { "id": 8, "tasks": ["7.1"] },
    { "id": 9, "tasks": ["7.2", "7.3"] },
    { "id": 10, "tasks": ["7.4", "7.5", "7.6", "7.7", "8.1"] },
    { "id": 11, "tasks": ["11.1"] },
    { "id": 12, "tasks": ["11.2", "12.1"] },
    { "id": 13, "tasks": ["12.2"] }
  ]
}
```
