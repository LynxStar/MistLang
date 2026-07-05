# Implementation Plan

## Overview

Collapse three associated type slot grammar rules into one unified rule, then propagate the node type changes through the compiler pipeline. The grammar change produces compile errors in all downstream code; tasks are ordered so each phase resolves a set of errors before moving to the next.

## Tasks

- [x] 1. Replace the three associated type slot rules (`AssociatedTypeSlotDeclaration`, `UnconstrainedAssociatedTypeSlotDeclaration`, `ModifiedAssociatedTypeSlotDeclaration`) with a single unified rule: `rule AssociatedTypeSlotDeclaration : HasModifiers = modifiers:Modifier* TYPE name:IDENTIFIER typeParams:TypeParameterList? constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)? SEMICOLON;`
- [x] 2. Remove the `ModifiedTypeHead` rule from the grammar
- [x] 3. Restructure the `MemberDeclaration` rule to use an inline block sharing the `Modifier* TYPE IDENTIFIER TypeParameterList?` prefix between `MemberTypeAliasDeclaration` and `AssociatedTypeSlotDeclaration`
- [x] 4. Update `MemberTypeAliasDeclaration` to use `modifiers:Modifier*` (from `Modifier+`) to match the shared inline prefix
- [x] 5. Collapse `TypeSlotBuilder.Resolve`, `ResolveUnconstrained`, and `ResolveModified` into a single `Resolve` instance method accepting `AssociatedTypeSlotDeclarationNode`, using `ModifierBuilder.Build(slotDecl, context.Diagnostics)` via the generic overload
- [x] 6. Replace the three case branches in `AbstractTypeBuilder` (`AssociatedTypeMemberMemberDeclarationNode`, `UnconstrainedAssociatedTypeMemberMemberDeclarationNode`, `ModifiedAssociatedTypeMemberMemberDeclarationNode`) with a single `AssociatedTypeMemberMemberDeclarationNode` case calling `typeSlotBuilder.Resolve`
- [x] 7. Replace the three case branches in `ConcreteTypeBuilder` for "type slot not allowed on concrete" with a single `AssociatedTypeMemberMemberDeclarationNode` case
- [x] 8. Collapse the three-case dispatch in `SemanticModelBuilder`'s abstract member loop into a single `AssociatedTypeMemberMemberDeclarationNode` case
- [x] 9. Merge `RegisterAssociatedTypeSlotDeclaration` and `RegisterUnconstrainedAssociatedTypeSlotDeclaration` in `SemanticModelBuilder` into a single method handling both constrained and unconstrained nodes
- [x] 10. Update `EmitModifiersOnAssociatedTypeSlotDiagnostic` to accept the unified `AssociatedTypeSlotDeclarationNode`
- [x] 11. Simplify `DetectDuplicateAssociatedTypeSlots` to match only the unified node type
- [x] 12. Add null checks for `Constraints` and `SlotWhereConstraints` in the where-clause resolution pass and any pattern-match expressions that access those fields
- [x] 13. Collapse the three emission methods in `MistPrettyPrinter` into a single `EmitAssociatedTypeSlotDeclaration` handling modifiers and optional constraints
- [x] 14. Update the member dispatch switch in `MistPrettyPrinter` to use the single `AssociatedTypeMemberMemberDeclarationNode` case
- [x] 15. Update `AssociatedTypeDisambiguationPropertyTests` to assert on the unified node type
- [x] 16. Build the solution and fix any remaining compile errors from removed node types
- [x] 17. Run `dotnet build Mist.slnx` and confirm zero errors and zero warnings
- [x] 18. Run `dotnet test Mist.slnx` and confirm all tests pass
- [x] 19. Run `dotnet format Mist.slnx --verify-no-changes` and confirm no formatting violations

## Task Dependencies

- Tasks 1, 2, 3, 4: No dependencies (Wave 1 - grammar changes)
- Tasks 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15: Depend on tasks 1, 2, 3, 4 (Wave 2 - fix compile errors)
- Task 16: Depends on tasks 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
- Task 17: Depends on task 16
- Task 18: Depends on task 17
- Task 19: Depends on task 18

## Notes

- The grammar change (tasks 1-4) will produce ~1000+ compile errors because the old node types vanish. This is expected — work through them systematically.
- The `inline` block syntax in task 3 must reference the full rules by name (not an anonymous prefix) per Storm's current inline block design.
- Task 4 changes `MemberTypeAliasDeclaration` from `Modifier+` to `Modifier*`. This means a modifier-less type alias becomes syntactically valid inside a type body. Semantic validation should reject it if undesired, but the grammar will accept it.
- The unified `AssociatedTypeSlotDeclarationNode` makes `Constraints` and `SlotWhereConstraints` nullable fields. All access sites must be audited for null safety (task 12).
