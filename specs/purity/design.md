# Purity Design

The requirements say what a `readonly`, `local` or `pure` member may do. This document says where each decision lives in the v2 compiler, so that the grammar, the model, the analysis phase and Standard agree on one representation. It follows the clean-room rule: nothing here derives from `Mist.Semantics` or `Mist.Analysis`.

## One representation, three consumers

A promise is recorded exactly once, on `MemberModifiers`, as two flags beside the existing `IsReadonly`:

- `IsPure` -- written `pure`, or inherited from a `pure` type.
- `IsLocal` -- written `local`, or inherited by an instance method, property or indexer of a `local` type.

`IsPure` always sets `IsReadonly` as well (Requirement 2.1), at the one place modifiers are built, so every existing readonly rule -- enum bases, `where T : readonly`, readonly parameters, `StableMembers` -- sees a pure member or type as readonly without being told. `local readonly` is `IsLocal` with `IsReadonly`.

A function type's promise is part of the type: `FunctionType.IsPure`, interned separately from the same shape without it.

The three consumers are the binder (placement and conflicts, Requirement 1), the analysis phase (everything a body does, Requirements 4 to 12 and 16) and Standard (Requirement 9). Emission reads no promise of its own and changes no behavior (Requirement 13.1). It reads `IsReadonly` as it always has, so a pure type's fields are emitted `readonly`, as a readonly type's are.

## Frontend

### Grammar

- `tokens/keywords.storm`: `token PURE = "pure";` and `token LOCAL = "local";` under a "Purity keywords" heading. Both become reserved: the three test sources that used `local` as a variable name (`ReadonlyAnalyzerPropertyTests`, `AssociatedTypeGenericContextPropertyTests`, `RefinedReturnContractDecisionTests`) rename it.
- `Modifier`: `| PURE as PureModifier` and `| LOCAL as LocalModifier`, giving `PureModifierModifierNode` and `LocalModifierModifierNode`.
- `LambdaModifier`: `| PURE as PureLambdaModifier` and `| LOCAL as LocalLambdaModifier`.
- `SingleTypeRef`: `FuncSingleTypeRef` and `NamedSingleTypeRef` each gain a leading `pure:PURE?`. `ElementTypeRef`'s `FuncElementTypeRef` and `NamedElementTypeRef` likewise. The named form exists for `pure Func<int, int>`, `pure Action<int>` and `pure Step` where `Step` is an alias; the resolver refuses it when what it names is not a function type.

Ordered choice already gives Requirement 16.2 for free: every declaration that admits modifiers reads `Modifier*` before its type, so a leading `pure` on a method, local function, field or property is a modifier, and only a parameter, a local variable, a lambda parameter, a type argument or an alias body reaches the type reading. A `pure` modifier on a field is then refused as a placement error, whose message names the alias route.

`LocalFunctionDeclaration` and `TypeBodyDeclaration` take the new modifiers through `Modifier*` unchanged.

### Model

- `MemberModifiers`: `IsPure`, `IsLocal`.
- `ModifierBuilder.Build`: the two cases; `PURE` sets `IsPure` and `IsReadonly`. Diagnostics, all at the declaration node:
  - `MIST0318` (error): both written on one declaration (Requirement 1.4).
  - `MIST0319` (info): `pure` and `readonly` both written (Requirement 1.3).
  - `MIST0320` (error): `local` with `static`, or on a constructor (Requirement 1.5).
  - `MIST0321` (error): `pure` or `local` on a field, a type alias, a newtype, an enum or an associated type, and `local` on an operator or cast operator (`ModifierBuilder.Misplaced`). Admitted on a class, struct or abstract declaration, a method, a constructor (`pure` only), a property, an indexer, an operator (`pure` only), a cast operator (`pure` only), a local function and a lambda. An accessor admits a promise, and this diagnostic never judges one: the placement check reads a declaration's `Modifier` list only, and an accessor's promise is its own grammar rule, `AccessorPromise`, written ahead of the accessor's accessibility (Requirement 1.5). `private pure get` is a parse error.
