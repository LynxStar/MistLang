# Requirements Document

## Introduction

This document specifies the requirements for revising the ownership keyword system in the Mist programming language. The revision renames two ownership specifiers to free the `ref` keyword for future C#-style pass-by-reference semantics:

- `frozen ref` becomes `frozen` (the `ref` is redundant since frozen is always a reference by definition)
- `ref` (bare) becomes `borrowed` (a new keyword that explicitly names the mutable borrow tier)

The semantic behavior of all ownership tiers remains unchanged. This is purely a surface syntax and naming revision. The `readonly ref` compound specifier is intentionally preserved because it will have dual meaning once C#-style refs are introduced in a future spec.

After this revision, tiers 3, 4, and 5 each have a single dedicated keyword for ownership semantics: `frozen`, `borrowed`, `owned`.

## Glossary

- **Lexer**: The component that tokenizes Mist source text into keyword and identifier tokens. Defined in `MistLexer.cs` and `keywords.storm`.
- **Parser**: The component that builds an AST from tokens using the Storm grammar rules defined in `Mist.storm`.
- **OwnershipSpecifier**: A grammar rule that matches ownership keywords preceding type references in declarations.
- **OwnershipTier**: A C# enum in the semantic model representing the five ownership levels: Unrestricted, ReadonlyRef, Frozen, Borrowed, Owned.
- **SemanticModelBuilder**: The component that maps AST nodes to semantic model types, including the `MapOwnershipSpecifier` method.
- **Pretty_Printer**: The `MistPrettyPrinter` class that converts AST nodes back to Mist source text.
- **TextMate_Grammar**: The JSON file at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json` that provides VS Code syntax highlighting.
- **OwnershipAnalyzer**: The built-in analyzer that enforces ownership rules using flow-sensitive analysis.
- **Sample_App**: The sample project at `samples/OwnershipSystem/Program.mist` demonstrating ownership patterns.

## Requirements

### Requirement 1: Add `borrowed` Keyword Token

**User Story:** As a language developer, I want the lexer to recognize `borrowed` as a reserved keyword, so that it can be used as the mutable borrow ownership specifier.

#### Acceptance Criteria

1. THE Lexer SHALL tokenize the text `borrowed` as a `BORROWED` keyword token distinct from `IDENTIFIER`.
2. THE `keywords.storm` file SHALL define a `BORROWED` token with the value `"borrowed"` in the ownership keywords section alongside `OWNED`, `FROZEN`, and `REF`.
3. WHEN `borrowed` appears in a source position where an `IDENTIFIER` token is expected, THE Parser SHALL report a syntax error, because the `BORROWED` keyword token takes priority over `IDENTIFIER` matching.
4. THE existing `FROZEN`, `OWNED`, and `REF` keyword tokens SHALL remain defined and unchanged (REF is reserved for future C#-style ref semantics).

### Requirement 2: Revise OwnershipSpecifier Grammar Rule

**User Story:** As a language developer, I want the parser grammar to use `frozen` (without `ref`) and `borrowed` (instead of bare `ref`) as ownership specifiers, so that the `ref` keyword is freed for future use.

#### Acceptance Criteria

1. THE Parser SHALL recognize `frozen` alone (without `ref`) as the frozen ownership specifier, producing an AST node named `FrozenSpecifier`.
2. THE Parser SHALL recognize `borrowed` alone as the borrowed ownership specifier, producing an AST node named `BorrowedSpecifier`.
3. THE Parser SHALL continue to recognize `readonly ref` as the compound readonly-ref ownership specifier, producing an AST node named `ReadonlyRefSpecifier`.
4. THE Parser SHALL continue to recognize `owned` as the owned ownership specifier, producing an AST node named `OwnedSpecifier`.
5. IF bare `ref` appears in an ownership specifier position, THEN THE Parser SHALL reject the input and emit a diagnostic indicating that `ref` is not a valid ownership specifier.
6. IF `frozen ref` appears in an ownership specifier position, THEN THE Parser SHALL reject the input and emit a diagnostic indicating that `frozen ref` is not a valid ownership specifier.
7. WHEN no ownership keyword is present on a reference-type declaration, THE Parser SHALL produce no `OwnershipSpecifier` AST node for that declaration, representing unrestricted ownership.
8. THE grammar rule `OwnershipSpecifier` SHALL define exactly four alternatives: `OwnedSpecifier`, `FrozenSpecifier`, `ReadonlyRefSpecifier`, and `BorrowedSpecifier`.

### Requirement 3: Rename OwnershipTier Enum Values

**User Story:** As a language developer, I want the semantic model enum to reflect the new keyword names, so that the codebase is consistent and self-documenting.

#### Acceptance Criteria

1. THE `OwnershipTier` enum SHALL rename `FrozenRef` to `Frozen`.
2. THE `OwnershipTier` enum SHALL rename `Ref` to `Borrowed`.
3. THE `OwnershipTier` enum SHALL retain `Unrestricted`, `ReadonlyRef`, and `Owned` unchanged.
4. THE `SemanticModelBuilder.MapOwnershipSpecifier` method SHALL map the new `FrozenSpecifier` AST node to `OwnershipTier.Frozen` and SHALL remove the mapping from the old `FrozenRefSpecifier` AST node.
5. THE `SemanticModelBuilder.MapOwnershipSpecifier` method SHALL map the new `BorrowedSpecifier` AST node to `OwnershipTier.Borrowed` and SHALL remove the mapping from the old `RefSpecifier` AST node.
6. WHEN the enum values are renamed, THE codebase SHALL update all references to the old `OwnershipTier.FrozenRef` and `OwnershipTier.Ref` values to use `OwnershipTier.Frozen` and `OwnershipTier.Borrowed` respectively, such that the solution compiles with zero errors and zero warnings.

### Requirement 4: Update Ownership Analyzer References

**User Story:** As a language developer, I want all analyzer code that references the old enum values to use the new names, so that the build compiles without errors.

#### Acceptance Criteria

1. THE `OwnershipAnalyzer` SHALL reference `OwnershipTier.Frozen` in every location where it previously referenced `OwnershipTier.FrozenRef`, including variable declaration borrow creation, non-storability checks, and any pattern-matching or comparison expressions.
2. THE `OwnershipAnalyzer` SHALL reference `OwnershipTier.Borrowed` in every location where it previously referenced `OwnershipTier.Ref`, including variable declaration borrow creation, non-storability checks, exclusivity checks, and any pattern-matching or comparison expressions.
3. THE `MethodFlowState` SHALL reference `OwnershipTier.Frozen` in every location where it previously referenced `OwnershipTier.FrozenRef`, including the `RestoreLenderState` switch case and the `HasRemainingFrozenBorrows` comparison.
4. THE `MethodFlowState` SHALL reference `OwnershipTier.Borrowed` in every location where it previously referenced `OwnershipTier.Ref`, including the `RestoreLenderState` switch case.
5. THE `BorrowEntry` record SHALL retain its `BorrowTier` property typed as `OwnershipTier` with no changes to the property name or type, accepting the renamed enum values `Frozen` and `Borrowed` where `FrozenRef` and `Ref` were used previously.
6. THE `VariableOwnershipState` enum SHALL retain its existing values (`Live`, `Moved`, `LentRef`, `LentFrozen`, `LentReadonly`) unchanged, since these names describe lending states and are not derived from the `OwnershipTier` enum member names.
7. WHEN the analyzer project is compiled after all renames are applied, THE build SHALL produce zero errors and zero warnings related to the `OwnershipTier` references in `OwnershipAnalyzer`, `MethodFlowState`, and `BorrowEntry`.

### Requirement 5: Update Pretty Printer

**User Story:** As a language developer, I want the pretty printer to emit the new keyword syntax, so that round-trip parsing produces correct output.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL emit `frozen ` (with trailing space) for the `FrozenSpecifier` ownership specifier AST node.
2. THE Pretty_Printer SHALL emit `borrowed ` (with trailing space) for the `BorrowedSpecifier` ownership specifier AST node.
3. THE Pretty_Printer SHALL continue to emit `readonly ref ` for the `ReadonlyRefSpecifier` ownership specifier.
4. THE Pretty_Printer SHALL continue to emit `owned ` for the `OwnedSpecifier` ownership specifier.
5. THE Pretty_Printer SHALL NOT emit `frozen ref ` or bare `ref ` as ownership specifier output.
6. FOR ALL valid ownership-annotated declarations (variable declarations, parameters, return types, and fields), THE Pretty_Printer SHALL satisfy print-parse idempotence: pretty-printing an AST, parsing the resulting source text, and pretty-printing again SHALL produce identical output strings.

### Requirement 6: Update TextMate Grammar

**User Story:** As a Mist developer using VS Code, I want the revised ownership keywords to be syntax-highlighted correctly, so that I get visual cues for the new keyword names.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL recognize `borrowed` as a storage modifier keyword (scope `storage.modifier.mist`).
2. THE TextMate_Grammar SHALL continue to recognize `frozen` as a storage modifier keyword (scope `storage.modifier.mist`).
3. THE TextMate_Grammar SHALL continue to recognize `owned` as a storage modifier keyword (scope `storage.modifier.mist`).
4. THE TextMate_Grammar SHALL continue to recognize `ref` as a storage modifier keyword (scope `storage.modifier.mist`) since it remains a reserved keyword for future use.
5. THE TextMate_Grammar SHALL match these keywords only at word boundaries so that identifiers containing a keyword as a substring (e.g., `borrowedItem`, `unfrozen`, `ownedPtr`) do NOT receive the `storage.modifier.mist` scope.

### Requirement 7: Update Sample Application

**User Story:** As a Mist developer, I want the ownership sample application to use the revised keywords, so that it demonstrates the current syntax and compiles without errors.

#### Acceptance Criteria

1. The sample at `samples/OwnershipSystem/Program.mist` SHALL use `frozen` (without `ref`) for all frozen ownership specifiers and SHALL NOT contain the two-token sequence `frozen ref`.
2. The sample SHALL use `borrowed` for all mutable borrow ownership specifiers and SHALL NOT contain bare `ref` as an ownership specifier (previously bare `ref` indicated mutable borrow).
3. The sample SHALL continue to use `readonly ref` for readonly-ref specifiers.
4. The sample SHALL continue to use `owned` for owned specifiers.
5. The sample SHALL demonstrate all five tiers by declaring at least one parameter or local variable using each specifier: unrestricted (bare, no annotation), `readonly ref`, `frozen`, `borrowed`, `owned`.
6. WHEN the sample runs, THE System SHALL print at least one line of output per demonstrated tier, each prefixed with a bracket label identifying the tier (e.g., `[frozen]`, `[borrowed]`), so that a tester can verify each tier executed by inspecting stdout.
7. `samples/AllFeatures/Program.mist` SHALL continue to call `OwnershipSystem.Program.RunDemo()`.
8. WHEN the sample is compiled and executed, THE System SHALL exit with code 0 and produce zero compiler warnings.

### Requirement 8: Update Existing Tests

**User Story:** As a language developer, I want all existing ownership tests to use the new keywords, so that the test suite passes with the revised syntax.

#### Acceptance Criteria

1. WHEN the `OwnershipLendingPropertyTests` generate Mist source containing a mutable borrow specifier, THE test generators SHALL emit `borrowed ` where they previously emitted `ref `.
2. WHEN the `OwnershipLendingPropertyTests` generate Mist source containing an immutable-blocking borrow specifier, THE test generators SHALL emit `frozen ` where they previously emitted `frozen ref `.
3. WHEN the `OwnershipStorabilityPropertyTests` generate Mist source via the `RefTierKeywords` array, THE array SHALL contain `"borrowed"`, `"readonly ref"`, and `"frozen"` instead of `"ref"`, `"readonly ref"`, and `"frozen ref"`.
4. WHEN the `OwnershipSignaturePropertyTests` generate specifier/label pairs, THE pairs SHALL use `"borrowed "` with label `"borrowed"` and `"frozen "` with label `"frozen"` instead of `"ref "` with label `"ref"` and `"frozen ref "` with label `"frozen ref"`.
5. THE `MistOwnershipGenerator.GenOwnershipSpecifier()` method SHALL emit `"borrowed "` and `"frozen "` instead of `"ref "` and `"frozen ref "` in its element set.
6. ALL test assertions that reference `OwnershipTier.FrozenRef` SHALL reference `OwnershipTier.Frozen` after the enum is renamed.
7. ALL test assertions that reference `OwnershipTier.Ref` SHALL reference `OwnershipTier.Borrowed` after the enum is renamed.
8. WHEN all keyword and enum updates are applied, THE full test suite SHALL compile with zero errors and all existing property-based tests SHALL pass with the same pass/fail semantics as before the keyword revision.

### Requirement 9: Round-Trip Parsing with Revised Keywords

**User Story:** As a language developer, I want the revised keywords to survive parse-print cycles, so that tooling can reliably reconstruct source.

#### Acceptance Criteria

1. FOR ALL valid ownership-annotated declarations using `frozen` (including variable declarations, parameter declarations, and field declarations), parsing the source, pretty-printing the resulting AST, parsing the pretty-printed output, and pretty-printing again SHALL produce textually identical output on both prints.
2. FOR ALL valid ownership-annotated declarations using `borrowed` (including variable declarations, parameter declarations, and field declarations), parsing the source, pretty-printing the resulting AST, parsing the pretty-printed output, and pretty-printing again SHALL produce textually identical output on both prints.
3. FOR ALL valid declarations using the compound specifier `readonly ref`, parsing the source, pretty-printing the resulting AST, parsing the pretty-printed output, and pretty-printing again SHALL produce textually identical output on both prints, with the literal text `readonly ref` present in the pretty-printed output.
4. IF parsing the pretty-printed output produces parse errors, THEN THE round-trip property SHALL be considered violated and the test SHALL fail, reporting the original source, the pretty-printed output, and the parse errors.
5. FOR ALL valid declarations using `owned`, parsing the source, pretty-printing the resulting AST, parsing the pretty-printed output, and pretty-printing again SHALL produce textually identical output on both prints.

### Requirement 10: Zero Warnings Build

**User Story:** As a language developer, I want the codebase to build with zero warnings after the keyword revision, so that code quality is maintained.

#### Acceptance Criteria

1. WHEN the solution is built in both Debug and Release configurations, THE solution SHALL produce zero C# compiler warnings (CS*) after all renames are applied.
2. WHEN the solution is built, THE solution SHALL produce zero Storm source generator warnings (STORM*) after grammar changes.
3. WHEN the solution is built, THE solution SHALL produce zero MSBuild warnings, excluding trim warnings originating from third-party package references.
4. IF any XML documentation comments reference the old names (`FrozenRef`, `Ref` as a tier name), THEN THE solution SHALL update them to reference the new names (`Frozen`, `Borrowed`).
