# Requirements Document

## Introduction

This document specifies what a member may do beyond its own scope, stated by three composable modifiers -- `readonly`, `local` and `pure` -- so that a caller can know what a call can touch without reading the member's body. `readonly` exists (`readonly-contexts` spec); this document adds `local` and `pure`, and amends `readonly` so the three agree.

The language already needs this in several places without having had a name for it: a default must start in the same state every time it is taken, a refinement predicate may only read what cannot change under it, and a pure scope that builds a collection must be able to call `Add` on it.

> **Delivered** 2026-09-27 on the isolated v2 compiler. The Deferred section names what waits on a promise Mist does not have yet; the analysis register records the adversarial hunt as open work.

### What `pure` means

`pure` means what it means in computer science generally, and in Haskell: a pure computation

1. has **no side effects** -- it changes no state that exists outside it and performs no input or output; and
2. is **deterministic** -- its result depends only on its inputs, so the same inputs always give the same result within one run of the program (Requirement 7 criterion 5 says what one run fixes).

Two refinements, both standard:

- **Allocation is not a side effect.** A pure computation may create objects and change them, as Haskell's `ST` lets a pure function mutate state that exists only inside it. Returning a list it built, and adding to it first, is pure.
- **Pure is not total.** A pure computation may still fail or not terminate, as a pure Haskell function may. Purity promises the same failure every time, not that none occurs.

Where other systems use the word differently, Mist follows the general meaning:

| System | Its word | What it promises | In Mist |
|---|---|---|---|
| Haskell, general CS | pure | no side effects, deterministic | `pure` |
| Solidity | `view` / `pure` | reads but writes no state / reads and writes no state | `readonly` / `pure` |
| D | weakly / strongly pure | no global state; weakly pure may read and change its arguments | `pure` does neither to what it was handed; `local` is the closer match to weakly pure |
| C# `[Pure]` attribute | pure | "does not make any visible state changes" | that is `readonly`, not `pure` |
| GCC `__attribute__((pure))` | pure | no side effects, may read global memory | weaker than `pure` |

### Every restriction is about what comes from outside

A scope may do anything with what it **owns** -- what it created, what was moved into it, its own copies of structs. Each modifier restricts only what the scope does with what comes from **outside** it:

| Modifier | May read from outside its own scope | May write outside its own scope |
|---|---|---|
| `readonly` | anything | nothing |
| `local` | only its own instance | only its own instance |
| `local readonly` | only its own instance | nothing |
| `pure` | only what cannot change, and inputs it holds frozen | nothing |

For a local function or lambda, "its own instance" is what it captured from the scope around it.

Every `pure` member is `readonly`. Every `local readonly` member is both `local` and `readonly`. A member with none of these may do anything, and is assumed to.

Each modifier may also be written on a type, as `readonly` already can: every member of the type then makes the promise (Requirement 15). A `pure` type cannot change once constructed, and the instance members of a `local` type never reach outside their instance.

### Where an input comes from

Purity is judged against the reference a scope holds, not the object behind it. A `frozen` reference can never observe a change: for its whole lifetime every read through it gives the same answer, even though the object behind it may change once the loan ends -- the next loan is a different input.

| Input | Origin | May a pure scope read its state? | May it write it? |
|---|---|---|---|
| struct, by value | the scope's own copy | yes | yes (the copy) |
| a reference-typed field inside that struct | external: the copy still points at another object | stable fields only | no |
| struct by `ref` or `in` | external: the caller's storage | stable fields only | no |
| class, unrestricted or `readonly ref` | external, and may change between reads | stable fields only | no |
| class, `frozen` | external, but fixed for its lifetime | yes | no |
| class, `owned` | moved in, so the scope's own | yes | yes |

### What a pure scope may call

| Calling... | Allowed on... | Because |
|---|---|---|
| a `pure` member | anything | it touches nothing outside its own scope |
| a `local readonly` member | an instance the scope owns, or a `frozen` input | it reads only that instance, which cannot change under it |
| a `local` member | an instance the scope owns | it may change that instance, and only the scope can observe it |
| a `readonly` member | nothing | it may read outside state that can change between calls |
| an unmarked member | nothing | it may do anything |

### Example