- `ModifierBuilder.ForMember`: as it inherits a readonly type's `IsReadonly`, it inherits a pure type's `IsPure` (with `IsReadonly`) onto every member, and a local type's `IsLocal` onto a non-static method, property or indexer that does not itself declare `pure` (Requirements 15.2 and 15.5).
- `NewtypeCloner` copies the two flags as it copies `readonly`.
- `LocalFunctionBinder.Refused` leaves `pure` and `local` admitted.
- `LambdaExpression`: `IsPure` and `IsLocal`. A lambda written `pure` has a pure function type whatever its target; one bound against a pure function type target already has it.
- `FunctionType`: `IsPure`; `Name` is `pure func<...>` when set, so `IsIdentity` separates the two (Requirement 16.6). `IFunctionType.IsPure`. `TypeInterner.GetOrCreateFunctionType` keys on the flag. `TypeSubstitution.Apply` carries it; `DelegateShape.InvokeShape` never sets it. `TypeDisplay.Mist` prints it.
- `ScopeResolver`: a `Pure` token on a function reference re-interns the resolved function type as pure. On a named reference it expands the alias and does the same, or reports `MIST0322` naming the type.
- Conversions (`ConversionClassifier`, `ConversionRelation`): a pure function type converts implicitly to the same shape without `pure`; nothing converts to a pure function type but a pure function type, a lambda or a method group (Requirement 16.5). `CrossesDelegateShape` never admits a CLR delegate into a pure function type. A cast to a pure function type has no conversion and is refused where casts are.
- `OverloadSignature` erases the flag, so two overloads differing only in it are the existing `DuplicateSignature` error (Requirement 16.7).
- The test shells in `AmbientProviderTestHelper.RegisterStandardTypes` set `IsPure` where they set `IsReadonly`, since Standard's declarations do.

### Pretty printer and editors

`MistPrettyPrinter` prints both modifiers, both lambda modifiers and the `pure ` type prefix. The TextMate grammar's `storage.modifier.mist` and both book highlighter keyword lists (`book/theme/mist-lang.js`, `book/theme/highlight.js`) gain `pure` and `local`.

### Emission

The modifiers change no behavior: `TypeNameEmitter.EmitFunction` reads parameters and result only, and `[Pure]` is not emitted. The one flag emission reads is `IsReadonly`, which it read before this spec. `IsPure` sets it, so a pure type's fields are emitted `readonly`, as a readonly type's are.

Culture is what emission does change (Requirement 13.2). Mist is culture-invariant: every conversion between a value and text, every ordering of text, every case mapping and every regular expression the emitted program performs on Mist's behalf names the invariant culture at the call. Text equality is ordinal, which no culture affects, so `==`, `switch` and `is` on strings are emitted as written. It is per call rather than a process-wide default, which the program could change, so a culture-aware member can later sit beside these without making the promise.

Text ordering uses `StringComparer.InvariantCulture`. Its order comes from the runtime's globalization mode and ICU data, which the process fixes when it starts. By Requirement 7.5 those are the program's setting, not an input, so the ordering is deterministic within a run, and nothing is pinned in the emitted runtimeconfig. Another ordering, ordinal among them, can be added later as its own member.

`Lowering/InvariantCulture` holds the rule, and it covers:

