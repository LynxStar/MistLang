# Implementation Plan: Abstract Type Unification

## Overview

This implementation unifies Mist's `interface` and `trait` keywords into a single `abstract` keyword across four sequential phases. The compiler is written in C#; generated output also targets C#.

**Phase 1 Strategy:** Migrate file by file with NO intermediate builds. Run verification (build + tests + grep) only after ALL files are migrated. Naming convention: `Interfaces` → `Abstracts`, `InterfaceType` → `AbstractType`, `SymbolKind.Interface` → `SymbolKind.Abstract`, `TypeKind.Interface` → `TypeKind.Abstract`, grammar fields `interfaceList`/`interfaces` → `abstractList`/`abstracts`.

**Important:** The C# emitter still emits `interface` in generated C# output (the CLR interop bridge is unchanged). Only the Mist-facing concept name changes.

## Tasks

- [ ] 1. Phase 1 — Pure Rename (interface → abstract)

  - [x] 1.1 Grammar: `keywords.storm`
    - Rename `INTERFACE` token → `ABSTRACT` with value `"abstract"`

  - [x] 1.2 Grammar: `Mist.storm`
    - Rename `InterfaceDeclaration` rule → `AbstractDeclaration`
    - Rename `InterfaceType` variant → `AbstractType`
    - Rename `InterfaceMemberDeclaration` → `AbstractMemberDeclaration`
    - Rename `InterfaceMethodDeclaration` → `AbstractMethodDeclaration`
    - Rename `InterfacePropertyDeclaration` → `AbstractPropertyDeclaration`
    - Rename `InterfaceAccessorDeclaration` → `AbstractAccessorDeclaration`
    - Rename all member variants (`InterfaceMethodMember` → `AbstractMethodMember`, etc.)
    - Rename field `interfaceList` → `abstractList`, `interfaces` → `abstracts` on StructDeclaration and AbstractDeclaration
    - Change `INTERFACE` token reference → `ABSTRACT`
    - Note: `TraitPropertyDeclaration` also references `InterfaceAccessorDeclaration` — update it too


  - [x] 1.3 Semantic model: `InterfaceType.cs` → `AbstractType.cs`
    - Rename file and class `InterfaceType` → `AbstractType`
    - Rename `TypeKind.Interface` → `TypeKind.Abstract` (property returns this)
    - Rename property `Interfaces` → `Abstracts`

  - [x] 1.4 Semantic model: `IType.cs` (TypeKind enum)
    - Rename `TypeKind.Interface` → `TypeKind.Abstract`

  - [x] 1.5 Semantic model: `ISymbol.cs` (SymbolKind enum)
    - Rename `SymbolKind.Interface` → `SymbolKind.Abstract`

  - [x] 1.6 Semantic model: `ClassType.cs`
    - Rename property `Interfaces` → `Abstracts`

  - [x] 1.7 Semantic model: `StructType.cs`
    - Rename property `Interfaces` → `Abstracts`

  - [x] 1.8 Semantic model: `TypeParameterConstraints.cs`
    - Rename property `InterfaceConstraints` → `AbstractConstraints`

  - [x] 1.9 Semantic model: `UnresolvedRelationship.cs`
    - Rename enum member `Interface` → `Abstract`

  - [x] 1.10 SemanticModelBuilder: method renames
    - `RegisterInterfaceDeclaration` → `RegisterAbstractDeclaration`
    - `RegisterInterfaceMethodDeclaration` → `RegisterAbstractMethodDeclaration`
    - `RegisterInterfacePropertyDeclaration` → `RegisterAbstractPropertyDeclaration`
    - `Pass1_ResolveInterfaceLists` → `Pass1_ResolveAbstractLists`
    - `ResolveClassInterfaceList` → `ResolveClassAbstractList`
    - `ResolveStructInterfaceList` → `ResolveStructAbstractList`
    - `ResolveInterfaceInterfaceList` → `ResolveAbstractParentList`
    - `ResolveInterfaceNames` → `ResolveAbstractNames`
    - `ResolveInterfaceEntries` → `ResolveAbstractEntries`
    - `Pass1_DetectInterfaceCycles` → `Pass1_DetectAbstractCycles`
    - `DetectInterfaceCyclesDfs` → `DetectAbstractCyclesDfs`
    - `DetectInterfaceCyclesDfsVisit` → `DetectAbstractCyclesDfsVisit`
    - `Pass1_ValidateInterfaceSatisfaction` → `Pass1_ValidateAbstractSatisfaction`
    - `ValidateInterfaceSatisfaction` → `ValidateAbstractSatisfaction`
    - `ResolveInterfaceDeclaration` → `ResolveAbstractDeclaration`
    - `ResolveExplicitMemberTypesForInterface` → `ResolveExplicitMemberTypesForAbstract`
    - `CollectRequiredMembers` — update tuple field names `Interface` → `Abstract`
    - `CollectRequiredAssociatedTypeSlots` — update tuple field names `Interface` → `Abstract`


  - [x] 1.11 SemanticModelBuilder: variable/comment cleanup
    - Rename all local variables: `interfaceName` → `abstractName`, `interfaceType` → `abstractType`, `interfaceDecl` → `abstractDecl`, `interfaceSymbol` → `abstractSymbol`, `interfaceSpan` → `abstractSpan`, `visitedInterfaces` → `visitedAbstracts`, etc.
    - Update all `SymbolKind.Interface` → `SymbolKind.Abstract` references
    - Update all `InterfaceType` type references → `AbstractType`
    - Update comments referring to "interface" in the Mist-concept sense
    - Update diagnostic message strings (e.g., "Interface/trait" → "Abstract type/trait")
    - Note: Keep legitimate C# `interface` references (like `ISymbol`, `IType`) unchanged

  - [x] 1.12 SemanticModelBuilder: property access updates
    - All `.Interfaces` property access → `.Abstracts`
    - All `.InterfaceList` AST property access → `.AbstractList` (generated from Storm)

  - [x] 1.13 ExtensionRegistry: method + variable renames
    - `ImplementsInterfaceStructural` → `ImplementsAbstractStructural`
    - `InterfaceMatches` → `AbstractMatches`
    - `IsArrayCompatibleInterface` → `IsArrayCompatibleAbstract`
    - Update all local variable names (`interfaces` → `abstracts`, `iface` can stay or → `abs`)
    - Update all `InterfaceType` type references → `AbstractType`
    - Update comments

  - [x] 1.14 IClonableDerivationEngine: method + comment renames
    - `InterfaceListHasIClonable` → `AbstractListHasIClonable`
    - `IsIClonableInterface` → `IsIClonableAbstract`
    - Update variable names and comments

  - [x] 1.15 ReadonlyInferenceEngine
    - Update `SymbolKind.Interface` → `SymbolKind.Abstract`

  - [x] 1.16 SemanticModelStdlibTypeProvider
    - Update `SymbolKind.Interface` → `SymbolKind.Abstract`

  - [x] 1.17 CSharpEmitter: field + method renames
    - Rename field `_currentInterfaceTypeParamSubs` → `_currentAbstractTypeParamSubs`
    - Rename method `EmitInterfaceDeclaration` → `EmitAbstractDeclaration`
    - Rename method `EmitInterfaceMethodDeclaration` → `EmitAbstractMethodDeclaration`
    - Rename method `EmitInterfacePropertyDeclaration` → `EmitAbstractPropertyDeclaration`
    - Rename method `EmitInterfaceWithAssociatedTypeArgs` → `EmitAbstractWithAssociatedTypeArgs`
    - Update all `InterfaceType` type references → `AbstractType`
    - Update all `SymbolKind.Interface` → `SymbolKind.Abstract`
    - Update generated AST node type references: `InterfaceDeclarationNode` → `AbstractDeclarationNode`, `InterfaceMethodDeclarationNode` → `AbstractMethodDeclarationNode`, `InterfacePropertyDeclarationNode` → `AbstractPropertyDeclarationNode`, `InterfaceMemberDeclarationNode` → `AbstractMemberDeclarationNode`, `InterfaceTypeTypeDeclarationNode` → `AbstractTypeTypeDeclarationNode`
    - Update generated AST node property accesses: `.InterfaceDeclaration` → `.AbstractDeclaration`, `.InterfaceList` → `.AbstractList`
    - Update generated member variant node types (e.g., `InterfaceMethodMemberInterfaceMemberDeclarationNode` → `AbstractMethodMemberAbstractMemberDeclarationNode`)
    - Note: Keep C# emission of `interface` keyword — that's intentional CLR output

  - [x] 1.17b TypeCheckerAnalyzer: type reference and method renames
    - Update all `InterfaceType` type references → `AbstractType`
    - Update all `SymbolKind.Interface` → `SymbolKind.Abstract`
    - Rename methods: `ImplementsInterface` → `ImplementsAbstract`, `ImplementsConstructedInterface` → `ImplementsConstructedAbstract`, `InterfaceListContains` → `AbstractListContains`, `IsInterfaceTargetType` → `IsAbstractTargetType`, `CheckInterfacePropertyRequirements` → `CheckAbstractPropertyRequirements`, `CheckReadonlyMembersOfInterface` → `CheckReadonlyMembersOfAbstract`
    - Update local variable names (`targetInterface` → `targetAbstract`, `sourceInterface` → `sourceAbstract`, etc.)
    - Update comments

  - [x] 1.17c OwnershipAnalyzer
    - Update `InterfaceDeclarationNode` reference → `AbstractDeclarationNode`
    - Rename field `InterfaceForbidden` (already covered in 1.23b)
    - Update comments

  - [x] 1.18 CSharpEmitter: variable/comment cleanup
    - Rename locals: `interfaceDecl` → `abstractDecl`, `interfaceList` → `abstractList`, `interfaceSymbol` → `abstractSymbol`, `interfaceType` → `abstractType`, etc.
    - Update `.Interfaces` property access → `.Abstracts`
    - Update generated AST node property accesses: `.InterfaceList` → `.AbstractList`, `.InterfaceDeclaration` → `.AbstractDeclaration`
    - Update generated member variant node type references (e.g., `InterfaceMethodMemberInterfaceMemberDeclarationNode` → `AbstractMethodMemberAbstractMemberDeclarationNode`)
    - Update comments referring to "interface" in Mist-concept sense
    - Keep comments about CLR interfaces as-is


  - [x] 1.19 TypeProjector: type reference renames
    - Update all `InterfaceType` references → `AbstractType`
    - Rename `ProjectInterface` method → `ProjectAbstract`
    - Update comments

  - [x] 1.20 AssemblyTypeProvider
    - Update `InterfaceType` reference → `AbstractType`

  - [x] 1.21 MistParser: add `interface` keyword error detection
    - Add `DetectRemovedInterfaceKeyword` method: scans token stream for identifiers with lexeme `"interface"` and emits diagnostic error directing user to use `abstract`
    - Since `INTERFACE` token no longer exists in grammar, lexer produces IDENTIFIER for "interface" — detect that

  - [x] 1.22 MistPrettyPrinter: renames
    - Update `InterfaceType` variant handling → `AbstractType` variant
    - Rename locals (`interfaceType` → `abstractType`, `interfaceClause` → `abstractClause`, `interfaceDecl` → `abstractDecl`)
    - Update `.Interfaces` → `.Abstracts` property access
    - Update generated AST node type references: `InterfaceDeclarationNode` → `AbstractDeclarationNode`, `InterfaceMethodDeclarationNode` → `AbstractMethodDeclarationNode`, `InterfacePropertyDeclarationNode` → `AbstractPropertyDeclarationNode`, `InterfaceMemberDeclarationNode` → `AbstractMemberDeclarationNode`
    - Update generated AST node property accesses: `.InterfaceDeclaration` → `.AbstractDeclaration`
    - Emit `abstract` keyword instead of `interface` in pretty-printed output

  - [x] 1.23 Other model files: comment updates
    - `BindingKind.cs` — update comment "interface or trait" → "abstract type or trait"
    - `AsConversionKind.cs` — update comment "interface satisfaction" → "abstract type satisfaction"
    - `DefaultValueKind.cs` — update comment "interface/trait field" → "abstract type/trait field"
    - `NullableOrigin.cs` — update comment "class, interface, string" → "class, abstract type, string"
    - `ParameterSignature.cs` — rename variable `interfaceMethod` → `abstractMethod`

  - [x] 1.23b Diagnostic IDs and field names
    - `DiagnosticId.cs`: rename fields `DefaultOnInterface` → `DefaultOnAbstract`, `TargetNewInterface` → `TargetNewAbstract`
    - `DiagnosticId.cs`: rename string values `"MIST_DEFAULT_ON_INTERFACE"` → `"MIST_DEFAULT_ON_ABSTRACT"`, `"MIST_TARGET_NEW_INTERFACE"` → `"MIST_TARGET_NEW_ABSTRACT"`
    - `SemanticModelBuilder.cs`: rename field `MissingInterfaceOverload` → `MissingAbstractOverload` (keep string `"MIST_SM_0022"` unchanged — it's a stable ID)
    - `SemanticModelBuilder.cs`: rename field `RefinementConstraintStrongerThanInterface` → `RefinementConstraintStrongerThanAbstract`
    - `SemanticModelBuilder.cs`: rename string IDs `"MIST_UNDEFINED_INTERFACE"` → `"MIST_UNDEFINED_ABSTRACT"`, `"MIST_NOT_AN_INTERFACE"` → `"MIST_NOT_AN_ABSTRACT"`, `"MIST_CIRCULAR_INTERFACE_INHERITANCE"` → `"MIST_CIRCULAR_ABSTRACT_INHERITANCE"`, `"MIST_MISSING_INTERFACE_PROPERTY"` → `"MIST_MISSING_ABSTRACT_PROPERTY"`, `"MIST_MISSING_INTERFACE_PROPERTY_ACCESSOR"` → `"MIST_MISSING_ABSTRACT_PROPERTY_ACCESSOR"`
    - `SemanticModelBuilder.cs`: update diagnostic MESSAGE strings ("interface" → "abstract type" in user-facing text)
    - `OwnershipAnalyzer.cs`: rename field `InterfaceForbidden` → `AbstractForbidden`, rename string `"MIST_OWN_INTERFACE_FORBIDDEN"` → `"MIST_OWN_ABSTRACT_FORBIDDEN"`
    - `TypeCheckerAnalyzer.cs`: rename field `ConstraintInterfaceNotImplemented` → `ConstraintAbstractNotImplemented`, update message strings
    - Update all tests that assert on these diagnostic ID strings (e.g., `InvalidAbstractReferencePropertyTests`, `AbstractCycleDetectionPropertyTests`, `ConstraintDiagnosticsTests`, `TargetTypedNewDiagnosticTests`, `InvalidEnumerableImplementationPropertyTests`, `OwnedTypeConstraintPropertyTests`)

  - [x] 1.24 Standard library `.mist` files
    - `Source/Standard/IEnumerator.mist` — `interface` → `abstract`
    - `Source/Standard/IEnumerable.mist` — `interface` → `abstract`
    - `Source/Standard/IClonable.mist` — `interface` → `abstract`

  - [x] 1.25 Test file: `InterfaceEmissionPropertyTests.cs`
    - Rename file → `AbstractEmissionPropertyTests.cs`
    - Rename class and all `Interface`-named members/variables
    - Update all `InterfaceType`/`InterfaceDeclaration` AST node references to new names
    - Update `SymbolKind.Interface` → `SymbolKind.Abstract`

  - [x] 1.26 Test file: `ClassInterfaceEmissionPropertyTests.cs`
    - Rename file → `ClassAbstractEmissionPropertyTests.cs`
    - Rename class and all `Interface`-named members/variables
    - Update AST node type references to new names


  - [x] 1.27 Test file: `InterfaceSatisfactionPropertyTests.cs`
    - Rename file → `AbstractSatisfactionPropertyTests.cs`
    - Rename class, records (`InterfaceName` → `AbstractName`, `InterfaceMethodNames` → `AbstractMethodNames`, etc.)
    - Update `InterfaceNamePool` → `AbstractNamePool`
    - Update AST node references and `.Interfaces`/`.InterfaceList` → `.Abstracts`/`.AbstractList`

  - [x] 1.28 Test file: `InterfaceSignatureMismatchPropertyTests.cs`
    - Rename file → `AbstractSignatureMismatchPropertyTests.cs`
    - Rename class, records, variables similarly

  - [x] 1.29 Test file: `InterfaceRoundTripPropertyTests.cs`
    - Rename file → `AbstractRoundTripPropertyTests.cs`
    - Rename class, update `interface` keyword in generated source strings → `abstract`

  - [x] 1.30 Test file: `InterfaceResolutionPropertyTests.cs`
    - Rename file → `AbstractResolutionPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.31 Test file: `InterfaceTypeRegistrationPropertyTests.cs`
    - Rename file → `AbstractTypeRegistrationPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.32 Test file: `InterfaceMemberLookupPropertyTests.cs`
    - Rename file → `AbstractMemberLookupPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.33 Test file: `InterfaceMemberLookupMissingPropertyTests.cs`
    - Rename file → `AbstractMemberLookupMissingPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.34 Test file: `InterfaceNonAssignabilityPropertyTests.cs`
    - Rename file → `AbstractNonAssignabilityPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.35 Test file: `InterfaceAssignabilityPropertyTests.cs`
    - Rename file → `AbstractAssignabilityPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.36 Test file: `InterfaceCycleDetectionPropertyTests.cs`
    - Rename file → `AbstractCycleDetectionPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.37 Test file: `InterfaceOverloadSatisfactionPropertyTests.cs`
    - Rename file → `AbstractOverloadSatisfactionPropertyTests.cs`
    - Rename class and all internal references

  - [x] 1.38 Test file: `InvalidInterfaceReferencePropertyTests.cs`
    - Rename file → `InvalidAbstractReferencePropertyTests.cs`
    - Rename class and all internal references


  - [x] 1.39 Test file: `TraitInterfaceCoexistencePropertyTests.cs`
    - Rename file → `TraitAbstractCoexistencePropertyTests.cs`
    - Rename class and all `Interface`-named members
    - Update `interface` keyword in source strings → `abstract`

  - [x] 1.40 Test file: `DependentInterfaceParameterValidationTests.cs`
    - Rename file → `DependentAbstractParameterValidationTests.cs`
    - Rename class, update `interface` keyword in source strings → `abstract`

  - [x] 1.41 Test file: `EntryPointWithInterfaceBugTests.cs`
    - Rename file → `EntryPointWithAbstractBugTests.cs`
    - Rename class, update `interface` keyword in source strings → `abstract`

  - [x] 1.42 Test file: `InterfaceTraitRefinementSatisfactionPropertyTests.cs`
    - Rename file → `AbstractTraitRefinementSatisfactionPropertyTests.cs`
    - Rename class, update `interface` keyword in source strings → `abstract`

  - [x] 1.43 Test files: other tests with `interface` in source strings
    - `ConstraintClauseParsingTests.cs` — update helper methods and source strings
    - `ConstraintClauseRoundTripPropertyTests.cs` — update `GenInterfaceDeclaration` → `GenAbstractDeclaration`
    - `DefaultConstraintRoundTripPropertyTests.cs` — update `GenInterfaceDeclaration` → `GenAbstractDeclaration`
    - `AssociatedTypeDisambiguationPropertyTests.cs` — update `ExtractInterfaceMember` and references
    - `AssociatedTypeSemanticPropertyTests.cs` — update `InterfaceType` references
    - `CrossFileTypeResolutionTests.cs` — update `Scenario4_CrossFileInterfaceMemberType`
    - `MistContextRoundTripGenerator.cs` — update `GenInterfaceMethodWithClauses` and source strings
    - `ValidEnumerableImplementationPropertyTests.cs` — update comments referencing "interface-related"
    - `InvalidEnumerableImplementationPropertyTests.cs` — update diagnostic ID references
    - `TypeArgumentInferenceConstraintPropertyTests.cs` — update `GenInterfaceConstraintViolation`, source strings using `interface` keyword
    - `StructInheritanceAssignabilityTests.cs` — update `MakeInterfaceDecl` → `MakeAbstractDecl`, `WrapInterface` → `WrapAbstract`, test method names, source strings
    - `SpanHeapEscapeParityPropertyTests.cs` — update `InterfaceBoxing` enum, source strings with `interface` keyword
    - `RefStructUsagePropertyTests.cs` — update `InterfaceImpl` enum, source strings
    - `ReadonlyModifierPropagationPropertyTests.cs` — update `GenInterfaceScenario` → `GenAbstractScenario`, source strings, `InterfaceType` references
    - `TargetTypedNewDiagnosticTests.cs` — update diagnostic ID references and source strings
    - `ConstraintDiagnosticsTests.cs` — update `MIST_CONSTRAINT_0009_InterfaceNotImplemented` test name, source strings
    - `OwnedTypeConstraintPropertyTests.cs` — update diagnostic ID references
    - `StorageBanNewDefaultTests.cs` — update comments

  - [x] 1.44 Sample: `samples/Interfaces/` → `samples/AbstractTypes/`
    - Rename directory
    - Replace `interface` keyword → `abstract` in all `.mist` files
    - Update comments to say "abstract type" instead of "interface" (Mist concept)
    - Update `mist.toml` if present

  - [x] 1.45 Sample: `samples/ShapeInterfaces/` → `samples/ShapeAbstracts/`
    - Rename directory
    - Replace `interface` keyword → `abstract` in all `.mist` files
    - Update comments

  - [x] 1.46 Sample: `samples/Properties/IPropertyDemo.mist`
    - Replace `interface` keyword → `abstract`
    - Update comment

  - [x] 1.47 Sample: `samples/ReadonlyContexts/Program.mist`
    - Replace `interface` keyword → `abstract` (e.g., `public readonly interface IReadable` → `public readonly abstract IReadable`)
    - Update comments referencing "interface"


  - [x] 1.48 Sample: `samples/NestedScoping/`
    - `Program.mist` — replace `interface` keyword → `abstract`, update comments
    - `NestedInInterfaceTrait.mist` — replace keyword, update comments, consider renaming file → `NestedInAbstractTrait.mist`

  - [x] 1.49 Sample: `samples/StructuredContext/Program.mist`
    - Replace `interface` keyword → `abstract`
    - Update all comments referencing "interface" in Mist sense

  - [x] 1.50 Sample: `samples/GenericTypes/`
    - `IGenericContainer.mist` — replace `interface` keyword → `abstract`, update comment
    - `GenericBox.mist` — update comment

  - [x] 1.51 Sample: `samples/VoidType/`
    - `IGenericContainer.mist` — replace `interface` keyword → `abstract`, update comment
    - `GenericBox.mist` — update comment

  - [x] 1.52 Sample: `samples/GenericConstraints/Program.mist`
    - Update comments referencing "interface constraint" → "abstract type constraint"

  - [x] 1.53 Sample: `samples/Iterators/Program.mist`
    - Update comment "satisfy the interface contract" → "satisfy the abstract type contract"

  - [x] 1.54 Sample: `samples/Enums/Program.mist`
    - Update comments referencing "interface underlying type"

  - [x] 1.55 Sample: `samples/AllFeatures/Program.mist`
    - Update entry point references for renamed samples (`Interfaces` → `AbstractTypes`, `ShapeInterfaces` → `ShapeAbstracts`)

  - [x] 1.55b Sample baselines: `samples/__baselines__/`
    - Update `AllFeatures.out` — replace "interface" in output strings with "abstract type" where the output comes from Mist concept references (e.g., `"--- Interface Properties ---"` → `"--- Abstract Type Properties ---"`, `"3. interface constraint:"` → `"3. abstract type constraint:"`, `"interface-implementing"` → `"abstract-implementing"`, `"Interface dispatch"` → `"Abstract type dispatch"`, `"Outer.IAction interface nested in class"` → `"Outer.IAction abstract type nested in class"`, etc.)
    - Update any per-sample baselines for `Interfaces.out`, `ShapeInterfaces.out` (rename to `AbstractTypes.out`, `ShapeAbstracts.out`)
    - Note: Output strings are determined by what the SAMPLE CODE prints — update the sample `.mist` files FIRST (tasks 1.44–1.54), then regenerate/update baselines to match

  - [x] 1.56 TextMate grammar: `mist.tmLanguage.json`
    - Remove `interface` from declaration keyword list, add `abstract`
    - Update scope: `entity.name.type.interface.mist` → `entity.name.type.abstract.mist`
    - Update generic declaration pattern: `class|struct|interface|trait` → `class|struct|abstract|trait`
    - Update comments

  - [x] 1.57 Book highlighting: `book/theme/mist-lang.js`
    - Replace `interface` with `abstract` in the Mist KEYWORDS string
    - Update the type declaration regex: `class|struct|interface|trait` → `class|struct|abstract|trait`

  - [x] 1.58 Book chapter: `book/src/object-model/interfaces.md` → `abstract-types.md`
    - Rename file
    - Update title to "Abstract Types"
    - Replace all `interface` keyword usage with `abstract`
    - Update `book/SUMMARY.md` to reference new filename and title


  - [x] 1.59 Book chapters: update "interface" references in other chapters
    - `book/src/introduction.md` — update "Interface" description
    - `book/src/basics/control-flow.md` — update `interface` in code example → `abstract`
    - `book/src/type-system/as-operator.md` — update "implemented interface" → "implemented abstract type"
    - `book/src/type-system/associated-types.md` — update all "interface" references
    - `book/src/type-system/generic-constraints.md` — update "Interface Constraint" section and all references
    - `book/src/type-system/dependent-types.md` — update "interface" references in explanation
    - `book/src/type-system/generics.md` — update "interface" references
    - `book/src/type-system/refinement-types.md` — update "interface" references
    - `book/src/advanced/dotnet-interop.md` — update "Interface" in type table (note: this may refer to CLR concept — keep if so)
    - `book/src/advanced/default-keyword.md` — update "Interfaces and traits" → "Abstract types and traits"
    - `book/src/tooling/editor-support.md` — update `interface` in keyword table → `abstract`

  - [x] 1.60 Book snippets: rename files with "interface" in name
    - `book/snippets/type-system/generics/interfaces.mist` → `abstracts.mist` + update content
    - `book/snippets/type-system/generics/interfaces.cs` → `abstracts.cs` + update content
    - `book/snippets/type-system/generic-constraints/interface-constraint.mist` → `abstract-constraint.mist`
    - `book/snippets/type-system/generic-constraints/interface-constraint.cs` → `abstract-constraint.cs`
    - `book/snippets/type-system/generic-constraints/interface-struct-constraints.mist` → `abstract-struct-constraints.mist`
    - `book/snippets/type-system/generic-constraints/interface-struct-constraints.cs` → `abstract-struct-constraints.cs`
    - `book/snippets/type-system/associated-types/implementor-interface.mist` → `implementor-abstract.mist`
    - `book/snippets/type-system/associated-types/implementor-interface.cs` → `implementor-abstract.cs`
    - `book/snippets/type-system/refinement-types/adhoc-interfaces.mist` → `adhoc-abstracts.mist`
    - `book/snippets/type-system/refinement-types/adhoc-interfaces.cs` → `adhoc-abstracts.cs`
    - `book/snippets/type-system/dependent-types/interfaces.mist` → `abstracts.mist`
    - `book/snippets/type-system/dependent-types/interfaces.cs` → `abstracts.cs`
    - Update all `{{#include ...}}` references in book markdown to point to new filenames
    - Replace `interface` keyword → `abstract` in all renamed `.mist` snippet files

  - [x] 1.61 Phase 1 verification gate
    - Build entire solution: `dotnet build` — zero errors, zero warnings
    - Run full test suite: `dotnet test` — all tests pass
    - Grep for `interface` across codebase (excluding `bin/`, `obj/`, `specs/`):
      - ALLOWED in: C# `interface` declarations (like `interface IType`), C# emitter output strings producing `interface`, CLR/ambient type handling, `book/theme/highlight.js` (other language keywords)
      - NOT ALLOWED in: `.mist` files, `.storm` files, TextMate grammar JSON, `book/theme/mist-lang.js` (Mist keywords section), book markdown (when referring to Mist concept), test names/comments referring to Mist concept, sample comments referring to Mist concept
    - Compile and run all samples — verify expected output matches baselines

  - [x] 1.62 Commit Phase 1
    - Commit all Phase 1 changes with message: "Phase 1: Rename interface → abstract (zero semantic change)"


- [x] 2. Phase 2 — Purge Traits (Pure Deletion)

  > **Reference docs:**
  > - `phase2-tasks.md` — 72 detailed subtasks with verification commands
  > - `trait-audit.md` — per-file breakdown (196 files, 5082 grep hits)
  > - `trait-grep-raw.txt` — raw grep output for line-level reference

  - [x] 2.1 Delete trait grammar (keywords.storm, Mist.storm) + add error recovery
  - [x] 2.2 Delete trait semantic model types (TraitType.cs, ConstructedTraitType.cs, TraitSatisfactionMap.cs, WithBlockRegistry.cs)
  - [x] 2.3 Remove Trait from enums (SymbolKind, TypeKind) and contracts (ISemanticModel)
  - [x] 2.4 Remove TraitConstraints from TypeParameterConstraints, SatisfiedTraits from ClassType/StructType
  - [x] 2.5 Purge trait from SemanticModelBuilder.cs (883 hits — passes, registration, diagnostics, helpers)
  - [x] 2.6 Purge trait from TypeCheckerAnalyzer.cs (293 hits — SatisfiesTrait, IsTraitTargetType, readonly checks, assignability)
  - [x] 2.7 Purge trait from OwnershipAnalyzer, ContextCheckerAnalyzer, CooperationContractVerifier, ReadonlyAnalyzer
  - [x] 2.8 Purge trait from CSharpEmitter.cs (148 hits — EmitTraitDeclaration, ExplicitTraitImplementations, constraint loops)
  - [x] 2.9 Purge trait from CSharpTypeMap, DependentTypeHelpersGenerator, NeedsAsyncPropagationPass
  - [x] 2.10 Purge trait from DiagnosticId.cs, MistPrettyPrinter.cs, CompilerDriver.cs, SemanticModel.cs
  - [x] 2.11 Purge trait from misc semantics (DefaultValueKind, BindingKind, ReadonlyInferenceEngine)
  - [x] 2.12 Delete 24 trait-only test files (generators, parsing, semantics, analysis, backend)
  - [x] 2.13 Modify ~40 remaining test files to remove trait scenarios/branches
  - [x] 2.14 Delete samples/Traits/ and samples/GenericTraits/ directories
  - [x] 2.15 Migrate samples (GenericConstraints, AssociatedTypes, NestedScoping, StructuredContext, DependentTypes, Enums, AllFeatures)
  - [x] 2.16 Regenerate sample out/ directories, update AllFeatures.out baseline
  - [x] 2.17 Update Editor syntax highlighting (mist.tmLanguage.json, package.json, test files)
  - [x] 2.18 Delete book traits chapter + snippet files (traits.md + ~22 snippet files)
  - [x] 2.19 Update remaining book chapters (SUMMARY.md, introduction, type-system/*, advanced/*, tooling/*)
  - [x] 2.20 Update book theme (mist-lang.js, highlight.js Mist section only)
  - [x] 2.21 Update distribute/steering files (mist-advanced-features.md, mist-language.md, README.md)
  - [x] 2.22 Update project/design-notes/regions-and-lifetimes.md
  - [x] 2.23 Final verification gate
    - Build: zero errors, zero warnings
    - Full test suite passes
    - `grep -rni 'trait' --include='*.cs' --include='*.mist' --include='*.storm' --include='*.md' --include='*.json' --include='*.js' --include='*.toml' --include='*.out' --exclude-dir='.git' --exclude-dir='.kiro' --exclude-dir='node_modules' --exclude-dir='obj' --exclude-dir='bin' .` returns ONLY: third-party highlight.js (Rust/PHP), xUnit `[Trait]` attributes
    - All samples compile and produce expected output
  - [x] 2.24 Commit Phase 2
    - Message: "Phase 2: Purge traits entirely"

- [x] 3. Phase 3 — Default Implementations (New Functionality)

  - [x] 3.1 Extend grammar for default implementations
    - Extend `AbstractMemberDeclaration` to accept `defaultMethod:MethodDeclaration as AbstractDefaultMethodMember`
    - Parser distinguishes slots (semicolon-terminated) from defaults (body-present)
    - Error: property with body inside abstract type

  - [x] 3.2 Extend semantic model for default methods
    - Add `DefaultMethods` dictionary to `AbstractType`
    - Register default method symbols in both `MemberScope` and `DefaultMethods`

  - [x] 3.3 Extend type checker for slot satisfaction with defaults
    - Exclude `DefaultMethods` members from required slots
    - Accept concrete type override when signatures match
    - Propagate defaults through abstract type inheritance

  - [x] 3.4 Extend CSharpEmitter for default interface methods (DIM)
    - Emit C# default interface method for `AbstractDefaultMethodMember` nodes
    - Emit forwarding stub on concrete types that don't override
    - When concrete type overrides, emit only concrete implementation

  - [x] 3.5 Add default implementation samples
    - Update `samples/AbstractTypes/Program.mist`
    - Verify dispatch semantics

  - [x] 3.6 Update book documentation for Phase 3

  - [x] 3.7 Phase 3 verification gate
    - Build, test, samples all pass

  - [x] 3.8 Commit Phase 3
    - Message: "Phase 3: Add default implementations for abstract types"


- [x] 4. Phase 4 — Late Binding via With Blocks (New Functionality)

  - [x] 4.1 Re-add with block grammar
    - Re-add `WithDeclaration` rule: `with TypeName : AbstractType { mappings }`
    - Re-add `MemberMapping` rule: `sourceName:IDENTIFIER AS targetName:IDENTIFIER SEMICOLON`
    - Re-add `WithItem` variant to `TopLevelItem`

  - [x] 4.2 Implement ConformanceMap semantic model
    - Create `ConformanceMapping` and `ConformanceMap` classes
    - Wire into semantic model

  - [x] 4.3 Implement with-block resolution pass
    - Add `Pass1_ResolveWithBlocks` pass
    - Validate type/abstract references, member mappings, signature compatibility
    - Register validated mappings in `ConformanceMap`

  - [x] 4.4 Extend type checker for with-block assignability
    - Consult `ConformanceMap` for assignability checks

  - [x] 4.5 Implement with-block C# emission
    - Add abstract type's interface to concrete type's C# inheritance list
    - Emit explicit interface implementations delegating to mapped members

  - [x] 4.6 Add with-block samples
    - Demonstrate conformance, `as` mapping, polymorphic usage

  - [x] 4.7 Update book documentation for Phase 4

  - [x] 4.8 Phase 4 verification gate
    - Build, test, samples all pass

  - [x] 4.9 Commit Phase 4
    - Message: "Phase 4: Add late binding via with blocks"

- [ ] 5. Final comprehensive verification

  - [ ] 5.1 Final grep verification
    - `interface` only in: C# source `interface` declarations, ambient type provider CLR handling, C# emission output, third-party code, spec files
    - `trait` only in: spec files and third-party code
  - [ ] 5.2 Full test suite passes
  - [ ] 5.3 All samples compile and produce expected output

## Notes

- All tasks are required
- Phase 1 is zero semantic change — purely mechanical rename, file by file, no intermediate builds
- Phase 2 is pure deletion
- Phase 3 and Phase 4 are additive — new functionality
- The C# emitter always emits `interface` in generated C# code (CLR interop bridge unchanged)
- Naming: `Interfaces` property → `Abstracts`, `InterfaceType` → `AbstractType`, `SymbolKind.Interface` → `SymbolKind.Abstract`
- Do NOT rename C# `interface` declarations (`IType`, `IBackend`, etc.) — those are C# language constructs
- Do NOT rename things like `IClonable`, `IEnumerable` — those are type NAMES, not the keyword
- The `highlight.js` file contains keywords for C#, Go, Kotlin, etc. — leave `interface` in those language sections alone

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4", "1.5"] },
    { "id": 2, "tasks": ["1.6", "1.7", "1.8", "1.9"] },
    { "id": 3, "tasks": ["1.10", "1.11", "1.12"] },
    { "id": 4, "tasks": ["1.13", "1.14", "1.15", "1.16"] },
    { "id": 5, "tasks": ["1.17", "1.18", "1.19", "1.20"] },
    { "id": 6, "tasks": ["1.21", "1.22", "1.23"] },
    { "id": 7, "tasks": ["1.24"] },
    { "id": 8, "tasks": ["1.25", "1.26", "1.27", "1.28", "1.29", "1.30", "1.31", "1.32", "1.33", "1.34", "1.35", "1.36", "1.37", "1.38", "1.39", "1.40", "1.41", "1.42", "1.43"] },
    { "id": 9, "tasks": ["1.44", "1.45", "1.46", "1.47", "1.48", "1.49", "1.50", "1.51", "1.52", "1.53", "1.54", "1.55"] },
    { "id": 10, "tasks": ["1.56", "1.57"] },
    { "id": 11, "tasks": ["1.58", "1.59", "1.60"] },
    { "id": 12, "tasks": ["1.61"] },
    { "id": 13, "tasks": ["1.62"] },
    { "id": 14, "tasks": ["2.1", "2.2", "2.3"] },
    { "id": 15, "tasks": ["2.4", "2.5", "2.6", "2.7", "2.8", "2.9", "2.10", "2.11"] },
    { "id": 16, "tasks": ["2.12", "2.13"] },
    { "id": 17, "tasks": ["2.14", "2.15"] },
    { "id": 18, "tasks": ["2.16", "2.17", "2.18", "2.19", "2.20", "2.21", "2.22"] },
    { "id": 19, "tasks": ["2.23"] },
    { "id": 20, "tasks": ["2.24"] },
    { "id": 21, "tasks": ["3.1"] },
    { "id": 22, "tasks": ["3.2"] },
    { "id": 23, "tasks": ["3.3"] },
    { "id": 24, "tasks": ["3.4"] },
    { "id": 25, "tasks": ["3.5", "3.6"] },
    { "id": 26, "tasks": ["3.7"] },
    { "id": 27, "tasks": ["3.8"] },
    { "id": 28, "tasks": ["4.1"] },
    { "id": 29, "tasks": ["4.2"] },
    { "id": 30, "tasks": ["4.3"] },
    { "id": 31, "tasks": ["4.4", "4.5"] },
    { "id": 32, "tasks": ["4.6", "4.7"] },
    { "id": 33, "tasks": ["4.8"] },
    { "id": 34, "tasks": ["4.9"] },
    { "id": 35, "tasks": ["5.1", "5.2", "5.3"] }
  ]
}
```
