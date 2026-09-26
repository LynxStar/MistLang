# Requirements Document

## Introduction

This document is the single statement of defaultability in Mist: which types have a default value, what that value is, how `default` and `new()` relate, and what the compiler must refuse. It consolidates and supersedes two earlier specs whose texts disagreed:

- `default-keyword` introduced the `default` keyword, default blocks and the default-value computation, under the rule that every concrete type is defaultable.
- `optional-defaultability` "builds on" it and replaced that rule: a type with constructors may have no default, and a type without constructors gets an implicit constructor that returns its default. It did not edit the earlier text, so the earlier text kept stating rules that no longer held.

A third spec, `definite-assignment`, replaced `optional-defaultability`'s array rule with the stampable-versus-initializer model, and the `stampability` spec extends that model; this document defers to both for arrays.

Where this document and either earlier spec differ, this document governs. The earlier specs remain in place, marked superseded, so references to them by requirement number still resolve; the appendix maps each of their requirements to where it now lives.

### Two ways to get a value

- **Default (data).** A type's default value is precomputed data: `default(T)` stamps it out without running any user-declared code. It is deterministic and side-effect-free, which is why every expression that contributes to it must be consistent: each evaluation starts from the same state (Requirement 6).
- **Construction (behavior).** `new T(...)` runs a constructor, which is behavior: it may perform IO, fail, or depend on external state.

A type is not required to have a default. A type with user-declared constructors has one only if it declares a `default { }` block. A type with no user-declared constructor must have a default, because that default is the only way to obtain an instance: the compiler generates an implicit parameterless constructor that returns it.

```mist
// No constructor: defaultable, and `new Point()` returns the default.
public class Point
{
    public int X;
    public int Y = 7;
}
Point a = default(Point);   // X == 0, Y == 7
var b = new Point();        // X == 0, Y == 7 -- the same value

// Constructors and a default block: constructible and defaultable, separately.
public class Config
{
    public string Environment;
    public int MaxRetries;

    public Config(string env)
    {
        this.Environment = env;
        this.MaxRetries = 3;
    }

    default {
        Environment = "development"
        MaxRetries = 1
    }
}
var prod = new Config("production");   // production, 3 -- the constructor
Config dev = default(Config);          // development, 1 -- the block
// new Config() is an error: user-declared constructors suppress the implicit one.

// Constructors and no default block: constructible, not defaultable.
public class DatabaseConnection
{
    public string ConnectionString;

    public DatabaseConnection(string connStr)
    {
        this.ConnectionString = connStr;
    }
}
// default(DatabaseConnection) is an error.
```

## Glossary

- **Compiler**: The Mist compiler, from source text through analysis to emitted code.
- **Grammar / Parser**: The Storm grammar (`Mist.storm`, `keywords.storm`) and the parser generated from it, shared by every pipeline.
- **Pretty_Printer**: The component that formats a Mist AST back into Mist source text.
- **C#_Backend**: The part of the Compiler that emits C#. Requirements addressed to it describe how the language's semantics must survive that target; other backends owe the same semantics, not the same shapes.
- **User_Constructor**: A constructor declared in source (`public TypeName(...)`).
- **Implicit_Constructor**: A compiler-generated parameterless constructor that returns the type's default value. Generated only for a class or struct that declares no User_Constructor.
- **Default_Block**: A member declaration `default { ... }` inside a class or struct that states field values of the type's default, replacing what they would otherwise be, or binds an abstract-typed field to a concrete type.
- **Field_Override**: A Default_Block entry `fieldName = Expression`.
- **Type_Binding**: A Default_Block entry `fieldName : ConcreteType`, optionally followed by `{ nestedField = Expression ... }`, giving an interface-, trait- or abstract-typed field the default of a concrete type.
- **Field_Initializer**: An expression assigned to a field at its declaration (`int y = 7;`).
- **Zero_Initialization**: The value a defaultable field with no Field_Initializer takes: `0` for integer types, `0.0` for floating-point types, `false` for `bool`, `'\0'` for `char`, `""` for `string`, the designated default variant for an enum, absence (`void`) for a union containing `void`, and the default value for a defaultable class or struct.
- **Inferred_Default**: The default of a type with no Default_Block, computed from its Field_Initializers and Zero_Initialization.
- **Explicit_Default**: The default of a type with a Default_Block: its Inferred_Default with the block's entries applied.
- **Defaultable_Type**: A type that has a default value and supports `default(T)`.
- **Non_Defaultable_Type**: A type with no default value. A valid state for a type with User_Constructors.
- **Default_Expression**: `default(TypeName)`.
- **Default_Literal**: A bare `default` whose type is taken from context.
- **Default_Constraint**: `where T : default`, requiring a type argument to be a Defaultable_Type.
- **Consistent_Expression**: An expression every evaluation of which produces a value in the same initial state: a pure expression, as the `purity` spec defines it (Requirement 6.4). Not the same as constant -- the value may be mutable -- nor as readonly alone, which forbids writes but not reads of state that can change.

