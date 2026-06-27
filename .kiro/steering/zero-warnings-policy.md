# Zero Warnings Policy

## Rule

The codebase MUST build with zero warnings. Do not introduce, ignore, or suppress warnings without explicit user approval.

## When a Warning Appears

If a change you are making introduces a build warning, you MUST:

1. Stop and explain the warning to the user before proceeding.
2. Present the tradeoffs clearly:
   - What the warning means
   - Why the change triggers it
   - Options to resolve it (fix the code, suppress it, or accept it)
   - The cost of each option
3. Wait for the user to choose an approach before continuing.

## Suppression Requires Justification

If the user approves suppressing a warning (e.g., `#pragma warning disable`, `<NoWarn>`, `SuppressMessage`), add a code comment explaining why the suppression is acceptable. Never suppress silently.

## Format Verification

After making changes to files under `Source/` or `Test/`, run `dotnet format --verify-no-changes` as part of your final sanity check before presenting the result. If it reports violations, fix them before committing.

This catches:
- Whitespace and indentation issues (WHITESPACE)
- Unused usings (IDE0005)
- Unused parameters (IDE0060)
- Unnecessary assignments (IDE0059)
- Style violations enforced by `.editorconfig`

If `dotnet format` crashes due to a namespace mismatch (IDE0130), fix that manually first and re-run.

## Applies To

- All C# compiler warnings (CS*)
- All Storm source generator warnings (STORM*)
- All .NET SDK / MSBuild warnings
- All `dotnet format` diagnostics enforced by `.editorconfig`
- Trim warnings from third-party packages are excluded (they come from dependencies we don't control)
