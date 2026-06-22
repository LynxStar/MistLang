# Requirements Document

## Introduction

This feature unifies Mist's `interface` and `trait` keywords into a single `abstract` keyword, creating a coherent contract mechanism with slot semantics. Members without bodies are required slots that implementers must fill. Members with bodies provide default implementations using Rust-style semantics (the default IS the implementation, regardless of how the value is referenced). Structural typing (the trait mechanism) is sunsetted; nominal typing and late binding (`with` blocks) are preserved under the new `abstract` keyword.

100% of existing `interface` behavior migrates to `abstract` with zero semantic change — this includes associated types, generic parameters, generic constraints, property signatures, and all existing interface inheritance patterns. The only change is the keyword.

## Glossary

- **Abstract_Type**: A type declared with the `abstract` keyword that defines a contract of slots and optional default implementations. Emits as a C# interface.
- **Slot**: A member declaration without a body in an abstract type. Concrete types implementing the abstract type must provide an implementation for every slot. Property slots cannot have default implementations.
- **Default_Implementation**: A method declaration with a body in an abstract type. Concrete types may provide their own implementation, but are not required to — the default is used if they don't. Only methods can have defaults; properties cannot.
- **Compiler**: The Mist compiler pipeline including lexing, parsing, semantic analysis, and C# transpilation.
- **Parser**: The Mist parser subsystem responsible for converting token streams into syntax tree nodes.
- **Emitter**: The C# transpiler backend that generates C# source code from the Mist semantic model.
- **With_Block**: A top-level declaration using `with Type : AbstractType { mappings }` syntax that establishes retroactive conformance between an existing type and an abstract type via explicit member mapping.
- **Nominal_Typing**: A type system where conformance is established by explicit declaration (`: AbstractType`) rather than structural member matching.
- **Ambient_Type_Provider**: The subsystem that exposes CLR types (from referenced assemblies) as Mist types, mapping C# virtual/abstract members to fillable slots.
- **Implementer**: A concrete class or struct that declares conformance to an abstract type via its base clause.
- **Type_Checker**: The analysis pass that verifies all slots are filled by implementers and validates type compatibility.
- **Slot_Filling**: The act of providing a concrete implementation for a slot, either via a method/property on the implementing type or via a `with` block mapping. Slot filling applies regardless of whether the slot has a default — filling always takes precedence over any default.

## Requirements

### Requirement 1: Abstract Type Declaration Parsing

**User Story:** As a Mist developer, I want to declare abstract types using the `abstract` keyword, so that I can define contracts with required slots and optional default implementations.

#### Acceptance Criteria

1. WHEN the Parser encounters the `abstract` keyword followed by a type name and a braced body, THE Parser SHALL produce an abstract type declaration syntax node.
2. WHEN an abstract type body contains a member signature without a body (no braces, terminated by semicolon), THE Parser SHALL classify that member as a slot.
3. WHEN an abstract type body contains a method declaration with a braced body, THE Parser SHALL classify that method as a default implementation.
4. THE Parser SHALL support method signatures and property signatures (`Type Name { get; }`, `Type Name { get; set; }`) as slot declarations within abstract type bodies. Field signatures are NOT supported — only properties.
5. WHEN an abstract type declaration includes generic type parameters (e.g., `abstract IContainer<T>`), THE Parser SHALL parse them identically to how generic parameters are parsed on class and struct declarations.
6. WHEN an abstract type declaration includes generic constraints (e.g., `where T : class`), THE Parser SHALL parse them identically to how they are parsed on class and struct declarations.
7. WHEN an abstract type declaration includes a base clause with other abstract types (e.g., `abstract IChild : IParent`), THE Parser SHALL parse it as abstract type inheritance.
8. WHEN an abstract type declaration includes associated type members (e.g., `type Output : IStringable;`), THE Parser SHALL parse them identically to how they are parsed in current interface declarations.
9. THE Parser SHALL allow an empty abstract type body (`abstract IMarker { }`) for marker type patterns.
10. WHEN a property signature appears with a body (attempting to provide a default for a property), THE Parser SHALL emit an error diagnostic: "Property slots cannot have default implementations. Only methods can have defaults."

### Requirement 2: Interface Keyword Removal

**User Story:** As a Mist developer, I want the `interface` keyword replaced by `abstract`, so that the language has a single unified contract mechanism.

#### Acceptance Criteria