## Requirements

### Requirement 1: Syntax

**User Story:** As a Mist developer, I want `default` to be a keyword usable for default blocks, default expressions, enum default variants and generic constraints.

#### Acceptance Criteria

1. THE Grammar SHALL define `default` as a keyword token, so the lexer never produces it as an identifier.
2. THE Grammar SHALL define a Default_Block as `default { DefaultFieldInitializer* }` and SHALL admit it as a member of a class or struct declaration, alongside constructors, methods, fields, properties, operators, cast operators, indexers and nested types.
3. THE Grammar SHALL define `DefaultFieldInitializer` in two forms: a Field_Override `fieldName = Expression`, and a Type_Binding `fieldName : TypeReference` optionally followed by a braced list of nested overrides `{ fieldName = Expression ... }`.
4. THE Parser SHALL reject a Default_Block inside an interface or trait declaration as a syntax error.
5. THE Grammar SHALL define a Default_Expression `default ( TypeReference )` and a Default_Literal `default` as primary expressions. WHEN `default` is followed by `(`, THE Parser SHALL parse a Default_Expression; WHEN it appears in expression position followed by neither `(` nor `{`, THE Parser SHALL parse a Default_Literal.
6. THE Grammar SHALL admit `default` as a modifier before an enum variant name (`default North = "N",`).
7. THE Grammar SHALL admit `default` as a constraint in a `where` clause (`where T : default`).

### Requirement 2: Pretty-Printing

**User Story:** As a Mist developer, I want formatting to preserve every `default` construct, so that round-tripping source loses nothing.

#### Acceptance Criteria

1. THE Pretty_Printer SHALL format a Default_Block with each entry on its own line inside the braces, an empty block as `default { }`, a Default_Expression as `default(TypeReference)`, a Default_Literal as `default`, an enum default variant with its `default` modifier, and a Default_Constraint as `default` in its constraint clause.
2. FOR ALL valid Mist ASTs containing these constructs, parsing then pretty-printing then parsing SHALL produce an equivalent AST.

### Requirement 3: Which Types Are Defaultable

**User Story:** As a Mist developer, I want defaultability to follow from how a type is declared, so that I only write a default block when I mean to give a type with constructors a default, or to bind an abstract field.

#### Acceptance Criteria

1. THE Compiler SHALL treat every primitive type (`int`, `uint`, `long`, `ulong`, `short`, `ushort`, `byte`, `sbyte`, `float`, `double`, `decimal`, `bool`, `char`) and `string` as Defaultable_Types, with the Zero_Initialization values as their defaults.
2. WHEN a class or struct declares no User_Constructor and no Default_Block, and every field has a Defaultable_Type or a Field_Initializer, THE Compiler SHALL classify it as defaultable with an Inferred_Default.
3. WHEN a class or struct declares no User_Constructor and has a Default_Block, and every field has a Defaultable_Type, a Field_Initializer or a Type_Binding, THE Compiler SHALL classify it as defaultable with an Explicit_Default.
4. WHEN a class or struct declares no User_Constructor and some field has none of a Defaultable_Type, a Field_Initializer or a Type_Binding, THE Compiler SHALL report an error naming the field and stating that either a constructor that assigns it or a Default_Block with a Type_Binding is required.
5. WHEN a class or struct declares a User_Constructor and a Default_Block, THE Compiler SHALL classify it as defaultable with an Explicit_Default, provided every field has a Defaultable_Type, a Field_Initializer or a Type_Binding; otherwise it SHALL report the field as in criterion 4.
6. WHEN a class or struct declares a User_Constructor and no Default_Block, THE Compiler SHALL classify it as a Non_Defaultable_Type and SHALL NOT report an error for that.
7. THE Compiler SHALL classify every interface and trait as a Non_Defaultable_Type; an abstract-typed field obtains a default only through a Type_Binding.
8. WHEN an enum declares exactly one variant with the `default` modifier, THE Compiler SHALL classify it as defaultable with that variant as its default. WHEN it declares none, THE Compiler SHALL classify it as a Non_Defaultable_Type, and SHALL NOT derive a default from the first variant, the underlying type's zero or any ordering. WHEN it declares more than one, THE Compiler SHALL report an error.
9. THE Compiler SHALL classify a union type containing `void` (including the optional `T?`, which is `T | void`) as defaultable with `void` as its default, and a union type not containing `void` as a Non_Defaultable_Type.
10. THE Compiler SHALL treat a type parameter as defaultable exactly when it carries a Default_Constraint.
11. WHEN a class or struct declares more than one Default_Block, THE Compiler SHALL report an error naming the type.

