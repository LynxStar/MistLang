# Implementation Plan: Grammar Type Declaration Unification

## Overview

This plan implements the grammar unification described in `design.md`: replacing the separate `ClassDeclaration`, `StructDeclaration`, and `AbstractDeclaration` grammar rules with a single `TypeBodyDeclaration` rule, absorbing newtypes and type aliases into the `TypeDeclaration` alternation, and removing `AbstractMemberDeclaration` (folding its members into `MemberDeclaration`). The migration follows the dependency order: Grammar → TypeBuilder → Phase 2 Builders → PrettyPrinter → CSharpEmitter → SemanticModelBuilder → Tests.

## Tasks

- [ ] 1. Grammar modification (atomic — all grammar changes in one commit)
  - [ ] 1.1 Add `TypeKeyword` rule: `rule TypeKeyword = CLASS as ClassKeyword | STRUCT as StructKeyword | ABSTRACT as AbstractKeyword;`
  - [ ] 1.2 Add `TypeBodyDeclaration` rule with keyword variant, attributes, modifiers, name, type params, base clause, where constraints, and `MemberDeclaration*` body
  - [ ] 1.3 Modify `TypeDeclaration` to: `BodyType` (TypeBodyDeclaration) | `EnumType` | `NewtypeType` (NewtypeDeclaration) | `TypeAliasType` (TypeAliasDeclaration)
  - [ ] 1.4 Simplify `TopLevelItem` — remove `NewtypeItem` and `TypeAliasItem` alternations (now reached through TypeDeclaration)
  - [ ] 1.5 Simplify `NamespaceBodyItem` — remove `NamespaceNewtypeItem` and `NamespaceTypeAliasItem` alternations
  - [ ] 1.6 Remove `ClassDeclaration`, `StructDeclaration`, `AbstractDeclaration` rules entirely
  - [ ] 1.7 Remove `AbstractMemberDeclaration` rule and all its sub-rules: `AbstractMethodDeclaration`, `AbstractPropertyDeclaration`, `AbstractAccessorDeclaration`
  - [ ] 1.8 Add `ModifiedAssociatedTypeSlotDeclaration` as a new alternation in `MemberDeclaration` (it was only reachable via `AbstractMemberDeclaration` before)
  - [ ] 1.9 Build to verify grammar regeneration succeeds (parser/lexer compile, source generator runs)

- [ ] 2. Update TypeBuilder (ProgramModel Phase 1 — type registration)
  - [ ] 2.1 Update `Build` method: remove `NewtypeItemTopLevelItemNode` and `TypeAliasItemTopLevelItemNode` cases (newtypes/aliases now come through `TypeItemTopLevelItemNode`)
  - [ ] 2.2 Update `ProcessNamespaceBody`: remove `NamespaceNewtypeItemNamespaceBodyItemNode` and `NamespaceTypeAliasItemNamespaceBodyItemNode` cases
  - [ ] 2.3 Update `CreateTypeDefinition`: replace `ClassTypeTypeDeclarationNode`/`StructTypeTypeDeclarationNode`/`AbstractTypeTypeDeclarationNode` cases with single `BodyTypeTypeDeclarationNode` case that switches on `body.TypeBody.Keyword`; add `NewtypeTypeTypeDeclarationNode` and `TypeAliasTypeTypeDeclarationNode` cases
  - [ ] 2.4 Update `RegisterType`: integrate newtype/type-alias registration into `RegisterType` (they now flow through `CreateTypeDefinition` instead of separate methods)
  - [ ] 2.5 Update `GetMembers`: replace class/struct switch with single `BodyTypeTypeDeclarationNode` case extracting `.TypeBody.Members`
  - [ ] 2.6 Update `RegisterNestedTypes`: verify it works with the new unified member path
  - [ ] 2.7 Remove now-dead `RegisterNewtype` and `RegisterTypeAlias` methods (logic folded into `CreateTypeDefinition`)

