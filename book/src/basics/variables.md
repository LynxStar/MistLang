# Variables

Variables in Mist store values that you can use and modify throughout your program. Mist provides explicit type annotations for clarity, `var` for type inference when the type is obvious from context, and tuple deconstruction for unpacking multiple values in a single declaration.

## Explicit Type Annotations

The most direct way to declare a variable is with an explicit type. The syntax is `Type name = expression;` — you state the type, give the variable a name, and assign an initial value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
int count = 10;
string greeting = "Hello, Mist!";
bool isReady = true;
double pi = 3.14;
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
int count = 10;
string greeting = "Hello, Mist!";
bool isReady = true;
double pi = 3.14;
```
{{#endtab }}
{{#endtabs }}

The type appears before the variable name, just like in C#. This form is useful when you want to be explicit about what a variable holds, or when the type isn't obvious from the right-hand side.

## Type Inference with `var`

When the type is clear from the initializer expression, you can use `var` to let the compiler infer it. The syntax is `var name = expression;` — the compiler determines the type from the assigned value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
var message = "Hello from Mist!";
var total = 0;
var origin = new Point(0, 0);
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var message = "Hello from Mist!";
var total = 0;
var origin = new Point(0, 0);
```
{{#endtab }}
{{#endtabs }}

The inferred type is determined at compile time — `message` is a `string`, `total` is an `int`, and `origin` is a `Point`. Using `var` reduces repetition without sacrificing type safety.

## Tuple Deconstruction

When a tuple is assigned to multiple variables at once, Mist supports deconstruction syntax. This unpacks each element of the tuple into its own named variable in a single statement.

### The `var` Form

Use `var (name1, name2) = tupleExpr;` to deconstruct a tuple with inferred types for each element:

{{#tabs }}
{{#tab name="Mist" }}
```mist
var (a, b) = (10, 20);
Console.WriteLine($"a={a} b={b}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var (a, b) = (10, 20);
Console.WriteLine($"a={a} b={b}");
```
{{#endtab }}
{{#endtabs }}

### The Typed Form

Use `(Type1 name1, Type2 name2) = tupleExpr;` to deconstruct with explicit types on each element:

{{#tabs }}
{{#tab name="Mist" }}
```mist
(int u, int v) = (100, 200);
Console.WriteLine($"u={u} v={v}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
(int u, int v) = (100, 200);
Console.WriteLine($"u={u} v={v}");
```
{{#endtab }}
{{#endtabs }}

Both forms create individual variables (`a` and `b`, or `u` and `v`) rather than a single tuple variable. The typed form is useful when you want to document the expected types explicitly or when the compiler can't infer them from context.

## Scoping Rules

Variables in Mist are scoped to the enclosing block, delimited by `{` and `}`. A variable declared inside a block is not accessible outside that block.

```mist
if (condition)
{
    int x = 42;
    Console.WriteLine(x); // OK — x is in scope
}
// x is not accessible here — it went out of scope
```

This applies to all block-introducing constructs: `if`/`else`, `while`, `for`, `foreach`, and plain brace-delimited blocks. Each block creates a new scope, and variables declared within it are destroyed when the block ends.

## Mutability

Variables declared with an explicit type or `var` are mutable by default. You can reassign them freely within their scope:

{{#tabs }}
{{#tab name="Mist" }}
```mist
var total = 0;
total = total + 5;
total = total + 10;
Console.WriteLine($"total={total}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var total = 0;
total = total + 5;
total = total + 10;
Console.WriteLine($"total={total}");
```
{{#endtab }}
{{#endtabs }}

There is no separate `let` or `const` keyword for local immutability — all local variables are reassignable. For immutability at the type level, Mist provides ownership modifiers and readonly contexts, covered in the [Advanced Features](../advanced/ownership.md) chapters.
