# Design Document

## Overview

Three stages change, each in the role it already has. The ambient provider records a fact it already
computes but throws away. Stage 5 asks one new question of the finished model and refuses what fails
it. The emitter spells one word. No grammar, no parser, no new model kind: the slot model that abstract
types already use is extended to the CLR class members Requirement 25 of abstract-type-unification
always said were slots.

```
CLR metadata ──MethodProjector──▶ Method.SlotKind      (Stage 4 input, representation)
                                        │
            Native_Slot_Fills(SourceType) ──▶ Fill(filler, slot, ancestor)   (one pure function)
                   │                                   │
     NativeSlotAnalyzer / DeclarationShapeAnalyzer     MethodEmitter / PropertyEmitter / IndexerEmitter
     (Stage 5: not filled, sealed, shape, redeclare)   (Stage 6: "override", "sealed override")
```

## Model

### `NativeSlotKind`

A new enum in `Source/Mist/ProgramModel/Model/Members/`:

```csharp
public enum NativeSlotKind { None, Required, Defaulted, Sealed }
```

`Method`, `Property` and `Indexer` each gain `public NativeSlotKind SlotKind { get; init; }`, default
`None`. It sits beside `HasNativeBody`, which keeps its present meaning for interface members. The two
never overlap: `HasNativeBody` is set only for interface members, `SlotKind` only for class and struct
members (Requirement 1.3).

It is a property on the member, not a flag on `MemberModifiers`, because `MemberModifiers` "simply
records what was declared" and no Mist declaration says this. It is also not a sentinel attribute: the
sentinels live in the backend as "target-specific concepts", and whether a member is fillable is a
language fact Stage 5 judges, so it belongs in the model.

### Projection

`MethodProjector.Project` sets `SlotKind` from the `MethodInfo`:

| CLR flags | SlotKind |
|---|---|
| `IsAbstract` | `Required` |
| `IsVirtual && IsFinal`, not `NewSlot` (a `sealed override`) | `Sealed` |
| `IsVirtual && IsFinal && NewSlot` (a non-virtual implementation of an interface member) | `None` |
| `IsVirtual` otherwise (new slot or non-final override) | `Defaulted` |
| not virtual, or declaring type is an interface | `None` |

The `NewSlot` split was found when re-measuring at `af91eec2`: the CLR marks `Collection<T>.Add`, which
implements `ICollection<T>.Add` without being virtual in C#, `virtual final newslot`, and the
redeclaration probe of the requirements introduction expects it refused as `MIST0177`, not as a sealed
slot. `MethodProjector.SlotKindOf` holds the table; the property and indexer projectors call it.

`PropertyProjector` and `IndexerProjector` apply the same table to `GetMethod ?? SetMethod`
(Requirement 1.2). `SentinelAttributes.ApplyMethodAttributes` is deleted with its four method
sentinels; `ApplyTypeAttributes` stays (Requirement 1.5).

A projected member still has no `DeclarationNode`. Nothing here fabricates one.

## Fill resolution: `NativeSlotFills`

`Source/Mist/ProgramAnalysis/NativeSlotFills.cs`, `internal static`, beside `ConformanceAnalyzer.Fillers`
and `SlotSignatures`, which it reuses. The emitter already reaches into this namespace for
`ConformanceAnalyzer.Reached` and `Fillers` (see `NativeSlotNullability`), so the dependency direction
is established.

```csharp
internal static class NativeSlotFills
{
    internal sealed record Fill(SlotMember Filler, SlotMember Slot, ConcreteType Ancestor, TypeSubstitution Inheritance);
    internal sealed record Collision(
        SlotMember Member, SlotMember Against, ConcreteType Ancestor, TypeSubstitution Inheritance, NativeSlotKind Kind,
        string? Mismatch, string? Declared);

    public static IEnumerable<Fill> Of(ConcreteType type, TypeInterner interner);
    public static IEnumerable<Collision> Collisions(ConcreteType type, TypeInterner interner);
    public static IEnumerable<(SlotMember Slot, ConcreteType Ancestor, TypeSubstitution Inheritance)> Unfilled(ConcreteType type, TypeInterner interner);

    public static IDisposable Enter(ProgramModel programModel);
    public static bool Fills(object member);
}

internal readonly record struct SlotMember(object Member);   // a Method, Property or Indexer
```

