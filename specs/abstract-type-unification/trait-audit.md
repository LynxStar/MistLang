# Phase 2 Trait Audit — Per-File Task List

> 196 files, organized by category. Each file = one task.
> Status: `[ ]` not started, `[x]` done, `[~]` in progress
> Action: **DELETE** = entire file removed, **MODIFY** = targeted edits, **REGENERATED** = auto-rebuilt after compiler changes
> Grep raw reference: `trait-grep-raw.txt`

---

## Category A: Grammar (2 files)

### A1. `Source/Mist/Grammar/keywords.storm` — MODIFY (2 hits)
- Delete `token TRAIT = "trait";` and the `// Trait-related keywords` comment
- Keep `WITH` and `AS` tokens (used by context/with-statements)

### A2. `Source/Mist/Grammar/Mist.storm` — MODIFY (17 hits)
- Delete `traitDeclaration:TraitDeclaration as TraitType` variant from `TypeDeclaration`
- Delete rules: `TraitDeclaration`, `TraitMemberDeclaration`, `TraitFieldDeclaration`, `TraitMethodDeclaration`, `TraitPropertyDeclaration`
- Delete all `TraitXxxMember` variants (7 total)
- Delete `WithDeclaration` rule (references `traitName`/`traitTypeArgs`)
- Delete `MemberMapping` rule, `WithItem` from `TopLevelItem`
- Add parser error recovery: emit helpful diagnostic when `trait` keyword encountered

---

## Category B: Semantic Model Contracts (3 files)

### B1. `Source/Mist/Semantics/Contracts/ISemanticModel.cs` — MODIFY (3 hits)
- Remove `TraitSatisfactionMap TraitSatisfactionMap { get; }` property
- Remove associated doc comments

### B2. `Source/Mist/Semantics/Contracts/ISymbol.cs` — MODIFY (1 hit)
- Remove `Trait` from `SymbolKind` enum

### B3. `Source/Mist/Semantics/Contracts/IType.cs` — MODIFY (2 hits)
- Remove `Trait` from `TypeKind` enum

---

## Category C: Semantic Model Types (7 files)

### C1. `Source/Mist/Semantics/Model/Types/TraitType.cs` — DELETE (11 hits)
- Entire file: defines `TraitType` class

### C2. `Source/Mist/Semantics/Model/Types/ConstructedTraitType.cs` — DELETE (9 hits)
- Entire file: defines `ConstructedTraitType` class

### C3. `Source/Mist/Semantics/Model/Types/TypeParameterConstraints.cs` — MODIFY (3 hits)
- Remove `TraitConstraints` property and its doc comment

### C4. `Source/Mist/Semantics/Model/Types/ClassType.cs` — MODIFY (2 hits)
- Remove `SatisfiedTraits` property/collection

### C5. `Source/Mist/Semantics/Model/Types/StructType.cs` — MODIFY (2 hits)
- Remove `SatisfiedTraits` property/collection

### C6. `Source/Mist/Semantics/Model/Types/TypeContainment.cs` — MODIFY (2 hits)
- Remove `TraitType`/`ConstructedTraitType` pattern matches in containment logic

### C7. `Source/Mist/Semantics/Model/Types/AssociatedTypeSlotType.cs` — MODIFY (2 hits)
- Remove trait-related references in associated type slot logic

---

## Category D: Semantic Model Core (5 files)

### D1. `Source/Mist/Semantics/Model/TraitSatisfactionMap.cs` — DELETE (28 hits)
- Entire file: defines `TraitSatisfactionMap` and `TraitSatisfaction` types

### D2. `Source/Mist/Semantics/Model/WithBlockRegistry.cs` — DELETE (18 hits)
- Entire file: stores resolved with-block mappings for trait satisfaction

### D3. `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` — MODIFY (883 hits)
- Delete diagnostic IDs: `TraitWithUndefinedType`, `TraitWithUndefinedTrait`, `TraitDuplicateWith`, `TraitWithUndefinedSource`, `TraitWithUndefinedTarget`, `TraitWithTypeMismatch`, `TraitMemberVisibility`, `TraitWithUnsatisfied`, `TraitParentNotTrait`, `TraitInheritanceCycle`, `TraitDuplicateParent`
- Delete `RegisterTraitDeclaration(...)` method
- Delete `case TraitTypeTypeDeclarationNode` in `RegisterTypeDeclaration`
- Delete passes: `Pass1_ResolveTraitParents`, `Pass1_DetectTraitCycles`, `DetectTraitCyclesDfsVisit`, `Pass1_ResolveWithBlocks`, `Pass1_ResolveTraitSatisfaction`
- Delete `TraitSatisfactionMap` construction and all references
- Delete `ConcreteTypeSatisfiesTrait(...)` method
- Remove all `foreach (var traitConstraint in constraints.TraitConstraints)` loops
- Remove `SymbolKind.Trait` and `TraitType tt` pattern matches throughout
- Remove `DetectDuplicateAssociatedTypeSlots` trait member handling
- Remove all doc comments referencing "trait"