1. WHEN the Parser encounters the `interface` keyword in source code, THE Compiler SHALL emit an error diagnostic indicating that `interface` has been removed.
2. THE error diagnostic for `interface` usage SHALL include the message text: "The 'interface' keyword has been removed. Use 'abstract' instead."
3. THE `interface` keyword SHALL NOT be valid Mist syntax. There is no deprecation period — it is a hard error.

### Requirement 3: Trait Keyword Removal

**User Story:** As a Mist developer, I want the `trait` keyword and structural typing to be removed, so that the language has a clear single-path contract system using nominal typing.

#### Acceptance Criteria

1. WHEN the Parser encounters the `trait` keyword in source code, THE Compiler SHALL emit an error diagnostic indicating that traits have been removed.
2. THE error diagnostic for `trait` usage SHALL include the message text: "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance."
3. WHEN a type has public members that match an abstract type's member signatures but does NOT explicitly declare conformance via `: AbstractType` or a `with` block, THE Type_Checker SHALL NOT automatically establish conformance.
4. THE Compiler SHALL require explicit nominal declaration (`: AbstractType`) or a `with` block for a type to implement an abstract type.
5. THE `trait` keyword SHALL NOT be valid Mist syntax. There is no deprecation period — it is a hard error.

### Requirement 4: Slot Satisfaction Checking

**User Story:** As a Mist developer, I want the compiler to verify that my concrete types fill all required slots, so that I get compile-time errors for missing implementations.

#### Acceptance Criteria

1. WHEN a concrete type declares `: AbstractType` and provides public method/property implementations matching every slot signature, THE Type_Checker SHALL accept the type as a valid implementer.
2. WHEN a concrete type declares `: AbstractType` and is missing one or more slot implementations, THE Type_Checker SHALL emit an error diagnostic listing each unfilled slot by name and signature.
3. WHEN an abstract type inherits from another abstract type (e.g., `abstract IChild : IParent`), THE Type_Checker SHALL require implementers of `IChild` to fill slots from both `IChild` and `IParent`.
4. WHEN a concrete type provides an implementation for a member that has a default implementation in the abstract type, THE Type_Checker SHALL accept the concrete type's implementation and use it instead of the default (slot filling takes precedence over defaults).
5. WHEN a concrete type does NOT provide an implementation for a method that has a default implementation, THE Type_Checker SHALL accept the type as valid (the default fills the method slot).
6. WHEN matching slot signatures, THE Type_Checker SHALL verify name, return type, and parameter types all match exactly.
7. WHEN an abstract type inherits from multiple parent abstract types that declare a slot with the same name and signature (diamond inheritance), THE Type_Checker SHALL require a single implementation from the implementer that satisfies both parent slots.

### Requirement 5: Default Implementation Semantics (Rust-style)

**User Story:** As a Mist developer, I want default implementations to behave consistently regardless of how I reference a value, so that there is no "lens" behavior.

#### Acceptance Criteria

1. WHEN a concrete type does not provide its own implementation for a default method, THE method SHALL be callable on the concrete type directly (not only through the abstract type reference).
2. WHEN a concrete type does not provide its own implementation for a default method, calling that method through the concrete type reference and through the abstract type reference SHALL produce identical behavior.
3. WHEN a concrete type provides its own implementation for a member with a default, THE concrete type's implementation SHALL be used regardless of whether the value is accessed through the concrete type or the abstract type reference.
4. WHEN a default implementation body references `this`, THE Compiler SHALL resolve member access on `this` against the abstract type's full member set (its own slots/defaults plus all inherited members from parent abstract types).
5. THE Emitter SHALL emit default implementations as C# default interface methods (DIM) in the generated interface, AND SHALL also emit a forwarding method on any concrete type that does not provide its own implementation, ensuring the method is accessible on the concrete type without casting.
6. WHEN a default implementation calls another default method on `this`, THE Compiler SHALL resolve the call against the full abstract type hierarchy and THE Emitter SHALL emit the call correctly through the forwarding mechanism.
7. Only methods can have default implementations. Property slots MUST always be filled by the implementer.

### Requirement 6: Late Binding via With Blocks

**User Story:** As a Mist developer, I want to use `with` blocks to adapt existing types to abstract types without modifying the original type, so that I can achieve retroactive conformance.

#### Acceptance Criteria