As delivered, `SlotMember` is the small union, and `Unfilled` reports required properties and indexers as
well as methods: `RequiredBase.Size` is as much a required slot as `RequiredBase.Key`.

The exact shape of the member abstraction is the implementer's; `Method`, `Property` and `Indexer` do
not share an interface today beyond `IOverload` for the two with parameters, so a small internal union
or three overloads is acceptable. What is fixed is the algorithm:

1. Walk the base chain as `DeclarationShapeAnalyzer.CheckRedeclarations` does: `Inherits(baseType)`
   through `ConstructedType` definitions, accumulating the path and building the inheritance
   substitution with `TypeSubstitution.ForPath`. Stop at the first ancestor whose `BaseUnresolved` is
   true (Requirement 3.4). Visit Mist ancestors and projected ancestors alike; only projected ones
   (`Assembly.Kind == MistAssemblyKind.Ambient`) contribute slots.
2. For each instance member of the source type with a `DeclarationNode`, find the **nearest** projected
   ancestor member of the same name whose parameter signature repeats under the inheritance substitution.
   The repetition question is `OverloadSignature.Of(later, Empty) == Of(earlier, inheritance)` with the
   declaration-node guard of `Repeats` lifted for the earlier member. A new overload
   `OverloadSignature.RepeatsProjected(IOverload later, IOverload earlier, TypeSubstitution inherited, TypeInterner)`
   expresses that; `Repeats` itself is unchanged for its Mist-to-Mist callers. `RepeatsProjected` compares
   position by position, and a source parameter of type `unknown` or `unknown | void` repeats a projected
   `System.Object` one there and nowhere else (Requirement 6.4). Properties repeat by name
   alone, indexers by parameter signature. Only an instance ancestor member is a match; a static source
   member is matched against a `Required` or `Defaulted` slot only, to be refused as `"static"`.