- `String.ToUpper` and `ToLower`: `ToUpperInvariant` and `ToLowerInvariant`.
- `Convert`'s conversions to and from text (`ToInt32(String)`): the overload taking `CultureInfo.InvariantCulture`.
- Regular expressions -- a literal, a cast from `String`, `new Regex(p)`, a clone's backing regex, `IsFullMatch`, and a regex pattern in `is` or `switch`: `RegexOptions.CultureInvariant`.
- An interpolated string with a numeric hole: `System.FormattableString.Invariant($"...")`. Not `string.Create`, whose handler cannot be held across an `await` in a hole. A `char` span in such a string is written as `new string(span)`, since a `FormattableString` boxes its holes.
- A numeric operand of a string concatenation, a numeric argument to `Console.Write` or `WriteLine`, and a call to the object's own `ToString` on a numeric: `ToString(CultureInfo.InvariantCulture)`. A conditional `x?.ToString()` on an optional numeric keeps the `?.`: `x?.ToString(CultureInfo.InvariantCulture)`.
- An argument to `Console.Write` or `WriteLine` whose type C# converts implicitly to a number -- a newtype over a numeric, a Mist enum over one (emitted as a record with an implicit operator to its underlying type), a Mist type declaring an implicit numeric conversion. C# would pass the number to the numeric overload, so the number is what prints, and it is written as that conversion formatted invariantly: `((double)m).ToString(CultureInfo.InvariantCulture)`. The conversion is the one C#'s overload resolution takes: the first of `Write`'s own numeric parameters, in C#'s better-conversion-target order (`int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`), that a conversion reaches, and of the conversions reaching it the one whose target is the most encompassing, so a type converting to both `byte` and `int` prints through `int`. Where C# has no single choice -- `decimal` beside `float` or `double`, which it leaves unranked -- or prefers `char`, `bool` or `string`, the argument is left as written for Roslyn to judge, as it is for classic. A `char`, and a type that is or converts implicitly to one, is left as written: C# prefers `WriteLine(char)`, which prints the character and reads no culture. A CLR enum converts to no number, so it is left as written as well, and `WriteLine(object)` prints its name. An enum's own `ToString`, for a value no variant names, formats its value the same way.
- A value whose text is decided at runtime -- a type parameter, `unknown`, a tuple, an optional of one of these, an anonymous object, a CLR type, a Mist type implementing `IFormattable` -- wherever it is formatted, an explicit `ToString()` that a CLR type declares and its method group included: `MistInvariant.Text`, a helper emitted into `__Invariant.g.cs` when used, which formats an `IFormattable` with the invariant culture, a tuple element by element, as `ValueTuple.ToString` would with the current one, a `KeyValuePair<,>` as `[key, value]`, as its own `ToString` would, and any other value through its own `ToString`, run with the current thread's culture set to the invariant culture for that one call and restored after it. That last arm reaches an anonymous object's generated `ToString` and a CLR type's own, such as `Lazy<double>`'s; it changes no culture the program sets, since it restores the one it found. A conditional `x?.ToString()` on such an optional is `MistInvariant.Optional(x)`, which answers null for the absent value. A CLR `ref struct` is the exception: no helper taking an `object` can be handed one, so its explicit `ToString()` is left as written.
- A newtype over a numeric primitive: its `ToString` formats the backing value with the invariant culture.
- `List<T>.Sort()`: `StringComparer.InvariantCulture` for `string` elements; `MistInvariant.Elements<T>(...)` for a tuple holding text, which compares element by element with each element's comparer; `MistInvariant.Comparer<T>()` for a type parameter or `unknown`, which orders two values that are both strings at run time invariantly and anything else as `Comparer<T>.Default` does; and `MistInvariant.ByValue<T, V>(...)` for a clone of an ordered intrinsic (a newtype over a number, `string`, `char` or `bool`), which orders by the backing value, a `string` one invariantly, since the emitted clone implements no `IComparable` and `Comparer<T>.Default` would throw. The element type is read from the `List<T>` the receiver reaches, whatever its own type is written as: a subclass through its base types, a type parameter through its constraint bounds.
- A method group of any member above -- `s.ToUpper`, `Convert.ToInt32`, `words.Sort`, a number's own `ToString` -- and of any other member `IntrinsicCalls` respells, such as `IsFullMatch`: written bare, Roslyn binds the CLR group, which is the culture-reading overload or one that does not exist. It is a lambda over the respelled call instead, `Lowering/RespelledGroups`. The receiver is passed into an immediately invoked lambda rather than captured by name, so it is evaluated once, where the group is formed, as a method group's receiver is.

What the rule does not reach, each by design:

- A CLR member the program names directly is the CLR call it names (Requirement 9.2): a qualified `System.Console.WriteLine(3.5)` binds the CLR `Console`, not Standard's, and prints with the current culture. It states no promise, so no pure scope can call it.
- A tuple held at run time as `T` or `unknown` orders through `Comparer<T>.Default`, which is `ValueTuple`'s own comparison and orders its text by the current culture; a list of optional tuples keeps the plain `Sort()`. A pure scope refuses both sorts, since the ordering runs code the program cannot see (`ElementComparisons`).

