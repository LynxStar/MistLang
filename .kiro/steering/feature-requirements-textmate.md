---
inclusion: fileMatch
fileMatchPattern: "**/specs/**/requirements.md"
---

# Feature Requirements: TextMate Grammar Policy

## Rule

Every requirements document for a new language feature that introduces keywords, declarations, or new syntactic constructs MUST include a requirement for updating the TextMate grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.

## What Must Be Covered

The requirements MUST specify that the TextMate grammar is updated to:

1. **Keyword highlighting** — Any new keyword (e.g., `enum`, `trait`, `type`) is added to the appropriate scope (`keyword.declaration.mist`, `keyword.control.mist`, `keyword.other.mist`, or `storage.modifier.mist`).
2. **Declaration name highlighting** — If the keyword introduces a named declaration (e.g., `enum Direction`), the identifier following the keyword is highlighted as a type name with an appropriate scope (e.g., `entity.name.type.enum.mist`).
3. **Additional syntax** — Any other visually distinct syntax introduced by the feature (e.g., new operators, special literals, or unique punctuation) receives appropriate scoping.

## Requirement Template

When writing the TextMate grammar requirement, follow this structure:

```markdown
### Requirement N: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want {feature} declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `{keyword}` as a declaration keyword (scope `keyword.declaration.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `{keyword}` as a type name (scope `entity.name.type.{feature}.mist`).
3. {Any additional syntax-specific criteria.}
```

## When This Does NOT Apply

- Features that do not introduce new keywords or syntactic constructs (e.g., a purely semantic change like adding a new type-checking rule) do not need a TextMate requirement.
- If the feature only reuses existing keywords in new positions, evaluate whether the existing grammar patterns already cover it before adding a requirement.

## Applies To

- All `requirements.md` files inside `.kiro/specs/*/`
