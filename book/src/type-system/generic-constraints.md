# Generic Constraints

Generic constraints restrict what types can be used as type arguments. By adding a `where` clause to a generic declaration, you tell the compiler that the type parameter must satisfy certain requirements — being a reference type, implementing an interface, extending a base class, or having a specific constructor. This lets you safely call methods, access members, or construct instances of the type parameter within the generic body.

## The `where` Clause Syntax

Constraints are declared after the type parameter list using the `where T : constraint` syntax. The `where` clause appears after the class body's opening parameters but before the body braces.

```
class Name<T> where T : constraint {
    // T is guaranteed to satisfy the constraint
}
```

The constraint can be a type name (interface, trait, base class), a structural constraint (`class`, `struct`), or a constructor constraint (`new()`). Multiple constraints on the same type parameter are separated by commas.

## Class Constraint (Reference Type)

The `class` constraint restricts a type parameter to reference types only. Value types like `int`, `bool`, or user-defined structs cannot be used as the type argument.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class RefOnly<T> where T : class {
    public T Item;
    public RefOnly(T item) {
        Item = item;
    }
    public string Show() {
        return $"RefOnly: {Item}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class RefOnly<T>
    where T : class
{
    public T Item;

    public RefOnly(T item)
    {
        Item = item;
    }

    public string Show()
    {
        return $"RefOnly: {Item}";
    }
}
```
{{#endtab }}
{{#endtabs }}

With this constraint, `RefOnly<string>` is valid (strings are reference types) but `RefOnly<int>` would be a compile error.

## Struct Constraint (Value Type)

The `struct` constraint restricts a type parameter to value types only. Reference types like classes and strings cannot be used as the type argument.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class ValOnly<T> where T : struct {
    public T Item;
    public ValOnly(T item) {
        Item = item;
    }
    public string Show() {
        return $"ValOnly: {Item}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class ValOnly<T>
    where T : struct
{
    public T Item;

    public ValOnly(T item)
    {
        Item = item;
    }

    public string Show()
    {
        return $"ValOnly: {Item}";
    }
}
```
{{#endtab }}
{{#endtabs }}

With this constraint, `ValOnly<int>` and `ValOnly<Point>` (where `Point` is a struct) are valid, but `ValOnly<string>` would be rejected.

## Interface Constraint

Constraining to an interface guarantees that the type parameter implements specific methods or properties. You can call any member declared on the interface through the constrained type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
interface IOrderable {
    int GetOrder();
}

class Sorter<T> where T : IOrderable {
    public T First;
    public T Second;
    public Sorter(T first, T second) {
        First = first;
        Second = second;
    }
    public int GetFirstOrder() {
        return First.GetOrder();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
interface IOrderable
{
    int GetOrder();
}

class Sorter<T>
    where T : IOrderable
{
    public T First;

    public T Second;

    public Sorter(T first, T second)
    {
        First = first;
        Second = second;
    }

    public int GetFirstOrder()
    {
        return First.GetOrder();
    }
}
```
{{#endtab }}
{{#endtabs }}

Because `T` is constrained to `IOrderable`, the compiler allows calling `First.GetOrder()` inside the generic body. Any type passed as `T` must implement `IOrderable`.

## Trait Constraint

Traits can also be used as constraints. A type satisfies a trait constraint if it structurally matches the trait's members (or has an explicit `with` mapping). The compiler emits the trait as an interface constraint in C#.

{{#tabs }}
{{#tab name="Mist" }}
```mist
trait Describable {
    string Describe();
}

class Describer<T> where T : Describable {
    public T Item;
    public Describer(T item) {
        Item = item;
    }
    public string GetDescription() {
        return Item.Describe();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
interface Describable
{
    string Describe();
}

class Describer<T>
    where T : Describable
{
    public T Item;

    public Describer(T item)
    {
        Item = item;
    }

    public string GetDescription()
    {
        return Item.Describe();
    }
}
```
{{#endtab }}
{{#endtabs }}

Any type with a public `string Describe()` method structurally satisfies the `Describable` trait and can be used as `T` in `Describer<T>`.

## Base Class Constraint

Constraining to a base class guarantees that the type parameter extends that class. You can access any public or protected members of the base class through the constrained type parameter.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class Animal {
    public string Species;
    public Animal(string species) {
        Species = species;
    }
    public string GetSpecies() {
        return Species;
    }
}

class AnimalHandler<T> where T : Animal {
    public T Animal;
    public AnimalHandler(T animal) {
        Animal = animal;
    }
    public string GetSpeciesName() {
        return Animal.GetSpecies();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class Animal
{
    public string Species;

    public Animal(string species)
    {
        Species = species;
    }

    public string GetSpecies()
    {
        return Species;
    }
}

class AnimalHandler<T>
    where T : Animal
{
    public T Animal;

    public AnimalHandler(T animal)
    {
        Animal = animal;
    }

    public string GetSpeciesName()
    {
        return Animal.GetSpecies();
    }
}
```
{{#endtab }}
{{#endtabs }}

With `where T : Animal`, only `Animal` or its subclasses (`Dog`, `Cat`, etc.) can be used as `T`. The generic body can call `GetSpecies()` because it's guaranteed to exist on any `Animal` subtype.

## Constructor Constraints

### Parameterless `new()`

The `new()` constraint guarantees that the type parameter has a parameterless constructor, allowing you to create instances with `new T()` inside the generic body.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class Creator<T> where T : new() {
    public T Create() {
        return new T();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class Creator<T>
    where T : new()
{
    public T Create()
    {
        return new T();
    }
}
```
{{#endtab }}
{{#endtabs }}

### Parameterized `new(Type)`

Mist extends C#'s constructor constraints with parameterized forms like `new(int)`. This guarantees the type has a constructor accepting the specified parameter types, allowing `new T(value)` in the generic body.

The compiler emits parameterized constructor constraints as factory delegates (`Func<ParamType, T>`) that are passed at the call site.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class IntFactory<T> where T : new(int) {
    public T Create(int value) {
        return new T(value);
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class IntFactory<T>
{
    private readonly Func<int, T> _factoryT_int;

    public IntFactory(Func<int, T> factoryT_int)
    {
        _factoryT_int = factoryT_int;
    }

    public T Create(int value)
    {
        return _factoryT_int(value);
    }
}
```
{{#endtab }}
{{#endtabs }}

Notice that the emitted C# uses a `Func<int, T>` delegate rather than a native `new()` constraint — C# doesn't support parameterized constructor constraints natively, so Mist lowers them to factory functions. At the call site, the compiler automatically generates the lambda: `new IntFactory<Widget>((arg0) => new Widget(arg0))`.

## Multiple Constraints on a Single Type Parameter

You can combine multiple constraints on the same type parameter by separating them with commas. The type argument must satisfy all listed constraints simultaneously.

{{#tabs }}
{{#tab name="Mist" }}
```mist
interface IPrintable {
    string Print();
}

class PrintableCreator<T> where T : class, IPrintable, new() {
    public T CreateNew() {
        return new T();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
interface IPrintable
{
    string Print();
}

class PrintableCreator<T>
    where T : class, IPrintable, new()
{
    public T CreateNew()
    {
        return new T();
    }
}
```
{{#endtab }}
{{#endtabs }}

Here `T` must be a reference type (`class`), implement `IPrintable`, and have a parameterless constructor (`new()`). Only types satisfying all three constraints can be used as `T`.

## Multiple `where` Clauses

When a generic type has multiple type parameters, each parameter can have its own `where` clause. Multiple `where` clauses are written one after another.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class Pair<K, V> where K : IOrderable where V : new() {
    public K Key;
    public V Value;
    public Pair(K key) {
        Key = key;
        Value = new V();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class Pair<K, V>
    where K : IOrderable
    where V : new()
{
    public K Key;

    public V Value;

    public Pair(K key)
    {
        Key = key;
        Value = new V();
    }
}
```
{{#endtab }}
{{#endtabs }}

`K` must implement `IOrderable` and `V` must have a parameterless constructor. Each `where` clause constrains exactly one type parameter.

## Constraints on Generic Methods

Constraints aren't limited to type declarations — generic methods can also have `where` clauses. The constraint appears after the parameter list and before the method body.

{{#tabs }}
{{#tab name="Mist" }}
```mist
class MethodConstraints {
    public static T CreateItem<T>() where T : new() {
        return new T();
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
class MethodConstraints
{
    public static T CreateItem<T>()
        where T : new()
    {
        return new T();
    }
}
```
{{#endtab }}
{{#endtabs }}

The `where T : new()` clause on the method means callers must provide a type argument that has a parameterless constructor: `MethodConstraints.CreateItem<Widget>()`.

## Constraints on Interfaces and Structs

Constraints can also appear on generic interface and struct declarations, not just classes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
interface IRepository<T> where T : class, new() {
    T GetById(int id);
    void Save(T item);
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
interface IRepository<T>
    where T : class, new()
{
    T GetById(int id);
    void Save(T item);
}
```
{{#endtab }}
{{#endtabs }}

{{#tabs }}
{{#tab name="Mist" }}
```mist
struct Holder<T> where T : IPrintable {
    public T Value;
    public Holder(T value) {
        Value = value;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
struct Holder<T>
    where T : IPrintable
{
    public T Value;

    public Holder(T value)
    {
        Value = value;
    }
}
```
{{#endtab }}
{{#endtabs }}

The same `where` clause syntax works uniformly across classes, structs, interfaces, and traits.

## Summary

| Constraint | Meaning |
|---|---|
| `class` | Must be a reference type |
| `struct` | Must be a value type |
| `InterfaceName` | Must implement the interface |
| `TraitName` | Must structurally satisfy the trait |
| `BaseClassName` | Must extend the base class |
| `new()` | Must have a parameterless constructor |
| `new(Type)` | Must have a constructor accepting that parameter type |

Constraints compose freely — combine `class`, interface, trait, base class, and constructor constraints on the same type parameter to express precise requirements. Use multiple `where` clauses when constraining different type parameters independently.