1. WHEN a `with` block declares `with TypeName : AbstractType { mappings }`, THE Compiler SHALL establish nominal conformance between that type and the abstract type.
2. WHEN a `with` block maps a source member to a slot using the `as` keyword (e.g., `Log as Write;`), THE Emitter SHALL generate an explicit interface implementation that delegates to the source member.
3. WHEN a `with` block references an abstract type that has default implementations, THE Compiler SHALL only require mappings for slots that the type does not already satisfy by name and signature. Default methods do not require mappings.
4. IF a `with` block references an undefined type or abstract type, THEN THE Compiler SHALL emit an error diagnostic identifying the undefined reference.
5. IF a `with` block maps a member whose type signature is incompatible with the slot's declared signature, THEN THE Compiler SHALL emit a type mismatch error diagnostic.
6. WHEN a type has conformance established via a `with` block, THE type SHALL be assignable to variables and parameters typed as the abstract type.
7. WHEN a `with` block maps a member to a slot that has a default implementation, THE mapping SHALL fill the slot — the mapped member is used instead of the default.
8. A type MAY have multiple `with` blocks establishing conformance to different abstract types. Each `with` block is independent.
9. WHEN a `with` block establishes conformance for a type that already declares `: AbstractType` in its base clause, THE Compiler SHALL emit an error diagnostic indicating redundant conformance.

### Requirement 7: C# Code Emission for Abstract Types

**User Story:** As a Mist developer, I want abstract types to emit as C# interfaces, so that the generated code interoperates with the .NET ecosystem.

#### Acceptance Criteria

1. THE Emitter SHALL emit each abstract type declaration as a C# `interface` declaration.
2. WHEN an abstract type contains only slots (no defaults), THE Emitter SHALL emit a standard C# interface with method/property signatures only.
3. WHEN an abstract type contains default implementations, THE Emitter SHALL emit a C# interface with default interface method bodies for each default.
4. WHEN a concrete type implements an abstract type, THE Emitter SHALL include the abstract type's interface name in the concrete type's C# inheritance list.
5. WHEN a slot is a property signature in the abstract type (e.g., `string Name { get; }`), THE Emitter SHALL emit it as a property signature on the C# interface.
6. WHEN a concrete type does not override a default implementation, THE Emitter SHALL emit a forwarding method on the concrete type that calls the interface's default implementation, ensuring the method is accessible without casting to the interface type.
7. WHEN an abstract type contains associated type declarations, THE Emitter SHALL emit them using the same mechanism currently used for interface associated types.

### Requirement 8: CLR Interop via Ambient Type Provider

**User Story:** As a Mist developer, I want C# abstract and virtual members from referenced assemblies to appear as fillable slots in Mist, so that I can use framework extension patterns naturally.

#### Acceptance Criteria

1. WHEN the Ambient_Type_Provider encounters a C# interface in a referenced assembly, THE Ambient_Type_Provider SHALL expose it as an abstract type in Mist with all interface members as slots.
2. WHEN the Ambient_Type_Provider encounters a C# class with `abstract` methods, THE Ambient_Type_Provider SHALL expose those abstract methods as required slots on the Mist type representation.
3. WHEN the Ambient_Type_Provider encounters a C# class with `virtual` methods that have implementations, THE Ambient_Type_Provider SHALL expose those virtual methods as slots with default implementations (fillable but not required).
4. WHEN a Mist type fills a slot that originated from a C# `abstract` or `virtual` method on a base class, THE Emitter SHALL generate the C# `override` keyword on the emitted method.
5. WHEN a Mist type does NOT fill a slot that originated from a C# `virtual` method, THE Emitter SHALL NOT emit any method for that slot (the base class's implementation is inherited normally).
6. THE distinction between Req 8 (CLR interop override) and Req 10 (Mist concrete hiding) SHALL be: `override` is emitted ONLY for filling slots that originate from CLR `abstract` or `virtual` members on base classes. Mist-to-Mist concrete class inheritance always uses hiding semantics with `new`. These are two separate mechanisms that do not conflict because they apply to different inheritance relationships (CLR base class slots vs Mist concrete base members).

### Requirement 9: Polymorphic Usage of Abstract Types

**User Story:** As a Mist developer, I want to use abstract types as variable types, parameter types, and return types, so that I can write polymorphic code.

#### Acceptance Criteria