The compiler reads no culture itself (Requirement 13.3). `LiteralBinder` reads real literals
with `NumberStyles.Float` and the invariant culture, refusing non-finite `float`/`double` values
and decimals outside their range with `MIST0377`. `IntegerLiterals`
reads decimal, hex and binary forms with separators and suffixes, preserving the exact value
and choosing an integral type that can represent it. Unary binding admits the signed minimum
boundaries. Dependent integer arguments use that same reader through `LiftIntegerLiteral`,
including positive values through `ulong.MaxValue`; `CharacterLiterals` decodes character escapes
for expressions and dependent arguments. Current unresolved obligations and historical closure
evidence are recorded in Carbonek project MIST.

`BodyEmitter.EmitLiteral` writes literals with the invariant culture, using `R` for `double`
and `float` and decimal's invariant form with its `m`. Diagnostics format arguments invariantly,
Roslyn messages are taken in the invariant culture, and the CLI formats timing summaries
invariantly, so a value quoted in build output reads the same on every machine.

## Analysis phase

Three classes under `Source/Mist/ProgramAnalysis/` carry the rule, each split into partial files: `Origins` (`Origins.Survey`), `PurityInference` (`.Roots`, `.Abstract`) and `PurityJudge` (`.Calls`, `.Reads`, `.Exits`). Five helpers each answer one question the three ask about code the program runs without writing it: `TypeInitializer`, `ThisEscapes`, `ElementComparisons`, `ClrCallbacks` and `Deconstructions`. One new analyzer, `PurityAnalyzer`, reports the judge's findings, and the existing rules listed under Amendments change to agree with it.

### `Origins`

Where a value comes from, for the scope judging it: `Own`, `Frozen` or `External`. Shared by the purity judge and the readonly amendment, so the two never disagree about what a scope owns.

A scope owns:

- a local or parameter declared `owned`;
- a fresh allocation used directly -- `new`, unless its constructor lets `this` escape (`ThisEscapes`) or the type is a view; an array creation or literal; a tuple or anonymous object; a clone whose type is a copy, or whose `Clone` returns `owned` (one declared on an owned `this` answers its source's origin, since it may return its receiver); a call whose method returns `owned` (an instance method with an owned `this` answers its receiver's origin, for the same reason);
- a local (never a parameter) whose every assignment is one of those, and which is never passed by `ref` -- the "what it created" of the glossary, read off the body without flow, since the value is fresh at every assignment;
- a struct held by value: a struct-typed local, parameter (not `ref` or `in`) or field, read as a copy;
- for a local scope only, `this` and every owned field of it (`ThisTiers.IsOwned`), and what those own in turn;
- for a lambda or local function, a captured variable the enclosing scope owns.

A value is frozen when it is a `frozen` parameter or local, or `this` in a method declared `with frozen`.

Everything else is external: `this` in a pure scope, an unrestricted or `readonly ref` parameter, a static, a `ref` struct (a span is a view over memory the scope does not own, unless it came from `stackalloc`), and any reference-typed field reached from anything -- an owned root included -- unless that field is itself owned (`ThisTiers.IsOwned`) and the holder is an owning reference: through a by-value struct, `this` of a struct's own member included, an owned reference field is shared with every other copy and so external. A field hop through an unowned reference field is external because the object behind it may be shared, whatever holds the reference.

`Origins.Survey` reads the body once, before any question: every local's assignments, which decide whether it is fresh at every assignment, and each `frozen` local's lender.

### `PurityInference`

Requirement 8's greatest fixpoint. Every member with a body -- methods, constructors, operators, cast operators, accessors with bodies, local functions and lambdas -- starts with all three promises. Each round re-judges every body under each promise it still holds, given the promises callees currently hold, and drops what the judge refuses; rounds repeat until nothing changes. A member with no body keeps what its modifiers state (Requirement 9.1); an auto getter over stable storage is pure, over mutable storage is local readonly; an auto setter or init is local. An abstract member keeps what it declares, or else the meet of what every implementation in the program keeps, found through `ConformanceAnalyzer.Fillers` over every concrete type in scope (Requirement 8.4).

A member is pure only when it is also readonly by `ReadonlyInference`, and local readonly only when it is local and readonly by it, so Requirement 2.1 holds by construction rather than by argument. `PurityInference` owns the `ReadonlyInference` it asks, and asks it afresh each round, because Requirement 11's admission of a local call on an owned receiver depends on the round's local answers.

