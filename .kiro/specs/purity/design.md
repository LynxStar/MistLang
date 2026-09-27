# Purity Design

The requirements say what a `readonly`, `local` or `pure` member may do. This document says where each decision lives in the v2 compiler, so that the grammar, the model, the analysis phase and Standard agree on one representation. It follows the clean-room rule: nothing here derives from `Mist.Semantics` or `Mist.Analysis`.

## One representation, three consumers

A promise is recorded exactly once, on `MemberModifiers`, as two flags beside the existing `IsReadonly`:

- `IsPure` -- written `pure`, or inherited from a `pure` type.
- `IsLocal` -- written `local`, or inherited by an instance method, property or indexer of a `local` type.

`IsPure` always sets `IsReadonly` as well (Requirement 2.1), at the one place modifiers are built, so every existing readonly rule -- enum bases, `where T : readonly`, readonly parameters, `StableMembers` -- sees a pure member or type as readonly without being told. `local readonly` is `IsLocal` with `IsReadonly`.

A function type's promise is part of the type: `FunctionType.IsPure`, interned separately from the same shape without it.

The three consumers are the binder (placement and conflicts, Requirement 1), the analysis phase (everything a body does, Requirements 4 to 12 and 16) and Standard (Requirement 9). Emission reads none of it (Requirement 13).

## Frontend

### Grammar

- `tokens/keywords.storm`: `token PURE = "pure";` and `token LOCAL = "local";` under a "Purity keywords" heading. Both become reserved: the two test sources that used `local` as a variable name (`ReadonlyAnalyzerPropertyTests`, `AssociatedTypeGenericContextPropertyTests`) rename it.
- `Modifier`: `| PURE as PureModifier` and `| LOCAL as LocalModifier`, giving `PureModifierModifierNode` and `LocalModifierModifierNode`.
- `LambdaModifier`: `| PURE as PureLambdaModifier` and `| LOCAL as LocalLambdaModifier`.
- `SingleTypeRef`: `FuncSingleTypeRef` and `NamedSingleTypeRef` each gain a leading `pure:PURE?`. `ElementTypeRef`'s `FuncElementTypeRef` and `NamedElementTypeRef` likewise. The named form exists for `pure Func<int, int>`, `pure Action<int>` and `pure Step` where `Step` is an alias; the resolver refuses it when what it names is not a function type.

Ordered choice already gives Requirement 16.2 for free: every declaration that admits modifiers reads `Modifier*` before its type, so a leading `pure` on a method, local function, field or property is a modifier, and only a parameter, a local variable, a lambda parameter, a type argument or an alias body reaches the type reading. A `pure` modifier on a field is then refused as a placement error, whose message names the alias route.

`LocalFunctionDeclaration` and `TypeBodyDeclaration` take the new modifiers through `Modifier*` unchanged.

### Model

- `MemberModifiers`: `IsPure`, `IsLocal`.
- `ModifierBuilder.Build`: the two cases; `PURE` sets `IsPure` and `IsReadonly`. Diagnostics, all at the declaration node:
  - `MIST_PM_PURE_AND_LOCAL` (error): both written on one declaration (Requirement 1.4).
  - `MIST_PM_READONLY_IMPLIED_BY_PURE` (info): `pure` and `readonly` both written (Requirement 1.3).
  - `MIST_PM_LOCAL_REQUIRES_INSTANCE` (error): `local` with `static`, or on a constructor (Requirement 1.5).
  - `MIST_PM_PURITY_MODIFIER_PLACEMENT` (error): `pure` or `local` on a field, a type alias, a newtype, an enum, an operator's or cast operator's `local`, or an accessor. Admitted on a class, struct or abstract declaration, a method, a constructor (`pure` only), a property, an indexer, an operator (`pure` only), a cast operator (`pure` only), a local function and a lambda.
- `ModifierBuilder.ForMember`: as it inherits a readonly type's `IsReadonly`, it inherits a pure type's `IsPure` (with `IsReadonly`) onto every member, and a local type's `IsLocal` onto a non-static method, property or indexer that does not itself declare `pure` (Requirements 15.2 and 15.5).
- `NewtypeCloner` copies the two flags as it copies `readonly`.
- `LocalFunctionBinder.Refused` leaves `pure` and `local` admitted.
- `LambdaExpression`: `IsPure` and `IsLocal`. A lambda written `pure` has a pure function type whatever its target; one bound against a pure function type target already has it.
- `FunctionType`: `IsPure`; `Name` is `pure func<...>` when set, so `IsIdentity` separates the two (Requirement 16.6). `IFunctionType.IsPure`. `TypeInterner.GetOrCreateFunctionType` keys on the flag. `TypeSubstitution.Apply` carries it; `DelegateShape.InvokeShape` never sets it. `TypeDisplay.Mist` prints it.
- `ScopeResolver`: a `Pure` token on a function reference re-interns the resolved function type as pure. On a named reference it expands the alias and does the same, or reports `MIST_PM_PURE_REQUIRES_FUNCTION_TYPE` naming the type.
- Conversions (`ConversionClassifier`, `ConversionRelation`): a pure function type converts implicitly to the same shape without `pure`; nothing converts to a pure function type but a pure function type, a lambda or a method group (Requirement 16.5). `CrossesDelegateShape` never admits a CLR delegate into a pure function type. A cast to a pure function type has no conversion and is refused where casts are.
- `OverloadSignature` erases the flag, so two overloads differing only in it are the existing `DuplicateSignature` error (Requirement 16.7).
- The test shells in `AmbientProviderTestHelper.RegisterStandardTypes` set `IsPure` where they set `IsReadonly`, since Standard's declarations do.

