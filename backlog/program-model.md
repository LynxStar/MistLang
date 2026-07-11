# Program Model Backlog

## TypeReferenceResolver: Incomplete Ambiguity Detection

The resolver does not exhaustively check for ambiguity in all resolution paths. Some places (e.g., `LookupInNamespace` comparing registry vs ambient candidates) do detect ambiguity, but many resolution paths bail on the first match without searching remaining equal-depth namespaces to confirm uniqueness.

This means:
- If two imported namespaces both contain a type with the same name, the resolver may silently pick whichever it finds first rather than reporting an ambiguity error
- The user gets no diagnostic and the wrong type could be resolved depending on import order

### Root Cause: Scope Cascading

The issue is in how resolution cascades up scopes. Scopes shadow outer scopes (providing closure), but within a single scope level, ALL visible namespaces at that level must be independently checked before declaring a match.

Example:
- Global scope imports namespaces A and B, both containing a type `Foo`
- A file also imports namespace A (redundantly)

The resolver should still produce an ambiguity error because at the same scope level, both A and B are visible and both contain `Foo`. The cascade up scopes is correct (inner shadows outer), but within a scope level the search must be exhaustive across all imported namespaces before returning a result.

Fix: all resolution paths that search multiple namespaces at equal priority must collect all matches and emit an ambiguity diagnostic when more than one is found (same behavior C# has for `CS0104`).
