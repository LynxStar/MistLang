---
inclusion: fileMatch
fileMatchPattern: "**/specs/**/requirements.md"
---

# Feature Requirements: Sample App Policy

## Rule

Every requirements document for a new language feature MUST include a requirement for a dedicated sample application and integration into the AllFeatures orchestrator.

## Dedicated Sample App

The requirements MUST specify a sample app located at `samples/{FeatureName}/Program.mist` that:

1. Exercises **every permutation** of the new feature — every syntax variant, every valid combination of modifiers, every interaction with existing features that the requirements describe.
2. Prints labeled output for each permutation so correctness can be verified by inspecting stdout.
3. Includes a `public static void RunDemo()` entry point that the AllFeatures orchestrator can call.

"Exhaust all permutations" means: if the feature has N independent axes of variation (e.g., simple alias, generic alias, union alias, alias used as parameter type, alias used as return type, alias used in construction), the sample MUST demonstrate each axis and meaningful cross-products.

## AllFeatures Integration

The requirements MUST include a criterion that `samples/AllFeatures/Program.mist` calls `{FeatureName}.Program.RunDemo()` so the feature is exercised as part of the full-suite sample run.

## Requirement Template

When writing the sample-app requirement, follow this structure:

```markdown
### Requirement N: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating type aliases, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/{FeatureName}/Program.mist`.
2. The sample SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: {list each permutation axis}.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `{FeatureName}.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.
```

## Applies To

- All `requirements.md` files inside `.kiro/specs/*/`
