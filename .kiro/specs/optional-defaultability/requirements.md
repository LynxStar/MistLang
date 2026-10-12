# Requirements Document

> **Superseded** by [`defaultability`](../defaultability/requirements.md) (2026-09-26), which states the rules as they now stand and governs wherever the two differ. This document is kept so references to it by requirement number still resolve; the new spec's appendix maps each requirement here to where it now lives.

## Introduction

This document specifies the requirements for redesigning Mist's defaultability semantics so that types are **no longer required** to be defaultable. Currently the compiler enforces that every concrete type must have a meaningful default value. This spec removes that enforcement and introduces a clear separation between:

- **Default (zero value)**: The type has a meaningful default value. No constructor runs — it is pure data stamping from a precomputed template. The value is deterministic and side-effect-free.
- **Constructor Required (`new()`)**: The type must be explicitly constructed. A constructor is behavior — it can perform IO, fail, and depend on external state.

The key change is that types CAN now be non-defaultable without triggering a compiler error. However, using `default(T)` on a non-defaultable type remains an error. Types without any user-defined constructor must still be defaultable (since there is no other way to initialize them). A new definite-assignment analysis ensures all fields are assigned by the end of any constructor body. A new generic constraint `where T : default` enables generic code to require defaultability.

This spec builds on the existing `default-keyword` spec and modifies its enforcement semantics.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST, including the `ComputeDefaultability` pass that performs topological defaultability analysis.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Emitter**: The C# code emission component (`CSharpEmitter` / `CSharpSourceWriter`) that translates the Mist semantic model into C# source files.
- **Defaultable_Type**: A type that supports the `default` operator. A type is defaultable if it has an inferred default (all fields are defaultable types) or an explicit `default {}` block. Primitive types and `string` are intrinsically defaultable (string defaults to `""`).
- **Non_Defaultable_Type**: A type that does not support the `default` operator. This is a valid state for types that have user-defined constructors.
- **Default_Block**: A `default { ... }` member declaration inside a class or struct that explicitly provides the type's default value. Its presence makes the type defaultable regardless of whether constructors exist.
- **Constructor**: A named constructor (e.g., `public ClassName(...)`) that defines behavioral initialization logic. Can perform IO, fail, and depend on external state.
- **Implicit_Constructor**: A compiler-generated parameterless constructor that returns the default value. Only generated for types without any user-defined constructor.
- **Definite_Assignment_Analysis**: A compiler analysis that verifies every field of a type is assigned a value by the end of each constructor body.
- **Default_Constraint**: A generic constraint written `where T : default` that requires the type argument to be a defaultable type.
- **ClassType**: The semantic model representation of a user-defined class type.
- **StructType**: The semantic model representation of a user-defined struct type.
- **EnumType**: The semantic model representation of a user-defined enum type.
- **InterfaceType**: The semantic model representation of a user-defined interface type.
- **TraitType**: The semantic model representation of a user-defined trait type.
- **Union_Type**: A type formed by the union of two or more types using the `|` operator.
- **Void_Type**: The unit type in Mist, representing the absence of a value.

## Requirements

### Requirement 1: Remove Mandatory Defaultability Enforcement

**User Story:** As a Mist developer, I want types to be allowed to not have a default value, so that I can model types where no meaningful zero value exists without compiler errors.

#### Acceptance Criteria

1. WHEN a class or struct has at least one user-defined constructor and no `default {}` block, THE Semantic_Model_Builder SHALL classify the type as Non_Defaultable without emitting a diagnostic error.
2. WHEN a class or struct has at least one user-defined constructor, has no `default {}` block, and has fields with non-defaultable types (interface or trait fields without type bindings), THE Semantic_Model_Builder SHALL classify the type as Non_Defaultable without emitting a diagnostic error.
3. WHEN a class or struct has no user-defined constructor and has at least one field whose type is non-defaultable, THE Semantic_Model_Builder SHALL emit a diagnostic error indicating that either a constructor or a `default {}` block with a type binding is required.
4. WHEN `default(T)` is used on a Non_Defaultable_Type, THE Type_Checker SHALL emit the existing `MIST_DEFAULT_NOT_SUPPORTED` diagnostic error.

