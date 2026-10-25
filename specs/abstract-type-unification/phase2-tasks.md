# Phase 2 — Purge Traits: Detailed Task Plan

> Each task has: description, files affected, verification command(s).
> Verification = how we KNOW this task is done (file deleted, grep returns 0 hits, etc.)
> Status: `[ ]` not started, `[x]` done

---

## Task Group 1: Grammar

### [ ] 2.1 — Delete TRAIT token from keywords.storm
**Files:** `Source/Mist/Grammar/keywords.storm`
**Action:** Delete `token TRAIT = "trait";` and the `// Trait-related keywords` comment. Keep `WITH`/`AS`.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Grammar/keywords.storm
# Expected: 0 hits
```

### [ ] 2.2 — Delete trait rules from Mist.storm
**Files:** `Source/Mist/Grammar/Mist.storm`
**Action:**
- Remove `traitDeclaration:TraitDeclaration as TraitType` from `TypeDeclaration`
- Delete rules: `TraitDeclaration`, `TraitMemberDeclaration` (+ all 7 variants), `TraitFieldDeclaration`, `TraitMethodDeclaration`, `TraitPropertyDeclaration`
- Delete `WithDeclaration` rule, `MemberMapping` rule
- Remove `WithItem` variant from `TopLevelItem`
- Add error recovery rule for `trait` keyword → helpful diagnostic
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Grammar/Mist.storm
# Expected: only the error recovery rule (1-2 hits max)
```

---

## Task Group 2: Semantic Model Contracts & Types

### [ ] 2.3 — Remove Trait from SymbolKind and TypeKind enums
**Files:** `Source/Mist/Semantics/Contracts/ISymbol.cs`, `Source/Mist/Semantics/Contracts/IType.cs`
**Action:** Remove `Trait` variant from both enums.
**Verify:**
```bash
grep -n 'Trait' Source/Mist/Semantics/Contracts/ISymbol.cs Source/Mist/Semantics/Contracts/IType.cs
# Expected: 0 hits
```

### [ ] 2.4 — Remove TraitSatisfactionMap from ISemanticModel
**Files:** `Source/Mist/Semantics/Contracts/ISemanticModel.cs`
**Action:** Remove `TraitSatisfactionMap` property and doc comments.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Contracts/ISemanticModel.cs
# Expected: 0 hits
```

### [ ] 2.5 — Delete TraitType.cs
**Files:** `Source/Mist/Semantics/Model/Types/TraitType.cs`
**Action:** Delete entire file.
**Verify:** File does not exist.

### [ ] 2.6 — Delete ConstructedTraitType.cs
**Files:** `Source/Mist/Semantics/Model/Types/ConstructedTraitType.cs`
**Action:** Delete entire file.
**Verify:** File does not exist.

### [ ] 2.7 — Delete TraitSatisfactionMap.cs
**Files:** `Source/Mist/Semantics/Model/TraitSatisfactionMap.cs`
**Action:** Delete entire file.
**Verify:** File does not exist.

### [ ] 2.8 — Delete WithBlockRegistry.cs
**Files:** `Source/Mist/Semantics/Model/WithBlockRegistry.cs`
**Action:** Delete entire file.
**Verify:** File does not exist.

### [ ] 2.9 — Remove TraitConstraints from TypeParameterConstraints
**Files:** `Source/Mist/Semantics/Model/Types/TypeParameterConstraints.cs`
**Action:** Remove `TraitConstraints` property and doc comment.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/Types/TypeParameterConstraints.cs
# Expected: 0 hits
```

### [ ] 2.10 — Remove SatisfiedTraits from ClassType and StructType
**Files:** `Source/Mist/Semantics/Model/Types/ClassType.cs`, `Source/Mist/Semantics/Model/Types/StructType.cs`
**Action:** Remove `SatisfiedTraits` property/list from both types.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/Types/ClassType.cs Source/Mist/Semantics/Model/Types/StructType.cs
# Expected: 0 hits
```

### [ ] 2.11 — Clean trait from TypeContainment and AssociatedTypeSlotType
**Files:** `Source/Mist/Semantics/Model/Types/TypeContainment.cs`, `Source/Mist/Semantics/Model/Types/AssociatedTypeSlotType.cs`
**Action:** Remove `TraitType`/`ConstructedTraitType` pattern matches.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/Types/TypeContainment.cs Source/Mist/Semantics/Model/Types/AssociatedTypeSlotType.cs
# Expected: 0 hits
```

---

## Task Group 3: Semantic Model Core

