# Structs

Structs are value types — they live on the stack (or inline within their containing type) and are copied on assignment. Use a struct when you need a small, self-contained piece of data with value semantics, such as a color, a coordinate, or a measurement. Unlike classes, two struct variables are independent copies: modifying one never affects the other.

## Struct Declaration

Declare a struct with the `struct` keyword followed by a name and a brace-delimited body. A struct can contain fields, constructors, and methods — the same members as a class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
public struct Color
{
    public int r;
    public int g;
    public int b;

    public Color(int r, int g, int b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public int Brightness()
    {
        return (this.r + this.g + this.b) / 3;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
public struct Color
{
    public int r;
    public int g;
    public int b;

    public Color(int r, int g, int b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public int Brightness()
    {
        return (this.r + this.g + this.b) / 3;
    }
}
```
{{#endtab }}
{{#endtabs }}

Structs support the same access modifiers (`public`, `private`), constructors, and instance methods as classes. The key difference is in how they behave at runtime.

## Value-Type vs Reference-Type Semantics

The fundamental distinction between structs and classes is their assignment behavior. When you assign a struct to a new variable, the entire value is copied. When you assign a class instance, only the reference is copied — both variables point to the same object.

{{#tabs }}
{{#tab name="Mist" }}
```mist
// Struct: copy semantics
var a = new Color(255, 0, 0);
var b = a;          // b is an independent copy
b.r = 0;            // modifying b does not affect a
Console.WriteLine($"a.r={a.r} b.r={b.r}");
// Output: a.r=255 b.r=0

// Class: reference semantics
var p1 = new Point(3, 4);
var p2 = p1;        // p2 points to the same object
// Changes through p2 would be visible through p1
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
// Struct: copy semantics
var a = new Color(255, 0, 0);
var b = a;          // b is an independent copy
b.r = 0;            // modifying b does not affect a
Console.WriteLine($"a.r={a.r} b.r={b.r}");
// Output: a.r=255 b.r=0

// Class: reference semantics
var p1 = new Point(3, 4);
var p2 = p1;        // p2 points to the same object
// Changes through p2 would be visible through p1
```
{{#endtab }}
{{#endtabs }}

This makes structs ideal for small, immutable data. Because each variable holds its own copy, there's no risk of unintended aliasing — a common source of bugs with reference types.

## Readonly Structs

Adding the `readonly` modifier to a struct declaration prevents mutation of its fields after construction. The compiler enforces that no method or property setter modifies the struct's state.

{{#tabs }}
{{#tab name="Mist" }}
```mist
public readonly struct Vector2
{
    public int X;
    public int Y;

    public Vector2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public int MagnitudeSquared()
    {
        return X * X + Y * Y;
    }

    public string Describe()
    {
        return $"Vector2(X={X}, Y={Y})";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
public readonly struct Vector2
{
    public int X;
    public int Y;

    public Vector2(int x, int y)
    {
        this.X = x;
        this.Y = y;
    }

    public readonly int MagnitudeSquared()
    {
        return X * X + Y * Y;
    }

    public readonly string Describe()
    {
        return $"Vector2(X={X}, Y={Y})";
    }
}
```
{{#endtab }}
{{#endtabs }}

Notice that in the emitted C#, methods on a `readonly struct` receive the `readonly` modifier. This tells the C# compiler that the method does not mutate the struct, enabling optimizations like avoiding defensive copies when the struct is passed by reference.

Readonly structs are a good default for value types that represent immutable data — coordinates, colors, measurements, or any "data carrier" where mutation after construction is not needed.