### Requirement 4: Construction and the Implicit Constructor

**User Story:** As a Mist developer, I want `new T()` and `default(T)` to relate predictably, so that I know which runs my constructor and which returns the default.

#### Acceptance Criteria

1. WHEN a class or struct declares no User_Constructor, THE Compiler SHALL generate an Implicit_Constructor, and `new T()` SHALL produce the type's default value -- Default_Block entries and Type_Bindings included.
2. WHEN a class or struct declares any User_Constructor, THE Compiler SHALL NOT generate an Implicit_Constructor; `new T()` SHALL resolve only against the User_Constructors, and SHALL be an error if none of them is parameterless.
3. WHEN a class or struct declares a parameterless User_Constructor and a Default_Block, `new T()` SHALL run that constructor and `default(T)` SHALL produce the Explicit_Default; the two SHALL NOT be conflated.
4. A Default_Expression or Default_Literal SHALL NOT invoke any User_Constructor of the type being defaulted.

### Requirement 5: The Default Value

**User Story:** As a Mist developer, I want the default of a type computed from its declaration, so that `default(T)` produces exactly what the declaration states.

#### Acceptance Criteria

1. THE Compiler SHALL give each field of a defaultable type its Field_Initializer's value where it has one, and its Zero_Initialization otherwise.
2. WHEN a field's type is itself a defaultable class or struct, THE Compiler SHALL give the field that type's default value.
3. WHEN a Default_Block contains a Field_Override for a field, THE Compiler SHALL use the override's value in place of the field's Field_Initializer or Zero_Initialization -- the override replaces the field's own answer rather than adding to it.
4. WHEN a Default_Block contains a Type_Binding for a field, THE Compiler SHALL give the field the default of the bound concrete type, and, where the binding has nested overrides, that default with the nested overrides applied. A nested override SHALL name a field of the bound type and SHALL be written in, and bound in, the scope of the type declaring the Default_Block.
5. THE Compiler SHALL compute defaults so that a type's default is available before any type holding it as a field is defaulted, and SHALL terminate on a cycle of field types, classifying a type whose default would be infinite as a Non_Defaultable_Type.
6. Two evaluations of `default(T)` SHALL NOT share any reference-typed instance: every class-typed field, at every depth, SHALL hold an instance of its own.
7. WHEN a type declares no User_Constructor and its Default_Block is empty, or every entry is a Field_Override whose value equals what the field would have without it, THE Compiler SHALL report an informational diagnostic that the block is redundant and can be removed. On a type with User_Constructors no Default_Block is redundant, however empty: it is what makes the type defaultable at all.

### Requirement 6: Defaults Are Consistent

**User Story:** As a Mist developer, I want every default of a type to start in the same state, so that `default(T)` -- and `new T()` on a type without constructors, which is the same value -- runs no code that changes anything and depends on nothing that can change.

```mist
public class Foo
{
    public int N;

    public Foo(int n) { N = n; }         // only sets its own fields
}

public class Blah
{
    public Foo Item = new Foo(7);        // consistent
}

var x = default(Blah);
var y = default(Blah);                   // x.Item and y.Item: two distinct Foos, both N == 7
```

#### Acceptance Criteria

1. THE Compiler SHALL require the Field_Initializer of every instance field of a defaultable class or struct to be a Consistent_Expression, and SHALL report an error naming the field and stating that a default must be consistent. The initializer of an auto-property is the Field_Initializer of its backing field.
2. THE Compiler SHALL require every Field_Override, and every nested override of a Type_Binding, to be a Consistent_Expression, and SHALL report an error naming the field otherwise.
3. A Field_Initializer of a Non_Defaultable_Type, or of a static field, is ordinary construction-time code and SHALL NOT be constrained by this requirement. Adding a Default_Block to a type with User_Constructors makes the type defaultable, and so brings its Field_Initializers under criterion 1.
4. A Consistent_Expression is a pure expression: THE Compiler SHALL accept it exactly when it would accept it as the body of a pure scope with no inputs, under the `purity` spec (Requirements 6 and 7). It writes nothing but what it creates, reads nothing that can change, and calls a pure member on anything, a `local readonly` or `local` member only on what it creates, and nothing else. `new Foo(7)` is one when `Foo(int)` is a pure constructor; `new List<int>()` is one because Standard declares that constructor pure.
5. Which members keep which promise -- inferred for a member with a Mist body, stated by the declaration for one without -- is the `purity` spec's (Requirements 8 and 9).
6. Consistent is not constant: the value MAY be mutable. Every class-typed value a Consistent_Expression produces SHALL be a fresh instance per evaluation (Requirement 5.6), so two defaults of a type start in the same state and are independent from then on.