### [ ] 2.12 — Remove trait from SemanticModel.cs
**Files:** `Source/Mist/Semantics/Model/SemanticModel.cs`
**Action:** Remove `traitSatisfactionMap` constructor param, `TraitSatisfactionMap` property.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/SemanticModel.cs
# Expected: 0 hits
```

### [ ] 2.13 — Purge trait from SemanticModelBuilder.cs (883 hits — largest file)
**Files:** `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`
**Action:**
- Delete 11 diagnostic IDs: `TraitWithUndefinedType`, `TraitWithUndefinedTrait`, `TraitDuplicateWith`, `TraitWithUndefinedSource`, `TraitWithUndefinedTarget`, `TraitWithTypeMismatch`, `TraitMemberVisibility`, `TraitWithUnsatisfied`, `TraitParentNotTrait`, `TraitInheritanceCycle`, `TraitDuplicateParent`
- Delete methods: `RegisterTraitDeclaration`, `Pass1_ResolveTraitParents`, `Pass1_DetectTraitCycles`, `DetectTraitCyclesDfsVisit`, `Pass1_ResolveWithBlocks` (+ helpers), `Pass1_ResolveTraitSatisfaction`, `ConcreteTypeSatisfiesTrait`, `DetectDuplicateAssociatedTypeSlots` (trait member handling)
- Remove `case TraitTypeTypeDeclarationNode` in registration
- Remove `TraitSatisfactionMap` construction/passing
- Remove all `foreach (var traitConstraint in constraints.TraitConstraints)` loops (~6 locations)
- Remove `SymbolKind.Trait` and `TraitType tt` pattern matches
- Remove trait doc comments
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/SemanticModelBuilder.cs
# Expected: 0 hits
```

### [ ] 2.14 — Clean trait from DefaultValueKind.cs and BindingKind.cs
**Files:** `Source/Mist/Semantics/Model/DefaultValueKind.cs`, `Source/Mist/Semantics/BindingKind.cs`
**Action:** Update comments removing "trait" references.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/Model/DefaultValueKind.cs Source/Mist/Semantics/BindingKind.cs
# Expected: 0 hits
```

### [ ] 2.15 — Clean trait from ReadonlyInferenceEngine.cs
**Files:** `Source/Mist/Semantics/ReadonlyInferenceEngine.cs`
**Action:** Remove trait-related branches in readonly inference logic.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Semantics/ReadonlyInferenceEngine.cs
# Expected: 0 hits
```

---

## Task Group 4: Analysis / Type Checker

### [ ] 2.16 — Purge trait from TypeCheckerAnalyzer.cs (293 hits)
**Files:** `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`
**Action:**
- Delete diagnostic: `ConstraintTraitNotSatisfied`
- Delete methods: `SatisfiesTrait`, `IsTraitTargetType`, `CheckReadonlyAbstractAndTraitContracts`, `CheckReadonlyTraitContracts`, `CheckReadonlyMembersOfTrait`, `FindTraitImplementingMember`, `HasMatchingTraitConstraint`
- Remove `target is TraitType`/`ConstructedTraitType` assignability branches (2 overloads)
- Remove `TypeKind.Trait` in cast/default logic
- Remove `case TraitType:` in default operator
- Remove `DiagnosticId.DefaultOnTrait`/`TargetNewTrait` usage
- Remove `ConstructedTraitType` substitution logic in member access
- Remove `baseConstraints.TraitConstraints` loops
- Remove `MIST_TRAIT_UNSATISFIED` emission
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs
# Expected: 0 hits
```

### [ ] 2.17 — Purge trait from OwnershipAnalyzer.cs (35 hits)
**Files:** `Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs`
**Action:** Remove all TraitType pattern matches and trait ownership validation logic.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Analysis/Builtin/OwnershipAnalyzer.cs
# Expected: 0 hits
```

### [ ] 2.18 — Purge trait from ContextCheckerAnalyzer.cs (8 hits)
**Files:** `Source/Mist/Analysis/Builtin/ContextCheckerAnalyzer.cs`
**Action:** Remove trait method with-clause context checking.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Analysis/Builtin/ContextCheckerAnalyzer.cs
# Expected: 0 hits
```

### [ ] 2.19 — Purge trait from CooperationContractVerifier.cs and ReadonlyAnalyzer.cs
**Files:** `Source/Mist/Analysis/Builtin/CooperationContractVerifier.cs`, `Source/Mist/Analysis/Builtin/ReadonlyAnalyzer.cs`
**Action:** Remove trait-related branches (2 hits each).
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Analysis/Builtin/CooperationContractVerifier.cs Source/Mist/Analysis/Builtin/ReadonlyAnalyzer.cs
# Expected: 0 hits
```