- [ ] 3. Update Phase 2 builders (ProgramModel)
  - [ ] 3.1 Update `BaseTypeResolver.Resolve`: replace three-case switch with single `BodyTypeTypeDeclarationNode` → `body.TypeBody.BaseList`
  - [ ] 3.2 Update `ConcreteTypeBuilder.BuildDeclaration`: cast to `BodyTypeTypeDeclarationNode`, extract modifiers from `body.TypeBody.Modifiers`
  - [ ] 3.3 Update `ConcreteTypeBuilder.BuildMembers`: cast to `BodyTypeTypeDeclarationNode`, extract members from `body.TypeBody.Members`
  - [ ] 3.4 Update `AbstractTypeBuilder.BuildDeclaration`: cast to `BodyTypeTypeDeclarationNode`, extract modifiers from `body.TypeBody.Modifiers`
  - [ ] 3.5 Update `AbstractTypeBuilder.BuildMembers`: iterate over `MemberDeclarationNode` entries from `body.TypeBody.Members` instead of `AbstractMemberDeclarationNode`
  - [ ] 3.6 Update `AbstractTypeBuilder.ResolveMember`: change parameter type from `AbstractMemberDeclarationNode` to `MemberDeclarationNode`, update case labels (e.g., `MethodMemberMemberDeclarationNode` with `MethodNoBody` for abstract methods, `PropertyMemberMemberDeclarationNode` for abstract properties)

- [ ] 4. Checkpoint — build ProgramModel layer and verify zero errors
  - [ ] 4.1 Run `dotnet build` on the solution (expect errors only in downstream consumers, not ProgramModel)

- [ ] 5. Update MistPrettyPrinter (`Source/Mist/Parsing/MistPrettyPrinter.cs`)
  - [ ] 5.1 Replace `EmitTypeDeclaration` dispatch: remove `ClassTypeTypeDeclarationNode`, `StructTypeTypeDeclarationNode`, `AbstractTypeTypeDeclarationNode` cases; add `BodyTypeTypeDeclarationNode` case calling new `EmitTypeBodyDeclaration`
  - [ ] 5.2 Add `EmitTypeBodyDeclaration(TypeBodyDeclarationNode)` method: emit attributes, modifiers, keyword (from `Keyword` variant), name, type params, base clause, constraints, and member list
  - [ ] 5.3 Add `NewtypeTypeTypeDeclarationNode` and `TypeAliasTypeTypeDeclarationNode` cases to `EmitTypeDeclaration` (previously handled at TopLevelItem/NamespaceBodyItem level)
  - [ ] 5.4 Remove `EmitClassDeclaration`, `EmitStructDeclaration`, `EmitAbstractDeclaration` methods (replaced by unified `EmitTypeBodyDeclaration`)
  - [ ] 5.5 Remove `EmitAbstractMemberDeclaration` method and its dispatch — abstract members are now `MemberDeclarationNode` variants handled by the existing `EmitMemberDeclaration`
  - [ ] 5.6 Update TopLevelItem emission: remove `NewtypeItemTopLevelItemNode` and `TypeAliasItemTopLevelItemNode` cases (now reached through `TypeItemTopLevelItemNode`)
  - [ ] 5.7 Update NamespaceBodyItem emission: remove `NamespaceNewtypeItemNamespaceBodyItemNode` and `NamespaceTypeAliasItemNamespaceBodyItemNode` cases

- [ ] 6. Update CSharpEmitter (`Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs`)
  - [ ] 6.1 Update type declaration dispatch (`EmitTypeDeclaration`): replace `ClassTypeTypeDeclarationNode`/`StructTypeTypeDeclarationNode`/`AbstractTypeTypeDeclarationNode` cases with single `BodyTypeTypeDeclarationNode` case that dispatches based on keyword variant
  - [ ] 6.2 Update `EmitClassDeclaration` / `EmitStructDeclaration` to accept `TypeBodyDeclarationNode` (or create unified `EmitTypeBody` method with keyword parameter)
  - [ ] 6.3 Update `EmitAbstractDeclaration`: switch from iterating `AbstractMemberDeclarationNode` to `MemberDeclarationNode`, identifying abstract methods as `MethodMemberMemberDeclarationNode` with `MethodNoBody`
  - [ ] 6.4 Update all TopLevelItem dispatch sites: remove `NewtypeItemTopLevelItemNode` and `TypeAliasItemTopLevelItemNode` cases; handle newtypes/aliases via `TypeItemTopLevelItemNode` → `NewtypeTypeTypeDeclarationNode` / `TypeAliasTypeTypeDeclarationNode`
  - [ ] 6.5 Update all NamespaceBodyItem dispatch sites: remove `NamespaceNewtypeItemNamespaceBodyItemNode` and `NamespaceTypeAliasItemNamespaceBodyItemNode` cases; handle through `NamespaceTypeItemNamespaceBodyItemNode`
  - [ ] 6.6 Update member extraction helpers (get members from declaration): replace class/struct switch with `BodyTypeTypeDeclarationNode` → `body.TypeBody.Members`
  - [ ] 6.7 Update `GetTypeContainingNamespace` and similar utility methods that switch on declaration node types
  - [ ] 6.8 Update `EmitExplicitInterfaceImplsForParameterizedSlots` signature: replace `ClassDeclarationNode` parameter with `TypeBodyDeclarationNode`

