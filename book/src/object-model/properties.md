# Properties

Properties in Mist provide controlled access to an object's data through getter and setter accessors. They look like fields from the caller's perspective but execute logic when read or written. Mist supports block-bodied accessors, expression-bodied accessors, init-only accessors, auto-implemented properties, property initializers, and accessor-level access modifiers.

## Block-Bodied Accessors

Block-bodied accessors use `get { }` and `set { }` blocks to define custom logic for reading and writing a property. The `set` accessor receives the assigned value through the implicit `value` parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Thermostat
{
    private int _temperature;

    public Thermostat(int initial)
    {
        this._temperature = initial;
    }

    public int Temperature
    {
        get
        {
            return _temperature;
        }
        set
        {
            if (value < -273)
            {
                _temperature = -273;
            }
            else
            {
                _temperature = value;
            }
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Thermostat
{
    private int _temperature;

    public Thermostat(int initial)
    {
        this._temperature = initial;
    }

    public int Temperature
    {
        get
        {
            return _temperature;
        }
        set
        {
            if (value < -273)
            {
                _temperature = -273;
            }
            else
            {
                _temperature = value;
            }
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

The `get` block returns the backing field, and the `set` block validates the incoming `value` before storing it. This pattern gives you full control over how a property behaves on read and write.

## Expression-Bodied Accessors

When an accessor contains a single expression, you can use the `=>` syntax for a more compact declaration. This works for both `get` and `set` accessors, as well as for read-only computed properties.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Rectangle
{
    private int _width;
    private int _height;

    public Rectangle(int width, int height)
    {
        this._width = width;
        this._height = height;
    }

    // Read-only computed property (top-level =>)
    public int Area => _width * _height;

    // Expression-bodied get accessor
    public int Width
    {
        get => _width;
    }

    // Expression-bodied get and set accessors
    public int Height
    {
        get => _height;
        set => _height = value;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Rectangle
{
    private int _width;
    private int _height;

    public Rectangle(int width, int height)
    {
        this._width = width;
        this._height = height;
    }

    public int Area => _width * _height;

    public int Width
    {
        get => _width;
    }

    public int Height
    {
        get => _height;
        set => _height = value;
    }
}
```
{{#endtab }}
{{#endtabs }}

A top-level `=>` after the property name creates a read-only computed property — it has no setter and recalculates its value each time it's accessed. Within an accessor block, `get =>` and `set =>` are shorthand for single-expression bodies.

## The `init` Accessor

The `init` accessor allows a property to be set during object construction (including property initializer blocks) but prevents modification afterward. This is useful for immutable data where you want the convenience of property syntax without allowing later mutation.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Record
{
    // Auto init-only property
    public string Name { get; init; }

    // Init with block body for validation
    public int Year
    {
        get;
        init
        {
            if (value > 0)
            {
                field = value;
            }
        }
    }

    public Record(string name, int year)
    {
        this.Name = name;
        this.Year = year;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Record
{
    public string Name { get; init; }

    private int _year;
    public int Year
    {
        get => _year;
        init
        {
            if (value > 0)
            {
                _year = value;
            }
        }
    }

    public Record(string name, int year)
    {
        this.Name = name;
        this.Year = year;
    }
}
```
{{#endtab }}
{{#endtabs }}

The `init` keyword replaces `set` in the accessor declaration. A simple `init;` creates an auto-implemented init-only property. A block-bodied `init { }` lets you add validation logic — the `field` keyword refers to the compiler-generated backing field. After construction completes, any attempt to assign to an init-only property is a compile-time error.

## Auto-Implemented Properties

Auto-implemented properties declare `get` and `set` (or `get` alone) with semicolons and no body. The compiler generates the backing field automatically.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Person
{
    // Read-write auto-property
    public string Name { get; set; }

    // Read-only auto-property (set only in constructor)
    public string Id { get; }

    // Auto-property with default value
    public int Age { get; set; } = 0;

    public Person(string name, string id)
    {
        this.Name = name;
        this.Id = id;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Person
{
    public string Name { get; set; }

    public string Id { get; }

    public int Age { get; set; } = 0;

    public Person(string name, string id)
    {
        this.Name = name;
        this.Id = id;
    }
}
```
{{#endtab }}
{{#endtabs }}

A `{ get; set; }` property is read-write — callers can both read and assign it. A `{ get; }` property is read-only — it can only be assigned in the constructor. Auto-properties eliminate the boilerplate of declaring a private field and writing trivial get/set logic.

## Property Initializers

A property initializer assigns a default value using `= expression;` after the accessor block. This value is used when no explicit assignment occurs in the constructor.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Config
{
    public int Timeout { get; set; } = 30;
    public bool Verbose { get; set; } = false;
    public string Name { get; set; } = "default";
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Config
{
    public int Timeout { get; set; } = 30;
    public bool Verbose { get; set; } = false;
    public string Name { get; set; } = "default";
}
```
{{#endtab }}
{{#endtabs }}

Property initializers run before the constructor body executes. They're useful for providing sensible defaults without requiring every constructor to repeat the same assignments.

You can also use property initializer blocks when constructing objects with `new`. This sets property values immediately after construction:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Config
{
    public int Timeout { get; set; } = 0;
    public bool Verbose { get; set; } = false;
    public string Name { get; set; } = "";

    public Config()
    {
        this.Timeout = 0;
        this.Verbose = false;
        this.Name = "";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var cfg = new Config { Timeout = 60, Verbose = true, Name = "production" };
        Console.WriteLine($"Timeout={cfg.Timeout}, Name={cfg.Name}");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Config
{
    public int Timeout { get; set; } = 0;
    public bool Verbose { get; set; } = false;
    public string Name { get; set; } = "";

    public Config()
    {
        this.Timeout = 0;
        this.Verbose = false;
        this.Name = "";
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var cfg = new Config { Timeout = 60, Verbose = true, Name = "production" };
        Console.WriteLine($"Timeout={cfg.Timeout}, Name={cfg.Name}");
    }
}
```
{{#endtab }}
{{#endtabs }}

The `new Config { Timeout = 60, Verbose = true }` syntax creates an instance and then assigns the listed properties. This works with both `set` and `init` accessors.

## Accessor Modifiers

You can restrict the visibility of individual accessors by placing an access modifier before `get`, `set`, or `init`. The most common pattern is a public getter with a private setter — the property is readable from anywhere but only writable within the class.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Demo;

public class Account
{
    // Public get, private set
    public string Name { get; private set; }

    // Public get, private set with initializer
    public int Balance { get; private set; } = 0;

    public Account(string name)
    {
        this.Name = name;
    }

    public void Deposit(int amount)
    {
        this.Balance = this.Balance + amount;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Demo;

public class Account
{
    public string Name { get; private set; }

    public int Balance { get; private set; } = 0;

    public Account(string name)
    {
        this.Name = name;
    }

    public void Deposit(int amount)
    {
        this.Balance = this.Balance + amount;
    }
}
```
{{#endtab }}
{{#endtabs }}

The `private set` modifier means external code can read `account.Name` but cannot assign to it — only methods inside the `Account` class can modify the value. This pattern provides encapsulation while keeping the property syntax clean for consumers.