---

## Task Group 5: Backends / Emitter

### [ ] 2.20 — Purge trait from CSharpEmitter.cs (148 hits)
**Files:** `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`
**Action:**
- Delete methods: `EmitTraitDeclaration`, `EmitTraitFieldAsProperty`, `EmitTraitMethodDeclaration`, `EmitTraitPropertyDeclaration`, `EmitExplicitTraitImplementations`, `EmitTraitMethodParametersWithSubstitution`, `EmitTraitMethodArguments`
- Remove `case TraitTypeTypeDeclarationNode`
- Remove `TraitType tt` pattern matches in namespace resolution (4 spots)
- Remove `classType.SatisfiedTraits` iteration
- Remove `TraitConstraints` loops in constraint emission
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs
# Expected: 0 hits
```

### [ ] 2.21 — Purge trait from CSharpTypeMap.cs
**Files:** `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs`
**Action:** Remove `ConstructedTraitType ctt =>` and `TraitType t =>` cases, comment.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs
# Expected: 0 hits
```

### [ ] 2.22 — Purge trait from DependentTypeHelpersGenerator.cs and NeedsAsyncPropagationPass.cs
**Files:** `Source/Mist/Backends/CSharpTranspiler/DependentTypeHelpersGenerator.cs`, `Source/Mist/Backends/CSharpTranspiler/NeedsAsyncPropagationPass.cs`
**Action:** Remove `TraitType tt` cases (2+1 hits).
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Backends/CSharpTranspiler/DependentTypeHelpersGenerator.cs Source/Mist/Backends/CSharpTranspiler/NeedsAsyncPropagationPass.cs
# Expected: 0 hits
```

---

## Task Group 6: Common, Parsing, Driver

### [ ] 2.23 — Purge trait DiagnosticIds from DiagnosticId.cs (14 hits)
**Files:** `Source/Mist/Common/Diagnostics/DiagnosticId.cs`
**Action:** Remove all trait-related `DiagnosticId` constants.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Common/Diagnostics/DiagnosticId.cs
# Expected: 0 hits
```

