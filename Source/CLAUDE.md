
# C# Coding Style

These rules apply to all C# code under the `Source/` directory. They reflect the project's `.editorconfig` and are enforced by `dotnet format`.

## Braces

Always use braces for control flow statements, even single-line bodies.

```csharp
// Bad
if (condition)
    DoSomething();

// Good
if (condition)
{
    DoSomething();
}
```

This applies to `if`, `else`, `for`, `foreach`, `while`, `do`, `using`, and `lock`.

## Field Naming

All private and internal fields use plain `camelCase` with no prefix. This applies regardless of whether the field is `readonly`.

```csharp
// Bad
private readonly ILogger _logger;
private int _count;

// Good
private readonly ILogger logger;
private int count;
```

Do not use `_` prefix, `m_` prefix, or any other Hungarian-style notation.

## Whitespace and Formatting

- No trailing whitespace on any line.
- Files end with a single newline (no extra blank lines at EOF).
- Use spaces for indentation (4 spaces per level).
- No alignment padding (don't add extra spaces to line up comments or assignments across lines).

Run `dotnet format whitespace Mist.slnx` to auto-fix whitespace issues before committing.

## Discard Unused Return Values

When a method returns a value that is intentionally ignored, assign it to a discard.

```csharp
// Bad
sb.Append("hello");

// Good
_ = sb.Append("hello");
```

## Method Bodies

Use block bodies for methods, not expression bodies.

```csharp
// Bad
public int GetCount() => items.Count;

// Good
public int GetCount()
{
    return items.Count;
}
```

## Blank Lines After Blocks

A blank line is required between a closing brace `}` and the next statement.

```csharp
// Bad
if (condition)
{
    DoA();
}
DoB();

// Good
if (condition)
{
    DoA();
}

DoB();
```

## Conditional Expressions (IDE0046)

Simplifying `if/else` returns into ternary expressions is a suggestion, not enforced. Use your judgment -- simple single-condition returns are fine as ternaries, but do not nest ternaries for multi-branch logic.

## Use `var`

Always use `var` when the type is apparent from the right-hand side. Do not use explicit types.

```csharp
// Bad
List<string> items = new List<string>();
string name = GetName();

// Good
var items = new List<string>();
var name = GetName();
```

## Remove Unnecessary Usings

Do not leave unused `using` directives. The formatter removes them automatically.

## Simplify Names

Use the shortest possible name for a type. Do not fully qualify types when a `using` is present.

```csharp
// Bad
System.Collections.Generic.List<int> items = new();

// Good
List<int> items = new();
```

## Quick Reference

| Rule | Diagnostic | Summary |
|------|-----------|---------|
| Braces required | IDE0011 | All control flow blocks need `{ }` |
| Field naming | IDE1006 | Private fields are `camelCase`, no `_` |
| Whitespace | WHITESPACE / FINALNEWLINE | No trailing spaces, single final newline |
| Discard unused values | IDE0058 | Assign ignored return values to `_` |
| Block body methods | IDE0022 | No expression-bodied methods |
| Blank line after block | IDE2003 | Blank line between `}` and next statement |
| Conditional returns | IDE0046 | Suggestion only, not enforced |
| Use var | IDE0007 | Use `var` instead of explicit types |
| Remove usings | IDE0005 | No unnecessary `using` directives |
| Simplify names | IDE0001 | Use shortest name, don't over-qualify |
| Pattern matching | IDE0078 | Use pattern matching where applicable |
| Import ordering | IMPORTS | Usings sorted per `.editorconfig` |