### D4. `Source/Mist/Semantics/Model/SemanticModel.cs` — MODIFY (3 hits)
- Remove `traitSatisfactionMap` parameter and `TraitSatisfactionMap` property

### D5. `Source/Mist/Semantics/Model/DefaultValueKind.cs` — MODIFY (1 hit)
- Update comment "interface/trait field" → remove trait reference

---

## Category E: Semantics Other (3 files)

### E1. `Source/Mist/Semantics/BindingKind.cs` — MODIFY (1 hit)
- Update comment "interface or trait" → remove trait reference

### E2. `Source/Mist/Semantics/ReadonlyInferenceEngine.cs` — MODIFY (3 hits)
- Remove trait-related branches in readonly inference logic

---

## Category F: Analysis / Type Checker (5 files)

### F1. `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` — MODIFY (293 hits)
- Delete `ConstraintTraitNotSatisfied` diagnostic ID
- Delete `DiagnosticId.DefaultOnTrait`, `DiagnosticId.TargetNewTrait` usage
- Delete methods: `SatisfiesTrait(...)`, `IsTraitTargetType(...)`, `CheckReadonlyAbstractAndTraitContracts(...)`, `CheckReadonlyTraitContracts(...)`, `CheckReadonlyMembersOfTrait(...)`, `FindTraitImplementingMember(...)`, `HasMatchingTraitConstraint(...)`
- Remove `target is TraitType` and `target is ConstructedTraitType` assignability branches
- Remove `TypeKind.Trait` in cast/default/assignability logic
- Remove `TraitType tt => tt.IsReadonly` and `TraitType => false` patterns
- Remove `ConstructedTraitType` substitution logic in member access
- Remove `foreach (var baseTrait in baseConstraints.TraitConstraints)` loops
- Remove `MIST_TRAIT_UNSATISFIED (historical)` emission

### F2. `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs` — MODIFY (35 hits)
- Remove trait-related ownership checking logic (TraitType pattern matches, trait field/method ownership validation)

### F3. `Source/Mist/Analysis/Builtin/ContextCheckerAnalyzer.cs` — MODIFY (8 hits)
- Remove trait method with-clause context checking

### F4. `Source/Mist/Analysis/Builtin/CooperationContractVerifier.cs` — MODIFY (2 hits)
- Remove trait-related cooperation contract verification

### F5. `Source/Mist/Analysis/Builtin/ReadonlyAnalyzer.cs` — MODIFY (2 hits)
- Remove trait-related readonly enforcement branches

---

## Category G: Backends / Emitter (4 files)

### G1. `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` — MODIFY (148 hits)
- Delete methods: `EmitTraitDeclaration`, `EmitTraitFieldAsProperty`, `EmitTraitMethodDeclaration`, `EmitTraitPropertyDeclaration`, `EmitExplicitTraitImplementations`, `EmitTraitMethodParametersWithSubstitution`, `EmitTraitMethodArguments`
- Remove `case TraitTypeTypeDeclarationNode` in type emission
- Remove `TraitType tt` pattern matches in namespace resolution (4 locations)
- Remove `classType.SatisfiedTraits` iteration in base list emission
- Remove `TraitConstraints.Count > 0` checks and `foreach (var trait in constraints.TraitConstraints)` loops in constraint emission

### G2. `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs` — MODIFY (3 hits)
- Remove `ConstructedTraitType ctt =>` case
- Remove `TraitType t => t.DisplayName` case
- Remove comment about ConstructedTraitType null defense

### G3. `Source/Mist/Backends/CSharpTranspiler/DependentTypeHelpersGenerator.cs` — MODIFY (2 hits)
- Remove `TraitType tt => tt.TypeParameters` case

### G4. `Source/Mist/Backends/CSharpTranspiler/NeedsAsyncPropagationPass.cs` — MODIFY (1 hit)
- Remove trait-related branch in async propagation

---

## Category H: Common / Diagnostics (1 file)

### H1. `Source/Mist/Common/Diagnostics/DiagnosticId.cs` — MODIFY (14 hits)
- Remove all trait-related `DiagnosticId` constants: `TraitParentNotTrait`, `TraitInheritanceCycle`, `TraitDuplicateParent`, `TraitWithUndefinedType`, `TraitWithUndefinedTrait`, `TraitDuplicateWith`, `TraitWithUndefinedSource`, `TraitWithUndefinedTarget`, `TraitWithTypeMismatch`, `TraitMemberVisibility`, `TraitWithUnsatisfied`, `DefaultOnTrait`, `TargetNewTrait`, `ConstraintTraitNotSatisfied`

---

## Category I: Parsing (1 file)

