# Program Model Backlog

## TypeReferenceResolver: Incomplete Ambiguity Detection — FIXED

Resolution now follows C# semantics with three priority tiers:

1. **Local types** (type parameters, nested types) — cascade with shadowing. The innermost scope with the name wins.
2. **Own namespace** — if found in any scope's own namespace, it shadows imports entirely.
3. **Imports** — accumulate from ALL scope levels into a flat pool. No shadowing between imports. Ambiguity (`MIST_PM_AMBIGUOUS_TYPE`) is reported if multiple distinct types match across the combined pool.

Same-object references (e.g., type forwarding where the same `IMistType` instance is registered in two namespaces) are excluded from ambiguity via `ReferenceEquals`.

The fix applies to both `ResolveNamedType` (simple names) and `LookupNestedQualifiedName` (dotted nested type keys).