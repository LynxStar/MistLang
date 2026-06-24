# Target-Typed New

When creating an object, you normally write the type name after `new`:

```mist
Point p = new Point(3, 4);
```

But when the target type is already obvious from context, the type name is redundant. Target-typed `new` lets you omit it:

```mist
Point p = new(3, 4);
```

The compiler infers the type from the left-hand side (or from a return type, parameter type, etc.) and emits fully-typed C# output. This is purely syntactic sugar — no runtime behavior changes.

## Syntax

The basic form is `new(args)` where the type name is omitted. The compiler resolves the type from the surrounding context.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/target-typed-new/syntax.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/target-typed-new/syntax.cs}}
```
{{#endtab }}
{{#endtabs }}

You can also use `new { ... }` with a property initializer block and no parentheses:

```mist
Point origin = new { X = 0, Y = 0 };
```

Or combine arguments with property initializers:

```mist
Point p = new(1, 2) { Label = "start" };
```

## Supported Contexts

Target-typed `new` works anywhere the compiler can determine a target type:

- **Field initializer** — inferred from the field's declared type
- **Local variable with explicit type** — inferred from the variable's type annotation
- **Return statement** — inferred from the method's return type
- **Method argument** — inferred from the parameter type
- **Assignment** — inferred from the target variable's declared type

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/target-typed-new/contexts.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/target-typed-new/contexts.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice in the emitted C# that the compiler fills in the full type name (`new Point(...)`) at every call site — the output is identical to writing the type explicitly.

## Generic Types

When the target type is a generic type, the full constructed type (including type arguments) is preserved. You don't need to repeat the type arguments in the `new` expression.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/target-typed-new/generics.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/target-typed-new/generics.cs}}
```
{{#endtab }}
{{#endtabs }}

The emitted C# shows `new Pair<int>(1, 2)` and `new Pair<string>("hello", "world")` — the type arguments are always explicit in the output.

## Error Cases

Target-typed `new` requires a statically known target type. The compiler rejects it in contexts where the type cannot be determined:

**`var` declarations** — `var` provides no target type, so `new()` is ambiguous:

```mist
var x = new();  // Error: The type of 'new()' cannot be inferred because 'var' does not provide a target type.
```

**No context** — if `new()` appears where no type can be inferred:

```mist
SomeMethod(new());  // Error if SomeMethod has overloads and the parameter type is ambiguous
```

**Abstract types** — you cannot instantiate an abstract type:

```mist
IDrawable d = new();  // Error: Cannot create an instance of abstract type 'IDrawable' with target-typed new.
```

**Abstract classes** — you cannot instantiate an abstract class:

```mist
Shape s = new();  // Error: Cannot create an instance of abstract type 'Shape'.
```

In all these cases, the fix is either to use an explicit type name (`new ConcreteType(...)`) or to change the target type to a concrete, non-abstract class or struct.
