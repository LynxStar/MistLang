# Type Literals Replace `typeof` in Mist

## Rule

Mist does not have a `typeof` keyword. Instead, use a **type literal** — simply write the type name in expression position. The C# emitter automatically generates `typeof(T)` for type literals.

## Mapping

| C# syntax | Mist syntax | Emitted C# |
|---|---|---|
| `typeof(int)` | `int` (in expression position) | `typeof(int)` |
| `typeof(string)` | `string` (in expression position) | `typeof(string)` |
| `typeof(AppSettings)` | `AppSettings` (in expression position) | `typeof(AppSettings)` |
| `typeof(List<int>)` | `List<int>` (in expression position) | `typeof(List<int>)` |

## Examples

```mist
// Getting a System.Type reference
var t = int;  // emits: var t = typeof(int);

// Passing to an API that accepts System.Type
services.AddSingleton(IMyService, MyServiceImpl);
// emits: services.AddSingleton(typeof(IMyService), typeof(MyServiceImpl));

// Using with reflection
var typeName = string.Name;  // emits: typeof(string).Name
```

## When This Applies

- Any CLR API that accepts a `System.Type` parameter
- Reflection scenarios
- Dependency injection registration with `Type` overloads
- Serialization configuration

## Notes

- Type literals in Mist are expressions of type `System.Type` at the CLR level
- When a type literal is followed by a member access (e.g., `int.Name`), it emits `typeof(int).Name`
- Generic types work: `List<int>` in expression position emits `typeof(List<int>)`
