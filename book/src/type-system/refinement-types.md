# Refinement Types

Refinement types let you express value constraints directly in the type system. A refinement type narrows an existing base type by attaching a predicate — only values satisfying that predicate belong to the refined type. This gives you compile-time rejection of invalid values and runtime validation via pattern matching, without writing manual guard code.

Unlike [type aliases](./type-aliases.md) (which are transparent names) or [newtypes](./newtypes.md) (which create distinct types cloned from their source), refinement types keep the same underlying representation as their base type but restrict which values are valid.

Refinement predicates use the same clause syntax as [pattern matching](./pattern-matching.md) — relational operators, `and` combinators, and property patterns. The key difference is that pattern clauses test a value at runtime and branch on the result, while refinement predicates declare a constraint that the compiler enforces statically wherever possible.

## Simple Refinements

A simple refinement type applies a single relational predicate to a base type using the syntax `type Name = BaseType predicate;`. The predicate uses a comparison operator and a constant value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/simple.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/simple.cs}}
```
{{#endtab }}
{{#endtabs }}

The compiler checks literal assignments against the predicate at compile time. Since `AdultAge` (`>= 18`) is a stricter constraint than `Positive` (`> 0`), any value satisfying `AdultAge` also satisfies `Positive` — so an `AdultAge` can be assigned to a `Positive`, but not the other way around.

## Compound Refinements

A compound refinement type combines multiple predicates using the `and` combinator. All predicates must hold simultaneously for a value to satisfy the type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/compound.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/compound.cs}}
```
{{#endtab }}
{{#endtabs }}

The compound predicate `>= 0 and <= 100` constrains the value to the inclusive range [0, 100]. The compiler rejects literals outside this range at compile time — assigning `-1` or `101` to a `Percentage` variable would produce a diagnostic error.

## Union Types with Refinements

A refinement type can use a union base type where each constituent has its own predicate. The value must satisfy at least one constituent's constraint to be valid.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/union-refinement.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/union-refinement.cs}}
```
{{#endtab }}
{{#endtabs }}

Each constituent in the union carries its own predicate. The literal `5` satisfies the `int >= 3 and <= 8` arm, while `50` satisfies the `byte >= 0 and < 64` arm.

## Property Pattern Refinements

A refinement type can constrain properties of a class or struct type using property pattern syntax. This lets you express structural invariants on complex types. A predicate may read only what cannot change once a value has been proven: a `readonly` field, or a property with no `set` accessor whose getter is pure, an `init`-only auto-property among them. A mutable field could change after validation, breaking the invariant, and so could a getter that reads one. A computed getter qualifies when it is pure, as `Interval.Length` is in [Where Mist Requires Purity](../advanced/purity.md#where-mist-requires-purity).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/property-pattern.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/property-pattern.cs}}
```
{{#endtab }}
{{#endtabs }}

The property pattern `{ Start: >= 0 }` constrains the `Start` property of `Interval`. Because `Interval` is declared `readonly`, the compiler can guarantee the constraint holds for the lifetime of the value after validation.

## Everything is Compile-Time

Refinement constraints are enforced at compile time whenever possible. When you assign a literal value to a refinement-typed variable, the compiler checks the predicate statically — invalid literals are rejected with a diagnostic error before the program runs.

A value the compiler cannot follow, such as a parameter, user input or the result of a call whose return type carries no refinement, is validated with the `is` pattern matching expression. The `is` check evaluates the predicate at runtime and branches on the result. A value computed in the same body often needs no check, because the compiler follows it from the values it was computed from, as [Proof Follows the Program](#proof-follows-the-program) describes.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/enforcement.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/enforcement.cs}}
```
{{#endtab }}
{{#endtabs }}

The `is AdultAge validAge` expression validates the predicate `>= 18` against `userInput`. If it holds, the binding variable `validAge` is available with the refined type.

A refinement of another refinement is tested whole. With `type Positive = int > 0;` and `type SmallPositive = Positive < 10;`, the test `x is SmallPositive` checks `> 0` as well as `< 10`, so it rejects `-1` and `10` and accepts `1`, and inside the branch `x` is known to be a `SmallPositive`.

Refinements of one base stay apart in a union. With `type Small = int < 10;` and `type Big = int > 100;`, a `Small` value assigned to a `Small | Big` is held as a `Small`, and `value is Small s` gives `s` the type `Small`, ready to pass where a `Small` is required.

## Pattern Matching — Narrowing and Widening

Refinement types integrate with Mist's flow-sensitive type narrowing. When a branch condition implies a refinement predicate, the compiler narrows the variable's type within that branch. Widening in the opposite direction — from a refinement type back to its base type — is always valid since a refined value is always a valid value of the base type.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/pattern-narrowing.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/pattern-narrowing.cs}}
```
{{#endtab }}
{{#endtabs }}

The `if (x >= 18)` condition is recognized as implying the `AdultAge` predicate, so `x` can be assigned to an `AdultAge` variable within that branch. Subset relationships also apply: `DrinkingAge` (`>= 21`) implies `AdultAge` (`>= 18`), so a `DrinkingAge` can always be assigned to an `AdultAge`.

## Refinements as Variable, Parameter, Return, and Field Types

Refinement types can appear anywhere a regular type can — as variable types, method parameters, return types, and class fields. When used as a parameter type, every call is checked at compile time: the argument must be a value the compiler can prove satisfies the predicate, exactly as an assignment must.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/all-positions.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/all-positions.cs}}
```
{{#endtab }}
{{#endtabs }}

Parameters, variables, fields, and return types all rely on the same static analysis: a literal is evaluated against the predicate, a refined value is checked by implication, and any other value is proven from what the compiler knows about it at that point: the conditions on the path and the assignments that produced it (see [Proof Follows the Program](#proof-follows-the-program)). When that is not enough, an `is` check or a condition that proves the predicate supplies the rest. Nothing is deferred to runtime, so a call that cannot be proven is a compile error at the call site rather than an exception in the callee.

Initializer blocks are held to the same contracts. An entry such as `Age = 20` in `new Person { Age = 20 }` is checked against the refinement of the member it assigns. Each element of a collection initializer, such as `-1` in `new Bag { -1 }`, is an argument to the `Add` overload the compiler selected for it, so it must satisfy that overload's parameter types and `where` predicates. That includes a parameter type closed by the constructed owner, such as `T` in `Bag<Positive>`. Each element is judged where it is evaluated, before the next one runs, and a refused element is reported at that element.

## Arithmetic and Unary Results

Built-in arithmetic on a refined value produces its base type. Unary negation follows the same rule: `-a` does not inherit the predicate attached to `a`. Logical not also produces the base `bool` when its operand has a Boolean refinement, because `!` can change a value that satisfies `true` into `false`.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/arithmetic.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/arithmetic.cs}}
```
{{#endtab }}
{{#endtabs }}

The expressions `a + 5`, `a - 2`, and `-a` produce `int` even though `a` is `AdultAge`; `!yes` produces `bool` even though `yes` is `Yes`. Assigning, returning, or passing these results to a refined destination requires proof of the result's predicate, and the operand's predicate alone is insufficient. The compiler can still prove it from the value: when it can show that the arithmetic cannot wrap, it knows what the result equals (see [Arithmetic That Cannot Wrap](#arithmetic-that-cannot-wrap)). Here `a` has no upper end, so `a + 10` could wrap past the largest `int`, and the `is AdultAge` check is what re-establishes the refinement. Supported constant expressions can still be proven directly, such as `Positive p = -(-2)`. Boolean-not constant expressions currently require a pattern check before use as a refined value; Boolean literal proofs such as `Yes y = true` remain supported.

A user-declared unary operator supplies its own return contract. If it explicitly returns a refinement type, callers retain that refinement, and the operator's return value must itself satisfy the declared predicate.

## Ad-Hoc Refinement Constraints

Beyond named refinement types, Mist supports **ad-hoc refinement constraints** — `where` clauses that attach predicates directly to method parameters, type parameters, constructors, and indexers without declaring a named type.

### Method Parameter Constraints

A `where` clause on a method parameter constrains which values can be passed at that call site. The compiler verifies literals and known values at compile time. The predicate itself is checked where it is declared, whether or not the method is ever called: a relational predicate needs a type with an ordering, and a constant predicate needs a constant some value of the type could equal, so `where key not 18` on a `string` parameter is an error at the declaration rather than a constraint no argument can ever satisfy.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-parameters.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-parameters.cs}}
```
{{#endtab }}
{{#endtabs }}

### Bounds That Name What the Call Supplies

A bound may name something other than a constant: another parameter, a dependent parameter of the type or method, or a member of the receiver -- `this.Count` on the declaring type, or `self.Count` on an extension block's receiver. Each is read at the call through what that call supplies: the argument passed for the other parameter, the value the construction closes the dependent parameter with (`Buffer<4>` closes `Size` to `4`, through an alias as well), or the member's declared or lifted value.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-symbolic-bounds.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-symbolic-bounds.cs}}
```
{{#endtab }}
{{#endtabs }}

`new Buffer<4>().At(4)`, `new Tally().Pick(3)` and `Ranges.Width(5, 2)` are each refused as violating their bound. A bound is proven when every value its source may take clears it, and refused as violating only when that source is one known value, so `Ranges.Width(1, h)` with an unknown `h` must be narrowed first. A bound may also name a stable member of another parameter, `where count <= owner.Limit`, and is then read on the argument passed for that parameter, so `if (n <= o.Limit) { Take(o, n); }` is proven. A receiver a local function captures is not the call's receiver, so a bound naming one of its members stays unproven.

### Type Argument Constraints

Ad-hoc refinements can also constrain dependent type parameters. This is useful for types whose behavior depends on a compile-time integer value (like fixed-size collections).

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-type-arguments.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-type-arguments.cs}}
```
{{#endtab }}
{{#endtabs }}

### Abstract Type Contracts

Abstract types can declare parameter refinements as part of their contract. Implementing classes may use the same constraint, a weaker constraint (accepting more values), or no constraint at all. A weaker constraint is valid because it accepts a superset of the abstract type's required inputs — this satisfies the Liskov substitution principle.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/adhoc-abstracts.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/adhoc-abstracts.cs}}
```
{{#endtab }}
{{#endtabs }}

An implementor cannot use a **stronger** constraint than the abstract type declares — that would reject values the abstract type promises to accept.

A bound that names a member, a parameter or a dependent type parameter (`where entry >= 0 and < Count`, `where lo < hi`) is compared by what it names, never by its spelling. The implementor's `Count` matches the abstract type's `Count` when it is the member that fills that slot, and a parameter matches the parameter in the same position. A strict bound implies the inclusive one, so an implementor may accept `<= Count` where the abstract type promises `< Count`, but not the reverse. When it compares two contracts, the compiler does no arithmetic on bounds: `< Count` and `<= Count - 1` are not treated as the same, and neither are `lo < hi` and `hi > lo`. A return or property type follows the same rule in the other direction: the implementor may promise a stronger bound, but not a weaker one.


Arithmetic consumes refinements while binding each expression. Integer numeric
types expose nested `Nonzero` refinements; signed types also expose
`QuotientDividend` and `QuotientDivisor` to exclude their minimum and negative one.
Division requires a nonzero denominator and either quotient exclusion. A positive
denominator proves both conditions. Decimal uses its existing real-valued
refinement ranges for overflow and zero-divisor proofs. A later guard cannot
change an earlier expression's type. See [Results](../standard-library/results.md).

## Proof Follows the Program

A value that is neither a literal nor of a refined type can still meet a refinement. At each point in a method body, the compiler keeps the facts it knows about the values in scope and proves each contract from them. It records facts from:

- the declared type of each local, parameter and stable member: a parameter `Capacity capacity` contributes `capacity >= 0` and `capacity <= 1_000_000`;
- the `where` clauses of the enclosing method, constructor or indexer;
- the conditions on the path: inside `if (used <= capacity)` the comparison holds, in its `else` the opposite holds, and a loop's condition holds in its body;
- assignments: `var a = b;`, `var a = 7;` and `var doubled = capacity * 2;` each record what the new value equals;
- the length of a sequence created with a known size.

A fact may relate several values, such as `used <= capacity` or `count <= items.Length - start`. To prove a contract, the compiler assumes it is broken, then combines that assumption with the facts it holds through the values they share, until it reaches a statement no value can satisfy.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-relations.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-relations.cs}}
```
{{#endtab }}
{{#endtabs }}

`Reserve` requires `needed <= available`. At the call, the compiler knows `used <= capacity`, `capacity >= 0` and `doubled == capacity * 2`. If `used` were greater than `doubled`, then `capacity * 2` would be less than `capacity`, which only a negative `capacity` allows, and `Capacity` rules that out. Passing the expression itself, `Reserve(used, capacity * 2)`, is proven the same way.

### Arithmetic That Cannot Wrap

`+`, `-`, and `*` by a constant record what their result equals only when the facts prove the result fits its type, because a result that wrapped is not the value the fact would name. In `Demo`, `capacity <= 1_000_000` proves that `capacity * 2` fits an `int`. Declare `capacity` as a plain `int` and the product may wrap, so `doubled` is known only as an `int` and the call is refused:

```text
error MIST0277: Argument 'used' is not proven to satisfy the refinement constraint '<= available' on parameter 'needed' (fails when used >= doubled + 1).
```

The same rule applies to refined operands. With `type Age = int >= 0 and <= 150;`, an `age` that a condition has shown to be `>= 18` makes `AdultAge later = age + 5;` valid with no check, because `age + 5` cannot wrap. In the [arithmetic example](#arithmetic-and-unary-results), `a` is an `AdultAge` with no upper end, so `a + 10` could wrap and needs the `is` check.

For `float` and `double`, negation is recorded, and so is `*` or `/` by a power of two when the operand is known to stay in range. `+` and `-` are never recorded for them, because the result can round. For `decimal`, only negation is recorded. Integer `/` and `%`, shifts, bitwise operators, element reads and calls record only what their result type says.

### Facts Belong to Values

A write gives a variable a new value, which starts with only its type's facts. What was known about the previous value stays with that value, so a value computed from it before the write keeps everything it was given.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-writes.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-writes.cs}}
```
{{#endtab }}
{{#endtabs }}

`y` was recorded as `x * 2` while `x` held `5`, so `NeedsTen(y)` is proven even though `x` holds `7` by then.

Facts are kept only about values nothing else can change while the body runs, such as its locals and parameters. A field of an object that other code can reach contributes only its type, even right after a condition tests it. So does a local that a lambda or local function writes.

### Where Paths Meet

Where two paths meet, the compiler keeps each path's facts as a separate alternative rather than merging them variable by variable. A contract after the join must hold under every alternative.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-joins.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-joins.cs}}
```
{{#endtab }}
{{#endtabs }}

After the `if`, either `low == 1` and `high == 2`, or `low == 100` and `high == 200`, and `low < high` holds under both. Had the compiler kept `low` as "1 or 100" and `high` as "2 or 200" separately, it would also have to allow `low == 100` with `high == 2`, which no path produces, and it would refuse the call.

When too many paths meet at one point, the compiler merges the oldest alternatives variable by variable. A refusal that rests on a merged fact ends its failing case with `after merging paths`: read that case as a possibility, not as a path the program takes.

### Loops

The head of a loop is where the path entering the loop meets the path coming round from the end of its body. The compiler runs the body a few rounds, then keeps each fact about a value the loop writes that held on every round and drops each one that changed. Facts about values the loop does not write are untouched.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-loops.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-loops.cs}}
```
{{#endtab }}
{{#endtabs }}

`i >= 0` holds on every round, while `i`'s upper end moves, so the head keeps `i >= 0`. Inside the body the loop condition adds `i < n`, and with `n <= items.Length` from the `where` clause, `items[i]` is proven in range. `total` grows on every round, so after the loop it is known only as an `int`. Passing it to a method declared `where total <= 100` is refused:

```text
error MIST0277: Argument 'total' is not proven to satisfy the refinement constraint '<= 100' on parameter 'total' (fails when total >= 101).
```

`i++` and `total += i` are the assignments they stand for, `i = i + 1` and `total = total + i`, and record facts under the same rule as any other arithmetic.

### Calls

At a call, the compiler reads the callee's signature, never its body. After `var d = Double(n);`, `d` is known only by what `Double`'s return type says, so a contract that relates `d` to `n` is refused even when `Double` returns `x * 2`. Writing `var d = n * 2;` in the caller proves it. Inside a method, the parameters' types and the `where` clauses are the facts the body starts from; nothing a caller knew reaches the body.

### Booleans, Reals and Strings

The same facts apply to the other primitive types.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-bools-and-reals.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-bools-and-reals.cs}}
```
{{#endtab }}
{{#endtabs }}

`var ok = i < n;` records two alternatives, `ok` true with `i < n` or `ok` false with `i >= n`, so `if (ok)` proves what `if (i < n)` would. For `float`, `double` and `decimal`, a strict comparison stays strict: `left < right` does not become `left <= right - 1` as it does for whole numbers.

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/type-system/refinement-types/flow-strings.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/type-system/refinement-types/flow-strings.cs}}
```
{{#endtab }}
{{#endtabs }}

A string is known by a literal it equals and by the regex patterns it matches. `text == "LHR"` gives `text` that literal, and the literal decides `AirportCode`'s pattern. `text == known` gives `text` the match that `known` carries. An `is` pattern that names a regex adds that match, so after the last `if`, `code` holds either the literal `"DEF"` or a `text` that matched, and both meet `AirportCode`. A string's `Length` takes part in facts like any other integer.

### When a Proof Fails

A refusal names the case the compiler could not rule out, in the program's own names. Continuing the `Age` example, `AdultAge earlier = age - 2;` is refused:

```text
error MIST0272: Cannot assign 'int' to 'AdultAge' without proving its refinement '>= 18' (fails when age >= 18 and age <= 19): narrow the value with a pattern first.
```

The failing case says that `age - 2` falls below 18 when `age` is 18 or 19. A condition or an `is` check on the path that rules out that case completes the proof.

A body with a very large number of interacting facts can exceed the compiler's proof budget. That is reported as `MIST0485`, separately from a refusal: the proof was not finished, and an explicit check on the path gives it fewer facts to combine.
