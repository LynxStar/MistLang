# Requirements Document

## Introduction

Mist has no `virtual` and no `override`. Its one mechanism for a type to supply behaviour that another
type dispatches to is the slot: an abstract type declares slots, with or without a default body, and a
conformer fills a slot by declaring a member of the matching name and signature. Overriding, in the
C# sense, is nothing more than choosing to fill a slot that has a default instead of taking the default.

abstract-type-unification Requirement 25 already extends that model to CLR classes. A CLR `virtual`
member is a slot with a native default, a CLR `abstract` member is a required slot, a Mist member with
the matching signature fills it, and the C# emitter spells the fill `override` (25.4). Requirement 26.5
confines that spelling to fills of CLR slots: Mist-to-Mist concrete inheritance never overrides.

Half of it is live. A CLR interface projects as an abstract type (25.1), a Mist type fills its slots by
conforming, native default bodies are honoured (14.8, 16.7) and the emitter writes the implementations;
`class Money : IComparable<Money>` driving `List<Money>.Sort()` is the standing proof. The class half is
not: the ambient provider stamps virtual, abstract, override and sealed on every projected class method
as sentinel attributes that nothing reads, and `HasNativeBody` is set for interface members only.
First measured at `8c0de050` on 2026-10-24, and re-measured before implementation at `af91eec2` on
2026-10-30 with the CLI built from that revision (`dotnet build Source/Mist.CLI/Mist.CLI.csproj`, then
`mist run <probe>`), which agreed with the first measurement but for the spelling of the Roslyn error,
renamed by the diagnostics reissue of 2026-10-25. Outputs at `af91eec2`:

| Program | Observed at `af91eec2` | Expected under Requirement 25 |
|---|---|---|
| `class Loud : Collection<int>` declaring `protected void InsertItem(int index, int item)` that counts calls, then two `Add` calls | compiles, prints `inserts=0 count=2`: the member hides and is never called | `inserts=2 count=0` |
| `class Catalog : KeyedCollection<string, Item>` with no `GetKeyForItem` | reaches Roslyn: `error MIST0003: 'Catalog' does not implement inherited abstract member 'KeyedCollection<string, Item>.GetKeyForItem(Item)'` | a Mist diagnostic naming the unfilled slot, `MIST0459`; nothing emitted |
| `class Hello : ComponentBase` declaring `protected void BuildRenderTree(RenderTreeBuilder builder)` that adds `hello from mist`, rendered through `HtmlRenderer` | compiles; emitted `protected void BuildRenderTree(...)` without `override`, so Blazor renders the empty base method: `rendered=[]` | emitted `protected override void BuildRenderTree(...)`; `rendered=[hello from mist]` |
| `class Marker : Attribute` declaring `public bool IsDefaultAttribute()` returning true, read through an `Attribute` variable | prints `False` | prints `True` |
| `class Loud : Collection<int>` declaring `public void Add(int item)`, a non-virtual CLR member | compiles, hides, `count=0` after `Add(5)` | refused as a redeclaration, `MIST0177`, as a Mist ancestor's member would be |

The last row has a precise cause: `OverloadSignature.Repeats` requires both members to carry a
declaration node, and a projected member has none, so the redeclaration rule in
`DeclarationShapeAnalyzer` cannot see any member of a CLR ancestor.

This specification makes the stamped facts live in three places: the program model records a slot kind
on projected class members, Stage 5 resolves fills and refuses what is neither a fill nor an overload,
and the emitter spells a fill `override`. It adds no Mist syntax. It is the prerequisite for any Mist
type that extends a framework base class with lifecycle hooks, `Microsoft.AspNetCore.Components.ComponentBase`
first among them, and it delivers items 1 and 2 of the archived Carbonek workstream MIST-1034.

## Glossary

- **Projected_Type**: a `ConcreteType` the Ambient_Type_Provider builds from CLR metadata; its assembly
  has `MistAssemblyKind.Ambient`. `System.Object` is one.
- **Source_Type**: a `ConcreteType` declared in Mist source, with a `DeclarationNode`.
- **Projected_Ancestor**: a Projected_Type in a Source_Type's base chain, reached through any number of
  Mist ancestors.
- **Slot_Kind**: what a projected class member says about filling, one of `None`, `Required`,
  `Defaulted`, `Sealed`. Carried on `Method`, `Property` and `Indexer`.
