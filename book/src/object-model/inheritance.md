# Inheritance

Inheritance lets you create a new class that reuses, extends, and specializes the behavior of an existing class. The derived class (child) inherits all public and private members from the base class (parent), and can add its own fields and methods or provide specialized implementations.

Mist uses the same colon syntax as C# for declaring inheritance relationships. A class can extend at most one base class. Base names can be namespace-qualified, such as `Models.Base`, and can supply type arguments, such as `Models.Base<int>`. A nested base under a constructed enclosing type can be spelled `Models.Outer<int>.Inner`. The same name resolution applies to struct bases and abstract type lists; qualification does not change which base kinds are permitted. Circular inheritance is refused at the base clause. Correct the cycle before the compiler can complete the remaining hierarchy-dependent checks.

## Extending a Base Class

To derive from a base class, place a colon (`:`) followed by the base class name after the class name in the declaration. The derived class inherits all members from the base class and can access public members directly.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/extending.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/extending.cs}}
```
{{#endtab }}
{{#endtabs }}

`Dog` extends `Animal`, inheriting `GetName()`, `GetAge()`, and `Describe()`. You can call any inherited public method on a `Dog` instance just as you would on an `Animal`. A derived type is also assignable to a variable or parameter of the base type — passing a `Dog` where an `Animal` is expected works without any cast.

## Base Constructor Chaining

When a derived class has a constructor, it must forward the required arguments to the base class constructor using `: base(...)`. This ensures the base class fields are properly initialized before the derived class adds its own state.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/base-constructor.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/base-constructor.cs}}
```
{{#endtab }}
{{#endtabs }}

`GuideDog` extends `Dog`, which itself extends `Animal` — forming a three-level hierarchy. The `GuideDog` constructor chains to `Dog`'s constructor via `: base(name, age, breed)`, which in turn chains to `Animal`'s constructor. Each level initializes its own fields while delegating the rest upward. A written `base(...)` chain must target an accessible constructor: public and protected base constructors are available to a derived type, while a private base constructor is refused at the chain in Mist source. A `this(...)` chain can call a private constructor of the same declaring type. The compiler retains the selected overload and checks its arguments before C# emission.

## Methods in Derived Classes

A derived class can define its own methods that call inherited methods to build on base class behavior. Each level in the hierarchy can compose richer functionality by reusing logic from its ancestors.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/inheritance/method-overriding.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/inheritance/method-overriding.cs}}
```
{{#endtab }}
{{#endtabs }}

Each level in the hierarchy defines its own describe method that calls inherited methods (`GetName()`, `GetAge()`, `GetBreed()`) to compose a richer description. Note that Mist does not support method overriding — derived classes cannot replace a base class method with the same signature. Instead, each class defines distinctly-named methods that build on inherited behavior.

## Static Dispatch Only

Both class and struct inheritance use static dispatch — there are no virtual methods on either concrete type. A derived class or struct can define new methods that call inherited ones, but cannot override them. Mist does not support method shadowing either — a derived class cannot in any capacity replace or hide an inherited method.

## Struct Inheritance

Structs support single inheritance using the same `:` syntax as classes. A derived struct contains all fields from its base struct plus its own — the compiler flattens the inheritance at emit time by copying base fields into the derived struct and synthesizing implicit conversion operators.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/structs/inheritance.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/structs/inheritance.cs}}
```
{{#endtab }}
{{#endtabs }}

Notice that the emitted C# has no inheritance relationship — `Point3D` does not extend `Point`. Instead, the compiler copies `X` and `Y` directly into `Point3D` and generates an implicit conversion operator so you can assign a `Point3D` to a `Point` variable without a cast.

### Class vs Struct Inheritance

While the syntax is the same, the runtime behavior differs:

- **Class inheritance** preserves a runtime subtype relationship. A `Dog` reference stored in an `Animal` variable still points to the original `Dog` object on the heap. No data is lost.
- **Struct inheritance** is a compile-time composition mechanism. Assigning a `Point3D` to a `Point` variable copies only the base fields (`X`, `Y`) into a new independent value. The `Z` field is not carried along — this is safe narrowing, not slicing a reference. Stored default and constructor factories are preserved during a generic struct conversion, so methods on the copied base value retain the capabilities supplied at construction.

### No Object Slicing

In C++, assigning a derived value to a base-typed variable silently truncates the object — the derived fields are lost, but the program continues as if nothing happened. This is the classic "object slicing" footgun: you pass a `Point3D` by value where a `Point` is expected, the `Z` field vanishes, and you get subtle bugs with no compiler warning.

Mist avoids this problem entirely because struct inheritance has no virtual dispatch and no identity. When you assign a `Point3D` to a `Point`, you get a new, independent `Point` value — not a corrupted `Point3D` pretending to be a `Point`. There is no object to slice because there was never a shared identity. The conversion is explicit in its semantics: you asked for a `Point`, you got a `Point`, and the `Point3D` you started with is unchanged.

## Widening and Narrowing

### Derived to Base (Widening)

A derived type can always be assigned to a variable of its base type implicitly — no cast required. For classes, this stores the derived reference in a base-typed variable (the full object is preserved on the heap). For structs, this copies only the base fields into a new value.

```mist
// Class: Dog → Animal (reference preserved, no data loss)
Animal a = new Dog("Rex", 5, "Labrador");
// a still points to the full Dog object

// Struct: Point3D → Point (copies X and Y, Z is discarded)
Point p = new Point3D(1, 2, 3);
// p is an independent Point with X=1, Y=2
```

This works transitively: if `C` extends `B` extends `A`, then `C` is assignable to both `B` and `A`.

### Base to Derived (Narrowing)

Assigning a base type to a derived type is always rejected — the compiler emits a type mismatch error. A base value does not have the derived type's additional fields, so the assignment would produce an incomplete value.

```mist
// Both of these are compile-time errors:
// Dog d = someAnimal;    // ERROR — Animal is not assignable to Dog
// Point3D p = somePoint; // ERROR — Point is not assignable to Point3D
```

For classes, you can use pattern matching to test whether a base-typed variable actually holds a derived instance at runtime:

```mist
Animal a = new Dog("Rex", 5, "Labrador");

if (a is Dog dog)
{
    // Inside this branch, `dog` is typed as Dog
    Console.WriteLine(dog.GetBreed());
}
```

For structs, pattern narrowing from base to derived is not possible — since the derived-to-base conversion copies only the base fields, the derived data is gone. There is no runtime identity to recover.