`PurityInference.Roots` decides what the fixpoint judges as one member. A constructor's roots are its body and the instance initializers it runs, which one chaining to a sibling leaves to that one. A type's implicit constructor is a member keyed by the type, whose body is its instance initializers, and an allocation through it is a call to it. `PurityInference.Abstract` finds an abstract member's implementations for the meet; an abstract member the CLR can implement too, a projected interface's or Standard's enumerables', keeps only what it declares, since the meet cannot see those implementers. Reached through a slot, an implementation that reads a context the slot does not declare is neither pure nor local.

### `PurityJudge`

One walk over a body under a scope kind -- `Pure`, `Local`, `LocalReadonly` -- yielding findings. `PurityJudge.Calls` judges calls, allocations and constructor chains, `PurityJudge.Reads` judges reads and formatting (`FormatsByType`), and `PurityJudge.Exits` finds the drops the emitter inserts where a scope ends. The judge is the whole of Requirements 5, 6, 7 and 16.4 in one place:

| Operation | Pure | Local | Local readonly |
|---|---|---|---|
| write a field, setter, index or element of an Own value | admitted | admitted | refused |
| write anything Frozen or External | refused | refused | refused |
| read a Stable_Field of anything | admitted | admitted | admitted |
| read a non-stable field, indexer, element of Own | admitted | admitted | admitted |
| read one of Frozen | admitted | refused | refused |
| read one of External, or through a reference-typed argument | refused | refused | refused |
| call a Pure_Member | admitted | admitted | admitted |
| call a Local_Readonly_Member | on Own or Frozen | on Own | on Own |
| call a Local_Member | on Own | on Own | refused |
| call a Readonly_Member or unmarked member | refused | refused | refused |
| call a pure function value | admitted | admitted | admitted |
| call a local holding a lambda (`Origins.LambdaHeldBy`), as a member keeping the lambda's own promise, declared or inferred | on its captures' origin, as the member rows | on its captures' origin, as the member rows | on its captures' origin, as the member rows |
| call any other function value | refused | refused | refused |
| `new` through a pure constructor, allocation of an array, tuple, anonymous object, lambda | admitted | admitted | admitted |
| `new` through any other constructor | refused | refused | refused |
| store a Frozen or External reference of a mutable type into an Own instance, by assignment or as an argument to a Local_Member called on it | refused | refused | -- |
| `await`, an async block, a `with` block | refused | refused | refused |

"Mutable type" excludes structs, readonly types and String. A `local` scope's arguments may be stored, passed on and compared, and a struct argument's fields read; a call through them is a read (Requirement 5.2). Intrinsic operators, conversions and comparisons without a declared operator are pure. A `foreach` reads the collection's elements with the collection's origin. A `default` of a known type is pure unless it chains to a base constructor that is not (`DefaultChainOf`); a `default` of a type parameter is refused (`DefaultsTypeParameter`), since the type argument chooses what it runs and a `default` constraint states no promise. An interpolated string, and a concatenation, is judged hole by hole: a hole whose text its type fixes (`FormatsByType` -- a primitive, `String`, an enum, a newtype, a function type, a Mist type not implementing `IFormattable`, and tuples, unions and optionals of these) is pure, and any other hole is refused as a call to its runtime `ToString`. An explicit call to the universal `ToString` is refused as a call to an unmarked member. A captured variable read by a pure lambda is external unless the local is `readonly` or has no assignment but its own declaration, or is a parameter never assigned (Requirement 16.4); a `local` lambda or local function treats its captures as its own instance.

What a body runs without writing it is a call the judge sees through a helper:

- `TypeInitializer`: a type's static initializers -- its static field and property initializers, its static constructor's body, an enum's variant values -- as one member. Reading a static field runs it; once the type declares a static constructor, any use of the type does.
- `ThisEscapes`: whether a member can hand out the object it runs on. A construction whose constructors and initializers cannot is fresh, so `Origins` counts it as owned.
- `ElementComparisons`: what `List<T>`'s `Contains`, `IndexOf`, `Remove` and `Sort`, and the equality of two anonymous values, run on the scope's behalf -- the element's `Equals` and `CompareTo`. When that is code the program cannot see, a CLR type's, an abstract's or a type parameter's, the call is refused.
- `ClrCallbacks`: the CLR interfaces through which the target calls back into a Mist type -- a `foreach` disposing its enumerator, a formatter asking for text, a comparer -- so the Mist member answering one is judged as a callee.
- `Deconstructions`: each target and value of a deconstructing assignment, so every rule sees each write as though it were written alone.
- `PurityJudge.Exits`: the `Drop` of every linear or relevant binding the body does not hand on, and a declared `Drop`'s droppable fields. It mirrors the backend's `DropInsertion` rather than sharing it, since analysis may not depend on a backend.

