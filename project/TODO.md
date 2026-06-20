# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## PEG parser silently drops unparseable content (no error recovery)

**Severity: high** — When a `TopLevelItem` or `NamespaceBodyItem` fails to parse, the PEG `*` repetition loop stops, truncates any diagnostics from the failed attempt, and the parser returns a successful root node with fewer items than the file actually contains. No error is reported. Everything after the first unparseable item is silently lost.

**Affects user code, not just stdlib.** Any file where the parser can't match a top-level item will silently drop that item and everything following it. Users get confusing downstream errors ("type not found") with no indication that the file didn't fully parse.

**How it happens:** The Storm-generated PEG parser's `*` loop pattern:
```csharp
if (_item == null || _pos == _listSaved)
{
    _hasError = _savedError;
    _pos = _listSaved;
    _diagnostics?.TruncateTo(_savedDiagCount);  // erases evidence
    break;
}
```

**Existing example:** `Array.mist` has silently failed to parse its class declaration since it was introduced (due to `Length:ZeroOrPositiveInt` in the type parameter list). It went unnoticed because `ArrayType` is a dedicated IType that doesn't rely on the parsed declaration.

**Action:**
1. After parsing the entry rule (`CompilationUnit`), check whether the parser consumed all tokens (i.e., `_pos` is at EOF). If not, emit a diagnostic like `MIST_PARSE_INCOMPLETE: Unexpected content starting at line X was not parsed`. This is a one-line check in `MistParser.cs` after the Storm-generated parse returns.
2. Optionally, implement token-skipping recovery in `*` loops for better UX: skip to the next synchronization point (e.g., a keyword that could start a new item) and retry, accumulating skipped tokens as error nodes.
3. Add a test that asserts every stdlib `.mist` file's token stream is fully consumed after parsing.

---

## TypeParameter constraint limited to PrimitiveType — blocks non-primitive dependent types

The `TypeParameter` grammar rule only supports `PrimitiveType` as a constraint:

```storm
rule TypeParameter
    = name:IDENTIFIER COLON valueType:PrimitiveType  as DependentTypeParameter
    | name:IDENTIFIER                                 as RegularTypeParameter
    ;
```

This means `Length:int` parses but `Length:ZeroOrPositiveInt` (or any user-defined refinement type) does not. Currently `Array.mist` and `Span.mist` both declare `Length:ZeroOrPositiveInt` in their type parameter lists, causing the entire type declaration to fail to parse (silently, per the issue above).

**Impact:** The constraint syntax is purely documentary today — the compiler uses the intrinsic `ArrayType`/`SpanType` machinery regardless. But if/when dependent types are extended to user-defined types, this grammar restriction will need to be lifted.

**Action:**
1. Extend `DependentTypeParameter` to accept `TypeReference` (or at least `QualifiedIdentifier`) as the constraint, not just `PrimitiveType`.
2. Once extended, `Array.mist` and `Span.mist` will parse their full declarations, registering proper `ClassType`/`StructType` nodes with member scopes.
3. At that point, the synthetic `EnsureSpanAliasesRegistered` workaround and the type-checker special cases for Span constructors/foreach can be removed in favor of the natural parsed declarations.
4. Update `StdlibCompilesTest` to assert that `Array` and `Span` types are actually registered in the semantic model's Standard namespace scope.

---

## 🔴 CRITICAL: Array (and Span) indexer upper bound is deferred to runtime for unknown-length types

**Severity: critical** — Mist advertises static bounds safety via the `ArrayBounds` refinement (`>= 0 and < Length`), but that guarantee silently degrades to a runtime check whenever the length is not a compile-time constant. A program can compile with zero errors and throw `System.IndexOutOfRangeException` at runtime.

**Verified empirically** — the following compiles clean (0 errors, 0 warnings) and crashes at runtime:

```mist
int[] numbers = new int[3];
numbers[7] = 10;   // compiles; throws System.IndexOutOfRangeException at runtime
```

**Root cause:** `ValidateArrayIndexRefinement` in `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs` (~line 13059). When an array's `ArrayType` has neither `KnownLength` nor `SymbolicLengthId` (the "unknown length" case), only the lower bound `>= 0` is enforced statically and the upper bound is "deferred to runtime — matching C# semantics." Crucially, declaring a local with an explicit array annotation (`int[] numbers = new int[3]`) **erases** the known length `3` to unknown, so even a freshly-created concrete-length array loses its static upper-bound protection the moment it is bound to an annotated local. A literal index `>= 0` is accepted (the literal-fallback branch returns when `!KnownLength.HasValue`), and non-constant indices on unknown-length arrays are accepted unconditionally (Case 4 returns without a diagnostic).

**Why this matters now:** The `stack-only-types` spec deliberately mirrors `Array` for `Span<T, Length>` (Option 1), so spans inherit this exact hole. Closing the array gap closes it for spans too, since spans reuse the same bounds machinery.

**Action (to scope into its own spec):**
1. Decide the intended guarantee: should unknown-length indexing be a hard compile error (no runtime fallback), or should it require an explicit opt-in / pattern-match to index?
2. Stop erasing `KnownLength` when a known-length creation is bound to an annotated local of the same element type (narrow the local's tracked length, like nullable flow narrowing) so `int[] x = new int[3]` retains length 3.
3. Re-evaluate the literal-fallback branch (`longVal >= 0 && !KnownLength.HasValue → return`) and Case 4's unconditional accept for unknown-length arrays.
4. Apply the same tightening to `Span<T, Length>` / `ReadOnlySpan<T, Length>` once they exist.
5. Audit the blast radius: existing samples/tests/snippets that rely on runtime-deferred indexing into unknown-length arrays will need updating.
