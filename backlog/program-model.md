# Program Model Backlog

## TypeReferenceResolver: Incomplete Ambiguity Detection — FIXED

Resolution now follows C# semantics with a priority order:

1. **Local types** (type parameters, nested types) — cascade with shadowing. The innermost scope with the name wins.
2. **Own namespace** — if found in any scope's own namespace, it shadows imports entirely.
3. **Imports** — accumulate from ALL scope levels into a flat pool. No shadowing between imports. Ambiguity (`MIST_PM_AMBIGUOUS_TYPE`) is reported if multiple distinct types match across the combined pool.
4. **FQN fallback** — if the name has a dot and nothing above matched, split at the last dot into namespace + type for a direct lookup.

Same-object references (e.g., type forwarding where the same `IMistType` instance is registered in two namespaces) are excluded from ambiguity via `ReferenceEquals`.

All name resolution (simple, qualified nested keys, FQN) flows through a single `ResolveNamedType` method.

## Refactor: FQN-Indexed Type Registry

### Problem

Types are currently registered by **simple name** within their namespace dictionary (`ns.Types["Int32"]` for `Standard.Int32`, `ns.Types["Outer.Inner"]` for nested types). This forces the resolver to:

- Know which namespace to look in before it can find anything
- Maintain a "tier 4" hack that splits dotted names at the last dot to guess the namespace
- Register nested types under composite keys like `"Outer.Inner"` which conflates the nested-type relationship with namespace membership

### Proposed Design

Index all types by their **fully-qualified name** in a single flat dictionary:

```
ProgramModel.TypesByFQN:
  "Standard.Int32"                    → Int32 type
  "System.Collections.Generic.List<>" → List<> type
  "MyApp.Models.Foo"                  → Foo type
  "MyApp.Models.Foo.Inner"            → nested Inner type
```

### How Resolution Changes

Given a name like `Foo` or `A.B` and a set of visible namespaces (own + all imports), the resolver constructs candidate FQNs by prepending each visible namespace:

- Name: `Foo`, visible: `[MyApp, System, NsA]`
- Candidates: `MyApp.Foo`, `System.Foo`, `NsA.Foo`
- Look up each in the flat dictionary, collect matches, apply ambiguity rules

For `A.B`:
- Candidates: `MyApp.A.B`, `System.A.B`, `NsA.A.B`
- Same algorithm — dots in the name are just part of the key

For a fully-qualified name that matches directly (no namespace prefix needed), it's found on the first pass through candidates when the empty/root namespace is included, or as a direct dictionary hit.

### Benefits

- **No tier 4 hack** — FQN lookup is just another candidate in the same algorithm
- **No composite nested-type keys** — `Outer.Inner` is just `Namespace.Outer.Inner` in the FQN dictionary
- **Single algorithm** for all name forms
- **Ambiguity detection is trivial** — collect all matching candidates, report if > 1 distinct type

### Scope of Change

- `ProgramModel.Namespaces[ns].Types[name]` → `ProgramModel.TypesByFQN[fqn]`
- `TypeBuilder.FinalizeRegistration` — register by FQN
- `AmbientTypeProviderV2` — shell creation, type parameter projection, and `BuildAndRegister` merge step all need to register and resolve by FQN rather than namespace + simple name
- `AmbientTypeResolver` — currently resolves CLR types by `(Namespace, ClrName)` tuple lookup against the shells dictionary; needs to resolve against the FQN dictionary instead
- `TypeReferenceResolver.ResolveNamedType` — construct candidates from visible namespaces
- `Scope` — may need to track the flat list of visible namespace names more explicitly
- Tests that directly access `ns.Types[name]` — update to use FQN

### Open Questions

- Should `Namespace` objects still exist for grouping, or just be implicit from FQN prefixes?
- How to handle the "all types in namespace X" query (needed for IDE completion)?
- Performance: flat dictionary with candidate construction vs. nested dictionaries with known namespace?