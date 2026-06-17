# Performance TODO

## Parsing (~4s for AllFeatures)

### Memoization coverage — REVISIT AFTER AttachHidden fix
After the AttachHidden fix, re-profile to determine:
1. Is the multi-caller heuristic (69 memoized rules / 130 total) providing meaningful speedup?
2. Is the dictionary overhead from 69 memoized rules visible in the profile?
3. Should we dial back to @memo-only (7 hand-picked rules) and remove the automatic heuristic?

Keep the `@memo` annotation infrastructure regardless — it's the right abstraction for explicit control.

### SetParentLinks — NEGLIGIBLE (2ms)
The `SetParentLinks()` call after parsing costs ~2ms for 148K nodes. Not worth optimizing.

## Emit (~3.5s for AllFeatures)

### IsNodeInAst O(N) tree walk — 5.8% total CPU
`CSharpTranspilerBackend.DetermineMainTypeName` calls `IsNodeInAst` which recursively walks
entire AST trees to check if a method declaration node belongs to a referenced project's AST.
With 148K nodes across multiple ASTs, this costs ~166ms.

**Fix:** Walk up via `Parent` links from the target node to find the root, then `ReferenceEquals`
against the AST root. O(tree depth) instead of O(N).

**Status:** DONE — 166ms → negligible.

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

**Status:** DONE — implemented pre-computed uses cache + early-out for read-only vars.

**Changes made:**
1. Pre-compute uses for all candidates into a `Dictionary<ISymbol, List<VariableUse>>` once before the loop.
2. Early-out: candidates with uses that are ALL `Read` get immediately set to `Unrestricted` and skip the loop entirely.
3. Fixed-point iterates only over `activeCandidates` (those with non-trivial uses).
4. Post-loop enforcement reuses the cache instead of re-walking the tree.