### Requirement 7: Default Expressions

**User Story:** As a Mist developer, I want `default` to be checked where it is written, so that misuse is reported against my source.

#### Acceptance Criteria

1. WHEN a Default_Expression names a Defaultable_Type, THE Compiler SHALL accept it and give it that type.
2. WHEN a Default_Expression names a Non_Defaultable_Type, THE Compiler SHALL report an error naming the type and saying why it has no default: a class or struct with User_Constructors and no Default_Block, a field with no default and no binding, an enum with no designated default variant, a union without `void`, an interface or trait, or a type parameter without a Default_Constraint.
3. WHEN a Default_Expression names a type that cannot be resolved, THE Compiler SHALL report the type as undefined.
4. WHEN a Default_Literal appears where the target type is known -- a typed local declaration, a return, an argument, an assignment -- THE Compiler SHALL treat it as a Default_Expression of that type.
5. WHEN a Default_Literal appears where no target type is known, including `var x = default;`, THE Compiler SHALL report that the type of `default` cannot be determined.

### Requirement 8: Default Constraint

**User Story:** As a Mist developer, I want to require a defaultable type argument, so that generic code can use `default(T)`.

#### Acceptance Criteria

1. WHEN a type parameter carries a Default_Constraint, THE Compiler SHALL accept `default(T)` on it.
2. WHEN a generic type or method whose type parameter carries a Default_Constraint is instantiated with a Non_Defaultable_Type argument, THE Compiler SHALL report an error naming the type parameter, the constraint and the argument.
3. No interface or trait SHALL satisfy a Default_Constraint.

### Requirement 9: Inheritance

**User Story:** As a Mist developer, I want a derived type's default built on its base's, so that I handle only what the derived type adds or changes.

#### Acceptance Criteria

1. WHEN a derived class declares no User_Constructor, THE Compiler SHALL classify it as defaultable only if its base class is defaultable and its own fields satisfy Requirement 3; otherwise it SHALL report that the base class is not defaultable.
2. WHEN a derived class declares User_Constructors and no Default_Block, THE Compiler SHALL accept a Non_Defaultable base class without error.
3. WHEN a derived class declares User_Constructors and a Default_Block over a Non_Defaultable base class, THE Compiler SHALL report that the base class is not defaultable, because the inherited fields have no defaults to start from.
4. A derived type's default SHALL give inherited fields the base type's default values, and its own fields values per Requirement 5.
5. A derived type's Default_Block MAY override inherited fields that are visible to it. WHEN an entry names a field that is not visible to the declaring type -- a base class's private field -- THE Compiler SHALL report an error naming the field.
6. WHEN a Default_Block entry names a member the type does not declare or inherit, THE Compiler SHALL report an error naming it.

### Requirement 10: Constructors Assign Every Field

**User Story:** As a Mist developer, I want every constructor to leave every field assigned, so that no instance ever holds a value the type system says cannot exist.

#### Acceptance Criteria

1. A field with a Field_Initializer, or of a Defaultable_Type, SHALL be definitely assigned at the start of every User_Constructor, the default of a Defaultable_Type being injected where no initializer is written.
2. A field of a Non_Defaultable_Type with no Field_Initializer SHALL be assigned on every path through every User_Constructor; THE Compiler SHALL report an error naming a field left unassigned.
3. Reads of fields before assignment within a constructor are governed by the `definite-assignment` spec.

### Requirement 11: Arrays

**User Story:** As a Mist developer, I want array element initialization to follow one rule, so that no slot is observable without a value.

#### Acceptance Criteria

1. Array construction SHALL follow the `definite-assignment` spec's stampable-versus-initializer model, as the `stampability` spec extends it: a bare `new T[size]` requires a stampable element type, and any other element type is constructed from an index-to-value function or an array literal.
2. `Array<TValue, Length>` SHALL NOT require `TValue : default`; defaultability is neither necessary nor sufficient for bare array creation.

### Requirement 12: C# Emission

