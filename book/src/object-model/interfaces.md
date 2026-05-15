# Interfaces

Interfaces define contracts that classes must fulfill. An interface declares method signatures and property signatures without providing implementations — any class that lists the interface in its base clause must supply concrete implementations for every member. Interfaces enable polymorphism: code can accept any type that implements a given interface without knowing the concrete class at compile time.

## Interface Declaration

An interface is declared with the `interface` keyword followed by a name and a body containing method and property signatures. Members have no access modifiers (they are implicitly public) and no method bodies.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Shapes;

interface IShape
{
    string Name { get; }
    string Describe();
    int Area();
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Shapes;

interface IShape
{
    string Name { get; }
    string Describe();
    int Area();
}
```
{{#endtab }}
{{#endtabs }}

An interface can contain method signatures (like `Describe()` and `Area()`) and property signatures (like `Name { get; }`). The compiler emits a C# interface with the same structure — Mist interfaces map directly to their C# equivalents.

Interfaces can also extend other interfaces using the colon syntax, inheriting all members from the parent interface:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Interfaces;

interface IDescribable
{
    string Describe();
}

interface IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Interfaces;

interface IDescribable
{
    string Describe();
}

interface IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}
```
{{#endtab }}
{{#endtabs }}

A class implementing `IFullyDescribable` must provide implementations for both `DetailedDescribe()` (declared directly) and `Describe()` (inherited from `IDescribable`).

## Class Implementation

A class implements an interface by listing the interface name after a colon in the class declaration. The class must provide concrete implementations for every member declared in the interface.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Interfaces;

interface IDescribable
{
    string Describe();
}

public class Person : IDescribable
{
    private string name;
    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {name}, age {age}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Interfaces;

interface IDescribable
{
    string Describe();
}

public class Person : IDescribable
{
    private string name;

    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {name}, age {age}";
    }
}
```
{{#endtab }}
{{#endtabs }}

The `Person` class declares `: IDescribable` in its header and provides a `Describe()` method that satisfies the interface contract. The emitted C# is structurally identical — the class implements the interface through the same nominal mechanism as standard C#.

When a class implements a child interface, it must satisfy all members from the entire interface hierarchy:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Interfaces;

public class Robot : IFullyDescribable
{
    private string model;
    private int version;

    public Robot(string model, int version)
    {
        this.model = model;
        this.version = version;
    }

    public string Describe()
    {
        return $"Robot: {model} v{version}";
    }

    public string DetailedDescribe()
    {
        return $"Robot model {model}, version {version}, fully operational";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Interfaces;

public class Robot : IFullyDescribable
{
    private string model;

    private int version;

    public Robot(string model, int version)
    {
        this.model = model;
        this.version = version;
    }

    public string Describe()
    {
        return $"Robot: {model} v{version}";
    }

    public string DetailedDescribe()
    {
        return $"Robot model {model}, version {version}, fully operational";
    }
}
```
{{#endtab }}
{{#endtabs }}

`Robot` implements `IFullyDescribable`, which extends `IDescribable`. The class provides both `Describe()` (from `IDescribable`) and `DetailedDescribe()` (from `IFullyDescribable`).

## Polymorphic Usage

Interface types can be used as variable types and parameter types, allowing code to work with any implementing class without knowing the concrete type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Interfaces;

public class Program
{
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        var person = new Person("Alice", 30);
        var robot = new Robot("T-800", 2);

        // Interface-typed variables
        IDescribable describable = person;

        // Pass implementing types to interface parameter
        PrintDescription(person);
        PrintDescription(robot);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Interfaces;

public class Program
{
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine(item.Describe());
    }

    public static void Main(string[] args)
    {
        var person = new Person("Alice", 30);
        var robot = new Robot("T-800", 2);

        // Interface-typed variables
        IDescribable describable = person;

        // Pass implementing types to interface parameter
        PrintDescription(person);
        PrintDescription(robot);
    }
}
```
{{#endtab }}
{{#endtabs }}

The `PrintDescription` method accepts any `IDescribable` — both `Person` and `Robot` can be passed because they both implement the interface. Similarly, a variable typed as `IDescribable` can hold a reference to any implementing class. The compiler dispatches the `Describe()` call to the correct implementation at runtime based on the actual object type.

Because `IFullyDescribable` extends `IDescribable`, a `Robot` (which implements `IFullyDescribable`) is also assignable to an `IDescribable`-typed variable or parameter — interface inheritance is transitive.