- **Native_Slot**: a projected class member whose Slot_Kind is `Required` or `Defaulted`.
- **Required_Slot**: Slot_Kind `Required`, a CLR `abstract` member, which has no body.
- **Defaulted_Slot**: Slot_Kind `Defaulted`, a CLR `virtual` member with a body, including a non-final
  `override`.
- **Sealed_Slot**: Slot_Kind `Sealed`, a CLR `sealed override`: virtual to the runtime, closed to
  further filling.
- **Candidate**: a Source_Type member whose name and parameter signature repeat a Projected_Ancestor
  member's, read through the inheritance substitution, by the same question `OverloadSignature`
  answers for Mist ancestors.
- **Fill**: a Candidate against a Native_Slot that also agrees in return type, accessibility and
  instance-ness. The member is the **Filler**.
- **Native_Slot_Fills**: the one function from a Source_Type to its fills, consumed by Stage 5 and by
  the emitter.
- **Redeclaration**: a Candidate against a member of Slot_Kind `None`, refused today for Mist
  ancestors as `MIST0177`.

## Requirements

### Requirement 1: Projected class members carry a slot kind

**User Story:** As the compiler, I want every member projected from a CLR class or struct to say whether
a derived Mist type may fill it, so that filling is decided from the model rather than from C#'s
reaction to the emitted code.

#### Acceptance Criteria

1. WHEN the Ambient_Type_Provider projects a method declared on a CLR class or struct, THE
   Ambient_Type_Provider SHALL record its Slot_Kind: `Required` where the CLR method is abstract;
   `Sealed` where it is virtual and final and overrides an inherited slot; `Defaulted` where it is
   virtual, not final and not abstract, whether a new virtual slot or a non-final override; `None`
   otherwise. A virtual, final new slot is how the CLR spells a member that implements an interface
   member without being virtual in C#, such as `Collection<T>.Add`, so it is `None`, not `Sealed`.
2. WHEN the Ambient_Type_Provider projects a property or indexer declared on a CLR class or struct, THE
   Ambient_Type_Provider SHALL record its Slot_Kind from its accessors by the same rule, reading the
   getter where one is projected and the setter otherwise.
3. THE Slot_Kind of a member projected from a CLR interface SHALL be `None`: an abstract type's members
   are its slots by definition, and `HasNativeBody` continues to say which carry a native default
   (abstract-type-unification 14.8, 16.7).
4. THE Slot_Kind of a member declared in Mist source SHALL be `None`. No Mist syntax declares a slot
   kind; the grammar is unchanged.
5. WHEN Slot_Kind is recorded, THE Ambient_Type_Provider SHALL stop stamping the `Virtual`, `Override`,
   `Abstract` and `Sealed` sentinel attributes on methods. The type-level `Sealed` and `Abstract`
   sentinels are not this specification's concern and are unchanged.

### Requirement 2: Fills are resolved by one function

**User Story:** As a Mist developer, I want a member I declare with the same name and signature as a
CLR base class's virtual or abstract member to fill that slot, with no keyword, exactly as I fill an
abstract type's default by declaring the member.

#### Acceptance Criteria

1. THE Compiler SHALL provide Native_Slot_Fills, a pure function over the program model that, for a
   Source_Type, walks its base chain through every Mist ancestor to every Projected_Ancestor, reading
   each ancestor's members through the substitution the inheritance path establishes, as
   `DeclarationShapeAnalyzer.CheckRedeclarations` already does.
2. A Source_Type member is a Candidate against an ancestor member WHEN both are instance members, their
   names are equal, and their parameter signatures repeat under the inheritance substitution by the
   question `OverloadSignature` answers. THE comparison SHALL NOT require the ancestor member to carry
   a declaration node: a projected member has none, and that is what hides every CLR ancestor from the
   rule today.
3. WHEN the nearest Projected_Ancestor declaring the signature has Slot_Kind `Required` or `Defaulted`,
   the Candidate is a Fill of that slot. The nearest projected declaration decides the kind; outer
   declarations of the same signature are not consulted, so a CLR class that overrides its own base's
   abstract member presents a Defaulted_Slot, not a Required one.
4. A Fill SHALL agree with its slot in return type by `SlotSignatures.Agree`, and in accessibility: a
   `public` slot is filled by a `public` member and a `protected` slot, which is also how a CLR
   `protected internal` member is projected, by a `protected` member.
5. WHEN a Mist ancestor fills a slot and a derived Source_Type declares the same signature, THE derived
   member SHALL also be a Fill of that slot, and SHALL NOT be a Redeclaration of the ancestor's member.
