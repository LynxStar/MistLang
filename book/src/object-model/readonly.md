# Readonly

The `readonly` modifier in Mist declares that something only reads data — it does not mutate it. "Mutating" means writing to a field on a type. A readonly thing guarantees that *you* cannot mutate it through that reference, but something else might still be able to. It is a promise about what you will do, not a global lock on the data.

In some contexts, `readonly` guarantees full immutability (a `readonly` type's fields can never be written after construction). In others, it is a view restriction — you see the data as immutable, but the underlying object may be mutated through a different reference that has write access.

All readonly enforcement is compile-time only. There is no runtime overhead — the modifier is either erased or lowered to C#'s own `readonly` modifier where one exists.

## Where Readonly Can Be Applied

The `readonly` modifier can appear on:

| Declaration | Effect |
|---|---|
| Local variable | Cannot be reassigned; only readonly members accessible |
| Field | Cannot be assigned outside the constructor |
| Property | Setter only callable from the constructor; getter/setter bodies are readonly contexts |
| Indexer | Setter only callable from the constructor; getter/setter bodies are readonly contexts |
| Method | Body is a readonly context — cannot mutate state |
| Constructor | Body is a readonly context with constructor privilege |
| Class | All members are implicitly readonly; the type is immutable after construction |
| Struct | All members are implicitly readonly; emits as C# `readonly struct` |
| Abstract Type | All members are implicitly readonly; implementors must satisfy the contract |

## Readonly Local Variables

The simplest use of `readonly` is on a local variable. A readonly local cannot be reassigned after its initial declaration, and you can only access fields, properties, and methods on it that are themselves readonly.

```mist
readonly var x = 7;
var doubled = x * 2;       // reading x is fine
Console.WriteLine(doubled); // prints 14

x = 10;  // ERROR: Cannot reassign readonly local variable 'x'
```

The variable works like any other — you can read it, pass it to functions, use it in expressions. The only thing you cannot do is point it at a different value.

This works with all variable declaration forms:

```mist
// Inferred type
readonly var name = "Alice";

// Explicit type
readonly int count = 5;

// Tuple deconstruction
readonly var (a, b) = (1, 2);

// Typed tuple deconstruction
readonly (int x, string label) = (10, "hello");

// Combined with ownership specifiers
readonly owned var resource = new MyResource();
```

A readonly local requires an initializer — you cannot declare one without assigning it immediately. The restriction is purely about reassignment: you cannot point the variable at a different value, and you cannot pass it by mutable `ref`.

```mist
public static void Example(ref int target)
{
    target = 99;
}

readonly var n = 5;
// Example(ref n);  // ERROR: Cannot pass readonly local variable 'n' by mutable reference
```

Readonly locals compose naturally with readonly blocks and readonly methods. Inside a readonly context, you can still declare and assign non-readonly locals (local variable operations are always permitted in readonly contexts), but a readonly local's reassignment restriction applies everywhere regardless of context.

## Readonly Fields

A field marked `readonly` cannot be assigned after the object is constructed:

```mist
public class Config
{
    public readonly string Name;
    public readonly int MaxRetries = 3;  // default value is fine
    public int CurrentRetries;           // mutable

    public Config(string name)
    {
        this.Name = name;           // writable from the constructor
        this.CurrentRetries = 0;
    }

    public void Retry()
    {
        CurrentRetries = CurrentRetries + 1;  // OK — mutable field
        // Name = "other";  // ERROR — cannot assign to readonly field outside constructor
    }
}
```

A readonly field can have a default value in its declaration — this is always safe because it runs as part of construction. Fields are also writable from the constructor body (see [Constructor Privilege](#constructor-privilege) for details on how this works in readonly contexts).

## Readonly Methods

A method marked `readonly` declares that it does not mutate the object's state. Inside a readonly method, the compiler enforces the readonly context base rules:

**Permitted:**
- Reading any field
- Calling other readonly methods, including `pure` and `local readonly` ones
- Calling readonly property/indexer getters
- Declaring and assigning local variables
- Allocating via `new` when the constructor is readonly (a `pure` constructor is readonly)
- On an object the method owns, such as one it just created: assigning its fields, writing through its index, and calling its `local` members, getters and setters included

**Prohibited:**
- Assigning to a field of anything the method does not own, `this` included
- Calling non-readonly methods, except a `local` method on something the method owns
- Calling non-readonly property/indexer getters, with the same exception
- Calling a property/indexer setter, except a `local` one on something the method owns
- Allocating via `new` when the constructor is not readonly

Local variables can be freely created and reassigned, but you are still in a readonly context when accessing them. What matters is where a local's value came from. If every value assigned to `x` is an object the method created, the method owns it, and may assign its fields and call its `local` members: `readonly` restricts what a method does to what it was given, not to what it made. A readonly method may therefore build a list, `Add` to it and return it. If `x` holds something the method was given, such as a parameter or a field's value, the method cannot call a non-readonly method on `x` or assign to its fields. This is also why only readonly constructors are permitted in `new` expressions: constructing an object is effectively calling a method on it, so the constructor must be readonly too. See [Pure and Local Members](../advanced/purity.md) for `local` and `pure`, and [Where an Input Comes From](../advanced/purity.md#where-an-input-comes-from) for exactly what a method owns.

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
        return Value;  // reading fields is fine
    }

    public readonly string Describe()
    {
        return $"Counter({GetValue()})";  // calling another readonly method is fine
    }

    public void Increment()
    {
        Value = Value + 1;  // mutates — this method cannot be readonly
    }
}
```

In a `readonly` type, all methods are implicitly readonly. In a regular type, you opt in per-method.

### Readonly Inference

The compiler infers that a method is readonly when its body contains only readonly-safe operations. You don't always need to write the modifier explicitly. The inference engine examines each member's body independently (it never traverses into called methods — it only checks whether the called method is itself readonly).

A member is inferred readonly if its body contains only:
- Literal expressions
- Field reads
- Local variable declarations and assignments
- Calls to methods/getters that are readonly (explicit or inferred)
- Allocations via readonly constructors
- Writes to, and `local` calls on, objects the member owns

Adding the `readonly` modifier explicitly is recommended when the intent is that the method should be readonly. The compiler will then enforce that intent — if you later add mutating code to the body, you get a compile-time error rather than silently losing readonly status.

The inference system allows safe consumption of code that is provably readonly without requiring annotations everywhere. But inferred readonly is more brittle: the implementation may change in a way that makes the method mutable, and since there's no explicit `readonly` in the signature, callers in readonly contexts will break without warning at the declaration site.

## Readonly Blocks

You don't have to make an entire method, constructor, or property readonly to get enforcement. A `readonly { }` block creates a region within any body where readonly rules apply:

```mist
public class Processor
{
    public int Total;
    public int LastInput;

    public void Process(int input)
    {
        // Mutable code — can assign fields freely
        this.LastInput = input;
        this.Total = Total + input;

        readonly
        {
            // Inside here, readonly rules are enforced:
            // this.Total = 0;  // ERROR — cannot assign fields
            // this.Reset();    // ERROR — cannot call non-readonly methods

            var snapshot = Total;  // reading is fine
            Console.WriteLine($"Total so far: {snapshot}");
        }

        // Back to mutable code
        this.Total = Total * 2;
    }
}
```

This is useful when a method mixes mutation and reading — you can isolate the reading portion in a `readonly` block to make the intent explicit and compiler-verified. The block has no runtime cost; it's erased in the emitted C# and becomes a plain `{ }` scope.

## Readonly Properties

A `readonly` auto-property with `{ get; set; }` behaves the same as a readonly field — it cannot be assigned outside the constructor:

```mist
public class User
{
    public readonly string Name { get; set; }
    public readonly int Age { get; set; }

    public User(string name, int age)
    {
        this.Name = name;  // writable from constructor
        this.Age = age;    // writable from constructor
    }

    // this.Name = "other";  // ERROR outside constructor — same as a readonly field
}
```

### Custom Accessor Bodies

When a readonly property has custom `get` or `set` blocks, those blocks are effectively readonly methods. All the same restrictions apply — you cannot call any non-readonly method, cannot mutate any non-local field, and cannot call any setter.

The sole exception is the **backing field exemption**: inside a readonly property's setter, the assignment `field = value` is permitted. This is how the constructor writes through the setter to initialize the property's storage.

```mist
public class Validated
{
    public readonly int Score
    {
        get
        {
            // This is a readonly context:
            // can read fields, call readonly methods, use locals
            return field;
        }
        set
        {
            // Also a readonly context, but with the backing field exemption:
            // `field = value` is the ONLY field assignment allowed here
            if (value >= 0)
            {
                field = value;
            }
            // this.OtherField = 5;  // ERROR — cannot assign other fields
            // this.SomeMutableMethod();  // ERROR — cannot call non-readonly methods
        }
    }

    public Validated(int score)
    {
        this.Score = score;  // constructor privilege: can call readonly setter
    }
}
```

A readonly property with only a `get` accessor (no `set`) is the property equivalent of a readonly field — assignable only in the constructor:

```mist
public readonly string Name { get; }
```

## Readonly Types

Marking a class or struct as `readonly` makes the entire type immutable after construction. All members are implicitly readonly, and no field can be assigned outside the constructor:

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

    public Point Translate(int dx, int dy)
    {
        // Allocating another readonly type is permitted
        return new Point(X + dx, Y + dy);
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

Since every method in a readonly type is implicitly readonly, the full set of restrictions applies to all method bodies:

```mist
public readonly class Sensor
{
    public int Reading;
    public string Label;

    public Sensor(int reading, string label)
    {
        this.Reading = reading;
        this.Label = label;
    }

    public void Example()
    {
        // Cannot mutate your own fields:
        // this.Reading = 0;  // ERROR

        // Cannot mutate anything else either:
        // someOtherObject.Value = 5;  // ERROR

        // Cannot call non-readonly methods (on this or anything else):
        // this.Reset();  // ERROR — if Reset() is not readonly
        // someList.Add(1);  // ERROR — Add is not readonly

        // Cannot allocate objects whose constructor isn't readonly:
        // var m = new MutableThing();  // ERROR

        // CAN read fields, call readonly methods, and allocate readonly types:
        var r = Reading;
        var desc = Label;
        var p = new Point(1, 2);  // OK — Point is readonly, so its constructor is readonly
    }
}
```

### Constructor Privilege

Constructors of readonly types operate in a readonly context but receive **constructor privilege**: they can assign to the type's own fields and call the type's own readonly property/indexer setters. This is how the object gets initialized. All other readonly rules still apply — the constructor cannot call non-readonly methods, non-readonly getters, or allocate via non-readonly constructors.

```mist
public readonly class Pair
{
    public int A;
    public int B;

    public Pair(int a, int b)
    {
        this.A = a;  // constructor privilege — field assignment allowed
        this.B = b;  // constructor privilege — field assignment allowed
        // Cannot call non-readonly methods here
    }
}
```

A `readonly { }` block inside a constructor **suspends** constructor privilege, creating a region where even the constructor cannot mutate fields. This is useful for validation after initialization:

```mist
public readonly class Validated
{
    public int X;
    public int Y;

    public Validated(int x, int y)
    {
        this.X = x;
        this.Y = y;

        readonly
        {
            // Constructor privilege is suspended here
            // this.X = 0;  // ERROR — cannot assign in readonly block
            var sum = X + Y;  // reading is fine
        }
    }
}
```

### Shallow Enforcement

Readonly on a type is enforced shallowly. If you have an instance of a readonly type, nothing inside that type can mutate its own fields, and nothing with a reference to it can mutate it directly. But if one of its fields holds a reference to a mutable object, that nested object can still be mutated:

```mist
public class MutableBuffer
{
    public int Count;

    public MutableBuffer()
    {
        this.Count = 0;
    }

    public void Increment()
    {
        this.Count = Count + 1;
    }
}

public readonly class Wrapper
{
    public MutableBuffer Buffer;

    public Wrapper(MutableBuffer buffer)
    {
        this.Buffer = buffer;
    }
}

// Outside:
var buf = new MutableBuffer();
var w = new Wrapper(buf);

// w.Buffer = new MutableBuffer();  // ERROR — cannot reassign field on readonly type

// But the buffer itself is still mutable through the original reference:
buf.Increment();  // w.Buffer.Count is now 1
```

The readonly type guarantees that `Wrapper`'s fields won't be reassigned, but it cannot prevent mutation of the objects those fields point to. For a deep immutability guarantee — where nothing nested can be mutated either — combine `readonly` types with the `owned` ownership modifier. See the [Ownership](../advanced/ownership.md) chapter for details.

> **Open design decision:** This shallow enforcement may be revisited. The `owned` modifier already requires that owned types only contain owned members. A similar constraint for readonly types — requiring that fields of a readonly type must themselves be readonly — could provide deep immutability by default. The current shallow behavior can be surprising since the intent of marking a type `readonly` suggests full immutability.

## Further Reading

For advanced readonly patterns including `frozen` parameters, readonly blocks in methods, readonly parameters, generic constraints (`where T : readonly`, `where T : mutable`), and interaction with the ownership system, see the [Readonly Contexts](../advanced/readonly-contexts.md) chapter.