### I1. `Source/Mist/Parsing/MistPrettyPrinter.cs` — MODIFY (28 hits)
- Delete `case TraitTypeTypeDeclarationNode` dispatch
- Delete methods: `EmitTraitDeclaration`, `EmitTraitMemberDeclaration`, `EmitTraitFieldDeclaration`, `EmitTraitMethodDeclaration`, `EmitTraitPropertyDeclaration`
- Delete all `case TraitXxxMemberTraitMemberDeclarationNode` branches
- Modify `WithDeclaration` emission (remove `TraitName`/`TraitTypeArgs` references — entire with-block pretty printing goes away)

---

## Category J: Driver (1 file)

### J1. `Source/Mist/Driver/CompilerDriver.cs` — MODIFY (1 hit)
- Remove trait-related reference (likely a comment or pass invocation)

---

## Category K: Test Generators (4 files)

### K1. `Test/Mist.Tests/Generators/MistTraitGenerator.cs` — DELETE (111 hits)
- Entire file: FsCheck generators for trait declaration/with-block ASTs

### K2. `Test/Mist.Tests/Generators/MistContextRoundTripGenerator.cs` — MODIFY (12 hits)
- Delete `GenTraitWithClauses()`, `GenTraitMethodWithClauses()`
- Remove trait scenario from `GenMixedProgram()`
- Update doc comments

### K3. `Test/Mist.Tests/Generators/MistRefParameterRoundTripGenerator.cs` — MODIFY (8 hits)
- Delete `GenTraitRefMethod()`
- Remove from OneOf lists, update section comment

### K4. `Test/Mist.Tests/Generators/MistNestedScopingGenerator.cs` — MODIFY (7 hits)
- Remove `AllTypeKind.Trait => "trait"` case
- Remove `Trait` from `AllTypeKind` enum (if local)

---

## Category L: Test — Parsing (7 files)

### L1. `Test/Mist.Tests/Parsing/TraitRoundTripPropertyTests.cs` — DELETE (17 hits)
- Entire file: trait round-trip property tests

### L2. `Test/Mist.Tests/Parsing/GenericTraitRoundTripPropertyTests.cs` — DELETE (115 hits)
- Entire file: generic trait round-trip property tests

### L3. `Test/Mist.Tests/Parsing/WithStatementParsingTests.cs` — MODIFY (15 hits)
- Delete tests: `TraitMethodWithClause_ParsesCorrectly`, `TraitWithBlock_StillParsesCorrectly`, `TraitWithBlock_CoexistsWithContextStatements`
- Remove trait-related comments

### L4. `Test/Mist.Tests/Parsing/WithStatementPrettyPrinterTests.cs` — MODIFY (4 hits)
- Remove trait-with-block pretty printer tests

### L5. `Test/Mist.Tests/Parsing/AdhocRefinementRoundTripPropertyTests.cs` — MODIFY (8 hits)
- Remove `TraitNames` array, delete `GenTraitConstraint()`, remove from `GenAllScenarios()`

### L6. `Test/Mist.Tests/Parsing/ContextRoundTripPropertyTests.cs` — MODIFY (1 hit)
- Remove "trait methods" from file-level comment

### L7. `Test/Mist.Tests/Parsing/AssociatedTypeRoundTripPropertyTests.cs` — MODIFY (17 hits)
- Remove trait-related associated type round-trip scenarios

---

## Category M: Test — Semantics (26 files)

### M1. `Test/Mist.Tests/Semantics/TraitEffectiveMembersPropertyTests.cs` — DELETE (248 hits)
### M2. `Test/Mist.Tests/Semantics/TraitStructuralMatchSubstitutionPropertyTests.cs` — DELETE (198 hits)
### M3. `Test/Mist.Tests/Semantics/TraitStructuralMatchPropertyTests.cs` — DELETE (174 hits)
### M4. `Test/Mist.Tests/Semantics/TraitAssignabilityPropertyTests.cs` — DELETE (147 hits)
### M5. `Test/Mist.Tests/Semantics/TraitWithBlockSatisfactionPropertyTests.cs` — DELETE (121 hits)
### M6. `Test/Mist.Tests/Semantics/GenericTraitTypeParamRegistrationPropertyTests.cs` — DELETE (120 hits)
### M7. `Test/Mist.Tests/Semantics/TraitTypeRegistrationPropertyTests.cs` — DELETE (117 hits)
### M8. `Test/Mist.Tests/Semantics/TraitArityDiagnosticsPropertyTests.cs` — DELETE (116 hits)
### M9. `Test/Mist.Tests/Semantics/TraitAbstractCoexistencePropertyTests.cs` — DELETE (115 hits)
### M10. `Test/Mist.Tests/Semantics/WithBlockGenericTraitPropertyTests.cs` — DELETE (114 hits)
### M11. `Test/Mist.Tests/Semantics/TraitInheritanceValidationPropertyTests.cs` — DELETE (114 hits)
### M12. `Test/Mist.Tests/Semantics/TraitAssignabilityGenericPropertyTests.cs` — DELETE (103 hits)
### M13. `Test/Mist.Tests/Semantics/TraitMemberAccessPropertyTests.cs` — DELETE (90 hits)
### M14. `Test/Mist.Tests/Semantics/TraitWithBlockValidationPropertyTests.cs` — DELETE (74 hits)
### M15. `Test/Mist.Tests/Semantics/TraitMemberAccessGenericPropertyTests.cs` — DELETE (51 hits)
### M16. `Test/Mist.Tests/Semantics/WithBlockResolutionTests.cs` — DELETE (50 hits)
### M17. `Test/Mist.Tests/Semantics/TraitAssignabilityDebugTest.cs` — DELETE (12 hits)

