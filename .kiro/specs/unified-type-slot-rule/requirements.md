# Requirements Document

## Introduction

This spec covers the refactoring of the Storm grammar's associated type slot declarations from three separate rules (`AssociatedTypeSlotDeclaration`, `UnconstrainedAssociatedTypeSlotDeclaration`, `ModifiedAssociatedTypeSlotDeclaration`) into a single unified rule. The unified rule uses `Modifier*` (zero or more) and an optional constraint clause, covering all three prior cases. This grammar collapse cascades into the parser-generated node types, the `MemberDeclaration` alternatives, the `inline ModifiedTypeHead` block, the ProgramModel builders (`AbstractTypeBuilder`, `TypeSlotBuilder`), the legacy `SemanticModelBuilder`, and the `MistPrettyPrinter`.

## Glossary

- **Storm_Parser**: The PEG parser generator that compiles `.storm` grammar files into C# parser and node types.
- **AssociatedTypeSlotDeclaration**: The unified grammar rule declaring an associated type slot inside an abstract type body, with optional modifiers and optional constraint clause.
- **MemberDeclaration**: The grammar rule that enumerates all possible member declarations inside a type body.
- **ModifiedTypeHead**: An inline prefix rule currently shared between `ModifiedAssociatedTypeSlotDeclaration` and `MemberTypeAliasDeclaration` to factor out the `modifiers TYPE name typeParams` prefix.
- **MemberTypeAliasDeclaration**: A grammar rule declaring a type alias member (`type Name = TypeExpr`), distinguished from a type slot by the presence of `EQUAL`.
- **AbstractTypeBuilder**: The ProgramModel builder that resolves abstract type members including associated type slots.
- **TypeSlotBuilder**: The ProgramModel builder that creates `TypeSlot` instances from associated type slot AST nodes.
- **SemanticModelBuilder**: The legacy semantic system builder that also dispatches on associated type slot node types.
- **MistPrettyPrinter**: The pretty-printer that emits Mist source from AST nodes, including associated type slot declarations.
- **HasModifiers**: A Storm grammar trait that causes the generated node type to implement `IHasModifiersNode`, enabling generic modifier resolution via `ModifierBuilder.Build`.
- **ConstraintClause**: The optional `COLON constraints:ConstraintList slotWhereConstraints:WhereClause*` suffix on an associated type slot declaration.
- **Inline_Block**: A Storm parser construct that shares a common prefix between multiple alternatives to satisfy the 3-token lookahead constraint.

## Requirements

### Requirement 1: Unified Grammar Rule

**User Story:** As a compiler maintainer, I want a single grammar rule for associated type slot declarations, so that the grammar is simpler and the generated node hierarchy has fewer types to dispatch on.

#### Acceptance Criteria

1. THE Storm_Parser SHALL recognize a single `AssociatedTypeSlotDeclaration` rule defined as: `modifiers:Modifier* TYPE name:IDENTIFIER typeParams:TypeParameterList? constraintClause:(COLON constraints:ConstraintList slotWhereConstraints:WhereClause*)? SEMICOLON`
2. THE `AssociatedTypeSlotDeclaration` rule SHALL carry the `HasModifiers` trait annotation, causing the generated node to implement `IHasModifiersNode`
3. WHEN the unified rule is defined, THE Storm_Parser SHALL no longer contain the `UnconstrainedAssociatedTypeSlotDeclaration` rule
4. WHEN the unified rule is defined, THE Storm_Parser SHALL no longer contain the `ModifiedAssociatedTypeSlotDeclaration` rule

### Requirement 2: MemberDeclaration Alternatives Collapse

**User Story:** As a compiler maintainer, I want the `MemberDeclaration` rule to reference a single associated type slot alternative, so that member dispatch is simpler.

#### Acceptance Criteria

1. THE `MemberDeclaration` rule SHALL contain exactly one alternative for associated type slot declarations referencing the unified `AssociatedTypeSlotDeclaration` rule
2. WHEN the unified rule is defined, THE `MemberDeclaration` rule SHALL no longer contain separate `UnconstrainedAssociatedTypeMember` or `ModifiedAssociatedTypeMember` alternatives
3. THE `MemberDeclaration` rule SHALL remain unambiguous with `MemberTypeAliasDeclaration` by using an inline block that shares the `Modifier* TYPE IDENTIFIER TypeParameterList?` prefix and differentiates on `EQUAL` (type alias) versus `COLON` or `SEMICOLON` (type slot)

### Requirement 3: Inline Prefix Restructuring

**User Story:** As a compiler maintainer, I want the inline block in `MemberDeclaration` restructured to share the common prefix between the unified type slot rule and the type alias rule, so that the 3-token lookahead constraint of the PEG parser is satisfied.

#### Acceptance Criteria