- [ ] 7. Checkpoint — build and verify zero errors in Source/Mist
  - [ ] 7.1 Run `dotnet build` on the solution (expect errors only in test projects)

- [ ] 8. Update SemanticModelBuilder (`Source/Mist/Semantics/Model/SemanticModelBuilder.cs`)
  - [ ] 8.1 Update Pass 1 type registration: replace `ClassTypeTypeDeclarationNode`/`StructTypeTypeDeclarationNode`/`AbstractTypeTypeDeclarationNode` dispatch in `RegisterTypeDeclaration` with `BodyTypeTypeDeclarationNode` → keyword switch
  - [ ] 8.2 Update `GetTypeNameAndNode` helper: replace three-case switch with `BodyTypeTypeDeclarationNode` → `(body.TypeBody.Name.Lexeme, body.TypeBody)`
  - [ ] 8.3 Update TopLevelItem processing: remove `TypeAliasItemTopLevelItemNode` and `NewtypeItemTopLevelItemNode` cases; handle through `TypeItemTopLevelItemNode` → `TypeAliasTypeTypeDeclarationNode` / `NewtypeTypeTypeDeclarationNode`
  - [ ] 8.4 Update NamespaceBodyItem processing: remove `NamespaceNewtypeItemNamespaceBodyItemNode` and `NamespaceTypeAliasItemNamespaceBodyItemNode` handling
  - [ ] 8.5 Update Pass 2 type resolution: replace three-case dispatch in `ResolveTypeDeclaration` with `BodyTypeTypeDeclarationNode` keyword switch calling the appropriate resolve method
  - [ ] 8.6 Update explicit member type resolution: replace three-case dispatch in `ResolveExplicitMemberTypes` with `BodyTypeTypeDeclarationNode` keyword switch
  - [ ] 8.7 Update abstract member iteration: replace `AbstractMemberDeclarationNode` switch with `MemberDeclarationNode` switch (identify abstract methods by `MethodNoBody`, abstract properties by `AutoGetAccessor`/`AutoSetAccessor`)
  - [ ] 8.8 Update `DetectDuplicateAssociatedTypeSlots`: change parameter from `ImmutableArray<AbstractMemberDeclarationNode>` to `ImmutableArray<MemberDeclarationNode>`
  - [ ] 8.9 Update conformance checking / abstract member emission: replace `AbstractMemberDeclarationNode` dispatch with `MemberDeclarationNode` dispatch
  - [ ] 8.10 Update `IsTopLevelType` helper and similar AST-matching utilities that reference old node types
  - [ ] 8.11 Update declaration node extraction in visibility/namespace resolution helpers
  - [ ] 8.12 Remove any remaining references to `ClassDeclarationNode`, `StructDeclarationNode`, `AbstractDeclarationNode` (these types no longer exist)

- [ ] 9. Checkpoint — full build zero warnings
  - [ ] 9.1 Run `dotnet build` on the full solution including test projects
  - [ ] 9.2 Run `dotnet format Mist.slnx --verify-no-changes` to confirm style compliance

- [ ] 10. Update test files — Generators
  - [ ] 10.1 Update `MistProgramGenerator.cs`: replace `ClassTypeTypeDeclarationNode { ClassDeclaration = ... }` with `BodyTypeTypeDeclarationNode { TypeBody = new TypeBodyDeclarationNode { Keyword = new ClassKeywordTypeKeywordNode(), ... } }`
  - [ ] 10.2 Update `MistOverloadGenerator.cs`: same pattern for class node construction
  - [ ] 10.3 Update `MistPropertyGenerator.cs`: update class and abstract node construction; replace `AbstractPropertyMemberAbstractMemberDeclarationNode` with appropriate `MemberDeclarationNode` variant
  - [ ] 10.4 Update `MistCastOperatorGenerator.cs`: update class and struct node construction
  - [ ] 10.5 Update remaining generators that construct type declaration nodes (`MistTypeGenerator.cs`, `MistEnumGenerator.cs`, `MistNewtypeGenerator.cs`, `MistTypeAliasGenerator.cs`, etc.)