### M18. `Test/Mist.Tests/Semantics/ExtensionTypeCheckerPropertyTests.cs` — MODIFY (17 hits)
- Delete `TraitAbstractExclusion_ExtensionsNotDiscoverableOnTraitTypedVars` test
- Remove "Trait/Abstract Type Exclusion" section

### M19. `Test/Mist.Tests/Semantics/OptionalDefaultabilityClassificationPropertyTests.cs` — MODIFY (17 hits)
- Remove trait-related defaultability classification branches

### M20. `Test/Mist.Tests/Semantics/UnknownInheritanceProhibitionTests.cs` — MODIFY (17 hits)
- Remove trait-related unknown inheritance prohibition tests

### M21. `Test/Mist.Tests/Semantics/RefTypeCheckerDiagnosticsPropertyTests.cs` — MODIFY (18 hits)
- Remove trait ref-type diagnostic test scenarios

### M22. `Test/Mist.Tests/Semantics/DefaultConstraintSatisfactionPropertyTests.cs` — MODIFY (9 hits)
- Remove trait constraint satisfaction scenarios

### M23. `Test/Mist.Tests/Semantics/NestedScopingPropertyTests.cs` — MODIFY (8 hits)
- Remove trait-in-class and class-in-trait nesting scenarios

### M24. `Test/Mist.Tests/Semantics/ConstraintResolutionTests.cs` — MODIFY (7 hits)
- Delete `TraitConstraint_PopulatesTraitConstraints` test
- Remove trait constraint resolution helpers

### M25. `Test/Mist.Tests/Semantics/TargetTypedNewDiagnosticTests.cs` — MODIFY (7 hits)
- Remove trait target-typed-new diagnostic tests

### M26. `Test/Mist.Tests/Semantics/SemanticModelBuilderUnitTests.cs` — MODIFY (7 hits)
- Remove trait registration unit tests

### M27. `Test/Mist.Tests/Semantics/RefStructParsingPropertyTests.cs` — MODIFY (7 hits)
- Remove trait-related ref struct parsing scenarios

### M28. `Test/Mist.Tests/Semantics/TypeKindTests.cs` — MODIFY (4 hits)
- Remove `TypeKind.Trait` test assertions

### M29. `Test/Mist.Tests/Semantics/LambdaTypeInferenceTests.cs` — MODIFY (2 hits)
- **KEEP** `[Trait("Feature", ...)]` — this is xUnit's `[Trait]` attribute, NOT Mist's trait concept

### M30. `Test/Mist.Tests/Semantics/FuncTypeAliasInteractionTests.cs` — MODIFY (1 hit)
- Remove trait-related comment or reference

### M31. `Test/Mist.Tests/Semantics/UnknownTypeTests.cs` — MODIFY (1 hit)
- Remove trait-related unknown type test reference

---

## Category N: Test — Analysis (20 files)

### N1. `Test/Mist.Tests/Analysis/TraitAssignabilityPropertyTests.cs` — DELETE (108 hits)
- Entire file: trait assignability type-checker tests

### N2. `Test/Mist.Tests/Analysis/UnknownTraitSatisfactionTests.cs` — DELETE (103 hits)
- Entire file: unknown type vs trait satisfaction tests

### N3. `Test/Mist.Tests/Analysis/AbstractTraitRefinementSatisfactionPropertyTests.cs` — DELETE (41 hits)
- Entire file: abstract+trait refinement coexistence tests

### N4. `Test/Mist.Tests/Analysis/ContextContractEnforcementPropertyTests.cs` — MODIFY (65 hits)
- Remove trait method context contract enforcement scenarios

### N5. `Test/Mist.Tests/Analysis/ContextCheckerContractTests.cs` — MODIFY (32 hits)
- Remove trait-related context checker contract tests

### N6. `Test/Mist.Tests/Analysis/OwnershipSignaturePropertyTests.cs` — MODIFY (20 hits)
- Remove `TraitMismatchScenario` and trait ownership signature tests

