# Array Initialization in Mist

## Rule

Mist does **not** support C#'s `new T[] { ... }` or `new T[] { e1, e2, ... }` array creation syntax. Use **array literals** instead.

Additionally, Mist's `foreach` requires an **explicit type annotation** — `var` is not allowed in foreach declarations.

## What Fails

```mist
// BAD: causes MIST_PARSE_INCOMPLETE
var items = new WorkItemState[] {
    WorkItemState.Pending,
    WorkItemState.Searching
};

// BAD: same issue — no size expression between brackets
return new string[] { "a", "b", "c" };

// BAD: foreach does not support var
foreach (var item in items) { }
```

The Mist grammar's `ArrayCreationExpression` requires an explicit size: `new T[size]`. Empty brackets `new T[]` with a braced initializer is not recognized by the parser.

The `ForeachStatement` grammar rule requires `type:TypeReference`, not `var`. Using `var` causes MIST_PARSE_INCOMPLETE.

## What Works

```mist
// GOOD: array literal (preferred for initialized arrays)
var items = [
    WorkItemState.Pending,
    WorkItemState.Searching
];

// GOOD: array literal as return value
WorkItemState[] GetAllStates() {
    return [
        WorkItemState.Pending,
        WorkItemState.Searching,
        WorkItemState.Complete
    ];
}

// GOOD: explicitly sized array (elements set individually)
var buffer = new int[5];
buffer[0] = 10;
buffer[1] = 20;

// GOOD: foreach with explicit type
foreach (WorkItemState state in allStates) { }
foreach (int n in numbers) { }
foreach (string item in items) { }
```

## Mapping

| C# syntax | Mist syntax |
|---|---|
| `new T[] { a, b, c }` | `[a, b, c]` |
| `new T[3]` | `new T[3]` (supported — explicit size) |
| `new List<T> { a, b, c }` | Not supported — use `var list = new List<T>(); list.Add(a);` etc. |
| `int[] x = { 1, 2, 3 };` | `int[] x = [1, 2, 3];` |
| `foreach (var x in items)` | `foreach (T x in items)` (explicit type required) |

## Why This Matters

The parser error surfaces as `MIST_PARSE_INCOMPLETE` at the enclosing class or method level, making it look like the return type or method signature is the problem. The actual failure point is either:
- A `new T[]` expression without a size
- A `foreach (var ...)` without an explicit type

## Applies To

- All `.mist` source files
- Any code generation that produces Mist array initialization or foreach loops
- Sample apps and test files under `samples/` and `Pendragon/`
