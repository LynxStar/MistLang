# Method Overloading

Method overloading lets you define multiple methods with the same name in a single class, as long as their parameter lists differ. The compiler distinguishes overloads by the number of parameters (arity) or by their types. This allows a single method name to handle different kinds of input without forcing callers to remember distinct names for each variant.

Mist's overloading rules match C# exactly — the transpiler emits each overload as a separate C# method with the same name and the compiler resolves calls based on argument types at compile time.

## Overloading by Parameter Count

Two or more methods can share a name if they accept different numbers of parameters. The compiler selects the correct overload based on how many arguments the caller passes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/by-count.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/by-count.cs}}
```
{{#endtab }}
{{#endtabs }}

Here `Add` takes two parameters while `Clamp` takes three. Although they are different methods in this example, the same principle applies when two methods share the exact same name but differ in arity — the compiler uses the argument count to pick the right one. Constructor overloading works the same way:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/constructor-overloading.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/constructor-overloading.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Printer` class has two constructors: one that accepts a `string` prefix and a parameterless one that delegates to the first via `: this("[default]")`. Callers can write `new Printer(">>>")` or `new Printer()` and the compiler resolves the correct constructor by argument count.

## Overloading by Parameter Type

Methods can also share a name and the same number of parameters if the parameter types differ. The compiler resolves the call based on the types of the arguments passed.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/by-type.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/by-type.cs}}
```
{{#endtab }}
{{#endtabs }}

Both methods are named `Add` and both take two parameters, but one accepts `int` arguments and the other accepts `double`. Calling `MathHelper.Add(3, 4)` resolves to the `int` overload, while `MathHelper.Add(1.5, 2.5)` resolves to the `double` overload.

The same pattern works for instance methods with any number of type-differentiated overloads:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/object-model/method-overloading/instance-type-overloads.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/object-model/method-overloading/instance-type-overloads.cs}}
```
{{#endtab }}
{{#endtabs }}

The `Printer` class defines four `Format` overloads — one each for `int`, `bool`, `double`, and `string`. When you call `printer.Format(42)` the compiler picks the `int` overload; `printer.Format(true)` picks the `bool` overload, and so on. Each overload emits as a distinct C# method with the same name, and the C# compiler performs the same overload resolution at the call site.