Findings, all `MIST_PA_* (historical scheme)`, reported through `AnalysisContext.Report`, each naming the operation, the promise and what would be needed (Requirement 10). The last two are reported by amended rules, not the judge:

- `MIST0137`: writing what the scope does not own.
- `MIST0138`: reading what can change between reads.
- `MIST0139`: a call the table refuses, naming the callee's promise and what the call would need.
- `MIST0140`: a read through a reference-typed argument of a local member.
- `MIST0141`: Requirement 6.4.
- `MIST0142`: a method group converting to a pure function type whose method is not pure.
- `MIST0143`: an operation in a field initializer or default entry that the pure judge refuses, naming the field and the type (Requirement 12.1; defaultability Requirement 6.1).
- `MIST0144`: an implementation that does not keep the promise its abstract member declares (Requirement 8.4). `ConformanceAnalyzer`.
- `MIST0145`, `MIST0146`: a runtime type test or cast to a pure function type, which no runtime value carries evidence for (Requirement 16.5). `CastAnalyzer` and `PatternAnalyzer`.

The scope is described in each message as `pure method 'M'`, `local property 'P'`, `the pure lambda`, `local function 'F'`, `the initializer of field 'F' of 'T'`.

### `PurityAnalyzer`

Id `purity-checking`, order 13 of the analysis register. It walks every body whose member declares a promise, every lambda that is `pure`, `local` or has a pure function type, every local function that declares one, every field initializer of a defaultable type and every default entry, and every settled method group of pure function type, and reports the judge's findings. Inferred promises are never reported (Requirement 3): a member that declares nothing is only ever consulted as a callee. A member declared pure or local readonly is judged here alone, not by `ReadonlyAnalyzer` as well: one mistake, one complaint.

### Amendments

- `ReadonlyInference` (with `ReadonlyInference.Triggers`) and `ReadonlyAnalyzer` (with `ReadonlyAnalyzer.Frame`), Requirement 11: a write to a field, setter, index or element of an Own value is not a write of the context, and a call to a Local_Member on an Own receiver is admitted, both through `Origins`. The local-rooted index admission already made is a case of this. A write through anything External counts, and `MIST0261` reports one through a `ref` parameter, an element of an array parameter, an object initializer's setter, a property pattern, an extension property or a conversion operator. `Triggers` adds the static initializers a node runs and the `IFormattable.ToString` a hole calls; `Frame` is the context a body is judged in. Base rule 10 needs nothing: a pure constructor is readonly by the flag.
- `StableMembers` (Requirement 12.2): a property is also stable when its getter is a Pure_Member by `PurityInference`, which admits a getter that calls pure members over stable fields. `DeclarationShapeAnalyzer` builds the inference for that question. The diagnostic message names the pure route.
- `ConformanceAnalyzer`, the contract (Requirement 8.4): an implementation keeps each promise its abstract slot declares, or `MIST0144` names the promise required and the one kept, and a slot returning `owned` is filled only by a member returning `owned` (`MIST0186`). `Fillers` and `Reached` also serve `PurityInference.Abstract`.
- `CastAnalyzer` and `PatternAnalyzer` (Requirement 16.5): a cast or runtime type test to a pure function type is `MIST0145`, `MIST0146`, and `PatternAnalyzer` judges a type parameter's test at every closing that makes it one.
- `OwnershipAnalyzer`, with `Rules/OwnedDestinations` (ownership-v2 Requirements 1.5, 6.7 and 6.9). The judge's Own origin trusts every owned slot, so both rules hold in every scope, judged or not. The owned-return rule: a `return` in a method returning `owned` is judged by `Origins` -- a construction, a clone, a call returning owned, an owned binding, a local fresh at every assignment, or a value of a type that cannot change -- and anything else is `MIST0263`. The owned-source rule: what moves into an owned binding, field, property or parameter yields ownership the same way, or is `MIST0264`; storing an owned binding into an owned field or property moves it.
- `TierAnalyzer` exposes its lend decisions, so the judge leaves a lend `TierAnalyzer` already refuses to it, and `AssignabilityAnalyzer` checks a `with` block's value against its key type, which a pure function type keys separately.