### Requirement 2: Implicit Constructor for Constructor-less Types

**User Story:** As a Mist developer, I want types without any explicit constructor to still be constructible via an implicit parameterless constructor, so that existing code that relies on `new T()` continues to work.

#### Acceptance Criteria

1. WHEN a class or struct has no user-defined constructor and is defaultable, THE Compiler SHALL generate an implicit parameterless constructor that returns the type's default value.
2. WHEN a class or struct has at least one user-defined constructor, THE Compiler SHALL NOT generate an implicit parameterless constructor.
3. WHEN a class or struct has no user-defined constructor, THE Compiler SHALL require the type to be defaultable (since the implicit constructor must return the default value).

### Requirement 3: Definite Assignment Analysis in Constructors

**User Story:** As a Mist developer, I want the compiler to verify that all fields are assigned by the end of every constructor body, so that no instance can exist with uninitialized fields.

#### Acceptance Criteria

1. WHEN a constructor body completes, THE Type_Checker SHALL verify that every field declared by the type (and not inherited with a default) has been assigned a value through one of: a field initializer, a defaultable type's implicit default injection, or an explicit assignment in the constructor body.
2. WHEN a field has a field initializer (e.g., `int x = 5;`), THE Type_Checker SHALL consider that field definitely assigned at the start of every constructor body.
3. WHEN a field's type is defaultable and the field has no explicit initializer, THE Type_Checker SHALL consider that field definitely assigned at the start of every constructor body (the default value is synthetically injected, equivalent to `type x = default;`).
4. WHEN a field's type is non-defaultable and the field has no explicit initializer, THE Type_Checker SHALL require the field to be explicitly assigned in every constructor body.
5. IF a constructor body completes without assigning a non-defaultable field that has no initializer, THEN THE Type_Checker SHALL emit a diagnostic error naming the unassigned field and stating that all fields must be assigned before the constructor completes.
6. THE Type_Checker SHALL perform the definite assignment check across all control-flow paths within the constructor body (every path from constructor entry to constructor exit must assign the field).

### Requirement 4: Default Block Interaction with Constructors

**User Story:** As a Mist developer, I want to be able to supply both constructors and a `default {}` block on the same type, so that the type is both constructible (via `new()`) and defaultable (via `default(T)`).

#### Acceptance Criteria

1. WHEN a type has both user-defined constructors and an explicit `default {}` block, THE Semantic_Model_Builder SHALL classify the type as defaultable (Explicit status).
2. WHEN a type has user-defined constructors but no `default {}` block, THE Semantic_Model_Builder SHALL classify the type as Non_Defaultable.
3. WHEN a type has no user-defined constructors and all fields are defaultable, THE Semantic_Model_Builder SHALL classify the type as defaultable (Inferred status) and generate an implicit parameterless constructor.
4. WHEN a type has no user-defined constructors, has a `default {}` block, and all fields are defaultable or bound in the default block, THE Semantic_Model_Builder SHALL classify the type as defaultable (Explicit status) and generate an implicit parameterless constructor.

### Requirement 5: Default Constraint for Generics

**User Story:** As a Mist developer, I want to constrain generic type parameters with `where T : default`, so that generic code can safely use `default(T)` only when the type argument is known to be defaultable.

#### Acceptance Criteria

1. THE Grammar SHALL recognize `default` as a valid constraint in a generic constraint clause (e.g., `where T : default`).
2. THE Parser SHALL parse `default` in a constraint position and produce a `DefaultConstraintConstraintNode` AST node.
3. THE Semantic_Model_Builder SHALL record the `default` constraint on the type parameter's constraint set.
4. WHEN `default(T)` is used on a type parameter that has a `default` constraint, THE Type_Checker SHALL treat the expression as valid.
5. WHEN `default(T)` is used on a type parameter that does NOT have a `default` constraint, THE Type_Checker SHALL emit the existing `MIST_DEFAULT_ON_TYPE_PARAMETER` diagnostic error.
6. WHEN a generic type or method with a `where T : default` constraint is instantiated with a type argument that is not defaultable, THE Type_Checker SHALL emit a diagnostic error indicating that the type argument does not satisfy the `default` constraint.
7. THE Pretty_Printer SHALL format the `default` constraint as `default` within the constraint clause.
8. FOR ALL valid Mist ASTs containing `default` constraints, parsing then pretty-printing then parsing SHALL produce an equivalent AST (round-trip property).