1. THE `MemberDeclaration` rule SHALL use an inline block that factors out the shared `Modifier* TYPE IDENTIFIER TypeParameterList?` prefix between `AssociatedTypeSlotDeclaration` and `MemberTypeAliasDeclaration`
2. WHEN the inline prefix is parsed, THE Storm_Parser SHALL differentiate into the type alias alternative upon encountering `EQUAL` and into the type slot alternative upon encountering `COLON` or `SEMICOLON`
3. WHEN the inline block is restructured, THE `ModifiedTypeHead` rule SHALL be removed from the grammar if it is no longer referenced by any other rule

### Requirement 4: TypeSlotBuilder Unification

**User Story:** As a compiler maintainer, I want `TypeSlotBuilder` to have a single resolution method for all associated type slots, so that the three-method dispatch is eliminated.

#### Acceptance Criteria

1. THE `TypeSlotBuilder` SHALL expose a single `Resolve` method that accepts the unified `AssociatedTypeSlotDeclarationNode`
2. WHEN the unified node has zero modifiers, THE `TypeSlotBuilder` SHALL produce a `TypeSlot` with default modifiers
3. WHEN the unified node has one or more modifiers, THE `TypeSlotBuilder` SHALL resolve modifiers using `ModifierBuilder.Build` and produce a `TypeSlot` with those modifiers
4. WHEN the unified node has a constraint clause, THE `TypeSlotBuilder` SHALL preserve the constraint information on the resulting `TypeSlot`
5. THE `TypeSlotBuilder` SHALL no longer contain the `ResolveUnconstrained` or `ResolveModified` methods

### Requirement 5: AbstractTypeBuilder Dispatch Simplification

**User Story:** As a compiler maintainer, I want `AbstractTypeBuilder` to dispatch on a single associated type slot node type, so that the three-case switch is reduced to one.

#### Acceptance Criteria

1. THE `AbstractTypeBuilder` SHALL handle associated type slot members via a single `case` branch matching the unified node type
2. WHEN a member is an associated type slot, THE `AbstractTypeBuilder` SHALL delegate to `TypeSlotBuilder.Resolve` with the unified node
3. THE `AbstractTypeBuilder` SHALL no longer contain separate case branches for `UnconstrainedAssociatedTypeMemberMemberDeclarationNode` or `ModifiedAssociatedTypeMemberMemberDeclarationNode`

### Requirement 6: SemanticModelBuilder Update

**User Story:** As a compiler maintainer, I want the legacy `SemanticModelBuilder` updated to handle the unified node type, so that existing compilation continues working through the old system.

#### Acceptance Criteria

1. THE `SemanticModelBuilder` SHALL dispatch on the single unified associated type slot member node type in the abstract member resolution switch
2. WHEN a unified node has modifiers, THE `SemanticModelBuilder` SHALL process modifiers using the same logic previously in the modified-slot path
3. WHEN a unified node has no constraint clause, THE `SemanticModelBuilder` SHALL process the slot using the same logic previously in the unconstrained path
4. WHEN a unified node has a constraint clause and no modifiers, THE `SemanticModelBuilder` SHALL process the slot using the same logic previously in the constrained path
5. THE `SemanticModelBuilder` SHALL no longer contain separate methods for `RegisterUnconstrainedAssociatedTypeSlotDeclaration` or separate handling for `ModifiedAssociatedTypeMemberMemberDeclarationNode`

### Requirement 7: PrettyPrinter Update

**User Story:** As a compiler maintainer, I want the `MistPrettyPrinter` updated to emit associated type slots from the unified node, so that round-trip printing remains correct.

#### Acceptance Criteria

1. THE `MistPrettyPrinter` SHALL handle associated type slot emission via a single method accepting the unified `AssociatedTypeSlotDeclarationNode`
2. WHEN the unified node has modifiers, THE `MistPrettyPrinter` SHALL emit the modifiers before the `type` keyword
3. WHEN the unified node has a constraint clause, THE `MistPrettyPrinter` SHALL emit `COLON` followed by the constraint list and where clauses
4. WHEN the unified node has no constraint clause and no modifiers, THE `MistPrettyPrinter` SHALL emit `type Name;` (the unconstrained form)
5. THE `MistPrettyPrinter` SHALL no longer contain separate methods `EmitUnconstrainedAssociatedTypeSlotDeclaration` or `EmitModifiedAssociatedTypeSlotDeclaration`

### Requirement 8: Existing Tests Continue Passing

**User Story:** As a compiler maintainer, I want all existing tests and sample programs that use associated type slots to continue compiling and passing, so that the refactoring introduces no regressions.

#### Acceptance Criteria

1. WHEN the refactoring is complete, THE compiler SHALL parse all existing Mist programs containing associated type slot declarations without errors
2. WHEN the refactoring is complete, THE full test suite SHALL pass with zero failures attributable to this change
3. WHEN the refactoring is complete, THE `AssociatedTypeDisambiguationPropertyTests` SHALL be updated to assert on the unified node type and continue passing