1. WHEN a variable is declared with an abstract type (e.g., `IDescribable item`), THE Type_Checker SHALL accept assignment of any concrete type that declares `: IDescribable` or has conformance established via a `with` block.
2. WHEN a method parameter is typed as an abstract type, THE Type_Checker SHALL accept any argument whose type implements that abstract type.
3. WHEN member access is performed on an abstract-typed variable, THE Compiler SHALL resolve the access against the abstract type's declared members (both slots and defaults).
4. WHEN an abstract type inherits from another abstract type, THE Type_Checker SHALL accept assignment of an implementer of the child abstract type to a variable typed as the parent abstract type (transitive assignability).

### Requirement 10: Concrete Type Hiding Semantics

**User Story:** As a Mist developer, I want concrete type inheritance to continue using hiding semantics (not override), so that base type behavior is preserved when viewed through the base type lens.

#### Acceptance Criteria

1. WHEN a derived concrete type declares a method with the same name and signature as a base concrete type method, THE Compiler SHALL treat this as member hiding — the base method is still invoked when the value is accessed through the base type.
2. WHEN a derived concrete type hides a base member intentionally, THE Compiler SHALL require the `new` keyword on the hiding member to suppress the hiding warning.
3. IF a derived concrete type declares a member matching a base concrete type member without the `new` keyword, THEN THE Compiler SHALL emit a warning diagnostic: "'{TypeName}.{MemberName}' hides inherited member '{BaseTypeName}.{MemberName}'. Use 'new' if intentional."
4. WHEN the `new` keyword is present on a member that does NOT hide a base member, THE Compiler SHALL emit a warning diagnostic indicating `new` is unnecessary.
5. Hiding semantics apply ONLY to Mist-to-Mist concrete class inheritance. They do NOT apply to filling abstract type slots (which is slot-filling, not hiding) or to filling CLR virtual/abstract slots (which emits `override`).

#### Proof Code

```mist
using Standard;

namespace HidingDemo;

public class Base
{
    public string Speak()
    {
        return "base";
    }
}

public class Derived : Base
{
    // PROOF: new keyword required for intentional hiding
    new public string Speak()
    {
        return "derived";
    }
}

public class Program
{
    private static void AcceptBase(Base b)
    {
        // PROOF: base behavior preserved when viewed through base type
        Console.WriteLine($"  via Base: {b.Speak()}");
    }

    public static void RunDemo()
    {
        var d = new Derived();

        // PROOF: derived type sees its own method
        Console.WriteLine($"via Derived: {d.Speak()}");

        // PROOF: same object, but viewed as Base, calls Base.Speak()
        AcceptBase(d);
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
via Derived: derived
  via Base: base
```

### Requirement 11: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating abstract types, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/AbstractTypes/Program.mist`.
2. The sample SHALL demonstrate: abstract type declaration with slots only, abstract type with default implementations, property slots, concrete types filling all slots, concrete types relying on defaults, abstract type inheritance, polymorphic variable assignment, polymorphic parameter passing, late binding via `with` blocks, a concrete type overriding a default implementation, and a `with` block filling a slot that has a default.
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `AbstractTypes.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Sample Migration Plan

The following existing samples SHALL be migrated (keyword change from `interface`/`trait` to `abstract`, nominal conformance preserved):

| Sample | Action | Rationale |
|---|---|---|
| `samples/Interfaces/` | Migrate `interface` → `abstract` | Pure nominal contract usage |
| `samples/ShapeInterfaces/` | Migrate `interface` → `abstract` | Nominal contracts with properties |
| `samples/GenericTypes/` | Migrate `interface` → `abstract` | Generic interface declaration |
| `samples/VoidType/` | Migrate `interface` → `abstract` | Generic interface declaration |
| `samples/Properties/` | Migrate `interface` → `abstract` | Interface property declarations |
| `samples/GenericConstraints/` | Migrate `interface` → `abstract`, `trait` → `abstract` + add `: AbstractType` conformance | Mixed interface/trait usage |
| `samples/AssociatedTypes/` | Migrate `interface` → `abstract`, `trait` → `abstract` + add `: AbstractType` conformance | Associated types carry over fully |
| `samples/DependentTypes/` | Migrate `interface` → `abstract`, `trait` → `abstract` + add `: AbstractType` conformance | Dependent type params on interfaces/traits |
| `samples/NestedScoping/` | Migrate `interface` → `abstract`, `trait` → `abstract` | Nested types in interface/trait |
| `samples/StructuredContext/` | Migrate `interface` → `abstract`, `trait` → `abstract` + add `: AbstractType` conformance | Interface/trait with `with`-clause methods |
| `samples/Enums/` | Migrate `interface` → `abstract` | Interface usage in enum context |
| `samples/GenericTraits/` | Migrate `trait` → `abstract`; types using structural satisfaction need `: AbstractType` added OR `with` block retained | Uses both structural and `with`-block conformance |

The following samples SHALL be removed:

| Sample | Action | Rationale |
|---|---|---|
| `samples/Traits/` | Remove entirely | Exists solely to demonstrate structural typing, which is sunsetted |

Notes:
- `samples/GenericTraits/MemoryStream.mist` uses structural satisfaction (no explicit conformance declaration). It must be updated to either add `: Readable, Writable, ReadWriteStream` to the class declaration or use a `with` block.
- All other samples not listed above that do not use `interface` or `trait` keywords remain unchanged.

#### Proof Code

```mist
using Standard;