```mist
intrinsic public class List<T>
{
    intrinsic public pure List();                        // a fresh, empty list
    intrinsic public local readonly int Count { get; }   // reads only this list, writes nothing
    intrinsic public local void Add(T item);             // touches only this list
}

pure int Seeded()
{
    owned var l = new List<int>();   // Seeded owns l
    l.Add(1);                        // local, on an instance Seeded owns
    return l.Count;                  // 1, every time
}

pure int Size(frozen List<int> xs)
{
    return xs.Count;                 // local readonly, on a frozen input
}

int Count(List<int> xs)
{
    return xs.Count;                 // readonly at most: xs can change between calls
}
```

`Seeded` is proven pure from declarations alone. The owned binding proves `Seeded` owns `l`, and `Add` promises to touch nothing but `l`, so everything `Add` touches is inside `Seeded`.

## Glossary

- **Scope**: The body of a member, local function or lambda, during one evaluation of it.
- **Owned (by a scope)**: What a scope created during its evaluation, what was moved into it through an `owned` parameter or binding, a struct it holds by value (but not what that struct's reference-typed fields point at), and, for a local function or lambda, what it captured that its enclosing scope owns.
- **External**: Everything a scope does not own: its reference-typed and by-reference inputs, statics, and the world outside the program.
- **Stable_Field**: A field that cannot change once its object is constructed: a `readonly` field, the backing field of a property with no setter or an `init`-only one, or a field of a readonly type. The storage of a readonly type's property that declares a setter is not stable, since a readonly setter may write it (`readonly-contexts` base rule 12). A static field is stable when it is `readonly`.
- **Own_Instance**: For an instance member, the instance it was called on and what that instance owns. For a local function or lambda, what it captured. Static members and constructors have none.
- **Pure_Member**, **Local_Member**, **Local_Readonly_Member**, **Readonly_Member**: A member declared, or inferred (Requirement 8), to keep the corresponding promise.
- **Pure_Type**, **Local_Type**, **Local_Readonly_Type**: A type declared with the corresponding modifier (Requirement 15). **Readonly_Type** is as the `readonly-contexts` spec defines it.
- **Pure_Function_Type**: A function type written with `pure` (Requirement 16).
- **Readonly_Context**: As defined by the `readonly-contexts` spec, amended by Requirement 11.

## Requirements

### Requirement 1: Syntax

**User Story:** As a Mist developer, I want to state what a member does beyond its own scope with a few modifiers that compose, rather than a keyword per combination.

#### Acceptance Criteria

1. THE Grammar SHALL define `local` and `pure` as reserved keywords. Neither is used as a name anywhere in the samples, the standard library or the book's snippets.
2. THE Compiler SHALL accept these combinations on a member, and on a type (Requirement 15): `readonly`, `local`, `local readonly`, `pure`. Order among modifiers SHALL NOT matter.
3. WHEN `pure` and `readonly` are both written, THE Compiler SHALL accept them and report an informational diagnostic that `readonly` is implied by `pure`.
4. WHEN `pure` and `local` are both written, THE Compiler SHALL report an error: `local` allows a member to change its own instance, which `pure` forbids.
5. `local` SHALL be admitted on instance methods, instance properties and indexers, local functions and lambdas, and SHALL be refused on static members and constructors, which have no Own_Instance. `pure` SHALL be admitted on methods, constructors, properties, indexers, local functions and lambdas, and on a function type (Requirement 16). On a property or indexer, a modifier applies to each accessor it declares; an accessor MAY state its own promise ahead of its accessibility, which replaces the declaration's for that accessor (`{ local readonly get; local set; }`). On a local function, `readonly` is admitted only within `local readonly` or through `pure`; a bare `readonly` stays refused, as the `local-functions` spec has it.
6. THE Grammar SHALL admit these modifiers on intrinsic member declarations and on abstract members.
7. THE Pretty_Printer SHALL preserve them, and parsing then printing then parsing SHALL produce an equivalent AST.

### Requirement 2: How the Promises Nest

#### Acceptance Criteria

1. Every Pure_Member SHALL be a Readonly_Member. Every Local_Readonly_Member SHALL be both a Readonly_Member and a Local_Member.
2. Wherever a readonly member, constructor or type is required -- a readonly context calling it, a readonly abstract member it implements, a `where T : readonly` bound -- a pure or local readonly one SHALL be accepted.
3. The converse SHALL NOT hold: a readonly member is pure only if it also keeps Requirement 7, and local readonly only if it also keeps Requirement 5.

### Requirement 3: A Promise, Not a Demand

**User Story:** As a Mist developer, I want these modifiers to cost ordinary callers nothing, so that marking `List<T>.Add` `local` does not change how anyone calls it.

#### Acceptance Criteria

1. A modifier from this document SHALL be a promise the member makes about itself. It SHALL add no requirement on an ordinary caller -- no ownership tier, no loan, no modifier -- and SHALL change neither how a call binds nor what is emitted.
2. Only a scope that is itself `readonly`, `local` or `pure`, or an expression the language requires to be pure (Requirement 12), SHALL consult these promises, and only to decide which calls it may make.
3. The one exception is `pure` on a function type (Requirement 16). It is part of a type, not a promise a member makes, so it is checked wherever a value converts to that type, as any type is.

### Requirement 4: `readonly`

#### Acceptance Criteria

1. A Readonly_Member SHALL write nothing External, and MAY read anything, as the `readonly-contexts` spec defines, amended by Requirement 11.
2. A readonly scope MAY call a Readonly_Member, Pure_Member or Local_Readonly_Member on anything, and a Local_Member on an instance it owns.

### Requirement 5: `local`

**User Story:** As a Mist developer, I want to promise that a member touches nothing but its own instance, so that a scope which owns that instance can call it and remain whatever it is.

#### Acceptance Criteria

1. A Local_Member SHALL read and write only its Own_Instance and what it creates.
2. A Local_Member MAY use its arguments as values -- store them, pass them on, compare them, read the contents of a struct passed by value -- and SHALL NOT read or write state reachable through a reference-typed or by-reference argument, which lies outside its Own_Instance. It SHALL NOT store an argument it does not own into an owned field, or into any field of another instance it owns: an owned slot holds only what the scope owns or what cannot change (Requirement 6.4).
3. A Local_Member SHALL call only Pure_Members, and Local_Members and Local_Readonly_Members on its Own_Instance or on what it creates. It SHALL perform no input or output and read no static that is not a Stable_Field.
4. A local function or lambda declared `local` SHALL treat what it captured as its Own_Instance. What it writes through a captured reference is its own only if the enclosing scope owns what that reference points at; THE Compiler SHALL judge that where the local function or lambda is called, against the enclosing scope's ownership.
5. A Local_Readonly_Member SHALL keep criteria 1 to 4 and SHALL write nothing, its Own_Instance included.

### Requirement 6: No Side Effects in a Pure Scope

#### Acceptance Criteria

1. A pure scope SHALL write only what it owns. A pure constructor MAY assign the fields of the instance under construction, which the constructing expression owns.
2. A pure scope SHALL perform no input or output, directly or through anything it calls.
3. A pure scope SHALL call only what the "What a pure scope may call" table admits: a Pure_Member on anything, a Local_Readonly_Member on an instance it owns or a `frozen` input, and a Local_Member on an instance it owns.
4. WHEN a pure scope passes an argument to a Local_Member that may store it in an instance the scope owns, the argument SHALL be owned by the scope or unable to change. A `frozen` reference SHALL NOT be stored, since the owned instance may outlive the loan.

### Requirement 7: Deterministic

#### Acceptance Criteria

1. A pure scope MAY read literals, `void`, enum variants, what it owns, the state of a `frozen` input, and any Stable_Field of any object it can reach, including a `readonly` static.
2. A pure scope SHALL NOT read a field that is not a Stable_Field of an External object that is not a `frozen` input: reading the current contents of a reference it does not hold frozen is not pure, because the same reference can show different contents at different times.
3. A pure scope SHALL NOT read any state outside the program -- the clock, randomness, the environment, the current culture, files.
4. A pure scope MAY create a lambda. The lambda's body is judged when it is called, or where it converts to a Pure_Function_Type (Requirement 16), not when it is created.
5. Deterministic means within one run of the program. What the process fixes when it starts -- the runtime and its version, its globalization mode and ICU, the operating system and machine -- is the program's setting, not an input, so a pure member MAY depend on it. What can change while the program runs -- the current culture, the clock, randomness, environment variables, files, statics that are not Stable_Fields -- is outside state, which criteria 2 and 3 forbid a pure scope to read.

### Requirement 8: Declaration and Inference

#### Acceptance Criteria

1. WHEN a member is declared `readonly`, `local`, `local readonly` or `pure`, THE Compiler SHALL check its body against the corresponding requirements and SHALL report an error at each operation that breaks them, naming the operation and the rule.
2. WHEN a member with a Mist body declares none of them, THE Compiler SHALL infer the strongest promise its body keeps.
3. THE Compiler SHALL infer over the whole program as a greatest fixpoint: every member with a Mist body starts with every promise, loses each one its body breaks or that a callee has lost, and the process repeats until nothing changes. Recursive members therefore keep a promise when nothing else takes it away.
4. WHEN an abstract member declares a promise, every implementation SHALL keep it. WHEN it declares none, it SHALL be inferred to keep exactly the promises every implementation in the program keeps.

### Requirement 9: Members Without a Mist Body

#### Acceptance Criteria

1. A member with no Mist body -- intrinsic, CLR-projected or extern -- SHALL keep a promise only when its Mist declaration states it. The compiler cannot see what such a member touches, so one that states nothing SHALL be treated as touching anything and performing input or output.
2. A CLR member with no Mist declaration SHALL keep no promise. To use one from a readonly, local or pure scope, Standard declares it as an intrinsic with its promise, or the program wraps it in a member of its own.
3. THE Standard declarations SHALL state the promise of each member they declare that keeps one: the primitive types' and `String`'s operations as `pure`, `Regex` matching as `pure`, and for any collection Standard declares, its constructors `pure`, its read-only members `local readonly`, and its adding and removing members `local`. Standard declares `List<T>` as such an intrinsic over the CLR list, with a pure constructor, so a constructorless type may hold one (defaultability Requirement 6).

### Requirement 10: Diagnostics

#### Acceptance Criteria

1. WHEN a declared promise is broken, THE Compiler SHALL report an error at the offending operation naming it, the promise, and the rule: the field written, the static read, the member called and the promise it lacks.
2. WHEN a readonly, local or pure scope makes a call the tables do not admit, THE Compiler SHALL report an error naming the member, the promise it keeps, and what the call would need -- an owned receiver, a `frozen` input, or a stronger promise.

### Requirement 11: Amendment to Readonly Contexts

**User Story:** As a Mist developer, I want a readonly member to be able to build a value before returning it, so that `readonly` restricts what I do to what I was given, not to what I made.

#### Acceptance Criteria

1. This requirement amends the `readonly-contexts` spec's Requirement 11 with a fifteenth rule: WHILE in a readonly context, THE Readonly_Analyzer SHALL permit writing a field of, writing through an index of, and calling a Local_Member -- a method, a getter or a setter -- on what the context owns; a member that is not local is admitted only as base rule 2 admits it. It SHALL count every write the context makes through anything External -- a `ref` parameter, an element of an array parameter, an object initializer's setter -- as a write (Requirement 4.1).
2. The admission of an index write into an array the body just allocated, which the analyzer already makes, SHALL be read as a case of criterion 1 rather than a rule of its own.
3. Base rule 10 SHALL admit allocation through a pure constructor, every pure constructor being readonly (Requirement 2).

### Requirement 12: Where Mist Requires Purity

#### Acceptance Criteria

1. A Field_Initializer of a defaultable type, and a Default_Block entry, SHALL be pure expressions -- evaluated as a pure scope with no inputs (`defaultability` spec, Requirement 6) -- so every default of a type starts in the same state.
2. A member a refinement predicate reads SHALL be a Pure_Member, or a Stable_Field. A Local_Readonly_Member is not enough there: it reads its instance's current state, which may change while the value exists.
3. Other specs MAY require any of these promises by referring to this document.

### Requirement 13: Emission

#### Acceptance Criteria

1. None of these modifiers SHALL change the emitted program's behavior. THE C# backend MAY emit `[System.Diagnostics.Contracts.Pure]` on a Readonly_Member as documentation, C#'s attribute promising what `readonly` does.
2. THE C# backend SHALL emit every conversion between a value and text, every ordering of text, every case mapping and every regular expression that Mist performs on the program's behalf with the invariant culture, which the call names, or runs under for that one call, so the members Requirement 9.3 declares pure read no state that can change while the program runs (Requirement 7 criterion 5). Text equality SHALL be ordinal, which no culture affects. Text ordering uses `StringComparer.InvariantCulture`, which is deterministic within a run by that criterion. A culture-aware form, or another ordering of text, if one is wanted, is a separate member; a culture-aware one does not make the promise. Two things are outside this criterion by design, and no pure scope can reach either: a CLR member the program names directly, such as a qualified `System.Console.WriteLine`, is the CLR call it names (Requirement 9.2); and a sort whose elements are held as a type parameter or `unknown`, or are optional tuples, orders a tuple's text through `Comparer<T>.Default`, which reads the current culture, so a pure scope refuses that sort (Requirement 7 criterion 3).
3. THE Compiler SHALL read no culture itself: it parses and prints every literal with the invariant culture, so a program compiles to the same code on any machine. It formats the values a diagnostic quotes, and the CLI its timing summary, with the invariant culture too.

### Requirement 14: Editor Support

#### Acceptance Criteria

1. THE TextMate grammar SHALL highlight `local` and `pure` as modifiers (`storage.modifier.mist`).

### Requirement 15: Promises on a Type

**User Story:** As a Mist developer, I want to make a promise once for a whole type, so that a type whose every member keeps it says so where the type is declared, as a `readonly` type already does.

#### Acceptance Criteria

1. THE Compiler SHALL accept `local`, `local readonly` and `pure` on a class, struct or abstract declaration, with the combinations and diagnostics of Requirement 1 criteria 2 to 4. On an enum or a newtype they are refused as a placement error (Deferred, item 2).
2. A modifier on a type SHALL be read as that modifier written on each of the type's members that admits it (Requirement 1 criterion 5): `pure` on every member, constructors and static members included, as `readonly` on a type does; `local` on every instance method, property and indexer. Constructors and static members of a Local_Type, which `local` does not admit, SHALL keep what they declare or infer.
3. A Pure_Type and a Local_Readonly_Type SHALL each be a Readonly_Type. Every field of a Pure_Type is therefore a Stable_Field, and no member writes an instance once it is constructed, so a Pure_Type cannot change after construction. A pure scope MAY read its state through any reference, not only a `frozen` one, as Requirement 7 criterion 1 already admits, and MAY call any of its members.
4. A Local_Type's members SHALL keep Requirement 5 each: every call they make is to a Pure_Member, or to a Local_Member or Local_Readonly_Member on the Own_Instance. The Own_Instance includes what the instance owns, so a member MAY call a `local` or stronger member on a field the instance owns, and only a Pure_Member on a field it does not own, whose object other references may share.
5. A member's own modifiers SHALL combine with its type's as though both were written on the member, with the diagnostics of Requirement 1. One exception: a member of a Local_Type MAY declare `pure`, which then replaces `local` instead of conflicting with it. Requirement 1 criterion 4 refuses `pure local` because `local` lets a member write its instance, and a pure member of a Local_Type writes nothing, so it breaks no promise of its type.

### Requirement 16: Pure Function Types

**User Story:** As a Mist developer, I want to write a pure member that calls a function it was given, so that higher-order helpers can be used in pure code when the function passed to them is pure.

```mist
pure int Twice(pure func<int, int> f, int x)
{
    return f(f(x));                                   // calling a pure function value
}

Twice(n => n + 1, 3);                                 // 5
Twice(n => { Console.WriteLine(n); return n; }, 3);   // error: the lambda writes to the console
```

#### Acceptance Criteria

1. THE Compiler SHALL accept `pure` before any type that resolves to a Func_Type (`first-class-functions` spec), through `func`, `Func`, `Action` or an alias. The result SHALL be a Pure_Function_Type with the same parameter and return types.
2. WHERE `pure` could belong to the enclosing declaration -- before the return type of a member or local function, or the type of a property or indexer -- it SHALL belong to the declaration. A Pure_Function_Type in those places SHALL be named through an alias (`type Step = pure func<int, int>;`).
3. Invoking a value of a Pure_Function_Type SHALL count as calling a Pure_Member, which a pure scope may do on anything (Requirement 6 criterion 3).
4. A lambda, or a local function's method group, SHALL convert to a Pure_Function_Type only when it keeps Requirements 6 and 7; a local function declared `pure` is held to the same. Each SHALL be judged once, where it converts or is declared, as a pure scope whose captured variables are External inputs. It MAY read a captured variable only when nothing can assign that variable after the capture, and through that variable only what Requirement 7 admits for an External input. A method group SHALL convert only when its method is a Pure_Member.
5. A Pure_Function_Type SHALL convert implicitly to the same function type without `pure`. The reverse SHALL be refused, by a cast as well: no function value, CLR delegate included, carries evidence that it is pure unless its type states it.
6. As a type argument, the promise SHALL be part of the type: `List<pure func<int, int>>` and `List<func<int, int>>` are different types, and neither converts to the other.
7. THE Compiler SHALL report an error for two overloads whose signatures differ only in whether a function type is pure. The promise is erased on emission (Requirement 13), so both would emit the same C# signature.
8. WHEN a conversion to a Pure_Function_Type is refused, THE Compiler SHALL report an error at the operation in the lambda or method that breaks purity, as Requirement 10 does for a declared promise.

### Requirement 17: Sample

**User Story:** As a Mist developer, I want one program that uses every promise in this document where the spec admits it, so that its output shows each admission compiled and run.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Purity/Program.mist` with a `mist.toml` where `name = "Purity"`.
2. The sample SHALL exercise: a pure scope owning a list it builds and adds to; a pure scope reading a `frozen` input lent from an owned binding; a Local_Type driven by a pure scope through an owned binding; a pure higher-order member given a lambda, a `pure` lambda and a method group; a Pure_Function_Type named through an alias, converted to the plain function type and to a CLR delegate; a pure lambda capturing a `readonly` local; a constructorless type whose field initializer calls a pure constructor, defaulted twice and shown independent, and one holding a `List<T>`, taken by `default` and by `new` (Requirement 12.1); a Pure_Type called on a receiver the scope neither owns nor holds frozen; accessors stating their own promises; a `local readonly` local function; a pure member calling a member whose promise is inferred (Requirement 8.2); a readonly member changing what it created (Requirement 11); and case mapping and number formatting in a pure scope (Requirement 13.2).
3. Each demonstration SHALL print a labelled line, so a wrong value is visible.
4. The sample SHALL expose `public static void RunDemo()` in `Purity.Program`. It SHALL be referenced from `samples/AllFeaturesV2` alone: classic's Standard declares no `List<T>`, and classic binds no local function.
5. The sample SHALL compile and run with no diagnostic of any severity.

#### Proof Code

```mist
// PROOF: `local` on a type makes every instance member promise to touch only its instance: Add is
// local, and Count, also written `readonly`, is local readonly. `local` does not reach a constructor,
// which states its own `pure`.
public local class Counter
{
    private int total;

    public pure Counter(int start)
    {
        total = start;
    }

    public void Add(int n)
    {
        total = total + n;
    }

    public readonly int Count
    {
        get { return total; }
    }
}

// PROOF: a pure scope may change what it owns -- a list it built, a Counter it created; `owned` on
// those bindings states what a local fresh at every assignment already is -- and may read a list it
// holds frozen, lent from an owned binding. The loan ends with the call, so the owner adds again and
// the next loan reads the new count.
public class CoreDemo
{
    public static pure int Seeded()
    {
        owned var l = new List<int>();
        l.Add(1);
        l.Add(2);
        l.Add(3);
        return l.Count;
    }

    public static pure int Size(frozen List<int> xs)
    {
        return xs.Count;
    }

    public static pure int Counted(int a, int b)
    {
        owned var c = new Counter(10);
        c.Add(a);
        c.Add(b);
        return c.Count;
    }

    public static void Run()
    {
        Console.WriteLine($"[core] Seeded()={Seeded()}");
        owned var xs = new List<int>();
        xs.Add(4);
        xs.Add(5);
        Console.WriteLine($"[core] Size(xs)={Size(xs)}");
        xs.Add(6);
        Console.WriteLine($"[core] Size(xs) after Add={Size(xs)}");
        Console.WriteLine($"[core] Counted(3, 4)={Counted(3, 4)}");
    }
}
```

**Expected output:**
```
[core] Seeded()=3
[core] Size(xs)=2
[core] Size(xs) after Add=3
[core] Counted(3, 4)=17
```

## Deferred

1. **A default that cannot fail.** A default is data, and it should not be able to fail. But Mist has no promise that a computation cannot fail, no notion of a total or panic-free member, so no rule can require it yet. Until Mist has such a promise, a default may fail the way any pure expression may (dividing by zero is pure). Because it is pure, it fails the same way every time it is taken. Once Mist gains such a promise, defaults should require it.
2. **Promises on enums and newtypes.** `readonly` is admitted on an enum and a newtype, and `local`, `local readonly` and `pure` probably should be too. Deferred by Arthur on 2026-09-27 until the purity work is done, to keep it from growing; until then they are a placement error there (Requirement 15.1).
