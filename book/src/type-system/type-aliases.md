# Type Aliases

Type aliases let you create readable, meaningful names for existing types. An alias introduces no new type — it's simply an alternative name that the compiler erases during transpilation. Everywhere you use the alias, the compiler substitutes the underlying type in the emitted C#. This makes aliases ideal for improving readability without introducing runtime overhead or type incompatibility.

## Simple Aliases

A simple type alias gives a new name to an existing type using the `type Name = ExistingType;` syntax. The alias is fully transparent — values of the alias type and the underlying type are interchangeable without casts.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace TypeAliases;

type UserId = int;
type UserName = string;
type Score = double;
type Flag = bool;

public class Program
{
    public static void Main(string[] args)
    {
        UserId id = 42;
        UserName name = "Alice";
        Score score = 98.5;
        Flag active = true;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace TypeAliases;

public class Program
{
    public static void Main(string[] args)
    {
        int id = 42;
        string name = "Alice";
        double score = 98.5;
        bool active = true;
    }
}
```
{{#endtab }}
{{#endtabs }}

Notice that the `type` declarations themselves produce no C# output — they are erased entirely. Every usage of `UserId` becomes `int`, `UserName` becomes `string`, and so on.

## Union Aliases

A type alias can name a union type, giving a concise label to a multi-type union that you use in several places.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace TypeAliases;

type StringOrInt = int | string;
type Outcome = int | string | bool;

public class Program
{
    public static void Main(string[] args)
    {
        StringOrInt x = 42;
        StringOrInt y = "hello";
        Outcome r1 = 99;
        Outcome r2 = "success";
        Outcome r3 = true;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace TypeAliases;

public class Program
{
    public static void Main(string[] args)
    {
        Union_Int32_String x = 42;
        Union_Int32_String y = "hello";
        Union_Boolean_Int32_String r1 = 99;
        Union_Boolean_Int32_String r2 = "success";
        Union_Boolean_Int32_String r3 = true;
    }
}
```
{{#endtab }}
{{#endtabs }}

The alias `StringOrInt` resolves to the union `int | string`, which emits as the generated helper struct `Union_Int32_String`. Similarly, `Outcome` resolves to `int | string | bool` and emits as `Union_Boolean_Int32_String`. The constituent type names are sorted alphabetically in the generated struct name.

## Closed Generic Aliases

An alias can name a specific instantiation of a generic type. This is useful when you frequently use a generic type with the same type argument and want a shorter name.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace TypeAliases;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}

type IntBox = GenericBox<int>;
type StringBox = GenericBox<string>;

public class Program
{
    public static void Main(string[] args)
    {
        IntBox intBox = new IntBox(42);
        StringBox strBox = new StringBox("Mist");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace TypeAliases;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        GenericBox<int> intBox = new GenericBox<int>(42);
        GenericBox<string> strBox = new GenericBox<string>("Mist");
    }
}
```
{{#endtab }}
{{#endtabs }}

The alias `IntBox` is erased to `GenericBox<int>` everywhere — both in variable declarations and in `new` expressions. No `IntBox` type exists in the emitted C#.

## Generic Aliases

A type alias can itself have type parameters. The type parameter is substituted into the underlying type expression, creating a parameterized shorthand.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace TypeAliases;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}

type Box<T> = GenericBox<T>;

public class Program
{
    public static void Main(string[] args)
    {
        Box<int> boxInt = new Box<int>(7);
        Box<string> boxStr = new Box<string>("world");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace TypeAliases;

public class GenericBox<T>
{
    public T value;

    public GenericBox(T v)
    {
        this.value = v;
    }

    public string Describe()
    {
        return $"Box({this.value})";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        GenericBox<int> boxInt = new GenericBox<int>(7);
        GenericBox<string> boxStr = new GenericBox<string>("world");
    }
}
```
{{#endtab }}
{{#endtabs }}

When you write `Box<int>`, the compiler substitutes `T = int` into the alias definition `GenericBox<T>`, producing `GenericBox<int>` in the emitted C#. The generic alias itself is erased — it never appears in the output.

## Transparency and Interchangeability

Type aliases are fully transparent. A value declared with an alias type can be assigned to a variable of the underlying type, and vice versa, with no cast or conversion required.

{{#tabs }}
{{#tab name="Mist" }}
```mist
namespace TypeAliases;

type UserId = int;

public class Program
{
    private static void PrintUserId(UserId id)
    {
        Console.WriteLine(id);
    }

    public static void Main(string[] args)
    {
        UserId aliasedId = 100;
        int rawId = aliasedId;

        int plainInt = 7;
        Program.PrintUserId(plainInt);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
namespace TypeAliases;

public class Program
{
    private static void PrintUserId(int id)
    {
        Console.WriteLine(id);
    }

    public static void Main(string[] args)
    {
        int aliasedId = 100;
        int rawId = aliasedId;

        int plainInt = 7;
        Program.PrintUserId(plainInt);
    }
}
```
{{#endtab }}
{{#endtabs }}

Because aliases are erased, there is no distinction between `UserId` and `int` at runtime. The parameter type `UserId` becomes `int` in the emitted method signature, so passing a plain `int` works without any conversion. This transparency applies to all alias forms — simple, union, closed generic, and generic aliases are all interchangeable with their underlying types.

If you need a type that is *not* interchangeable with its underlying type — one that creates a distinct type identity — use a [newtype](./newtypes.md) instead.