### N7. `Test/Mist.Tests/Analysis/EnumReadonlyAcceptancePropertyTests.cs` — MODIFY (16 hits)
- Remove trait-related enum readonly acceptance scenarios

### N8. `Test/Mist.Tests/Analysis/ReadonlyModifierPropagationPropertyTests.cs` — MODIFY (12 hits)
- Remove trait readonly propagation test scenarios

### N9. `Test/Mist.Tests/Analysis/ConstrainedMemberAccessibilityPropertyTests.cs` — MODIFY (8 hits)
- Remove trait-constrained member accessibility scenarios

### N10. `Test/Mist.Tests/Analysis/AssociatedTypeErrorPropertyTests.cs` — MODIFY (7 hits)
- Remove trait-related associated type error scenarios

### N11. `Test/Mist.Tests/Analysis/EntryPointWithAbstractBugTests.cs` — MODIFY (6 hits)
- Remove trait references from entry-point bug tests

### N12. `Test/Mist.Tests/Analysis/ConstraintDiagnosticsTests.cs` — MODIFY (4 hits)
- Remove trait constraint diagnostic tests

### N13. `Test/Mist.Tests/Analysis/OwnedTypeConstraintPropertyTests.cs` — MODIFY (3 hits)
- Remove trait-related owned type constraint scenarios

### N14. `Test/Mist.Tests/Analysis/AssociatedTypeUnresolvedRefPropertyTests.cs` — MODIFY (3 hits)
- Remove trait-related unresolved ref scenarios

### N15. `Test/Mist.Tests/Analysis/DependentAbstractParameterValidationTests.cs` — MODIFY (2 hits)
- Remove trait-related dependent parameter tests

### N16. `Test/Mist.Tests/Analysis/AssociatedTypeInvalidRefDiagnosticTests.cs` — MODIFY (2 hits)
- Remove trait-related invalid ref diagnostic scenarios

### N17. `Test/Mist.Tests/Analysis/LambdaDiagnosticsTests.cs` — MODIFY (2 hits)
- Remove trait-related lambda diagnostic scenarios

### N18. `Test/Mist.Tests/Analysis/TypeArgumentInferenceConstraintPropertyTests.cs` — MODIFY (1 hit)
- Remove trait type argument inference reference

### N19. `Test/Mist.Tests/Analysis/StorageBanNewDefaultTests.cs` — MODIFY (1 hit)
- Remove trait storage ban test reference

### N20. `Test/Mist.Tests/Analysis/EnumReadonlyRestrictionTests.cs` — MODIFY (1 hit)
- Remove trait enum readonly restriction reference

### N21. `Test/Mist.Tests/Analysis/CastOperatorPriorityPropertyTests.cs` — MODIFY (1 hit)
- Remove trait cast operator priority reference

### N22. `Test/Mist.Tests/Analysis/ConstraintSatisfactionPropertyTests.cs` — MODIFY (1 hit)
- Remove trait constraint satisfaction reference

---

## Category O: Test — Backend (6 files)

### O1. `Test/Mist.Tests/Backend/TraitEmissionPropertyTests.cs` — DELETE (171 hits)
- Entire file: trait C# emission property tests

### O2. `Test/Mist.Tests/Backend/GenericTraitEmissionPropertyTests.cs` — DELETE (147 hits)
- Entire file: generic trait C# emission property tests

### O3. `Test/Mist.Tests/Backend/ConstraintEmissionOrderingPropertyTests.cs` — MODIFY (6 hits)
- Remove trait constraint emission ordering scenarios

### O4. `Test/Mist.Tests/Backend/MistContextGenerationTests.cs` — MODIFY (4 hits)
- Remove trait context generation test scenarios

### O5. `Test/Mist.Tests/Backend/ConstraintEmissionTests.cs` — MODIFY (3 hits)
- Remove trait constraint emission test cases

### O6. `Test/Mist.Tests/Backend/FuncTypeEmissionTests.cs` — MODIFY (2 hits)
- Remove trait-related func type emission references

---

## Category P: Test — Readonly (2 files)

### P1. `Test/Mist.Tests/ReadonlyContexts/ReadonlyGenericsPropertyTests.cs` — MODIFY (5 hits)
- Remove trait-related readonly generics scenarios

### P2. `Test/Mist.Tests/ReadonlyContexts/ReadonlyParsingPropertyTests.cs` — MODIFY (7 hits)
- Remove trait readonly parsing scenarios (likely `readonly trait` pattern)

---

## Category Q: Test — Driver (2 files)

### Q1. `Test/Mist.Tests/Driver/TopLevelConstructCountPropertyTests.cs` — MODIFY (3 hits)
- Remove trait from top-level construct count expectations

### Q2. `Test/Mist.Tests/Driver/GlobalUsingResolutionEquivalencePropertyTests.cs` — MODIFY (1 hit)
- Remove trait-related global using resolution reference

---