namespace AbstractTypes;

// PROOF: abstract keyword parses as a type declaration
// PROOF: members without bodies are slots (must be filled)
abstract IDescribable
{
    string Describe();
}

// PROOF: abstract type inheritance works
abstract IFullyDescribable : IDescribable
{
    string DetailedDescribe();
}

// PROOF: property slots on abstract types
abstract ILabeled
{
    string Label { get; }
}

// PROOF: abstract type with default implementation
abstract IGreeting
{
    string Name();

    // PROOF: default implementation has a body
    string Greet()
    {
        return $"Hello, I am {this.Name()}";
    }
}

// PROOF: concrete type fills required slots including property slot
public class Person : IDescribable, IGreeting, ILabeled
{
    private string name;
    private int age;

    public Person(string name, int age)
    {
        this.name = name;
        this.age = age;
    }

    public string Describe()
    {
        return $"Person: {this.name}, age {this.age}";
    }

    // PROOF: fills the Name() slot required by IGreeting
    public string Name()
    {
        return this.name;
    }

    // PROOF: fills the property slot from ILabeled
    public string Label { get; } = "person";

    // PROOF: does NOT provide Greet() — takes the default
}

// PROOF: concrete type that overrides a default
public class Robot : IFullyDescribable, IGreeting
{
    private string model;

    public Robot(string model)
    {
        this.model = model;
    }

    public string Describe()
    {
        return $"Robot: {this.model}";
    }

    public string DetailedDescribe()
    {
        return $"Robot model {this.model}, fully operational";
    }

    public string Name()
    {
        return this.model;
    }

    // PROOF: overrides the default Greet() with custom implementation
    public string Greet()
    {
        return $"Beep boop, I am {this.model}";
    }
}

// PROOF: late binding via with block (retroactive conformance)
public class Logger
{
    public string GetLabel()
    {
        return "SystemLogger";
    }
}

abstract INamed
{
    string Name();
}

with Logger : INamed
{
    GetLabel as Name;
}

// PROOF: with block filling a slot that has a default
abstract ITaggable
{
    string Tag();

    string DisplayTag()
    {
        return $"[{this.Tag()}]";
    }
}

public class Widget
{
    public string GetWidgetTag()
    {
        return "widget-tag";
    }

    public string GetWidgetDisplay()
    {
        return "<Widget>";
    }
}

with Widget : ITaggable
{
    GetWidgetTag as Tag;
    GetWidgetDisplay as DisplayTag;
}

public class Program
{
    // PROOF: abstract type as parameter type (polymorphism)
    private static void PrintDescription(IDescribable item)
    {
        Console.WriteLine($"  {item.Describe()}");
    }