**User Story:** As a Mist developer, I want the emitted C# to preserve these semantics, so that the compiled program behaves as the declaration states.

#### Acceptance Criteria

1. WHEN a type is defaultable, THE C#_Backend SHALL emit a static factory `__CreateDefault()` on it that returns a new instance holding the type's default, and SHALL lower every Default_Expression and Default_Literal of that type to a call to it. WHEN a type is Non_Defaultable, THE C#_Backend SHALL emit no factory for it.
2. THE factory SHALL NOT invoke any User_Constructor. WHEN the type declares User_Constructors, THE C#_Backend SHALL reach the instance through a private constructor that no Mist source can name, so that it grants C# no construction Mist refuses.
3. WHEN the type declares no User_Constructor, THE C#_Backend SHALL emit the Implicit_Constructor as a public parameterless constructor producing the default, and MAY route the factory through it.
4. Every Field_Initializer SHALL run exactly once per construction and once per default -- C# runs field initializers in every constructor that does not chain to another, so a constructor the backend synthesizes SHALL NOT assign a field's own initializer again.
5. WHEN a type has a Type_Binding, THE C#_Backend SHALL build the field from the bound type's default and apply its nested overrides through a local of the bound type, then assign the field.
6. WHEN an enum has a designated default variant, THE C#_Backend SHALL emit its factory returning that variant.
7. WHEN a type parameter carries a Default_Constraint, THE C#_Backend SHALL supply what `default(T)` needs inside the generic body, the constraint having no C# equivalent; its enforcement belongs to the Compiler's analysis.

### Requirement 13: Editor Support

#### Acceptance Criteria

1. THE TextMate grammar SHALL highlight `default` as a keyword in expression and block position, as a modifier (`storage.modifier.mist`) before an enum variant, and as a constraint keyword in a `where` clause.

### Proof

The behavior above is exercised by `samples/DefaultKeyword`, `samples/OptionalDefaultability` and `samples/NewtypeClones` (the `Person` default block and its construction with `new`), and documented in the book's Default Keyword chapter (`book/src/advanced/default-keyword.md`).

## Appendix: Where the Superseded Requirements Went

| Earlier requirement | Now |
|---|---|
| default-keyword 1, 2, 3 | 1.1-1.5 |
| default-keyword 4 | 2 |
| default-keyword 5.1, 5.2 | 3.2, 3.3 |
| default-keyword 5.3 | 3.4 (an error only for a type with no User_Constructor) |
| default-keyword 5.4 | 3.11 |
| default-keyword 5.5 | dropped: how status is queried is an implementation matter |
| default-keyword 5.6 | 3.1 |
| default-keyword 6.1-6.6 | 5.1-5.4 |
| default-keyword 6.7, 6.8 | 9.4, 9.5 |
| default-keyword 6.9 (compile-time constants) | 6: consistent rather than constant, defined by 6.4 and 6.5 |
| default-keyword 6.10 | 5.5 |
| default-keyword 7 | 7, 8 |
| default-keyword 8 (layout and pointer model) | 5.2, 5.6; the layout itself is a backend matter and is dropped |
| default-keyword 9 | 9.5 |
| default-keyword 10 | 9 |
| default-keyword 11 | 5.7 |
| default-keyword 12.1-12.5, 12.7-12.9 | 12.1, 12.2, 12.5, 5.6 |
| default-keyword 12.6 (private parameterless constructor) | 12.2, 12.3: private only where User_Constructors exist; otherwise the public Implicit_Constructor |
| default-keyword 13 | folded into the criteria that report each error (13.4 is 6.1, 6.2) |
| default-keyword 14 | 13 |
| default-keyword 15 (CSHARP_FEATURES.md) | dropped: delivered |
| optional-defaultability 1 | 3.4-3.6, 7.2 |
| optional-defaultability 2 | 4.1, 4.2 |
| optional-defaultability 3 | 10 |
| optional-defaultability 4 | 3.2, 3.3, 3.5, 3.6, 4 |
| optional-defaultability 5 | 1.7, 2, 3.10, 8 |
| optional-defaultability 6 | 1.6, 3.8, 12.6 |
| optional-defaultability 7 | 3.9 |
| optional-defaultability 8 | 3.7, 8.3 |
| optional-defaultability 9 (Array requires `TValue : default`) | 11: replaced by `definite-assignment` |
| optional-defaultability 10 | 9.1-9.3 |
| optional-defaultability 11 | folded into the criteria that report each error |
| optional-defaultability 12 | 12 |
| optional-defaultability 13 | 13 |
| optional-defaultability 14, 15 (sample, book) | Proof section |