- [ ] 11. Update test files — Parsing tests
  - [ ] 11.1 Update `AbstractDefaultMethodParsingTests.cs`: replace `AbstractMemberDeclarationNode` assertions with `MemberDeclarationNode` assertions
  - [ ] 11.2 Update `AbstractDefaultMethodClassificationPropertyTests.cs`: same pattern
  - [ ] 11.3 Update `GenericRoundTripPropertyTests.cs`: update abstract node construction and `GenAbstractMethodList` to use `MemberDeclarationNode`
  - [ ] 11.4 Update `DependentTypeRoundTripPropertyTests.cs`: update `AbstractDeclarationNode` construction
  - [ ] 11.5 Update `UnknownTypeRoundTripPropertyTests.cs`, `OptionalOperatorParsingPropertyTests.cs`, `ArrayLiteralPatternDisambiguationPropertyTests.cs`: replace `ClassTypeTypeDeclarationNode` casts/assertions with `BodyTypeTypeDeclarationNode`
  - [ ] 11.6 Update `NewtypeParserPropertyTests.cs`, `NewtypeMissingSemicolonPropertyTests.cs`: update node type assertions (newtypes now come through `TypeItemTopLevelItemNode` → `NewtypeTypeTypeDeclarationNode`)
  - [ ] 11.7 Update `TypeAliasParserPropertyTests.cs`, `AssociatedTypeDisambiguationPropertyTests.cs`, `ConstraintClauseParsingTests.cs`: update node type assertions for type aliases (now through `TypeItemTopLevelItemNode` → `TypeAliasTypeTypeDeclarationNode`)

- [ ] 12. Update test files — Backend tests
  - [ ] 12.1 Update `EmitterOutputTests.cs`: replace all `ClassTypeTypeDeclarationNode`/`StructTypeTypeDeclarationNode` construction with `BodyTypeTypeDeclarationNode`
  - [ ] 12.2 Update `AbstractEmissionPropertyTests.cs`: replace `AbstractTypeTypeDeclarationNode` construction
  - [ ] 12.3 Update `ValueTaskEmissionPropertyTests.cs`: replace class node construction
  - [ ] 12.4 Update remaining backend test files that construct AST nodes with old types

- [ ] 13. Update test files — Semantics tests
  - [ ] 13.1 Update `TypeAliasRegistrationPropertyTests.cs`: replace `TypeAliasItemTopLevelItemNode` construction with `TypeItemTopLevelItemNode` wrapping `TypeAliasTypeTypeDeclarationNode`
  - [ ] 13.2 Update `NewtypeSymbolRegistrationPropertyTests.cs`: replace `NewtypeItemTopLevelItemNode` construction with `TypeItemTopLevelItemNode` wrapping `NewtypeTypeTypeDeclarationNode`
  - [ ] 13.3 Update remaining semantics tests that reference old node types

- [ ] 14. Update Generated extension files
  - [ ] 14.1 Update `CompilationUnitNodeExtensions.cs`: if `AsTopLevelItems()` or `AsNamespaceBodyItems()` helpers reference removed node types, update them
  - [ ] 14.2 Verify all hand-written extension methods in `Source/Mist/Generated/` compile with the new node hierarchy

- [ ] 15. Final verification
  - [ ] 15.1 Full build zero warnings: `dotnet build Mist.slnx`
  - [ ] 15.2 All tests pass: `dotnet test Mist.slnx`
  - [ ] 15.3 Run `dotnet format Mist.slnx --verify-no-changes`
  - [ ] 15.4 Verify sample programs compile and produce identical output (run `samples/AllFeatures`)

## Task Dependency Graph

```json
{
  "waves": [
    {"tasks": [1]},
    {"tasks": [2]},
    {"tasks": [3]},
    {"tasks": [4]},
    {"tasks": [5, 6, 8]},
    {"tasks": [7]},
    {"tasks": [9]},
    {"tasks": [10, 11, 12, 13, 14]},
    {"tasks": [15]}
  ]
}
```

## Notes

- **After task 1, the code will NOT compile** until tasks 2–3 are complete (ProgramModel layer). Tasks 5–8 can then proceed to clear remaining errors wave by wave.
- **Tasks 5, 6, and 8 are independent** of each other once task 4 passes. They can be done in any order, but the listed order follows compilation dependency (PrettyPrinter has no dependencies, CSharpEmitter depends on SemanticModel types but not on PrettyPrinter, SemanticModelBuilder is standalone).
- **Task 8 is the largest** (~27,000 line file) but the transformation is mechanical: every occurrence of the old three-way dispatch becomes a single `BodyTypeTypeDeclarationNode` case with a keyword switch.
- **Test tasks 10–14 are independent** of each other and can be done in parallel once task 9 passes (the production code compiles).
- **The `MemberDeclaration` rule itself does NOT change** — it already has all needed variants. But `AbstractMemberDeclaration` references throughout the codebase must be updated to use `MemberDeclarationNode` types instead.