### Pretty printer and editors

`MistPrettyPrinter` prints both modifiers, both lambda modifiers and the `pure ` type prefix. The TextMate grammar's `storage.modifier.mist` and both book highlighter keyword lists (`book/theme/mist-lang.js`, `book/theme/highlight.js`) gain `pure` and `local`.

### Emission

Nothing. `TypeNameEmitter.EmitFunction` reads parameters and result only; the modifiers are not read. `[Pure]` is not emitted.

## Analysis phase

Three new files under `Source/Mist/ProgramAnalysis/`, one analyzer, and amendments to two existing rules.

### `Origins`

Where a value comes from, for the scope judging it: `Own`, `Frozen` or `External`. Shared by the purity judge and the readonly amendment, so the two never disagree about what a scope owns.

A scope owns:

- a local or parameter declared `owned`;
- a fresh allocation used directly -- `new`, an array creation or literal, a tuple or anonymous object, a clone, a call whose method returns `owned`;
- a local (never a parameter) whose every assignment is one of those, and which is never passed by `ref` -- the "what it created" of the glossary, read off the body without flow, since the value is fresh at every assignment;
- a struct held by value: a struct-typed local, parameter (not `ref` or `in`) or field, read as a copy;
- for a local scope only, `this` and every owned field of it (`ThisTiers.IsOwned`), and what those own in turn;
- for a lambda or local function, a captured variable the enclosing scope owns.

A value is frozen when it is a `frozen` parameter or local, or `this` in a method declared `with frozen`.

Everything else is external: `this` in a pure scope, an unrestricted or `readonly ref` parameter, a static, a `ref` struct (a span is a view over memory the scope does not own, unless it came from `stackalloc`), and any reference-typed field reached from anything -- an owned root included -- unless that field is itself owned (`ThisTiers.IsOwned`) and the holder is an owning reference: through a by-value struct, `this` of a struct's own member included, an owned reference field is shared with every other copy and so external. A field hop through an unowned reference field is external because the object behind it may be shared, whatever holds the reference.

### `PurityInference`

Requirement 8's greatest fixpoint. Every member with a body -- methods, constructors, operators, cast operators, accessors with bodies, local functions and lambdas -- starts with all three promises. Each round re-judges every body under each promise it still holds, given the promises callees currently hold, and drops what the judge refuses; rounds repeat until nothing changes. A member with no body keeps what its modifiers state (Requirement 9.1); an auto getter over stable storage is pure, over mutable storage is local readonly; an auto setter or init is local. An abstract member keeps what it declares, or else the meet of what every implementation in the program keeps, found through `ConformanceAnalyzer.Fillers` over every concrete type in scope (Requirement 8.4).

A member is pure only when it is also readonly by `ReadonlyInference`, and local readonly only when it is local and readonly by it, so Requirement 2.1 holds by construction rather than by argument.

### `PurityJudge`

One walk over a body under a scope kind -- `Pure`, `Local`, `LocalReadonly` -- yielding findings. The judge is the whole of Requirements 5, 6, 7 and 16.4 in one place:

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
| call any other function value | refused | refused | refused |
| `new` through a pure constructor, allocation of an array, tuple, anonymous object, lambda | admitted | admitted | admitted |
| `new` through any other constructor | refused | refused | refused |
| store a Frozen or External reference of a mutable type into an Own instance, by assignment or as an argument to a Local_Member called on it | refused | refused | -- |
| `await`, an async block, a `with` block | refused | refused | refused |

"Mutable type" excludes structs, readonly types and String. A `local` scope's arguments may be stored, passed on and compared, and a struct argument's fields read; a call through them is a read (Requirement 5.2). Intrinsic operators, conversions and comparisons without a declared operator are pure. A `foreach` reads the collection's elements with the collection's origin. A `default(T)` is pure. An interpolated string is pure. A captured variable read by a pure lambda is external unless the local is `readonly` or has no assignment but its own declaration, or is a parameter never assigned (Requirement 16.4); a `local` lambda or local function treats its captures as its own instance.

