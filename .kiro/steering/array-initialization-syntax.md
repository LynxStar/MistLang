# Array Initialization in Mist

## Rule

Mist does **not** support C#'s `new T[] { ... }` or `new T[] { e1, e2, ... }` array creation syntax. Use **array literals** instead.

## What Fails

```mist
// BAD: causes MIST_PARSE_INCOMPLETE
var items = new WorkItemState[] {
    WorkItemState.Pending,
    WorkItemState.Searching
};

// BAD: same issue — no size expression between brackets
return new string[] { "a", "b", "c" };
```

The Mist grammar's `ArrayCreationExpression` requires an explicit size: `new T[size]`. Empty brackets `new T[]` with a braced initializer is not recognized by the parser.

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

// GOOD: foreach with var (type-inferred)
foreach (var state in allStates) { }

// GOOD: foreach with explicit type
foreach (WorkItemState state in allStates) { }
```

## Mapping

| C# syntax | Mist syntax |
|---|---|
| `new T[] { a, b, c }` | `[a, b, c]` |
| `new T[3]` | `new T[3]` (supported — explicit size) |
| `new List<T> { a, b, c }` | Not supported — use `var list = new List<T>(); list.Add(a);` etc. |
| `int[] x = { 1, 2, 3 };` | `int[] x = [1, 2, 3];` |
| `foreach (var x in items)` | `foreach (var x in items)` (supported) |

## Why This Matters

The parser error surfaces as `MIST_PARSE_INCOMPLETE` at the enclosing class or method level, making it look like the return type or method signature is the problem. The actual failure point is a `new T[]` expression without a size.

## Applies To

- All `.mist` source files
- Any code generation that produces Mist array initialization
- Sample apps and test files under `samples/` and `Pendragon/`
