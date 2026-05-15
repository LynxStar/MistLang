# Generics

Generics let you write type-safe code that works with any type without sacrificing compile-time checking. Instead of duplicating a class or method for every concrete type, you declare one or more type parameters that callers supply when they use the type. Mist generics map directly to C# generics — the emitted code preserves the same type parameter structure.

## Generic Classes

A generic class declares one or more type parameters in angle brackets after the class name. Inside the class body, the type parameter can be used as a field type, parameter type, or return type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class GenericBox<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }

    public string Describe()
    {
        return $"GenericBox({item})";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class GenericBox<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }

    public string Describe()
    {
        return $"GenericBox({item})";
    }
}
```
{{#endtab }}
{{#endtabs }}

The type parameter `T` acts as a placeholder. When you create a `GenericBox<int>`, the compiler treats `item` as an `int`; when you create a `GenericBox<string>`, it's a `string`. The emitted C# is structurally identical — Mist generics are a direct pass-through to the .NET generics system.

## Generic Structs

Structs can also be generic. A generic struct follows the same syntax as a generic class but uses the `struct` keyword. Since structs are value types, a `Pair<int, int>` is stored inline without heap allocation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public struct Pair<A, B>
{
    public A First;
    public B Second;

    public Pair(A first, B second)
    {
        this.First = first;
        this.Second = second;
    }

    public string Describe()
    {
        return $"({First}, {Second})";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public struct Pair<A, B>
{
    public A First;

    public B Second;

    public Pair(A first, B second)
    {
        this.First = first;
        this.Second = second;
    }

    public string Describe()
    {
        return $"({First}, {Second})";
    }
}
```
{{#endtab }}
{{#endtabs }}

Generic structs are useful for lightweight containers like pairs, coordinates, or result wrappers where heap allocation is undesirable.

## Generic Interfaces

Interfaces can declare type parameters, allowing implementors to provide concrete types when they satisfy the contract.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

interface IGenericContainer<T>
{
    T GetItem();
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

interface IGenericContainer<T>
{
    T GetItem();
}
```
{{#endtab }}
{{#endtabs }}

A class implements a generic interface by specifying the concrete type argument (or forwarding its own type parameter). Here, `GenericBox<T>` implements `IGenericContainer<T>` by forwarding `T`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class GenericBox<T> : IGenericContainer<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class GenericBox<T> : IGenericContainer<T>
{
    private T item;

    public GenericBox(T item)
    {
        this.item = item;
    }

    public T GetItem()
    {
        return item;
    }
}
```
{{#endtab }}
{{#endtabs }}

Because `GenericBox<T>` implements `IGenericContainer<T>`, you can assign a `GenericBox<int>` to a variable typed `IGenericContainer<int>` and call `GetItem()` through the interface.

## Generic Methods

Methods can declare their own type parameters independently of the enclosing class. This is useful for utility methods that operate on arbitrary types without requiring the entire class to be generic.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class Utilities
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void PrintItem<T>(T item)
    {
        Console.WriteLine($"Item: {item}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class Utilities
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void PrintItem<T>(T item)
    {
        Console.WriteLine($"Item: {item}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The type parameter `<T>` appears after the method name and before the parameter list. The compiler infers `T` from the argument type at the call site in most cases, but you can also specify it explicitly (see below).

## Type Arguments at Call Sites

When constructing a generic type or calling a generic method, you supply type arguments in angle brackets. The compiler uses these to produce a concrete instantiation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Explicit type argument on construction
        var box = new GenericBox<int>(42);
        Console.WriteLine(box.GetItem());

        // Explicit type argument on a generic method call
        var result = Utilities.Identity<string>("hello");
        Console.WriteLine(result);

        // Interface-typed variable with type argument
        IGenericContainer<int> container = new GenericBox<int>(99);
        Console.WriteLine(container.GetItem());
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class Program
{
    public static void Main(string[] args)
    {
        // Explicit type argument on construction
        var box = new GenericBox<int>(42);
        Console.WriteLine(box.GetItem());

        // Explicit type argument on a generic method call
        var result = Utilities.Identity<string>("hello");
        Console.WriteLine(result);

        // Interface-typed variable with type argument
        IGenericContainer<int> container = new GenericBox<int>(99);
        Console.WriteLine(container.GetItem());
    }
}
```
{{#endtab }}
{{#endtabs }}

Type arguments appear inside `<` and `>` after the type or method name. For generic methods, the compiler can often infer the type argument from the passed value — `Utilities.Identity("hello")` would also work without the explicit `<string>`. Explicit type arguments are required when the compiler cannot infer the type (for example, when the return type is the only place the type parameter appears).

## Multiple Type Parameters

A generic type or method can declare multiple type parameters separated by commas. Each parameter is independent and can be supplied with a different concrete type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class KeyValueStore<K, V>
{
    private K key;
    private V value;

    public KeyValueStore(K key, V value)
    {
        this.key = key;
        this.value = value;
    }

    public K GetKey()
    {
        return key;
    }

    public V GetValue()
    {
        return value;
    }

    public string Describe()
    {
        return $"{key} => {value}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class KeyValueStore<K, V>
{
    private K key;

    private V value;

    public KeyValueStore(K key, V value)
    {
        this.key = key;
        this.value = value;
    }

    public K GetKey()
    {
        return key;
    }

    public V GetValue()
    {
        return value;
    }

    public string Describe()
    {
        return $"{key} => {value}";
    }
}
```
{{#endtab }}
{{#endtabs }}

At the call site, you supply each type argument in order:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace GenericTypes;

public class MultiParamDemo
{
    public static void Run()
    {
        var store = new KeyValueStore<string, int>("age", 30);
        Console.WriteLine(store.Describe());

        var mixed = new KeyValueStore<int, bool>(1, true);
        Console.WriteLine(mixed.Describe());
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace GenericTypes;

public class MultiParamDemo
{
    public static void Run()
    {
        var store = new KeyValueStore<string, int>("age", 30);
        Console.WriteLine(store.Describe());

        var mixed = new KeyValueStore<int, bool>(1, true);
        Console.WriteLine(mixed.Describe());
    }
}
```
{{#endtab }}
{{#endtabs }}

Multiple type parameters are common for collection-like types (dictionaries, result types) and mapping functions. The type parameters are positional — `KeyValueStore<string, int>` means `K` is `string` and `V` is `int`.

Generic constraints (restricting what types can be used as type arguments) are covered in the [Generic Constraints](generic-constraints.md) chapter.
