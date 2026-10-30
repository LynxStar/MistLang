# Implementation Plan: Tuple Positional Switch Patterns

## Overview

Enable tuple positional patterns (`(pattern, pattern, ...)`) in switch expression arms by reordering the `PrimaryPattern` grammar alternatives. The core fix is moving `TuplePositionalPattern` between `TypePattern` and `BareTypePattern` so parenthesized patterns without a trailing binding name are parsed as positional patterns rather than tuple type references.

## Tasks

- [ ] 1. Reorder PrimaryPattern grammar alternatives in `Source/Mist/Grammar/Mist.storm`: move `TuplePositionalPattern` to position 2 (after `TypePattern`, before `BareTypePattern`) and `ParenthesizedPattern` to position 3. Rebuild solution to regenerate the parser.
- [ ] 2. Run full test suite (`dotnet test` on Mist.Tests) and confirm zero regressions. Run `mist run samples/AllFeatures` and `mist run samples/TuplePatternMatching` and confirm correct output.
- [ ] 3. Verify Pendragon.Tests compiles and passes with the tuple switch version of `IsValidTransition`: run `mist test Pendragon/Pendragon.Tests` and confirm all 4 tests pass with zero `MIST_UNDEFINED_TYPE (historical)` errors.
- [ ] 4. Add enum declarations to `samples/TuplePatternMatching/`: `Direction` (name-only), `LogLevel` (string-backed, explicit values), `LogCategory` (string-backed, auto-inferred), `HttpMethod` (string-backed, auto-inferred), `HttpStatus` (int-backed, explicit values).
- [ ] 5. Add proof code to `samples/TuplePatternMatching/Program.mist` RunDemo(): enum tuple switch, string-backed enum switch, three-element tuple switch, mixed enum/discard, enum with var binding, cross-type tuple (enum + int). Verify build and run produces expected output.
- [ ] 6. Update AllFeatures baseline: run `mist run samples/AllFeatures --quiet`, update `samples/__baselines__/AllFeatures.out`, confirm `SamplesCompileAndMatchBaselineTest` passes.
- [ ] 7. Verify TextMate grammar highlights tuple positional patterns with enum members correctly (no changes expected — verification only).
- [ ] 8. Add book documentation: create `book/snippets/type-system/switch-expressions/tuple-positional-switch.mist` and `.cs`, add "Tuple Positional Patterns in Switch Arms" section to `book/src/type-system/switch-expressions.md`.

## Task Dependency Graph

```json
{
  "waves": [
    [1],
    [2, 4, 7],
    [3, 5],
    [6, 8]
  ]
}
```

## Notes

- Task 1 is the only compiler code change. All downstream components already handle `TuplePositionalPatternPrimaryPatternNode`.
- Tasks 2 and 3 are verification-only — if they fail, the grammar reorder needs adjustment.
- Task 7 is likely a no-op (existing TextMate rules cover the tokens) but needs manual verification.