3. Classify by the nearest match's `SlotKind`:
   - `Required` or `Defaulted`: check return-type agreement with `SlotSignatures.Agree`, accessibility
     equality, for properties the accessor subset, for a property none of whose accessors has a body
     (a C# auto-property) every accessor the slot has (Requirement 4.4), and for a generic method that
     `SlotSignatures.Strengthened` finds no constraint the slot does not promise (Requirement 4.7). All
     pass: a `Fill`. Any fails: a `Collision` with `Mismatch` naming what differs (`"static"`,
     `"return type"`, `"accessibility"`, `"get accessor"`, `"set accessor"`, `"the constraints of 'T'"`)
     and `Declared` saying what the slot declares. A CLR result the metadata leaves
     nullable projects as `T | void`; a fill returning `T` agrees with it, as C# lets an `override` of
     `string? ToString()` return `string`.
   - `Sealed`: a `Collision` with `Kind = Sealed`.
   - `None`: a `Collision` with `Kind = None`, the redeclaration case.
4. `Unfilled`: every `Required` slot of every projected ancestor, read through the inheritance
   substitution, for which neither the source type nor any Mist ancestor on the path yields a `Fill`.
   A Mist ancestor's fill is found by running step 2 for that ancestor; the walk already has its path.

A derived source type whose Mist ancestor fills a slot (Requirement 2.5) falls out of step 2: the
nearest *projected* declaration is still the CLR one, and the Mist ancestor's member is skipped because
it is not projected. `DeclarationShapeAnalyzer` then has to be told not to call the pair a
redeclaration; see below.

## Stage 5

### `NativeSlotAnalyzer` (new, `native-slot-filling`)

For each `ConcreteType { DeclarationNode: not null }` in `context.Types`:

- each `Unfilled` slot reports `MIST0459` at the type's declaration;
- each `Collision` with `Kind = Sealed` reports `MIST0460` at the member;
- each `Collision` against a `Required` or `Defaulted` slot with a `Mismatch` reports
  `MIST0461` at the member.

Collisions with `Kind = None` are not this analyzer's: they are redeclarations and stay with the rule
that owns that diagnostic.

### `DeclarationShapeAnalyzer.CheckRedeclarations`

Two changes. It compares against projected ancestors' members too, by switching its `Hides` to the
projected-tolerant repetition for an ancestor that is a projected type, so a `Collision` of kind `None`
draws `MIST0177` exactly as a Mist ancestor's member does. And it skips any member
that `NativeSlotFills.Of` reports as a `Fill`, which is what lets a derived type restate a Mist
ancestor's fill (Requirement 2.5) without being refused for repeating that ancestor. As delivered it
also skips a member that collides with a `Sealed` slot or differs from a `Required` or `Defaulted` one,
which `NativeSlotAnalyzer` reports, so one mistake draws one diagnostic; and it reports a member once
however many projected ancestors declare the signature it repeats, since a CLR hierarchy restates
members as a Mist one cannot.

### Diagnostics

Three descriptors in `ProgramAnalysisDiagnostics.NativeSlots.cs`, severity Error, category `DEC`, policy
Fixed, their Numbers minted under diagnostics Requirement 1.4 as the lowest unused values above every
Number issued (`MIST0458` was the highest at `af91eec2`):

| Number | Field and title | Template |
|---|---|---|
| `MIST0459` | `NativeSlotNotFilled`, CLR slot not filled | `'{type}' does not fill slot '{slot}' of '{base}': the CLR declares it abstract and no member of '{type}' or a base answers it.` |
| `MIST0460` | `NativeSlotSealed`, CLR slot sealed | `'{type}.{member}' cannot fill slot '{slot}' of '{base}': the CLR declares it sealed.` |
| `MIST0461` | `NativeSlotShape`, CLR slot fill shape mismatch | `'{type}.{member}' fills slot '{slot}' of '{base}' but differs in {difference}: the slot declares {declared}.` |

`MIST0183` is not reused: its text says "no public member", and a class slot is as often
protected.

## Stage 6

### Emission of a fill

`MethodEmitter.Emit`, `PropertyEmitter` and `IndexerEmitter` each build their modifier string through
`MemberModifierEmitter.Emit`. A fill adds `override`, or `sealed override` when `Modifiers.IsSealed`,
immediately after the accessibility keyword. `MemberModifierEmitter.Emit` gains a `fills` parameter,
decided by the caller like `emitReadonly` is, for the same reason its header gives: the emitter of
modifiers has neither the declaring type nor the model in hand.

The callers learn whether a member fills from a per-emission scope, `NativeSlotFills.Enter(model)`,
built the way `NativeSlotNullability.Enter` is: computed once before anything is written, held
thread-statically for the emission, queried by member identity. The two scopes can share one walk.

A generic method fill writes only its `class` and `struct` constraints, through
`ConstraintEmitter.Lines(..., overriding: true)`: C# refuses any other restated on an override (CS0460)
and the override inherits them from the slot (Requirement 5.6).

`MethodEmitter.HidesObjectToString` yields to a fill: `new` is written only when the method is not in
the fill set (Requirement 5.3). A source type with no projected ancestor has no fills, so its `ToString`
keeps `new` and nothing observable changes for it.

### Nullability

`NativeSlotNullability.Answering` today collects parameters answering nullable *interface* slots.
It also collects the parameters of every `Fill` whose slot parameter has `ClrDeclaresNullable`, so a
fill of `Equals(object? obj)` is written `object?` and C# raises no CS8765.

### What is not emitted

A `Defaulted` slot the type leaves alone emits nothing. A `Required` slot the type leaves alone never
reaches emission: Stage 5 refused the program.

## Behaviour changes for existing programs

Two, both to be checked against `AllFeaturesV2` before delivery:

1. A Mist member repeating a non-virtual CLR ancestor member becomes `MIST0177`.
   Today it is admitted and hides. No banked sample is known to do this; the baseline run is the proof.
2. A `ToString`, `Equals` or `GetHashCode` on a Mist class that extends a CLR class flips from `new`
   to `override`, so reading the value through the base type now reaches the Mist member. Same check.

A Mist class with no CLR base is untouched by either.

## Testing

Tiers follow `.claude/skills/testing-philosophy/SKILL.md`. Each test declares exactly one tier trait.

### Fixtures

`Test/Mist.Tests.InteropFixtures/SlotShapes.cs`, compiled C# the Tier 2 projector tests load:

```csharp
public abstract class RequiredBase { protected abstract string Key(int n); public abstract int Size { get; } }
public class DefaultedBase { public virtual int Twice(int x) => x * 2; protected virtual void Hook() { } public virtual string Label { get; set; } = ""; public void Fixed() { } }
public class SealingDerived : DefaultedBase { public sealed override int Twice(int x) => x; }
public class OverridingDerived : RequiredBase { protected override string Key(int n) => n.ToString(); public override int Size => 1; }
```

As delivered the fixture is written in the repository's block-bodied style and adds a virtual indexer to
`DefaultedBase` (Requirement 1.2) and `ImplicitImplementer : IDisposable`, whose `Dispose` is the
`virtual final newslot` shape the projection table maps to `None`.

### Tier 2: projection (`AmbientTypeProviderV2SlotKindTests`)

Over the fixture types through `AmbientTypeProviderV2InteropFixture`: `Key` and `Size` on
`RequiredBase` are `Required`; `Twice`, `Hook` and `Label` on `DefaultedBase` are `Defaulted`; `Fixed`
is `None`; `Twice` on `SealingDerived` is `Sealed`; `Key` on `OverridingDerived` is `Defaulted`
(Requirement 2.3's nearest-declaration rule rests on this); every member of `IGreeter` is `None`;
`System.Object.ToString` is `Defaulted`. The fixture's provider projects only the fixture assembly, so
the `System.Object` rows (`ToString`, `Equals`, `GetHashCode`) read the runtime's metadata through
`AmbientProviderTestHelper.RuntimeResolvingContext` and ask `MethodProjector.SlotKindOf` directly rather
than project CoreLib.

### Tier 1: resolution (`NativeSlotFillsTests`)

Hand-built with `HandBuiltModel`: a projected `ConcreteType` whose members carry `SlotKind`, a source
type deriving it. Rows: fill of a `Defaulted` method; fill of a `Required` method; `Unfilled` reports a
`Required` slot and not a `Defaulted` one; a `Sealed` match is a `Collision`; a `None` match is a
`Collision`; return-type, accessibility and static mismatches each name their `Mismatch`; a property
fill with a subset of accessors is a fill when it has a body, an auto-property omitting one is a shape
collision, and one adding a setter is a shape collision; a generic method fill restating its slot's
constraints is a fill and one strengthening them is a shape collision; `unknown` and `unknown | void`
repeat a projected `System.Object` parameter and no other projected type; a generic
ancestor read through `TypeSubstitution.ForPath` (`Collection<int>.InsertItem(int, int)`); a derived
type restating a Mist ancestor's fill is a fill; a method whose own type parameter is positionally
matched (`SlotSignatures` already answers this for abstract slots); a chain with an unresolved base
yields nothing.

### Tier 1: decisions (`NativeSlotAnalyzerTests`, `DeclarationShapeNativeRedeclarationTests`)

Hand-built models driven through the analyzer alone, after `ConformanceDecisionTests`: each of the three
new diagnostics reported once at the right node; `MIST0177` for a `None` collision
against a projected ancestor; no redeclaration for a restated fill; silence for an unfilled `Defaulted`
slot; silence under an unresolved base.

### Tiers 1 and 2: emission (`MethodEmitterOverrideTests`, property and indexer rows)

Hand-built `Method` in and out of a `NativeSlotFills` scope: `protected override void Hook()`,
`public sealed override int Twice(int x)`, a `ToString` not in scope keeps `new`, a `ToString` in scope
has `override` and no `new`, a fill's `object?` parameter under `NativeSlotNullability`. The spelling of
the modifiers is one function, `MemberModifierEmitter.Emit`, and its rows are Tier 1. The declaration
rows run `MethodEmitter`, `PropertyEmitter` and `IndexerEmitter` with their collaborators and the
real fill scope, which is the emitter's internal chain, so they are Tier 2, as the existing
`ToStringDeclarationEmissionTests` and `NativeSlotNullabilityEmissionTests` over the same emitters are.

### Tier 6

`samples/ClrSlotFilling` composed into `AllFeaturesV2`, with the lines Requirement 7 names. No Tier 5
test: the sample's one test is the orchestrator's.

## Open points for the implementer

- Whether `Property` and `Indexer` need the same `RepeatsProjected` or match by name alone is settled
  above (name; parameters); whether to introduce a tiny union over the three member kinds or write
  three overloads of `Of` is a code-shape choice.
- The analyzer's place in `ProgramAnalysisRunner`'s list: `native-slot-filling` runs after
  `ConformanceAnalyzer` so a type's diagnostics read in the order abstract-then-CLR.