## Standard

- The intrinsic primitives (`Boolean`, `Byte`, `Char`, `Decimal`, `Double`, `Int16`, `Int32`, `Int64`, `SByte`, `Single`, `UInt16`, `UInt32`, `UInt64`), `String` and `Regex` keep `readonly` on the type and declare every member `pure`: every operation of a value that cannot change is pure, `Clone` included. `pure` cannot replace `readonly` on the type while classic, which parses the keyword and ignores it, judges enum bases and readonly constraints by the type's `readonly`.
- `Math`, `Convert`, `Index` and `Range` are declared `pure` types.
- `Array` needs no declaration: element access is intrinsic and the judge reads it as a field of the receiver.
- `Console`, `Task`, `Type` and `Span` state nothing.
- `Source/Standard/Experimental/List.mist`, which only the experimental pipeline loads, declares `intrinsic public class List<T> : IEnumerable<T>` over `System.Collections.Generic.List<T>`, never `owned` (a container cannot keep the cooperation contract for what callers hand it): the constructor `pure`; `Count`, `Contains`, `IndexOf`, `ToArray`, `GetEnumerator` and the indexer's getter `local readonly`; `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear`, `Sort`, `Reverse` and the indexer's setter `local`; its `Enumerator` struct's `MoveNext` local and `Current` local readonly. The surface is what the samples, tests and snippets use, found by measurement. The indexer's two accessors differ, which is why an accessor may state its own promise (Requirement 1.5): `PropertyAccessor.OwnModifiers` records it, and it replaces the declaration's for that accessor everywhere a promise is read.

## Verification

- Parse, print and parse again for each keyword position; the modifiers survive on every declaration kind that admits them.
- Model probes for every conflict and placement diagnostic, for type-level inheritance, for the interned pure function type and each conversion direction.
- Tier 2 rows over hand-built fragments for every row of the judge's table, in each scope kind, and for every diagnostic: `PurityJudgeTests`, `PurityRefusalTests`, `PuritySiteTests`, the soundness and round classes, `AccessorPromiseRefusalTests`, `ReadonlyOwnedWritesTests`, `DefaultConsistencyTests`, `StablePureGetterTests`, `PurityInferenceTests`, `PurityContractTests` and `PureFunctionConversionTests`; binder refusals at Tier 3 in `PurityModifierTests` and `PurityBindingRefusalTests`. There is no refusal corpus: the repo's was migrated to Tier 1 and Tier 2 rows.
- Culture (Requirement 13): each emitted form pinned at Tier 3 over `HandBuiltModel`, the direct forms in `InvariantCultureEmissionTests` and the indirect ones in `InvariantCultureIndirectFormEmissionTests` (method groups, conditional `ToString`, `Sort` through subclasses and bounds, `Console` through conversions); literal reading and writing under other cultures at Tier 1, with the out-of-range refusal at Tier 2, in `LiteralCultureInvarianceTests`; literal binding at Tier 2 in `NumericLiteralBindingTests` (`long.MinValue` under a minus, dependent-value literals and their refusals) and, in `RefinementSiteTests`, a negated `float`, `double` or `decimal` bound quoted in a finding as the negated literal; and one Tier 4 program, `CultureInvariantEmissionTests`, compiled under `de-DE` and run under `tr_TR.UTF-8` and `de_DE.UTF-8`.
- Every banked sample still banks. Only `samples/Purity` declares promises, so the only new refusals any other sample can meet are Requirement 12's, and any it does meet is measured before it is decided.
- `samples/Purity` exercises declared and inferred promises, `local` on a type, a pure function type and a default over a pure constructor, and is composed into `AllFeaturesV2` as V2-ONLY.
- Book chapter `book/src/advanced/purity.md` with snippets under `book/snippets/advanced/purity/`, generated by `SnippetEmitter`.
- Zero-warning build, formatter, the full ledger, and both registers updated with measured evidence.