### Requirement 6: Enum Defaultability

**User Story:** As a Mist developer, I want enum defaultability to be explicit rather than implicit, so that there is no ambiguity about which variant is the default.

#### Acceptance Criteria

1. THE Compiler SHALL NOT implicitly derive a default value for enum types (no guessing from the first variant, the backing type's zero value, or positional ordering).
2. THE Grammar SHALL recognize `default` as a modifier on an enum variant (e.g., `default North,`). The `default` keyword appears before the variant name in the enum member list.
3. WHEN an enum type declares a variant with the `default` modifier, THE Semantic_Model_Builder SHALL classify the enum as defaultable with the designated variant as its default value.
4. WHEN an enum type does not have any variant with the `default` modifier, THE Semantic_Model_Builder SHALL classify the enum as Non_Defaultable.
5. WHEN `default(EnumType)` is used on an enum that has a designated default variant, THE Type_Checker SHALL treat the expression as valid and assign it the enum type.
6. WHEN `default(EnumType)` is used on an enum that does NOT have a designated default variant, THE Type_Checker SHALL emit the `MIST_DEFAULT_NOT_SUPPORTED` diagnostic error.
7. WHEN an enum declares more than one variant with the `default` modifier, THE Compiler SHALL emit a diagnostic error indicating that only one variant may be designated as the default.

### Requirement 7: Union Type Defaultability

**User Story:** As a Mist developer, I want union types that include `void` to be defaultable with `void` as the default value, so that optional types (`T?` sugar for `T | void`) have a natural empty state without requiring explicit construction.

#### Acceptance Criteria

1. WHEN a union type includes `void` as one of its constituents (e.g., `T | void` or the sugar `T?`), THE Compiler SHALL classify the union type as defaultable with `void` as the default value.
2. WHEN a union type does NOT include `void` as one of its constituents, THE Compiler SHALL classify the union type as Non_Defaultable.
3. WHEN `default(T?)` is used (where `T?` desugars to `T | void`), THE Type_Checker SHALL treat the expression as valid and produce the `void` value.
4. WHEN `default(A | B)` is used on a union that does not include `void`, THE Type_Checker SHALL emit the `MIST_DEFAULT_NOT_SUPPORTED` diagnostic error.

### Requirement 8: Interface and Trait Non-Defaultability

**User Story:** As a Mist developer, I want interfaces and traits to never be directly defaultable, so that the type system enforces that concrete implementations are always required.

#### Acceptance Criteria

1. THE Compiler SHALL classify all InterfaceType instances as Non_Defaultable.
2. THE Compiler SHALL classify all TraitType instances as Non_Defaultable.
3. WHEN `default(InterfaceType)` is used, THE Type_Checker SHALL emit the existing `MIST_DEFAULT_ON_INTERFACE` diagnostic error.
4. WHEN `default(TraitType)` is used, THE Type_Checker SHALL emit the existing `MIST_DEFAULT_ON_TRAIT` diagnostic error.
5. WHEN a `default` constraint is used on a type parameter, THE Compiler SHALL NOT allow an interface or trait type to satisfy that constraint.

### Requirement 9: Array Defaultability Constraint

**User Story:** As a Mist developer, I want `Array<TValue, Length>` to require `where TValue : default`, so that array slot initialization is well-defined and I get a clear error when trying to create arrays of non-defaultable types.

#### Acceptance Criteria

1. THE Standard Library `Array<TValue, Length>` type SHALL declare `where TValue : default` as a constraint on the `TValue` type parameter.
2. WHEN a developer instantiates `Array<T, N>` where `T` is not defaultable, THE Type_Checker SHALL emit a diagnostic error indicating that `T` does not satisfy the `default` constraint required by Array.
3. WHEN a developer uses `T?` (which is `T | void`, a defaultable union type) as the element type of an Array, THE Type_Checker SHALL accept the instantiation because `T | void` satisfies the `default` constraint.

### Requirement 10: Inheritance and Defaultability

**User Story:** As a Mist developer, I want the relationship between base and derived type defaultability to be well-defined, so that derived types correctly inherit or override defaultability status.

#### Acceptance Criteria

1. WHEN a derived class has no user-defined constructors and its base class is defaultable, THE Semantic_Model_Builder SHALL allow the derived class to be defaultable if all of its own new fields are also defaultable.
2. WHEN a derived class has no user-defined constructors and its base class is NOT defaultable, THE Semantic_Model_Builder SHALL classify the derived class as Non_Defaultable and emit the existing `MIST_DEFAULT_BASE_NOT_DEFAULTABLE` diagnostic error (since the implicit constructor requires base defaultability).
3. WHEN a derived class has user-defined constructors and its base class is NOT defaultable, THE Semantic_Model_Builder SHALL allow the derived class to be Non_Defaultable without error (the constructor handles initialization).
4. WHEN a derived class has user-defined constructors and an explicit `default {}` block, but its base class is NOT defaultable, THE Semantic_Model_Builder SHALL emit the `MIST_DEFAULT_BASE_NOT_DEFAULTABLE` diagnostic error (a derived default still requires the base to be defaultable because inherited fields need base defaults).
5. THE Semantic_Model_Builder SHALL continue to process defaultability in topological order (base-before-derived) as currently implemented.

### Requirement 11: Diagnostic Messages for Optional Defaultability

**User Story:** As a Mist developer, I want clear error messages when defaultability rules are violated, so that I can understand whether I need a constructor, a default block, or a type constraint.

#### Acceptance Criteria

1. WHEN a type has no user-defined constructor and has a non-defaultable field without a type binding, THE Compiler SHALL emit a diagnostic error stating: the field's type is not defaultable, and either a constructor must be added to assign the field, or a `default {}` block with a type binding must be provided.
2. WHEN a constructor body does not assign a non-defaultable field, THE Compiler SHALL emit a diagnostic error naming the field and stating that all fields must be definitely assigned before the constructor completes.
3. WHEN a type argument does not satisfy a `default` constraint, THE Compiler SHALL emit a diagnostic error naming the type parameter, the constraint, and the type argument that does not satisfy it.
4. WHEN an enum has no designated default variant and `default(EnumType)` is used, THE Compiler SHALL emit a diagnostic error stating that the enum has no designated default variant and suggesting the `default VariantName` syntax on a variant.
5. WHEN a union type without `void` is used with the `default` operator, THE Compiler SHALL emit a diagnostic error stating that only union types containing `void` are defaultable.

### Requirement 12: C# Code Emission Changes

**User Story:** As a Mist developer, I want the transpiled C# code to correctly reflect the new optional defaultability semantics, so that non-defaultable types work correctly at runtime.

#### Acceptance Criteria

1. WHEN a type is classified as Non_Defaultable, THE Emitter SHALL NOT emit a `__CreateDefault()` factory method for that type.
2. WHEN a type is defaultable (Inferred or Explicit), THE Emitter SHALL continue to emit the `__CreateDefault()` factory method as currently implemented.
3. WHEN a type has user-defined constructors and is Non_Defaultable, THE Emitter SHALL emit the constructors as normal C# constructors without any implicit default logic.
4. WHEN a type has user-defined constructors and is also defaultable (via an explicit `default {}` block), THE Emitter SHALL emit both the constructors and the `__CreateDefault()` factory method.
5. WHEN a `default` constraint appears on a type parameter, THE Emitter SHALL emit a C# comment documenting the constraint (since C# has no direct equivalent for a `default` constraint beyond `where T : struct` or `new()`). The actual enforcement occurs at compile time in the Mist type checker.
6. WHEN an enum has a designated default variant, THE Emitter SHALL emit the `__CreateDefault()` factory method returning the designated variant value.

### Requirement 13: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want the `default` modifier on enum variants to be syntax-highlighted correctly, so that I get the same visual cues as other modifiers.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `default` as a modifier keyword when it appears before an enum variant name (scope `storage.modifier.mist`).
2. THE existing highlighting of `default` as a keyword in expression contexts (e.g., `default(T)`, bare `default`) SHALL remain unchanged.
3. THE TextMate grammar SHALL recognize `default` in constraint position (`where T : default`) with the same scope as other constraint keywords.

### Requirement 14: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating optional defaultability, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/OptionalDefaultability/Program.mist`.
2. The sample SHALL demonstrate: types with constructors but no default (non-defaultable types), types with both constructors and a `default {}` block, types with no constructors (implicitly defaultable), the `where T : default` generic constraint, enum defaultability with the `default` variant modifier, union type defaultability (`T?` and `T | void`), definite assignment in constructors for non-defaultable fields, and arrays requiring `TValue : default`.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `OptionalDefaultability.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// OptionalDefaultability — Demonstrates the optional defaultability system.

using Standard;

namespace OptionalDefaultability;

// ─── Non-defaultable type: has constructor, no default block ─────────────────
// PROOF: Compiler does NOT generate __CreateDefault for this type
// PROOF: Definite assignment passes because constructor assigns all fields
public class DatabaseConnection
{
    public string ConnectionString;
    public int Timeout;

    public DatabaseConnection(string connStr, int timeout)
    {
        this.ConnectionString = connStr;
        this.Timeout = timeout;
    }
}

// ─── Type with both constructor AND default block ────────────────────────────
// PROOF: default(Config) uses the default block values
// PROOF: new Config("prod") uses the constructor
// PROOF: Both paths produce fully initialized instances
public class Config
{
    public string Environment;
    public int MaxRetries;

    public Config(string env)
    {
        this.Environment = env;
        this.MaxRetries = 3;
    }

    default
    {
        Environment = "development"
        MaxRetries = 1
    }
}

// ─── Type with no constructor: implicitly defaultable ────────────────────────
// PROOF: Has implicit parameterless constructor
// PROOF: default(Point) produces zero-initialized fields
// PROOF: new Point() also produces zero-initialized fields (same as default)
public class Point
{
    public int X;
    public int Y;
}

// ─── Enum with explicit default variant modifier ─────────────────────────────
// PROOF: default(Direction) returns North because it has the default modifier
// PROOF: The default modifier is a variant-level annotation, not a separate block
public enum Direction : string
{
    default North = "N",
    South = "S",
    East = "E",
    West = "W"
}

// ─── Enum without default variant: non-defaultable ───────────────────────────
// PROOF: This enum cannot be used with default(Priority)
// PROOF: Must be explicitly constructed by naming a variant
public enum Priority : int
{
    Low = 1,
    Medium = 2,
    High = 3
}

// ─── Generic function with default constraint ────────────────────────────────
// PROOF: where T : default allows default(T) inside the body
// PROOF: Can only be called with defaultable type arguments
public class Factory
{
    public static T Create<T>() where T : default
    {
        return default(T);
    }
}

// ─── Union with void is defaultable (T? sugar) ──────────────────────────────
// PROOF: int? is defaultable because it contains void
// PROOF: default(int?) produces the void value
public class OptionalHolder
{
    public int? MaybeValue;
}

// ─── Defaults through generics ───────────────────────────────────────────────
// PROOF: a field of type T takes T's own default, default block included, under new, default(T),
//        new T() and new T(int); `where T : default` builds a fresh one on every default(T)
public class Box<T>
{
    public T Value;
}

public class Widget
{
    public int Value;

    default {
        Value = 7
    }
}

public class Creator<T> where T : new()
{
    public T Create()
    {
        return new T();
    }
}

public class Builder<T> where T : new(int)
{
    public T Create(int value)
    {
        return new T(value);
    }
}

public class Initializing<T> where T : Widget, new()
{
    public T Create(int value)
    {
        return new T() { Value = value };
    }
}

public class Defaults<T> where T : default
{
    public T Get()
    {
        return default(T);
    }
}

public class Chosen<T> where T : default
{
    public int Value;
    public T Child;

    public Chosen(int value)
    {
        Value = value;
        Child = default(T);
    }

    public T Get()
    {
        return default(T);
    }

    default {
        Value = 4
    }
}

// ─── Default block type binding ──────────────────────────────────────────────
// PROOF: an abstract-typed field has no default of its own, so the block names the
//        concrete type to build for it: `field : ConcreteType`
// PROOF: the braced form also overrides that concrete type's own fields
// PROOF: two fields of the SAME abstract type bind to DIFFERENT concrete types, so
//        the binding is per-field and not per-abstract-type
// PROOF: a binding with no braces falls back to the bound type's own defaults
public abstract Shape
{
    string Describe();
}

public class Circle : Shape
{
    public int Radius;
    public string Label;

    public string Describe()
    {
        return $"Circle(Radius={Radius}, Label={Label})";
    }
}

public class Square : Shape
{
    public int Side;

    public string Describe()
    {
        return $"Square(Side={Side})";
    }
}

public class Scene
{
    public int Layers;
    public Shape Primary;
    public Shape Secondary;

    // A braceless binding must come LAST. Putting a further entry after one makes the
    // classic pipeline collect that entry as a nested override of the bound type as well
    // as a sibling, so `Layers = 3` after `Secondary : Square` emits
    // `__temp_Secondary.Layers = 3` and fails to compile. The parse is correct -- the
    // entry is a sibling -- so this is nested-override collection over-reaching, and the
    // shape below avoids it rather than relying on it.
    default {
        Layers = 3
        Primary : Circle { Radius = 7 Label = "primary" }
        Secondary : Square
    }
}

// ─── Array stampable-vs-initializer model ────────────────────────────────────
// PROOF: int[] works because int is stampable (primitive) — bare new int[N] accepted
// PROOF: Point[] requires initializer construction because Point is a class (reference type)
public class ArrayDemo
{
    public static void ShowArrays()
    {
        var ints = new int[3];
        Console.WriteLine($"  ints[0]={ints[0]} ints[1]={ints[1]} ints[2]={ints[2]}");

        var points = new Array<Point, 2>(i => new Point());
        Console.WriteLine($"  points[0].X={points[0].X} points[0].Y={points[0].Y}");
    }
}

// ─── Entry point ─────────────────────────────────────────────────────────────

public class Program
{
    public static void Main(string[] args)
    {
        RunDemo();
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Optional Defaultability Sample ===");

        // 1. Non-defaultable type requires constructor
        Console.WriteLine("--- Non-Defaultable Type ---");
        var db = new DatabaseConnection("Server=localhost", 30);
        Console.WriteLine($"  conn={db.ConnectionString} timeout={db.Timeout}");

        // 2. Type with both constructor and default block
        Console.WriteLine("--- Constructor + Default Block ---");
        var prodCfg = new Config("production");
        Config devCfg = default(Config);
        Console.WriteLine($"  prod: env={prodCfg.Environment} retries={prodCfg.MaxRetries}");
        Console.WriteLine($"  dev: env={devCfg.Environment} retries={devCfg.MaxRetries}");

        // 3. Implicitly defaultable type (no constructor)
        Console.WriteLine("--- Implicitly Defaultable ---");
        Point p = default(Point);
        var p2 = new Point();
        Console.WriteLine($"  default: x={p.X} y={p.Y}");
        Console.WriteLine($"  new: x={p2.X} y={p2.Y}");

        // 4. Enum with explicit default variant
        Console.WriteLine("--- Enum Default Variant ---");
        Direction dir = default(Direction);
        Console.WriteLine($"  direction={dir}");

        // 5. Generic default constraint
        Console.WriteLine("--- Generic Default Constraint ---");
        var pt = Factory.Create<Point>();
        Console.WriteLine($"  factory point: x={pt.X} y={pt.Y}");

        // 6. Optional (T?) is defaultable via void constituent
        Console.WriteLine("--- Optional Defaultability ---");
        OptionalHolder holder = default(OptionalHolder);
        Console.WriteLine($"  maybeValue={holder.MaybeValue}");

        // 7. Arrays — stampable vs. initializer construction
        Console.WriteLine("--- Array Stampability ---");
        ArrayDemo.ShowArrays();

        // 8. Default block type binding — abstract-typed fields bound to concrete types
        Console.WriteLine("--- Default Block Type Binding ---");
        Scene scene = default(Scene);
        Console.WriteLine($"  layers={scene.Layers}");
        Console.WriteLine($"  primary={scene.Primary.Describe()}");
        Console.WriteLine($"  secondary={scene.Secondary.Describe()}");

        Console.WriteLine("--- Defaults Through Generics ---");
        var fresh = new Box<Widget>();
        var other = default(Box<Widget>);
        fresh.Value.Value = 9;
        Console.WriteLine($"  new Box<Widget>(): {fresh.Value.Value} default(Box<Widget>): {other.Value.Value}");
        var creator = new Creator<Box<Widget>>();
        Console.WriteLine($"  new T() at Box<Widget>: {creator.Create().Value.Value}");
        var built = new Builder<Chosen<Widget>>().Create(11);
        Console.WriteLine($"  new T(11) at Chosen<Widget>: {built.Value} child={built.Child.Value}");
        var initialized = new Initializing<Widget>().Create(13);
        Console.WriteLine($"  new T() with initializer: {initialized.Value}");
        var defaults = new Defaults<Widget>();
        var first = defaults.Get();
        first.Value = 12;
        Console.WriteLine($"  default(T) twice: {first.Value} then {defaults.Get().Value}");
        var chosen = new Chosen<string>(8);
        var zero = default(Chosen<string>);
        Console.WriteLine($"  constructed: {chosen.Value} defaulted: {zero.Value} default(string): [{chosen.Get()}]");

        Console.WriteLine("Done!");
    }
}
```

**Expected output:**
```
=== Optional Defaultability Sample ===
--- Non-Defaultable Type ---
  conn=Server=localhost timeout=30
--- Constructor + Default Block ---
  prod: env=production retries=3
  dev: env=development retries=1
--- Implicitly Defaultable ---
  default: x=0 y=0
  new: x=0 y=0
--- Enum Default Variant ---
  direction=North
--- Generic Default Constraint ---
  factory point: x=0 y=0
--- Optional Defaultability ---
  maybeValue=Void
--- Array Stampability ---
  ints[0]=0 ints[1]=0 ints[2]=0
  points[0].X=0 points[0].Y=0
--- Default Block Type Binding ---
  layers=3
  primary=Circle(Radius=7, Label=primary)
  secondary=Square(Side=0)
--- Defaults Through Generics ---
  new Box<Widget>(): 9 default(Box<Widget>): 7
  new T() at Box<Widget>: 7
  new T(11) at Chosen<Widget>: 11 child=7
  new T() with initializer: 13
  default(T) twice: 12 then 7
  constructed: 8 defaulted: 4 default(string): []
Done!
```

### Requirement 15: Book Documentation

**User Story:** As a Mist developer, I want the language book to document optional defaultability semantics, so that I can learn when types are defaultable, when they require constructors, and how the `default` constraint works.

#### Acceptance Criteria

1. THE book SHALL update the existing `default-keyword` section under Advanced Features to reflect the new optional defaultability semantics.
2. THE updated chapter SHALL cover: the distinction between `default` (data) and `new()` (behavior), types that are defaultable vs. non-defaultable, how constructor-less types are implicitly defaultable, how types with constructors opt out of defaultability unless they supply a `default {}` block, the `where T : default` generic constraint, enum default variant designation with the `default` modifier, union type defaultability rules (`T | void` is defaultable, other unions are not), and the definite assignment guarantee for constructors.
3. THE chapter SHALL explain the design rationale: not all types have a meaningful zero value, and requiring defaultability universally was overly restrictive.
4. All code examples SHALL be compiled snippets following the Book Snippet Policy.
5. THE book's SUMMARY.md SHALL reference the updated section in its existing location (no move required if the section already exists).