### [ ] 2.24 — Purge trait from MistPrettyPrinter.cs (28 hits)
**Files:** `Source/Mist/Parsing/MistPrettyPrinter.cs`
**Action:** Delete `EmitTraitDeclaration`, `EmitTraitMemberDeclaration`, `EmitTraitFieldDeclaration`, `EmitTraitMethodDeclaration`, `EmitTraitPropertyDeclaration`, all `TraitXxxMember` branches, `WithDeclaration` trait emission.
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Parsing/MistPrettyPrinter.cs
# Expected: 0 hits
```

### [ ] 2.25 — Purge trait from CompilerDriver.cs (1 hit)
**Files:** `Source/Mist/Driver/CompilerDriver.cs`
**Action:** Remove trait reference (comment or pass invocation).
**Verify:**
```bash
grep -ni 'trait' Source/Mist/Driver/CompilerDriver.cs
# Expected: 0 hits
```

---

## Task Group 7: Delete Trait-Only Test Files (24 files)

### [ ] 2.26 — Delete trait test generators
**Files to delete:**
- `Test/Mist.Tests/Generators/MistTraitGenerator.cs`
**Verify:** File does not exist.

### [ ] 2.27 — Delete trait parsing tests
**Files to delete:**
- `Test/Mist.Tests/Parsing/TraitRoundTripPropertyTests.cs`
- `Test/Mist.Tests/Parsing/GenericTraitRoundTripPropertyTests.cs`
**Verify:** Files do not exist.

### [ ] 2.28 — Delete trait semantics tests (17 files)
**Files to delete:**
- `Test/Mist.Tests/Semantics/TraitEffectiveMembersPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitStructuralMatchSubstitutionPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitStructuralMatchPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitAssignabilityPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitWithBlockSatisfactionPropertyTests.cs`
- `Test/Mist.Tests/Semantics/GenericTraitTypeParamRegistrationPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitTypeRegistrationPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitArityDiagnosticsPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitAbstractCoexistencePropertyTests.cs`
- `Test/Mist.Tests/Semantics/WithBlockGenericTraitPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitInheritanceValidationPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitAssignabilityGenericPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitMemberAccessPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitWithBlockValidationPropertyTests.cs`
- `Test/Mist.Tests/Semantics/TraitMemberAccessGenericPropertyTests.cs`
- `Test/Mist.Tests/Semantics/WithBlockResolutionTests.cs`
- `Test/Mist.Tests/Semantics/TraitAssignabilityDebugTest.cs`
**Verify:**
```bash
ls Test/Mist.Tests/Semantics/Trait* Test/Mist.Tests/Semantics/WithBlock* 2>&1
# Expected: "No such file or directory" for all
```

### [ ] 2.29 — Delete trait analysis tests (3 files)
**Files to delete:**
- `Test/Mist.Tests/Analysis/TraitAssignabilityPropertyTests.cs`
- `Test/Mist.Tests/Analysis/UnknownTraitSatisfactionTests.cs`
- `Test/Mist.Tests/Analysis/AbstractTraitRefinementSatisfactionPropertyTests.cs`
**Verify:**
```bash
ls Test/Mist.Tests/Analysis/Trait* Test/Mist.Tests/Analysis/Unknown*Trait* Test/Mist.Tests/Analysis/Abstract*Trait* 2>&1
# Expected: "No such file or directory" for all
```

### [ ] 2.30 — Delete trait backend tests (2 files)
**Files to delete:**
- `Test/Mist.Tests/Backend/TraitEmissionPropertyTests.cs`
- `Test/Mist.Tests/Backend/GenericTraitEmissionPropertyTests.cs`
**Verify:**
```bash
ls Test/Mist.Tests/Backend/Trait* Test/Mist.Tests/Backend/GenericTrait* 2>&1
# Expected: "No such file or directory" for all
```

---

## Task Group 8: Modify Remaining Test Files (trait references to remove)

### [ ] 2.31 — Clean trait from test generators (3 files)
**Files:**
- `Test/Mist.Tests/Generators/MistContextRoundTripGenerator.cs` (12 hits)
- `Test/Mist.Tests/Generators/MistRefParameterRoundTripGenerator.cs` (8 hits)
- `Test/Mist.Tests/Generators/MistNestedScopingGenerator.cs` (7 hits)
**Action:** Delete trait-related generator methods, remove `Trait` from enums/OneOf lists, update comments.
**Verify:**
```bash
grep -ni 'trait' Test/Mist.Tests/Generators/MistContextRoundTripGenerator.cs Test/Mist.Tests/Generators/MistRefParameterRoundTripGenerator.cs Test/Mist.Tests/Generators/MistNestedScopingGenerator.cs
# Expected: 0 hits
```

### [ ] 2.32 — Clean trait from parsing tests (5 files)
**Files:**
- `Test/Mist.Tests/Parsing/WithStatementParsingTests.cs` (15 hits)
- `Test/Mist.Tests/Parsing/AdhocRefinementRoundTripPropertyTests.cs` (8 hits)
- `Test/Mist.Tests/Parsing/AssociatedTypeRoundTripPropertyTests.cs` (17 hits)
- `Test/Mist.Tests/Parsing/WithStatementPrettyPrinterTests.cs` (4 hits)
- `Test/Mist.Tests/Parsing/ContextRoundTripPropertyTests.cs` (1 hit)
**Action:** Delete trait-specific tests/methods/arrays, update comments.
**Verify:**
```bash
grep -ni 'trait' Test/Mist.Tests/Parsing/WithStatementParsingTests.cs Test/Mist.Tests/Parsing/AdhocRefinementRoundTripPropertyTests.cs Test/Mist.Tests/Parsing/AssociatedTypeRoundTripPropertyTests.cs Test/Mist.Tests/Parsing/WithStatementPrettyPrinterTests.cs Test/Mist.Tests/Parsing/ContextRoundTripPropertyTests.cs
# Expected: 0 hits
```

### [ ] 2.33 — Clean trait from semantics tests (14 files, MODIFY only)
**Files:** `ExtensionTypeCheckerPropertyTests.cs`, `OptionalDefaultabilityClassificationPropertyTests.cs`, `UnknownInheritanceProhibitionTests.cs`, `RefTypeCheckerDiagnosticsPropertyTests.cs`, `DefaultConstraintSatisfactionPropertyTests.cs`, `NestedScopingPropertyTests.cs`, `ConstraintResolutionTests.cs`, `TargetTypedNewDiagnosticTests.cs`, `SemanticModelBuilderUnitTests.cs`, `RefStructParsingPropertyTests.cs`, `TypeKindTests.cs`, `FuncTypeAliasInteractionTests.cs`, `UnknownTypeTests.cs`, `LambdaTypeInferenceTests.cs`
**Action:** Remove trait scenarios/branches/references. **Exception:** Keep xUnit `[Trait("Feature",...)]` attributes in `LambdaTypeInferenceTests.cs` (that's xUnit metadata, not Mist's trait).
**Verify:**
```bash
grep -ni 'trait' Test/Mist.Tests/Semantics/ExtensionTypeCheckerPropertyTests.cs Test/Mist.Tests/Semantics/OptionalDefaultabilityClassificationPropertyTests.cs Test/Mist.Tests/Semantics/UnknownInheritanceProhibitionTests.cs Test/Mist.Tests/Semantics/RefTypeCheckerDiagnosticsPropertyTests.cs Test/Mist.Tests/Semantics/DefaultConstraintSatisfactionPropertyTests.cs Test/Mist.Tests/Semantics/NestedScopingPropertyTests.cs Test/Mist.Tests/Semantics/ConstraintResolutionTests.cs Test/Mist.Tests/Semantics/TargetTypedNewDiagnosticTests.cs Test/Mist.Tests/Semantics/SemanticModelBuilderUnitTests.cs Test/Mist.Tests/Semantics/RefStructParsingPropertyTests.cs Test/Mist.Tests/Semantics/TypeKindTests.cs Test/Mist.Tests/Semantics/FuncTypeAliasInteractionTests.cs Test/Mist.Tests/Semantics/UnknownTypeTests.cs
# Expected: 0 hits
# LambdaTypeInferenceTests.cs: only xUnit [Trait] attributes remain
```

### [ ] 2.34 — Clean trait from analysis tests (19 files, MODIFY only)
**Files:** `ContextContractEnforcementPropertyTests.cs`, `ContextCheckerContractTests.cs`, `OwnershipSignaturePropertyTests.cs`, `EnumReadonlyAcceptancePropertyTests.cs`, `ReadonlyModifierPropagationPropertyTests.cs`, `ConstrainedMemberAccessibilityPropertyTests.cs`, `AssociatedTypeErrorPropertyTests.cs`, `EntryPointWithAbstractBugTests.cs`, `ConstraintDiagnosticsTests.cs`, `OwnedTypeConstraintPropertyTests.cs`, `AssociatedTypeUnresolvedRefPropertyTests.cs`, `DependentAbstractParameterValidationTests.cs`, `AssociatedTypeInvalidRefDiagnosticTests.cs`, `LambdaDiagnosticsTests.cs`, `TypeArgumentInferenceConstraintPropertyTests.cs`, `StorageBanNewDefaultTests.cs`, `EnumReadonlyRestrictionTests.cs`, `CastOperatorPriorityPropertyTests.cs`, `ConstraintSatisfactionPropertyTests.cs`
**Action:** Remove trait-related test scenarios, assertions, and helper methods.
**Verify:**
```bash
grep -rni 'trait' Test/Mist.Tests/Analysis/ | grep -v 'node_modules'
# Expected: 0 hits (all Trait* files already deleted in 2.29)
```

### [ ] 2.35 — Clean trait from backend tests (4 files, MODIFY only)
**Files:** `ConstraintEmissionOrderingPropertyTests.cs`, `MistContextGenerationTests.cs`, `ConstraintEmissionTests.cs`, `FuncTypeEmissionTests.cs`
**Action:** Remove trait constraint emission scenarios and references.
**Verify:**
```bash
grep -rni 'trait' Test/Mist.Tests/Backend/ | grep -v 'node_modules'
# Expected: 0 hits (all Trait* files already deleted in 2.30)
```

### [ ] 2.36 — Clean trait from readonly and driver tests (4 files)
**Files:** `ReadonlyGenericsPropertyTests.cs`, `ReadonlyParsingPropertyTests.cs`, `TopLevelConstructCountPropertyTests.cs`, `GlobalUsingResolutionEquivalencePropertyTests.cs`
**Action:** Remove trait-related scenarios.
**Verify:**
```bash
grep -ni 'trait' Test/Mist.Tests/ReadonlyContexts/ReadonlyGenericsPropertyTests.cs Test/Mist.Tests/ReadonlyContexts/ReadonlyParsingPropertyTests.cs Test/Mist.Tests/Driver/TopLevelConstructCountPropertyTests.cs Test/Mist.Tests/Driver/GlobalUsingResolutionEquivalencePropertyTests.cs
# Expected: 0 hits
```

---

## Task Group 9: Samples

### [ ] 2.37 — Delete samples/Traits/ directory
**Files:** `samples/Traits/` (5 files: Printable.mist, User.mist, Product.mist, Program.mist, mist.toml)
**Action:** Delete entire directory.
**Verify:** Directory does not exist.

### [ ] 2.38 — Delete samples/GenericTraits/ directory
**Files:** `samples/GenericTraits/` (10 files)
**Action:** Delete entire directory.
**Verify:** Directory does not exist.

### [ ] 2.39 — Migrate samples/GenericConstraints/Program.mist (11 hits)
**Files:** `samples/GenericConstraints/Program.mist`
**Action:** Replace `trait Describable` → `abstract Describable`, `trait Transformable<T>` → `abstract Transformable<T>`, update comments/output strings.
**Verify:**
```bash
grep -ni 'trait' samples/GenericConstraints/Program.mist
# Expected: 0 hits
```

### [ ] 2.40 — Migrate samples/AssociatedTypes/Program.mist (6 hits)
**Files:** `samples/AssociatedTypes/Program.mist`
**Action:** Replace `trait Consumable` → `abstract Consumable`, update "Trait slot" comments/strings.
**Verify:**
```bash
grep -ni 'trait' samples/AssociatedTypes/Program.mist
# Expected: 0 hits
```

### [ ] 2.41 — Migrate samples/NestedScoping/ (23 hits across 2 files)
**Files:** `samples/NestedScoping/Program.mist`, `samples/NestedScoping/NestedInAbstractTrait.mist`
**Action:** Replace `trait` → `abstract`, update comments/strings, consider renaming `NestedInAbstractTrait.mist` → `NestedInAbstract.mist`.
**Verify:**
```bash
grep -rni 'trait' samples/NestedScoping/*.mist
# Expected: 0 hits
```

### [ ] 2.42 — Migrate samples/StructuredContext/Program.mist (14 hits)
**Files:** `samples/StructuredContext/Program.mist`
**Action:** Replace `trait ContextReader` → `abstract ContextReader`, update all trait comments/strings.
**Verify:**
```bash
grep -ni 'trait' samples/StructuredContext/Program.mist
# Expected: 0 hits
```

### [ ] 2.43 — Migrate samples/DependentTypes/Program.mist (3 hits)
**Files:** `samples/DependentTypes/Program.mist`
**Action:** Replace `trait ISized<N:int>` → `abstract ISized<N:int>`, update section header.
**Verify:**
```bash
grep -ni 'trait' samples/DependentTypes/Program.mist
# Expected: 0 hits
```

### [ ] 2.44 — Migrate samples/Enums/Program.mist (7 hits)
**Files:** `samples/Enums/Program.mist`
**Action:** Update comments and output strings referencing "trait underlying types".
**Verify:**
```bash
grep -ni 'trait' samples/Enums/Program.mist
# Expected: 0 hits
```

### [ ] 2.45 — Update samples/AllFeatures/ orchestrator (4 hits)
**Files:** `samples/AllFeatures/Program.mist`, `samples/AllFeatures/mist.toml`
**Action:** Remove Traits/GenericTraits demo calls and project references.
**Verify:**
```bash
grep -ni 'trait' samples/AllFeatures/Program.mist samples/AllFeatures/mist.toml
# Expected: 0 hits
```

### [ ] 2.46 — Regenerate all sample out/ directories
**Files:** All `samples/*/out/` directories (28 files with hits)
**Action:** Run the Mist compiler on each modified sample to regenerate emitted C#. Delete `samples/AllFeatures/out/references/Traits/` and `samples/AllFeatures/out/references/GenericTraits/` directories entirely.
**Verify:**
```bash
grep -rni 'trait' samples/*/out/ samples/AllFeatures/out/references/
# Expected: 0 hits (excluding any third-party referenced code)
```

### [ ] 2.47 — Update samples/__baselines__/AllFeatures.out (24 hits)
**Files:** `samples/__baselines__/AllFeatures.out`
**Action:** Remove Traits/GenericTraits demo output, update remaining output strings to not say "trait".
**Verify:**
```bash
grep -ni 'trait' samples/__baselines__/AllFeatures.out
# Expected: 0 hits
```

---

## Task Group 10: Editor & Syntax Highlighting

### [ ] 2.48 — Update mist.tmLanguage.json (6 hits)
**Files:** `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`
**Action:** Remove `trait` from keyword patterns, remove `entity.name.type.trait.mist` scope, update comments.
**Verify:**
```bash
grep -ni 'trait' Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json
# Expected: 0 hits
```

### [ ] 2.49 — Update package.json scope list (1 hit)
**Files:** `Editor/mist-syntax-highlighting/package.json`
**Action:** Remove `entity.name.type.trait.mist` from contributing scopes.
**Verify:**
```bash
grep -ni 'trait' Editor/mist-syntax-highlighting/package.json
# Expected: 0 hits
```

### [ ] 2.50 — Update editor test file (1 hit)
**Files:** `Editor/mist-syntax-highlighting/tests/dependent-types.mist`
**Action:** Replace `trait Bounded<Limit:byte>` → `abstract Bounded<Limit:byte>`.
**Verify:**
```bash
grep -ni 'trait' Editor/mist-syntax-highlighting/tests/dependent-types.mist
# Expected: 0 hits
```

---

## Task Group 11: Book — Delete Files

### [ ] 2.51 — Delete book/src/object-model/traits.md
**Action:** Delete entire chapter.
**Verify:** File does not exist.

### [ ] 2.52 — Delete book/snippets/object-model/traits/ directory
**Files:** 12 files (6 `.mist` + 6 `.cs`)
**Action:** Delete entire directory.
**Verify:** Directory does not exist.

### [ ] 2.53 — Delete trait snippet files in type-system/
**Files to delete:**
- `book/snippets/type-system/generic-constraints/trait-constraint.mist` + `.cs`
- `book/snippets/type-system/dependent-types/traits.mist` + `.cs`
- `book/snippets/type-system/generics/traits.mist` + `.cs`
- `book/snippets/type-system/associated-types/trait-slot.mist` + `.cs`
**Verify:**
```bash
find book/snippets -name '*trait*' -o -name '*traits*'
# Expected: 0 results
```

---

## Task Group 12: Book — Modify Chapters

### [ ] 2.54 — Update book/src/SUMMARY.md (1 hit)
**Action:** Remove `- [Traits](object-model/traits.md)` entry.
**Verify:**
```bash
grep -ni 'trait' book/src/SUMMARY.md
# Expected: 0 hits
```

### [ ] 2.55 — Update book/src/introduction.md (3 hits)
**Action:** Remove "Trait" bullet, update Object Model and Associated Types descriptions.
**Verify:**
```bash
grep -ni 'trait' book/src/introduction.md
# Expected: 0 hits
```

### [ ] 2.56 — Update book/src/type-system/generic-constraints.md (9 hits)
**Action:** Delete "Trait Constraint" section, remove TraitName from table, update include directives and text.
**Verify:**
```bash
grep -ni 'trait' book/src/type-system/generic-constraints.md
# Expected: 0 hits
```

### [ ] 2.57 — Update book/src/type-system/dependent-types.md (5 hits)
**Action:** Rename "Dependent Types on Traits" → "Dependent Types on Abstract Types", update text/includes.
**Verify:**
```bash
grep -ni 'trait' book/src/type-system/dependent-types.md
# Expected: 0 hits
```

### [ ] 2.58 — Update book/src/type-system/associated-types.md (8 hits)
**Action:** Replace trait references with abstract type references, update includes.
**Verify:**
```bash
grep -ni 'trait' book/src/type-system/associated-types.md
# Expected: 0 hits
```

### [ ] 2.59 — Update book/src/type-system/generics.md (4 hits)
**Action:** Remove/update trait references, update includes.
**Verify:**
```bash
grep -ni 'trait' book/src/type-system/generics.md
# Expected: 0 hits
```

### [ ] 2.60 — Update remaining book chapters (7 files, 1-4 hits each)
**Files:** `refinement-types.md`, `target-typed-new.md`, `readonly.md`, `default-keyword.md`, `nested-scoping.md`, `ownership.md`, `structured-concurrency.md`, `editor-support.md`, `project-configuration.md`
**Action:** Remove/update trait references in each.
**Verify:**
```bash
grep -rni 'trait' book/src/
# Expected: 0 hits
```

---

## Task Group 13: Book Theme & Output

### [ ] 2.61 — Update book/theme/mist-lang.js (4 hits)
**Action:** Remove `trait` from KEYWORDS and declaration pattern regex.
**Verify:**
```bash
grep -n 'trait' book/theme/mist-lang.js
# Expected: 0 hits
```

### [ ] 2.62 — Update book/theme/highlight.js (10 hits, keep 3rd-party)
**Action:** Remove `trait` from Mist language block ONLY. Keep Rust/PHP/other language definitions.
**Verify:**
```bash
grep -n 'trait' book/theme/highlight.js | grep -i 'mist'
# Expected: 0 hits (Rust/PHP hits still present and correct)
```

### [ ] 2.63 — Regenerate book output
**Files:** `book/output/` (3 JS files)
**Action:** Run `mdbook build` to regenerate.
**Verify:**
```bash
grep -n 'trait' book/output/*.js | grep -iv 'rust\|php\|ruby'
# Expected: only third-party language references remain
```

---

## Task Group 14: Distribute & Steering

### [ ] 2.64 — Update distribute/steering/mist-advanced-features.md (12 hits)
**Action:** Delete "Traits (Structural Typing)" section, "Traits vs Interfaces" table, update associated types section, fix opening sentence.
**Verify:**
```bash
grep -ni 'trait' distribute/steering/mist-advanced-features.md
# Expected: 0 hits
```

### [ ] 2.65 — Update distribute/steering/mist-language.md (6 hits)
**Action:** Remove "traits" from #mist-advanced-features description, delete "Traits" section, remove from closing bullet.
**Verify:**
```bash
grep -ni 'trait' distribute/steering/mist-language.md
# Expected: 0 hits
```

### [ ] 2.66 — Update distribute/steering/README.md (1 hit)
**Action:** Remove "Traits" from mist-advanced-features.md description.
**Verify:**
```bash
grep -ni 'trait' distribute/steering/README.md
# Expected: 0 hits
```

---

## Task Group 15: Design Notes

### [ ] 2.67 — Update project/design-notes/regions-and-lifetimes.md (1 hit)
**Action:** Remove or update trait reference.
**Verify:**
```bash
grep -ni 'trait' project/design-notes/regions-and-lifetimes.md
# Expected: 0 hits
```

---

## Task Group 16: Final Verification Gate

### [ ] 2.68 — Full codebase grep verification
**Action:** Run final comprehensive grep.
**Verify:**
```bash
grep -rni 'trait' --include='*.cs' --include='*.mist' --include='*.storm' --include='*.md' --include='*.json' --include='*.js' --include='*.toml' --include='*.out' --exclude-dir='.git' --exclude-dir='.kiro' --exclude-dir='node_modules' --exclude-dir='obj' --exclude-dir='bin' .
```
**Expected remaining hits (ONLY these are acceptable):**
- `book/theme/highlight.js` — Rust, PHP, other third-party language defs containing "trait"
- `Test/Mist.Tests/Semantics/LambdaTypeInferenceTests.cs` — xUnit `[Trait("Feature",...)]` attribute (NOT Mist's trait)

### [ ] 2.69 — Build verification
**Action:** `dotnet build` from solution root.
**Verify:** Zero errors, zero warnings.

### [ ] 2.70 — Full test suite
**Action:** `dotnet test` from solution root.
**Verify:** All tests pass.

### [ ] 2.71 — Sample compilation & baseline verification
**Action:** Compile and run all samples, compare against baselines.
**Verify:** All samples produce expected output.

### [ ] 2.72 — Commit Phase 2
**Action:** `git add -A && git commit -m "Phase 2: Purge traits entirely"`

---

## Summary

| Group | Tasks | Type |
|-------|-------|------|
| 1. Grammar | 2.1–2.2 | MODIFY |
| 2. Contracts & Types | 2.3–2.11 | DELETE + MODIFY |
| 3. Model Core | 2.12–2.15 | DELETE + MODIFY |
| 4. Analysis | 2.16–2.19 | MODIFY |
| 5. Backends | 2.20–2.22 | MODIFY |
| 6. Common/Parsing/Driver | 2.23–2.25 | MODIFY |
| 7. Delete test files | 2.26–2.30 | DELETE (24 files) |
| 8. Modify test files | 2.31–2.36 | MODIFY (~40 files) |
| 9. Samples | 2.37–2.47 | DELETE + MODIFY + REGEN |
| 10. Editor | 2.48–2.50 | MODIFY |
| 11. Book delete | 2.51–2.53 | DELETE (~22 files) |
| 12. Book modify | 2.54–2.60 | MODIFY |
| 13. Book theme/output | 2.61–2.63 | MODIFY + REGEN |
| 14. Steering | 2.64–2.66 | MODIFY |
| 15. Design notes | 2.67 | MODIFY |
| 16. Verification | 2.68–2.72 | GATE |
| **TOTAL** | **72 tasks** | |
