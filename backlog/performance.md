# Performance TODO

## Current baseline: AllFeatures builds in ~1.6s

## Parsing (~270ms for AllFeatures)

### Memoization coverage — REVISIT
Re-profile to determine:
1. Is the multi-caller heuristic (69 memoized rules / 130 total) providing meaningful speedup?
2. Is the dictionary overhead from 69 memoized rules visible in the profile?
3. Should we dial back to @memo-only (7 hand-picked rules) and remove the automatic heuristic?

Keep the `@memo` annotation infrastructure regardless — it's the right abstraction for explicit control.

## Emit (~1,068ms for AllFeatures)

### Roslyn in-process compilation (~870ms)
The dominant cost is Roslyn compiling 120 .cs files to a DLL (parsing C#, semantic analysis, IL emission). Hard to improve without:
- Incremental compilation (cache Roslyn syntax trees between builds, only re-emit changed files)
- Direct IL emission via Mono.Cecil or System.Reflection.Metadata (bypass Roslyn entirely — major effort)

The remaining ~200ms is Mist's C# string generation — already ~1.6ms/file, not worth parallelizing.
