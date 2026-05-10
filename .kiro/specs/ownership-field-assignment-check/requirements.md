# Requirements Document

## Introduction

This document specifies the requirements for fixing a bug in the `OwnershipAnalyzer` where field assignments do not enforce lending compatibility. Currently, assigning an unrestricted variable to a field declared with an explicit ownership tier (e.g., `public owned Resource Inner`) is incorrectly allowed. The fix adds a lending compatibility check in `AnalyzeAssignment` when the assignment target is a field access and the resolved field symbol has a non-Unrestricted ownership tier.

This is a bugfix — no new keywords, no new syntax, no sample app changes, and no TextMate grammar changes are needed. The fix reuses the existing `IsLendingCompatible` matrix and the existing diagnostic IDs `MIST_OWN_INCOMPATIBLE_LEND` and `MIST_OWN_UNRESTRICTED_TO_REF`.

## Glossary

- **OwnershipAnalyzer**: The built-in analyzer (`OwnershipAnalyzer.cs`) that enforces ownership and reference system rules against the semantic model.
- **AnalyzeAssignment**: The method within `OwnershipAnalyzer` that processes assignment expressions, checking for moves, non-storability, and mutation blocking.
- **IsLendingCompatible**: The existing static method that implements the lending compatibility matrix, determining whether a source ownership tier can provide access to a target ownership tier.
- **Field_Symbol**: The `ISymbol` resolved from a `MemberAccessSuffixPostfixSuffixNode` representing the field being assigned to.
- **Source_Tier**: The ownership tier of the variable (or expression) on the right-hand side of the assignment.
- **Target_Tier**: The ownership tier declared on the field symbol being assigned to.

## Requirements

### Requirement 1: Lending Compatibility Check on Field Assignment

**User Story:** As a Mist developer, I want the compiler to reject assignments to ownership-annotated fields when the source variable's tier is incompatible, so that ownership guarantees on fields are enforced at assignment sites.

#### Acceptance Criteria

1. WHEN an assignment target resolves to a field with a non-Unrestricted ownership tier, THE OwnershipAnalyzer SHALL check lending compatibility between the Source_Tier of the right-hand side and the Target_Tier of the field.
2. WHEN the Source_Tier is Unrestricted and the Target_Tier is Owned, THE OwnershipAnalyzer SHALL emit diagnostic `MIST_OWN_UNRESTRICTED_TO_REF` with a message indicating that an unrestricted variable cannot be assigned to an owned field.
3. WHEN the Source_Tier is Unrestricted and the Target_Tier is Ref or FrozenRef, THE OwnershipAnalyzer SHALL emit diagnostic `MIST_OWN_UNRESTRICTED_TO_REF` with a message indicating that an unrestricted variable cannot be assigned to a field requiring that tier.
4. WHEN the Source_Tier and Target_Tier combination is not in the permitted lending compatibility set and the Source_Tier is not Unrestricted, THE OwnershipAnalyzer SHALL emit diagnostic `MIST_OWN_INCOMPATIBLE_LEND` with a message identifying the source tier, variable name, and target tier.
5. WHEN the Source_Tier and Target_Tier combination is in the permitted lending compatibility set, THE OwnershipAnalyzer SHALL permit the assignment without emitting a diagnostic.
6. WHEN the field's ownership tier is Unrestricted, THE OwnershipAnalyzer SHALL skip the lending compatibility check for that assignment.

### Requirement 2: Field Assignment Target Resolution

**User Story:** As a Mist developer, I want the compiler to correctly identify field assignments (both `this.Field = value` and `obj.Field = value`) for ownership checking, so that all field assignment sites are covered.

#### Acceptance Criteria

1. WHEN the assignment target is a `PostfixNode` whose last suffix is a `MemberAccessSuffixPostfixSuffixNode`, THE OwnershipAnalyzer SHALL resolve the field symbol from the member access suffix using the semantic model.
2. WHEN the resolved field symbol has `SymbolKind.Field` and a non-Unrestricted `OwnershipTier`, THE OwnershipAnalyzer SHALL apply the lending compatibility check.
3. WHEN the assignment target is `this.Field` (ThisExpressionPrimaryNode with a MemberAccessSuffix), THE OwnershipAnalyzer SHALL resolve and check the field symbol.
4. WHEN the assignment target is `obj.Field` (IdentifierExpressionPrimaryNode with a MemberAccessSuffix), THE OwnershipAnalyzer SHALL resolve and check the field symbol.
5. WHEN the field symbol cannot be resolved (e.g., due to earlier errors), THE OwnershipAnalyzer SHALL skip the check without emitting a diagnostic.

### Requirement 3: Value Type Exemption on Field Assignment

**User Story:** As a Mist developer, I want value types to be freely assignable to any ownership-annotated field, so that structs work naturally without false positives.

#### Acceptance Criteria

1. WHEN the right-hand side expression resolves to a variable whose declared type is a value type, THE OwnershipAnalyzer SHALL skip the lending compatibility check for the field assignment.
2. WHEN the right-hand side is a literal or constructor expression that cannot be resolved to a symbol, THE OwnershipAnalyzer SHALL skip the lending compatibility check.

### Requirement 4: Diagnostic Messages for Field Assignment Violations

**User Story:** As a Mist developer, I want clear error messages when a field assignment violates lending compatibility, so that I understand what tier the field requires and what tier the source provides.

#### Acceptance Criteria

1. WHEN `MIST_OWN_UNRESTRICTED_TO_REF` is emitted for a field assignment, THE diagnostic message SHALL identify the variable name and the field's required ownership tier.
2. WHEN `MIST_OWN_INCOMPATIBLE_LEND` is emitted for a field assignment, THE diagnostic message SHALL identify the source tier, the variable name, and the field's required ownership tier.
3. THE diagnostic location SHALL point to the assignment expression node so the developer can identify the offending assignment.
