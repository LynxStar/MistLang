# Stampability Design

## One Property, Three Uses

An unmarked type is classified from its storage. The declaration modifier requests validation of that classification for all permitted instances. The generic constraint carries the same proof into an open context. None of these forms invents a default value or changes constructor availability.

The implementation uses v2's existing `Stampability` classifier rather than a second predicate or a cached flag on a generic definition. One generic definition may produce both a stampable `Box<int>` and a non-stampable `Box<string>`. Consequently, declaration intent belongs on the definition, while inferred classification belongs to the receiver being examined.

## Frontend

Add the keyword to the shared grammar's modifier and constraint productions. Preserve the modifier on v2 declarations and introduce a distinct keyword-constraint kind. Extend the pretty printer with both spellings. The syntax is shared; the semantic implementation remains isolated in `ProgramModel`, `ProgramAnalysis`, and the v2 backend.

Type resolution and alias expansion continue through the ordinary v2 resolver. Constraint closure records remain the authoritative inventory of generic usages to check; allocation binding must not create a parallel path that drops closings or constructs fresh type identities.

## Structural Proof

For a receiver, establish its category first. Reject reference types, ref structs, refinements, newtypes, enums, and other existing excluded categories before examining fields. Intrinsic primitives retain their existing acceptance. A tuple qualifies only when every component does.

For a struct receiver:

1. Build the existing receiver substitution from its own and enclosing type arguments.
2. Apply that substitution to inherited receivers and each non-static field type.
3. Recurse into inherited instance storage, ordinary fields, automatic-property backing storage, and backing storage introduced by explicit `field` use in custom accessors.
4. Return the first specific refusal reason or a successful proof.

Computed properties contribute no storage. Static fields do not participate. Initializer expressions and methods do not supply a substitute for structural proof. Readonly and mutability are orthogonal.

Structural inference currently covers source-defined structs, intrinsic primitives, and supported tuples. Arbitrary imported CLR structs remain conservatively non-stampable: ambient projection does not establish complete private and backing-field storage, and SDK reference assemblies may omit implementation layout. Extending inference to those types requires storage metadata and an explicit unknown-layout state, together with deliberate handling of the implicit `System.ValueType` base. Public member lookup alone cannot supply that proof.

An unresolved type parameter qualifies when it has a directly declared `stampable` bound. That proof is available when forwarding the parameter into another stampable-constrained generic use. Indirect bounds such as `where U : T` do not acquire new entailment semantics. `struct`, `readonly`, and `default` alone are insufficient.

## Termination and Identity

A definition-only visited set incorrectly rejects finite nested constructions such as `Box<Box<int>>`. A receiver-only set can fail to terminate when each field expands to a distinct construction. The proof must distinguish active receiver instances while also recognizing recursive expansion that cannot yield finite value storage. Cycles fail conservatively and leave ordinary invalid-layout diagnostics to their existing checker.

Finite substituted nesting must succeed without an arbitrary small depth limit. A failure must not poison later independent classifications of the same generic definition. Substitution and proof may reuse the existing type machinery, but must not replace interned model identities or record spurious generic uses.

## Declaration Validation

A Stage 5 analyzer walks every explicitly marked declaration, including unused declarations. It first rejects invalid declaration categories, then asks the shared classifier to prove the open declaration's storage under its declared constraints.

An unconstrained stored `T` fails even if all current callers happen to use `int`: the declaration promises validity for every permitted instance. An unconstrained marker parameter that is never stored is harmless. Error messages name the declaration and the offending field or storage path.

The modifier is retained for diagnostics and intent. It is not treated as proof without inspecting the declaration, and invalid declarations cannot override the allocation gates.

## Generic Satisfaction

Extend the existing constraint analyzer's keyword dispatch with the shared stampability proof. Check concrete arguments and open forwarded parameters at every recorded closing, including those introduced by aliases. Preserve all existing constraint kinds.

Constraint declaration validation rejects `class, stampable`. Combining `struct, stampable` is permitted; it does not require a new warning. Stampability itself does not grant `default`, `new()`, or other constructor capabilities.

## C# Mapping

The declaration modifier has no emitted C# counterpart. A stampable generic bound maps to the CLR representation guarantee required for generic stack allocation, normally `where T : unmanaged`. When another source constraint contributes `struct`, emit a valid combined representation rather than duplicate mutually exclusive C# primary constraints.

C# `unmanaged` is a backend representation tool rather than the language definition. Mist continues to reject its existing excluded categories even when C# would consider them unmanaged. Arrays and stack allocation retain their existing lowering and size evaluation.

## Verification

- Parse/print/parse tests cover both keyword positions and invalid placements.
- Model probes cover receiver substitution, enclosing arguments, inherited fields, automatic properties, explicit `field` accessor storage, aliases, static exclusions, unused parameters, finite nesting, expansion termination, and existing category gates.
- Analyzer seam tests cover unused invalid guarantees and every new constraint refusal, including forwarding.
- Compiled sample output covers inferred closed allocation and open constrained allocation, including computed sizes with a visible call count.
- Book snippets compile through `SnippetEmitter`; their checked-in C# is generated output. New prose distinguishes structural inference from explicit intent and from generic proof.
- Required build, ledger tests, formatting, sample composition, and registers provide the final evidence.
