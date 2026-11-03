# Results

`Result<T, E>` carries either a success of type `T` or a failure of type `E`.
`Result<T>` abbreviates `Result<T, Error>`. The internal tag distinguishes the
alternatives even when their payload types are identical, optional, `void`, or
another Result. It is a sealed class with private construction and readonly tagged
storage. There is no default Result, unchecked payload accessor, or
implicit conversion from a Result to its success payload.

A declared Result destination accepts a payload that fits exactly one alternative.
If both alternatives accept it, select the case with `AsSuccess()` or
`AsFailure()`. The destination supplies the other type argument. For construction
without a destination, use the closed type's `Success(value)` or `Failure(error)`
factory. These operations construct a case; applying them to an existing Result
creates a deliberate outer layer. Ordinary Result assignment preserves its case
and layers. A plain `var` binding does not turn a payload into a Result.

`Inspect()` returns `(True, T) | (False, E)`, with literal bool refinements in
the tag position. Matching the tag binds the corresponding payload from the same
snapshot. A different Result, or a later assignment to the original, cannot
borrow that proof. Inspection transfers an owned payload without duplicating it;
a Result or snapshot carrying a linear resource follows that resource's move
and cleanup rules.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/standard-library/results/propagation.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/standard-library/results/propagation.cs}}
```
{{#endtab }}
{{#endtabs }}

Postfix `!` evaluates one Result once, yields its success payload, or returns
its failure from the nearest callable. That callable's return destination must
accept the error, including an admitted error-union constituent. For a Result
return, only its error alternative is considered.
Use inspection and an explicit mapping when the error types differ. Constructors
cannot propagate failures; use a Result-returning factory. Lambdas, local
functions, and async bodies each use their own return destination. Propagation
performs ordinary context restoration and owed cleanup.

One `!` handles one layer. Parenthesize a compound expression before applying it.
A following optional access, such as `ReadPerson()!?.Name`, tests the optional
success payload for presence. Identity propagation on a plain value preserves its
type and optionality, and is allowed in a constructor. A proven failure payload
is not a successful identity value. Prefix `!` remains logical negation.

| API | Contract |
| --- | --- |
| `Result<T, E>.Success(T value)` | Explicit success construction |
| `Result<T, E>.Failure(E error)` | Explicit failure construction |
| `value.AsSuccess()` / `value.AsFailure()` | Select a case using a Result destination |
| `result.Inspect()` | Correlated tuple inspection after one receiver evaluation |
| `Error.Message` | Shared abstract error contract |
| `new MessageError(message)` | Ordinary general-purpose error carrying a message |

Domain error types can carry additional structured data and conform to `Error`
through the ordinary abstract-type mechanisms. They do not inherit from a CLR
exception.

Integer division and remainder use their own numeric width. When operand facts
prove safety the expression is numeric; known zero divisors or unrepresentable
signed quotients are compile-time errors; uncertainty produces
`Result<T, DivisionError>`. This includes narrow signed minimum divided by negative
one. Signed minimum remainder negative one is zero. Integer addition, subtraction,
multiplication and signed negation wrap. Float and double keep IEEE values.

Decimal binary arithmetic follows the same three-way rule for overflow and zero
division, returning `Result<Decimal, System.Exception>` when safety is unknown.
Decimal remainder needs no representable quotient. Constants, declared refinements
and sound guards can prove safety; an expected numeric destination cannot.
Arithmetic facts do not survive writable reference exposure or assignment, and
a check of a property or call does not prove a later evaluation.

Standard declares arithmetic composition for all numeric types in Result/value,
value/Result and Result/Result arrangements. Native integer and decimal error
channels retain their types. Concrete overloads provide `Result<T, Error>` for shared Error contracts and
Standard CLR exception adaptation. Generic overloads retain the input error
domain or an explicit union of domains, adding a native numeric error when needed.
Different error destinations can be mapped explicitly through inspection. These declarations preserve one Result layer, evaluate both
operands before entering the operator, and select the left failure when both fail.
A final grouped postfix `!` propagates from the caller; operators themselves do
not cause an implicit return from it.


{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/standard-library/results/arithmetic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/standard-library/results/arithmetic.cs}}
```
{{#endtab }}
{{#endtabs }}