## Category R: Samples — Source (21 files)

### R1. `samples/Traits/Printable.mist` — DELETE (4 hits)
### R2. `samples/Traits/User.mist` — DELETE (3 hits)
### R3. `samples/Traits/Product.mist` — DELETE (2 hits)
### R4. `samples/Traits/Program.mist` — DELETE (10 hits)
### R5. `samples/Traits/mist.toml` — DELETE (1 hit)

### R6. `samples/GenericTraits/Traits.mist` — DELETE (14 hits)
### R7. `samples/GenericTraits/Program.mist` — DELETE (14 hits)
### R8. `samples/GenericTraits/MemoryStream.mist` — DELETE (2 hits)
### R9. `samples/GenericTraits/Logger.mist` — DELETE (1 hit)
### R10. `samples/GenericTraits/IntComparer.mist` — DELETE (1 hit)
### R11. `samples/GenericTraits/StringComparer.mist` — DELETE (1 hit)
### R12. `samples/GenericTraits/ContainerTypes.mist` — DELETE (1 hit)
### R13. `samples/GenericTraits/ConverterTypes.mist` — DELETE (1 hit)
### R14. `samples/GenericTraits/ProviderTypes.mist` — DELETE (1 hit)
### R15. `samples/GenericTraits/mist.toml` — DELETE (1 hit)

### R16. `samples/GenericConstraints/Program.mist` — MODIFY (11 hits)
- Replace `trait Describable` → `abstract Describable`
- Replace `trait Transformable<T>` → `abstract Transformable<T>`
- Update comments and output strings referencing "trait constraint"

### R17. `samples/AssociatedTypes/Program.mist` — MODIFY (6 hits)
- Replace `trait Consumable` → `abstract Consumable`
- Update "Trait slot" comments and output strings

### R18. `samples/NestedScoping/NestedInAbstractTrait.mist` — MODIFY (6 hits)
- Replace `public trait SerializableTrait` → `public abstract Serializable`
- Update comments "Class nested inside a trait"
- Update output string `"SerializableTrait.Config (class in trait)"`
- Consider renaming file

### R19. `samples/NestedScoping/Program.mist` — MODIFY (17 hits)
- Replace `public trait Describable` → `public abstract Describable`
- Update all "Trait in class" comments and output strings
- Update "Class in trait" section comments and prints
- Update `DemoTraitInClass()` method name and return string

### R20. `samples/StructuredContext/Program.mist` — MODIFY (14 hits)
- Replace `trait ContextReader` → `abstract ContextReader`
- Update all "trait" comments (~10 occurrences)
- Update output strings referencing trait dispatch

### R21. `samples/DependentTypes/Program.mist` — MODIFY (3 hits)
- Replace `trait ISized<N:int>` → `abstract ISized<N:int>`
- Update "Dependent types on traits" section header

### R22. `samples/Enums/Program.mist` — MODIFY (7 hits)
- Update comments referencing "trait underlying types"
- Update output strings

### R23. `samples/AllFeatures/Program.mist` — MODIFY (2 hits)
- Remove trait demo calls (Traits, GenericTraits RunDemo invocations)

### R24. `samples/AllFeatures/mist.toml` — MODIFY (2 hits)
- Remove `Traits` and `GenericTraits` project references

---

## Category S: Samples — Emitted Output (22 files, all REGENERATED)

> These are compiler-generated `.cs` files in `out/` directories.
> They will be automatically regenerated after the compiler changes.
> Listed for completeness — no manual edits needed.

### S1–S9. `samples/AllFeatures/out/references/GenericTraits/*.cs` — REGENERATED (9 files)
- `ContainerTypes.cs`, `ConverterTypes.cs`, `IntComparer.cs`, `Logger.cs`, `MemoryStream.cs`, `Program.cs`, `ProviderTypes.cs`, `StringComparer.cs`, `Traits.cs`
- These will be deleted when the GenericTraits sample is deleted

### S10–S13. `samples/AllFeatures/out/references/Traits/*.cs` — REGENERATED (4 files)
- `Printable.cs`, `Product.cs`, `Program.cs`, `User.cs`
- These will be deleted when the Traits sample is deleted

