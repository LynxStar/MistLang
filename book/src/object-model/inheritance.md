# Inheritance

Inheritance lets you create a new class that reuses, extends, and specializes the behavior of an existing class. The derived class (child) inherits all public and private members from the base class (parent), and can add its own fields and methods or provide specialized implementations.

Mist uses the same colon syntax as C# for declaring inheritance relationships. A class can extend at most one base class.

## Extending a Base Class

To derive from a base class, place a colon (`:`) followed by the base class name after the class name in the declaration. The derived class inherits all members from the base class and can access public members directly.

{{#tabs }}
{{#tab name="Mist" }}
```mist
public class Animal
{
    private string name;
    private int age;

    public Animal(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string GetName()
    {
        return name;
    }

    public int GetAge()
    {
        return age;
    }

    public string Describe()
    {
        return $"{name} (age {age})";
    }
}

public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string GetBreed()
    {
        return breed;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
public class Animal
{
    private string name;
    private int age;

    public Animal(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string GetName()
    {
        return name;
    }

    public int GetAge()
    {
        return age;
    }

    public string Describe()
    {
        return $"{name} (age {age})";
    }
}

public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string GetBreed()
    {
        return breed;
    }
}
```
{{#endtab }}
{{#endtabs }}

`Dog` extends `Animal`, inheriting `GetName()`, `GetAge()`, and `Describe()`. You can call any inherited public method on a `Dog` instance just as you would on an `Animal`. A derived type is also assignable to a variable or parameter of the base type — passing a `Dog` where an `Animal` is expected works without any cast.

## Base Constructor Chaining

When a derived class has a constructor, it must forward the required arguments to the base class constructor using `: base(...)`. This ensures the base class fields are properly initialized before the derived class adds its own state.

{{#tabs }}
{{#tab name="Mist" }}
```mist
public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string GetHandler()
    {
        return handler;
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string GetHandler()
    {
        return handler;
    }
}
```
{{#endtab }}
{{#endtabs }}

`GuideDog` extends `Dog`, which itself extends `Animal` — forming a three-level hierarchy. The `GuideDog` constructor chains to `Dog`'s constructor via `: base(name, age, breed)`, which in turn chains to `Animal`'s constructor. Each level initializes its own fields while delegating the rest upward.

## Method Overriding

A derived class can provide a specialized implementation of a method by declaring a method that builds on or replaces the behavior inherited from the base class. The derived method can call inherited methods to reuse base class logic while adding its own behavior.

{{#tabs }}
{{#tab name="Mist" }}
```mist
public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string DescribeDog()
    {
        return $"{GetName()} is a {breed}, age {GetAge()}";
    }
}

public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string DescribeGuideDog()
    {
        return $"{GetName()} is a {GetBreed()} guide dog, age {GetAge()}, handler: {handler}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
public class Dog : Animal
{
    private string breed;

    public Dog(string name, int age, string breed) : base(name, age)
    {
        this.breed = breed;
    }

    public string DescribeDog()
    {
        return $"{GetName()} is a {breed}, age {GetAge()}";
    }
}

public class GuideDog : Dog
{
    private string handler;

    public GuideDog(string name, int age, string breed, string handler) : base(name, age, breed)
    {
        this.handler = handler;
    }

    public string DescribeGuideDog()
    {
        return $"{GetName()} is a {GetBreed()} guide dog, age {GetAge()}, handler: {handler}";
    }
}
```
{{#endtab }}
{{#endtabs }}

Each level in the hierarchy provides its own describe method that calls inherited methods (`GetName()`, `GetAge()`, `GetBreed()`) to compose a richer description. This pattern lets derived classes specialize behavior while reusing the logic defined in their ancestors.