    public static void RunDemo()
    {
        Console.WriteLine("=== Abstract type with slots only ===");
        var person = new Person("Alice", 30);
        Console.WriteLine(person.Describe());

        Console.WriteLine("=== Property slot filled ===");
        // PROOF: property slot accessible on concrete type
        Console.WriteLine($"  Label: {person.Label}");

        Console.WriteLine("=== Default implementation taken (Rust-style) ===");
        // PROOF: Greet() is callable directly on Person (no cast needed)
        Console.WriteLine(person.Greet());

        Console.WriteLine("=== Default implementation overridden ===");
        var robot = new Robot("T-800");
        Console.WriteLine(robot.Greet());

        Console.WriteLine("=== Polymorphic variable assignment ===");
        IDescribable describable = person;
        Console.WriteLine($"  {describable.Describe()}");

        Console.WriteLine("=== Transitive interface assignability ===");
        IDescribable robotAsDescribable = robot;
        Console.WriteLine($"  {robotAsDescribable.Describe()}");

        Console.WriteLine("=== Polymorphic parameter passing ===");
        PrintDescription(person);
        PrintDescription(robot);

        Console.WriteLine("=== Default via abstract-typed reference ===");
        // PROOF: same result whether called on concrete type or abstract reference
        IGreeting greetingRef = person;
        Console.WriteLine(greetingRef.Greet());

        Console.WriteLine("=== Late binding via with block ===");
        var logger = new Logger();
        INamed named = logger;
        Console.WriteLine($"  {named.Name()}");

        Console.WriteLine("=== With block overrides default ===");
        // PROOF: with block maps GetWidgetDisplay to DisplayTag slot,
        // overriding the default implementation
        var widget = new Widget();
        ITaggable taggable = widget;
        Console.WriteLine($"  Tag: {taggable.Tag()}");
        Console.WriteLine($"  DisplayTag: {taggable.DisplayTag()}");

        Console.WriteLine("Done!");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== Abstract type with slots only ===
Person: Alice, age 30
=== Property slot filled ===
  Label: person
=== Default implementation taken (Rust-style) ===
Hello, I am Alice
=== Default implementation overridden ===
Beep boop, I am T-800
=== Polymorphic variable assignment ===
  Person: Alice, age 30
=== Transitive interface assignability ===
  Robot: T-800
=== Polymorphic parameter passing ===
  Person: Alice, age 30
  Robot: T-800
=== Default via abstract-typed reference ===
Hello, I am Alice
=== Late binding via with block ===
  SystemLogger
=== With block overrides default ===
  Tag: widget-tag
  DisplayTag: <Widget>
Done!
```

### Requirement 12: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want `abstract` declarations to be syntax-highlighted correctly, so that I get the same visual cues as other type declarations.

#### Acceptance Criteria

1. THE TextMate grammar SHALL recognize `abstract` as a declaration keyword (scope `keyword.declaration.mist`).
2. THE TextMate grammar SHALL highlight the identifier following `abstract` as a type name (scope `entity.name.type.abstract.mist`).
3. THE TextMate grammar SHALL remove `trait` from the keyword list since the keyword is removed from the language.
4. THE TextMate grammar SHALL remove `interface` from the keyword list since the keyword is removed from the language.
5. THE book's syntax highlighting files (`book/theme/highlight.js` and `book/theme/mist-lang.js`) SHALL be updated to add `abstract` to the keyword list and remove `trait` and `interface`.

### Requirement 13: Book Documentation

**User Story:** As a Mist developer, I want the language book to document abstract types with slots and default implementations, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a chapter titled "Abstract Types" (replacing the current "Interfaces" chapter) covering: declaration syntax, slots, default implementations, property slots, abstract type inheritance, implementation by concrete types, polymorphic usage, and late binding via `with` blocks.
2. THE book chapter SHALL explain the semantic difference between slots (required) and defaults (optional), and that defaults use Rust-style semantics (no lens behavior).
3. THE book chapter SHALL explicitly state that only methods can have default implementations; property slots must always be filled by the implementer.
4. THE book chapter SHALL include compiled snippet examples demonstrating: a simple abstract type with slots only, an abstract type with property slots, an abstract type with method defaults, a concrete type filling all slots, a concrete type taking method defaults, a concrete type overriding a method default, abstract type inheritance, polymorphic usage, and `with` block late binding.
5. All code examples SHALL be compiled snippets following the Book Snippet Policy (`.mist` files in `book/snippets/` with corresponding `.cs` files).
6. THE book's `SUMMARY.md` SHALL reference the new "Abstract Types" chapter in the Object Model section.
7. THE current "Traits" chapter SHALL be removed or replaced with a brief note that structural typing has been sunsetted and will be reintroduced in a different form in the future.
8. THE chapter SHALL include a comparison table showing how `abstract` relates to C#'s `interface` and Rust's `trait`, highlighting what Mist borrows from each.
9. THE chapter SHALL document diamond inheritance behavior: when multiple parent abstract types declare the same slot, a single implementation from the concrete type satisfies all of them.