### S14. `samples/AllFeatures/out/Program.cs` — REGENERATED (2 hits)
### S15. `samples/AllFeatures/out/references/NestedScoping/NestedInInterfaceTrait.cs` — REGENERATED (1 hit)
### S16. `samples/AllFeatures/out/references/NestedScoping/Program.cs` — REGENERATED (7 hits)
### S17. `samples/AllFeatures/out/references/StructuredContext/Program.cs` — REGENERATED (5 hits)
### S18. `samples/AllFeatures/out/references/AssociatedTypes/Program.cs` — REGENERATED (1 hit)
### S19. `samples/AllFeatures/out/references/DependentTypes/Program.cs` — REGENERATED (1 hit)
### S20. `samples/AllFeatures/out/references/Enums/Program.cs` — REGENERATED (1 hit)
### S21. `samples/AllFeatures/out/references/GenericConstraints/Program.cs` — REGENERATED (2 hits)
### S22. `samples/GenericConstraints/out/Program.cs` — REGENERATED (2 hits)
### S23. `samples/NestedScoping/out/NestedInInterfaceTrait.cs` — REGENERATED (1 hit)
### S24. `samples/NestedScoping/out/Program.cs` — REGENERATED (7 hits)
### S25. `samples/StructuredContext/out/Program.cs` — REGENERATED (5 hits)
### S26. `samples/AssociatedTypes/out/Program.cs` — REGENERATED (1 hit)
### S27. `samples/DependentTypes/out/Program.cs` — REGENERATED (1 hit)
### S28. `samples/Enums/out/Program.cs` — REGENERATED (1 hit)

---

## Category T: Samples — Baselines (1 file)

### T1. `samples/__baselines__/AllFeatures.out` — MODIFY (24 hits)
- Remove all Traits demo output lines
- Remove all GenericTraits demo output lines
- Update remaining lines:
  - `"--- Abstract Type/Trait underlying types ---"` → rename
  - `"4. trait constraint:"` → rename
  - `"16. generic trait:"` → rename
  - `"--- 9. Dependent types on traits ---"` → rename
  - `"trait-satisfying (CountReader):"` → rename
  - `"--- 16. Trait dispatch with with-clause ---"` → rename
  - `"--- 4. Trait in class ---"` / `"Outer.Describable trait nested in class"` → rename
  - `"--- 8. Class in trait ---"` → rename
  - `"--- Trait Slot ---"` → rename

---

## Category U: Editor / Syntax Highlighting (3 files)

### U1. `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` — MODIFY (6 hits)
- Remove `trait` from declaration keyword pattern
- Remove standalone `trait` declaration-name rule + `entity.name.type.trait.mist` scope
- Remove `trait` from generic declaration pattern match
- Update associated type slot comment

### U2. `Editor/mist-syntax-highlighting/package.json` — MODIFY (1 hit)
- Remove `entity.name.type.trait.mist` scope from contributing section

### U3. `Editor/mist-syntax-highlighting/tests/dependent-types.mist` — MODIFY (1 hit)
- Replace `trait Bounded<Limit:byte>` → `abstract Bounded<Limit:byte>`

---

## Category V: Book — Chapters (16 files)

### V1. `book/src/object-model/traits.md` — DELETE (40 hits)
- Entire chapter on traits

### V2. `book/src/SUMMARY.md` — MODIFY (1 hit)
- Remove `- [Traits](object-model/traits.md)` entry

### V3. `book/src/introduction.md` — MODIFY (3 hits)
- Remove/update "Trait" bullet point
- Update "Object Model" description
- Update "Associated Types" description

### V4. `book/src/type-system/generic-constraints.md` — MODIFY (9 hits)
- Delete/rewrite "Trait Constraint" section
- Remove `TraitName` row from summary table
- Update `include` directives for deleted snippet files
- Update text mentioning "trait" throughout

### V5. `book/src/type-system/dependent-types.md` — MODIFY (5 hits)
- Rename section "Dependent Types on Traits" → "Dependent Types on Abstract Types"
- Update text and `include` directives

### V6. `book/src/type-system/associated-types.md` — MODIFY (8 hits)
- Replace trait references with abstract type references
- Update `include` directives

### V7. `book/src/type-system/generics.md` — MODIFY (4 hits)
- Remove/update trait references in generics discussion
- Update `include` directives

### V8. `book/src/type-system/refinement-types.md` — MODIFY (1 hit)
- Remove trait reference

### V9. `book/src/object-model/target-typed-new.md` — MODIFY (2 hits)
- Update "Traits" restriction section

### V10. `book/src/object-model/readonly.md` — MODIFY (1 hit)
- Update table row mentioning "Trait"

### V11. `book/src/advanced/default-keyword.md` — MODIFY (4 hits)
- Remove trait default-keyword restrictions/examples

### V12. `book/src/advanced/nested-scoping.md` — MODIFY (1 hit)
- Remove trait nesting reference

### V13. `book/src/advanced/ownership.md` — MODIFY (1 hit)
- Remove trait ownership reference

### V14. `book/src/advanced/structured-concurrency.md` — MODIFY (1 hit)
- Remove trait structured concurrency reference

### V15. `book/src/tooling/editor-support.md` — MODIFY (1 hit)
- Remove `trait` from declaration keywords list

### V16. `book/src/tooling/project-configuration.md` — MODIFY (1 hit)
- Remove `Traits = { path = "../Traits" }` reference

---

## Category W: Book — Snippets (10 files)