Findings, all `MIST_PA_*`, reported through `AnalysisContext.Report`, each naming the operation, the promise and what would be needed (Requirement 10):

- `MIST_PA_PURE_SCOPE_WRITES`: writing what the scope does not own.
- `MIST_PA_PURE_SCOPE_READS_MUTABLE`: reading what can change between reads.
- `MIST_PA_PURE_SCOPE_CALLS_IMPURE`: a call the table refuses, naming the callee's promise and what the call would need.
- `MIST_PA_LOCAL_SCOPE_READS_ARGUMENT`: a read through a reference-typed argument of a local member.
- `MIST_PA_PURE_SCOPE_STORES_UNOWNED`: Requirement 6.4.
- `MIST_PA_PURE_FUNCTION_REQUIRED`: a method group converting to a pure function type whose method is not pure.
- `MIST_PA_DEFAULT_NOT_CONSISTENT`: an operation in a field initializer or default entry that the pure judge refuses, naming the field and the type (Requirement 12.1; defaultability Requirement 6.1).

The scope is described in each message as `pure method 'M'`, `local property 'P'`, `the pure lambda`, `local function 'F'`, `the initializer of field 'F' of 'T'`.

### `PurityAnalyzer`

Id `purity-checking`, order 13 of the analysis register. It walks every body whose member declares a promise, every lambda that is `pure`, `local` or has a pure function type, every local function that declares one, every field initializer of a defaultable type and every default entry, and every settled method group of pure function type, and reports the judge's findings. Inferred promises are never reported (Requirement 3): a member that declares nothing is only ever consulted as a callee.

### Amendments

- `ReadonlyInference` and `ReadonlyAnalyzer` (Requirement 11): a write to a field, setter, index or element of an Own value is not a write of the context, and a call to a Local_Member on an Own receiver is admitted, both through `Origins`. The local-rooted index admission already made is a case of this. Base rule 10 needs nothing: a pure constructor is readonly by the flag.
- `StableMembers` (Requirement 12.2): a property is also stable when its getter is a Pure_Member by `PurityInference`, which admits a getter that calls pure members over stable fields. `DeclarationShapeAnalyzer` builds the inference for that question. The diagnostic message names the pure route.

## Standard

- The intrinsic primitives (`Boolean`, `Byte`, `Char`, `Decimal`, `Double`, `Int16`, `Int32`, `Int64`, `SByte`, `Single`, `UInt16`, `UInt32`, `UInt64`), `String` and `Regex` keep `readonly` on the type and declare every member `pure`: every operation of a value that cannot change is pure, `Clone` included. `pure` cannot replace `readonly` on the type while classic, which parses the keyword and ignores it, judges enum bases and readonly constraints by the type's `readonly`.
- `Math`, `Convert`, `Index` and `Range` are declared `pure` types.
- `Array` needs no declaration: element access is intrinsic and the judge reads it as a field of the receiver.
- `Console`, `Task`, `Type` and `Span` state nothing.
- `Source/Standard/List.mist` declares `intrinsic public class List<T> : IEnumerable<T>` over `System.Collections.Generic.List<T>`, never `owned` (a container cannot keep the cooperation contract for what callers hand it): the constructor `pure`; `Count`, `Contains`, `IndexOf`, `ToArray`, `GetEnumerator` and the indexer's getter `local readonly`; `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear`, `Sort`, `Reverse` and the indexer's setter `local`; its `Enumerator` struct's `MoveNext` local and `Current` local readonly. The surface is what the samples, tests and snippets use, found by measurement. The indexer's two accessors differ, which is why an accessor may state its own promise (Requirement 1.5): `PropertyAccessor.OwnModifiers` records it, and it replaces the declaration's for that accessor everywhere a promise is read.

## Verification

- Parse, print and parse again for each keyword position; the modifiers survive on every declaration kind that admits them.
- Model probes for every conflict and placement diagnostic, for type-level inheritance, for the interned pure function type and each conversion direction.
- Analyzer probes for every row of the judge's table, in each scope kind, and one refusal-corpus case per diagnostic under `refusals/purity-checking/` and `refusals/binding/`.
- Every banked sample still banks: no sample declares a promise, so the only new refusals a sample can meet are Requirement 12's, and any it does meet is measured before it is decided.
- `samples/Purity` exercises declared and inferred promises, `local` on a type, a pure function type and a default over a pure constructor, and is composed into `AllFeaturesV2` as V2-ONLY.
- Book chapter `book/src/advanced/purity.md` with snippets under `book/snippets/advanced/purity/`, generated by `SnippetEmitter`.
- Zero-warning build, formatter, the full ledger, and both registers updated with measured evidence.