6. A static member SHALL never be a Fill.
7. Native_Slot_Fills SHALL be the only place the fill decision is made. Stage 5 and the emitter consume
   it; neither re-derives it.

### Requirement 3: A required slot must be filled

**User Story:** As a Mist developer, I want to be told in Mist terms when my type leaves a CLR abstract
member unfilled, instead of learning it from a C# error over code I did not write.

#### Acceptance Criteria

1. WHEN a Source_Type has a Projected_Ancestor with a Required_Slot, and neither the Source_Type nor
   any Mist ancestor fills it, THE Analyzer SHALL report `MIST0459` at the
   Source_Type's declaration, naming the type, the slot's signature and the projected type that declares
   it.
2. A Defaulted_Slot left unfilled SHALL be admitted silently: the native default runs.
3. WHEN `MIST0459` is reported, THE Compiler SHALL emit no C# for the program:
   the `MIST0003` (C# compilation error) seen before this specification never occurs.
4. THE rule SHALL stay silent where a base in the chain did not resolve, as `ConformanceAnalyzer`
   does through `InheritsUnseen`: the unresolved base was reported where it was written.

### Requirement 4: What is neither a fill nor an overload is refused

**User Story:** As a Mist developer, I want a member that collides with a CLR base member to be refused
the way Mist refuses hiding a Mist ancestor's member, so that nothing I write silently hides a framework
method.

#### Acceptance Criteria

1. WHEN a Candidate's nearest projected declaration has Slot_Kind `None`, THE Analyzer SHALL report
   `MIST0177`, the diagnostic a Mist ancestor's member draws today, naming the
   Source_Type, the member and the projected ancestor.
2. WHEN a Candidate's nearest projected declaration has Slot_Kind `Sealed`, THE Analyzer SHALL report
   `MIST0460` at the member, naming the member, the slot and the projected type that
   sealed it.
3. WHEN a Candidate against a Native_Slot differs from the slot in return type, in accessibility, or is
   static, THE Analyzer SHALL report `MIST0461` at the member, naming what differs
   and what the slot declares.
4. WHEN a property or indexer Fill declares an accessor its slot does not, THE Analyzer SHALL report
   `MIST0461` naming the accessor. A Fill may omit an accessor the slot has, unless it is an
   auto-property: WHEN a property Fill none of whose accessors has a body omits an accessor its slot
   has, THE Analyzer SHALL report `MIST0461` naming the omitted accessor, since C# requires an
   auto-property override to override every accessor (CS8080). A get-only Fill with a body is admitted.
5. A Source_Type member whose name matches an ancestor member but whose parameter signature does not
   is an overload, admitted as today.
6. THE purity, readonly and ownership modifiers of a Fill are the Filler's own and SHALL NOT be judged
   against the slot: a CLR member promises none of them. A `ref` return SHALL agree, as
   `SlotSignatures` already requires of abstract slots.
7. WHEN a generic method Fill constrains a type parameter by a constraint its slot's corresponding
   parameter does not entail, judged as `SlotSignatures.Strengthened` judges an abstract slot's answer,
   THE Analyzer SHALL report `MIST0461` naming the parameter and the constraint. A Fill may restate
   the slot's constraints or omit them.

### Requirement 5: A fill is emitted as an override

**User Story:** As the C# backend, I want to spell a fill of a CLR slot with the one keyword C# accepts
for it, so that the runtime dispatches to the Mist member.

#### Acceptance Criteria

1. WHEN a method, property or indexer is a Fill, THE Emitter SHALL write `override` after its
   accessibility; WHEN the Mist declaration also carries `sealed`, THE Emitter SHALL write
   `sealed override`.
2. THE Emitter SHALL write the Fill's accessibility as the Filler declares it: `protected` for a
   `protected` or `protected internal` slot, `public` for a `public` one.
3. WHEN a Source_Type with no Projected_Ancestor declares a parameterless instance `ToString`, THE
   Emitter SHALL keep writing `new`, as today. WHEN `ToString` is a Fill, reached through a
   Projected_Ancestor chain that ends at `System.Object`, THE Emitter SHALL write `override` and
   SHALL NOT write `new`.
4. WHEN a slot's parameter is declared nullable in CLR metadata, THE Emitter SHALL annotate the Fill's
   parameter nullable, extending `NativeSlotNullability` from interface slots to class slots, so the
   emitted C# compiles without CS8765.
5. THE Emitter SHALL write nothing for a Defaulted_Slot the Source_Type leaves unfilled: inheritance
   supplies the native default.
6. WHEN a generic method Fill restates constraints, THE Emitter SHALL write only its `class` or
   `struct` constraints, the only ones C# lets an override restate (CS0460); the override inherits the
   rest from the slot, and 4.7 has ensured the Fill requires no more.

### Requirement 6: System.Object is a projected type like any other

**User Story:** As the language owner, I want Mist classes to keep having no object base, while a Mist
class that extends a CLR class fills `System.Object`'s slots through the chain like any other.

#### Acceptance Criteria

1. `System.Object`'s virtual members (`ToString`, `Equals`, `GetHashCode`) SHALL carry Slot_Kind
   `Defaulted` like any projected virtual member.
2. THE Compiler SHALL NOT give a Source_Type an implicit `System.Object` base. A Source_Type with no
   Projected_Ancestor has no Native_Slot to fill, and its `ToString`, `Equals` and `GetHashCode` are
   ordinary members emitted as today, `==` operator suppression included.
3. WHEN a Source_Type declares `System.Object` as its base explicitly, or reaches it through a
   Projected_Ancestor, its matching members ARE Fills under Requirements 2 to 5.
4. WHEN Native_Slot_Fills compares a Source_Type member's parameters with a Projected_Ancestor
   member's, a parameter of type `unknown`, which is what Mist's `object` resolves to, or of its
   optional `unknown | void`, SHALL repeat a parameter whose projected type is `System.Object`, so
   `public bool Equals(object other)` fills `Object.Equals` instead of hiding it. The rule is
   confined to that comparison: `OverloadSignature.Repeats`, and with it the redeclaration and
   overload rules between Mist members, is unchanged, and `unknown` repeats no projected type but
   `System.Object`, at the top of a parameter only.

Decision recorded 2026-10-24 (Arthur): slot filling is the model and `override` is only the emitter's
spelling of a fill; `System.Object` is not an implicit root and is reached only through an explicit CLR
base chain.

Decision recorded 2026-10-31 (Arthur): "Let unknown repeat a projected System.Object for slot matching
only." That is criterion 4. The alternatives were refusing the collision with the shape diagnostic and
leaving the silent hide; a member that silently never runs is the failure this specification removes.

### Requirement 7: Delivered shapes are banked

**User Story:** As a maintainer, I want the observable behaviour this specification changes recorded
where the repository records progress.

#### Acceptance Criteria

1. THE delivery SHALL add a sample at `samples/ClrSlotFilling`, composed into `AllFeaturesV2` and
   recorded in `samples/AGENTS.md` with V2-only provenance, that prints at least:
   - a `Collection<int>` subclass whose `InsertItem` fill counts calls: `inserts=2 count=0` after two
     `Add` calls, the count being zero because the fill replaces the default and never stores the item;
   - a `KeyedCollection<string, Item>` subclass whose `GetKeyForItem` fill makes `cat["pear"].qty`
     and `cat.Contains("apple")` work: `keyed=7 has=True`;
   - an `Attribute` subclass whose `IsDefaultAttribute` fill is read through an `Attribute` variable:
     `True`;
   - a `ToString` fill on a class with a CLR base, read through the base type;
   - a virtual property fill, `Exception.Message` or an equivalent, read through the base type.
2. THE sample SHALL contain no member that is merely a Redeclaration: the refusals of Requirement 4
   are proven at Stage 5's own tier, since `AllFeaturesV2` cannot see a refusal.
3. A sample project SHALL exist at `samples/ClrSlotFilling/Program.mist`.

## Out of scope

- **No new keywords.** `virtual`, `override` and `new` do not enter the grammar. Mist-to-Mist concrete
  inheritance keeps refusing a repeated signature as `MIST0177`; the `new`-keyword
  hiding of abstract-type-unification 26.2 to 26.4 was never implemented and this specification does not
  revive it.
- **No base call.** The grammar spells `base` only in a constructor chain. A Fill replaces the default
  entirely and cannot invoke it. `ComponentBase`'s defaults are empty, so Blazor is unaffected; a type
  that needs the default's behaviour and its own is a later decision.
- **Instantiating a projected abstract class** still reaches Roslyn. The type-level `Abstract` sentinel
  is inert and stays so here.
- **Events** are not projected and are not slots.
- **Covariant returns.** C# admits a narrower return type on an override; a Fill must agree exactly.