### W1. `book/snippets/object-model/traits/declaration.mist` — DELETE
### W2. `book/snippets/object-model/traits/extending.mist` — DELETE
### W3. `book/snippets/object-model/traits/polymorphic-usage.mist` — DELETE
### W4. `book/snippets/object-model/traits/structural-satisfaction.mist` — DELETE
### W5. `book/snippets/object-model/traits/with-block-fields.mist` — DELETE
### W6. `book/snippets/object-model/traits/with-block-methods.mist` — DELETE
- Delete entire `book/snippets/object-model/traits/` directory (plus matching `.cs` files)

### W7. `book/snippets/type-system/generic-constraints/trait-constraint.mist` — DELETE
- Also delete matching `.cs` file

### W8. `book/snippets/type-system/dependent-types/traits.mist` — DELETE
- Also delete matching `.cs` file

### W9. `book/snippets/type-system/generics/traits.mist` — DELETE
- Also delete matching `.cs` file

### W10. `book/snippets/type-system/associated-types/trait-slot.mist` — DELETE
- Also delete matching `.cs` file

---

## Category X: Book — Theme (2 files)

### X1. `book/theme/mist-lang.js` — MODIFY (4 hits)
- Remove `trait` from KEYWORDS string
- Remove `trait` from declaration pattern regex (2 occurrences)

### X2. `book/theme/highlight.js` — MODIFY (10 hits)
- Remove `trait` from Mist language block (the custom Mist section at bottom)
- **KEEP** trait references in PHP, Rust, and other third-party language blocks — those are correct

---

## Category Y: Book — Output (3 files, all REGENERATED)

### Y1. `book/output/highlight-a57a67d3.js` — REGENERATED (10 hits)
### Y2. `book/output/searchindex-b9339578.js` — REGENERATED (1 hit)
### Y3. `book/output/toc-2f3eb3ba.js` — REGENERATED (1 hit)
- All rebuilt by `mdbook build` after source changes

---

## Category Z: Distribute / Steering (3 files)

### Z1. `distribute/steering/mist-advanced-features.md` — MODIFY (12 hits)
- Delete entire "Traits (Structural Typing)" section
- Delete "Traits vs Interfaces" table
- Update "Associated Types" section trait mentions
- Remove trait from opening sentence

### Z2. `distribute/steering/mist-language.md` — MODIFY (6 hits)
- Remove "traits" from `#mist-advanced-features` description
- Delete entire "Traits (Structural Typing)" section
- Remove trait from closing bullet list

### Z3. `distribute/steering/README.md` — MODIFY (1 hit)
- Remove "Traits" from `mist-advanced-features.md` description

---

## Category AA: Project / Design Notes (1 file)

### AA1. `project/design-notes/regions-and-lifetimes.md` — MODIFY (1 hit)
- Remove or update trait reference in design note

---

## Summary

| Category | Files | DELETE | MODIFY | REGENERATED |
|----------|-------|--------|--------|-------------|
| A. Grammar | 2 | 0 | 2 | 0 |
| B. Contracts | 3 | 0 | 3 | 0 |
| C. Model Types | 7 | 2 | 5 | 0 |
| D. Model Core | 5 | 2 | 3 | 0 |
| E. Semantics Other | 2 | 0 | 2 | 0 |
| F. Analysis | 5 | 0 | 5 | 0 |
| G. Backends | 4 | 0 | 4 | 0 |
| H. Diagnostics | 1 | 0 | 1 | 0 |
| I. Parsing | 1 | 0 | 1 | 0 |
| J. Driver | 1 | 0 | 1 | 0 |
| K. Test Generators | 4 | 1 | 3 | 0 |
| L. Test Parsing | 7 | 2 | 5 | 0 |
| M. Test Semantics | 31 | 17 | 14 | 0 |
| N. Test Analysis | 22 | 3 | 19 | 0 |
| O. Test Backend | 6 | 2 | 4 | 0 |
| P. Test Readonly | 2 | 0 | 2 | 0 |
| Q. Test Driver | 2 | 0 | 2 | 0 |
| R. Samples Source | 24 | 15 | 9 | 0 |
| S. Samples Output | 28 | 0 | 0 | 28 |
| T. Baselines | 1 | 0 | 1 | 0 |
| U. Editor | 3 | 0 | 3 | 0 |
| V. Book Chapters | 16 | 1 | 15 | 0 |
| W. Book Snippets | 10 | 10 | 0 | 0 |
| X. Book Theme | 2 | 0 | 2 | 0 |
| Y. Book Output | 3 | 0 | 0 | 3 |
| Z. Distribute | 3 | 0 | 3 | 0 |
| AA. Design Notes | 1 | 0 | 1 | 0 |
| **TOTAL** | **196** | **55** | **109** | **31** |

> **55 files deleted**, **109 files modified**, **31 files auto-regenerated** = 196 total
> Total grep hits: **5,082 lines** containing "trait" (case-insensitive)
