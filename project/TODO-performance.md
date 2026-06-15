# Performance TODO

## Parsing (~4s for AllFeatures)

### AttachHidden O(n) scan — CRITICAL
The `AttachHidden<T>` method in the generated parser does linear scans through `_allTokens` to find the mapping from default-channel token indices to all-tokens indices. This is called for every AST node (148K nodes). Profile shows it at **51% total CPU / 28% self CPU** during parsing.

**Fix:** Pre-build a `int[] _defaultToAllIndex` mapping array in `Init()` — one O(n) pass at startup, then O(1) lookups in `AttachHidden`.

**Status:** Implementing now.

### Memoization coverage — REVISIT AFTER AttachHidden fix
After the AttachHidden fix, re-profile to determine:
1. Is the multi-caller heuristic (69 memoized rules / 130 total) providing meaningful speedup?
2. Is the dictionary overhead from 69 memoized rules visible in the profile?
3. Should we dial back to @memo-only (7 hand-picked rules) and remove the automatic heuristic?

Keep the `@memo` annotation infrastructure regardless — it's the right abstraction for explicit control.

### SetParentLinks — NEGLIGIBLE (2ms)
The `SetParentLinks()` call after parsing costs ~2ms for 148K nodes. Not worth optimizing.

## Emit (~3.5s for AllFeatures)

### Roslyn in-process compilation
The dominant cost is Roslyn compiling 120 .cs files to a DLL (parsing C#, semantic analysis, IL emission). Hard to improve without:
- Switching to direct IL emission (Mono.Cecil or System.Reflection.Metadata)
- Incremental compilation (only re-emit changed files)
- Parallel C# emission (generate source strings concurrently)

**Status:** Low priority — Roslyn cost is inherent to the architecture.

## Ownership Analysis (~1.2s for AllFeatures)

### CollectUsesRecursive — 22.83% total CPU (new #1 hotspot post-AttachHidden fix)
`OwnershipInferencePass.CollectUsesRecursive` walks the entire function body AST for each variable binding. It's called inside the fixed-point loop (N candidates × K iterations = N×K full tree walks) plus once more in the post-loop minimum-tier enforcement pass.

The uses of a symbol don't change between iterations — only the inferred tier changes. Pre-computing all uses for all candidates once before the loop (into a `Dictionary<ISymbol, List<VariableUse>>`) would eliminate the repeated walks entirely, reducing the cost from O(N×K×M) to O(N×M) where M is the function body AST size.

The second call site (post-loop enforcement for `Unrestricted` bindings) could also reuse the pre-computed cache.

**Status:** Investigate — straightforward cache fix, high impact.
