# Readonly

The `readonly` modifier in Mist creates compile-time immutability guarantees. It can be applied to types, fields, and methods. When something is in a "readonly context," the compiler proves that no mutation occurs — and rejects the code if it can't.

## Readonly Types

Marking a class or struct as `readonly` makes the entire type immutable after construction. All methods are implicitly readonly, and no field can be assigned outside the constructor:

```mist
public readonly class Point
{
    public int X;
    public int Y;

    public Point(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    // Implicitly readonly — cannot mutate X or Y
    public int DistanceSquared()
    {
        return X * X + Y * Y;
    }

    public string Describe()
    {
        return $"Point({X}, {Y})";
    }
}
```

This works for both classes and structs:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/readonly-struct.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/readonly-struct.cs}}
```
{{#endtab }}
{{#endtabs }}

A `readonly` type is a strong guarantee: once constructed, the object's state never changes. This makes it safe to share across threads, cache freely, and reason about without worrying about aliasing.

## Readonly Fields

Individual fields can be marked `readonly` to prevent assignment after construction:

```mist
public class Config
{
    public readonly string Name;
    public readonly int MaxRetries;
    public int CurrentRetries;  // mutable

    public Config(string name, int maxRetries)
    {
        this.Name = name;            // allowed in constructor
        this.MaxRetries = maxRetries; // allowed in constructor
        this.CurrentRetries = 0;
    }

    public void Retry()
    {
        CurrentRetries = CurrentRetries + 1;  // OK — mutable field
        // Name = "other";  // ERROR — readonly field
    }
}
```

Readonly fields can only be assigned in the constructor. Any attempt to assign them elsewhere is a compile-time error.

## Readonly Methods

A method can be explicitly marked `readonly` to declare that it does not mutate the object's state. Inside a readonly method, the compiler rejects any field assignment or call to a non-readonly method:

```mist
public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = value;
    }

    public readonly int GetValue()
    {
        return Value;  // reading is fine
    }

    public readonly string Describe()
    {
        return $"Counter({GetValue()})";  // calling another readonly method is fine
    }

    public void Increment()
    {
        Value = Value + 1;  // mutates — cannot be readonly
    }
}
```

In a `readonly` type, all methods are implicitly readonly. In a regular type, you opt in per-method.

## Readonly Inference

The compiler can infer that a method is readonly when it only reads fields and calls other readonly methods. You don't always need to write the modifier explicitly — but adding it serves as documentation and catches accidental mutations if you later add mutating code.

## What Readonly Prevents

Inside a readonly context (a readonly method, a readonly type's method, or a `frozen` parameter), the compiler rejects:

- Assigning to instance fields
- Calling non-readonly methods on `this`
- Passing `this` to a parameter that expects a mutable reference

These checks are compile-time only. The emitted code has no runtime overhead — the `readonly` modifier is either erased or lowered to C#'s own `readonly` modifier for struct methods.

## Further Reading

For advanced readonly patterns including `frozen` parameters, readonly blocks, and interaction with the ownership system, see the [Readonly Contexts](../advanced/readonly-contexts.md) chapter.
