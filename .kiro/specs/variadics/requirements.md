# Requirements Document

## Introduction

This document specifies the requirements for adding variadics to the Mist programming language. Variadics arrive in two connected forms, both derived from the approved design:

1. **Type-parameter packs** (variadic generics): a single type parameter that stands for an ordered, arity-varying list of type arguments. A family such as `Func` or a tuple type is written once, for all arities, with both the arity and the filled-in type arguments participating in type identity.
2. **Value-level `params`**: a homogeneous, C#-style variadic value parameter (for example `void Log(params string[] parts)`), where the callee receives a real array and the call site is flat.

The keystone of the design is that a type-parameter pack is not a new kind of entity: it is sugar for a single dependent type parameter whose value domain is a compile-time `Type[]`. Writing `params TArgs` is exactly writing `Foo<Type[]:TArgs>`. This reuses three settled features: a type name in expression position is a `Type` value (type-literals), an array literal such as `[int, string, bool]` is a first-class value (array-literals), and a compile-time value participates in type identity (dependent-types). A variadic family is therefore a type-level function from `Type[]` values to types.

The motivating proof point is collapsing the `Func`/`Action` bootstrap: the 17 hand-written `Func` classes plus 17 `Action` aliases (34 declarations) in `Source/Standard/Bootstrap/Func.mist` and `Source/Standard/Bootstrap/Action.mist` become one declaration each.

These requirements are derived from and consistent with the approved design document (`design.md`). The feature is implemented in the compiler's sole pipeline, the standard pipeline (`Source/Mist/Driver/StandardPipeline.cs`). The feature was first delivered while a second, classic (semantics) pipeline also existed; that pipeline is no longer in the tree, and the design's historical architecture notes describe the arrangement of that time. Where a behavior maps to a diagnostic, the requirement references the design's dedicated `MIST_VAR_*` range so requirements and design remain traceable. All code examples in this document use ASCII-only characters and Mist array-literal syntax (`[a, b, c]`).

## Glossary

### Components

- **Compiler**: The Mist compiler as a whole. It comprises a shared front end (lexing, parsing, and standard-library load) followed by the standard pipeline (`Source/Mist/Driver/StandardPipeline.cs`), which performs program-model construction (Stage 4), analysis (Stage 5), and C# emission (Stage 6). It is the only pipeline.
- **Grammar**: The Storm grammar definition (`Source/Mist/Grammar/Mist.storm`) that defines Mist syntax.
- **Parser**: The Storm-generated parser that produces an abstract syntax tree from a token stream.
- **Pretty_Printer**: The `MistPrettyPrinter` component that formats a Mist AST back into valid Mist source text.
- **Program_Model_Builder**: The model-construction role that builds the resolved type model (types, type parameters, parameters, constructed types) from the AST. It is realized as `ProgramModelBuilder` (`Source/Mist/ProgramModel/`), which produces the program model.
- **Type_Checker**: The design role that validates type rules over the program model. It is not one component: a rule is realized as a diagnostic reported during program-model construction or as a Stage 5 analysis rule (`Source/Mist/ProgramAnalysis/`), and each `MIST_VAR_*` descriptor declares the stage that reports it.
- **Backend**: The C# transpiler stage (`CSharpEmitterV2`, `Source/Mist/Backends/CSharpTranspiler/Emitter/`) that emits C# source from the program model.
- **TextMate_Grammar**: The VS Code syntax highlighting grammar at `Editor/mist-syntax-highlighting/syntaxes/mist.tmLanguage.json`.
- **Standard_Library**: The built-in Mist library at `Source/Standard/`.

### Feature Terms

- **Comptime**: Compile-time. A Comptime value is fully known during compilation and may participate in type identity.
- **Type_Parameter_Pack** (pack): A type parameter that stands for an ordered, Comptime-known, readonly `Type[]` value. Declared as `params TArgs` or `Type[]:TArgs`. It is a dependent type parameter whose value domain is `Type[]`.
- **Type_Array_Domain**: The value domain of a pack: the metatype array `Type[]` (unknown length) or `Type[N]` (fixed length `N`).
- **Unbracketed_Pack**: The single pack in a parameter list that is declared with the `params` modifier and constructed bracket-free at the use site.
- **Bracketed_Pack**: A `Type[]:` pack whose arguments are supplied in brackets (`[...]`) at the use site.
- **Binding_Rule**: The rule that assigns a flat use-site argument list to declared slots by binding fixed scalar slots and Bracketed_Pack slots inward from both ends and assigning the remaining contiguous middle to the single Unbracketed_Pack.
- **Constructed_Variadic_Type**: A variadic type family applied to concrete type arguments, identified by the pair (family, the `Type[]` value bound to its pack slot).
- **Spread**: The postfix `...` operator. A `Type[]` spread (for example `TArgs...`) expands into type slots; a tuple-value spread (for example `args...`) expands into argument slots.
- **Value_Pack**: A value-side pack parameter written `params TArgs args`. Its value `args` has tuple type `(TArgs...)` and its parameter uses the Flattened_Call_Convention. A plain `(TArgs...) args` parameter (no `params`) is instead an ordinary tuple parameter passed as one tuple at the call site.
- **Flattened_Call_Convention**: The calling convention under which a value-side `params` parameter -- a Value_Pack (`params TArgs args`) or a Value_Level_Params (`params T[]`) -- is supplied as a flat, comma-separated argument list at the call site rather than a single packaged argument.
- **Zip**: The parallel expansion of two spread operands element by element (for example `field... = args...`), permitted only when the operands are proven to have equal length.
- **Spread_Generated_Member**: A member produced when a Spread appears in a member-declaring position, named by a Stem plus a 1-based index (`Item1 .. ItemN`).
- **Stem**: The base identifier of a Spread_Generated_Member declaration (for example `Item` in `public TArgs... Item;`).
- **Whole_Pack_Constraint**: A constraint written `where TArgs : IConstraint` that applies elementwise to every element type of the pack.
- **Value_Level_Params**: A homogeneous, C#-style variadic value parameter written `params T[] xs`, where `xs` is a real `T[]` and the call site is flat.
- **Arity_Ceiling**: A Backend-stage limit imposed by a target CLR type's capacity (for example `System.Func`/`System.Action` capping at 16 inputs, `System.ValueTuple` capping at 7 elements before nesting).

### Diagnostics

The design defines a dedicated `MIST_VAR_*` diagnostic range. Requirements reference these codes where relevant. The compiler declares the range's descriptors in `ProgramModelDiagnostics` and, for the Backend ceiling, in `BackendDiagnostics`; a code marked reserved has no descriptor at the current revision, so no diagnostic carries it:

- **MIST_VAR_0001** (Program model): More than one Unbracketed_Pack (`params`) in a single type-parameter or value-parameter list.
- **MIST_VAR_0002** (Type checker): A pack identifier used in bare type position.
- **MIST_VAR_0003** (Type checker): Indexing `TArgs[i]` where the length is not proven to be at least `i + 1`.
- **MIST_VAR_0004** (Type checker): Out-of-bounds index on a fixed-size pack `Type[N]` (for example `TArgs[N]`).
- **MIST_VAR_0005** (Type checker): A flat argument list that cannot be bound because there are too few arguments to fill the fixed and bracketed slots.
- **MIST_VAR_0006** (Type checker, reserved): A Zip between operands not proven to have equal length. No descriptor defines this code.
- **MIST_VAR_0007** (Program model): A Spread Stem that collides with another member.
- **MIST_VAR_0008** (Program model, reserved): An empty Spread Stem. No descriptor defines this code: the grammar requires a Stem identifier on a Spread member declaration, so there is no declaration for it to report on.
- **MIST_VAR_0009** (Type checker, reserved): A pack element that does not satisfy a Whole_Pack_Constraint. No descriptor defines this code.
- **MIST_VAR_0010** (Backend): A construction that exceeds the target CLR type capacity.
- **MIST_VAR_0011** (Type checker): A value-side spread whose expansion cannot be proven: the operand's length is not known where it is spread and the destination is not a slot declared over the same pack, so the number of arguments it stands for is not known for every instantiation (for example a symbolic value pack `args...` spread into a fixed-arity destination such as `Sum3(args...)`). A spread of a tuple whose shape is known is expanded into its elements and judged as ordinary arguments.
- **MIST_VAR_0012** (Program model): A value-side `params` collection parameter (`params T[] xs`, or another collection a flat list is gathered into) that is not the last parameter. Only the homogeneous collection form is pinned to the end; a Value_Pack (`params TArgs args`) binds inward from both ends and may sit anywhere in the list.
- **MIST_VAR_0013** (Program model): A `params` parameter that is neither a Value_Pack nor a collection a flat list is gathered into (an array, a span, a list, or an enumerable interface), or that is passed by reference.

## Requirements

### Requirement 1: Grammar for Pack Declarations, Spreads, and Value-Level params

**User Story:** As a Mist developer, I want the grammar to accept pack declarations, spread syntax, and value-level `params`, so that I can write variadic code that parses into the correct AST.

#### Acceptance Criteria

1. THE Grammar SHALL accept both the `params TArgs` spelling and the explicit `Type[]:TArgs` spelling as valid type-parameter productions, so that the two forms are interchangeable surface spellings of the same dependent type parameter.
2. THE Grammar SHALL accept the fixed-size domain annotation `Type[N]:TArgs`, where `N` is a Comptime length, as a valid pack type-parameter production.
3. THE Grammar SHALL accept a postfix Spread suffix `...` applied to an identifier operand in type-argument lists, argument lists, tuple-type element lists, base-type lists (including abstract types), and member-declaration type positions, regardless of whether the operand denotes a type pack (for example `TArgs...`) or a tuple value (for example `args...`), and SHALL NOT accept the `...` suffix as a value-parameter declaration marker (a value pack parameter is declared with `params`, per acceptance criterion 4).
4. THE Grammar SHALL accept a leading `params` modifier on a value parameter as a valid value-parameter production, whether the parameter's declared type is a pack type parameter (for example `params TArgs args`) or an array type `T[]` (for example `params string[] parts`).
5. WHEN the Parser encounters either a `params` modifier or an explicit `Type[]:` or `Type[N]:` domain annotation inside a type-parameter list, THE Parser SHALL parse the annotated type parameter as a Type_Parameter_Pack declaration.
6. WHEN the Parser encounters a `params` modifier on a value parameter within a value parameter list, THE Parser SHALL parse that parameter as a value-side `params` parameter -- a Value_Pack when its declared type is a pack type parameter (for example `params TArgs args`) and a Value_Level_Params when its declared type is an array type `T[]` (for example `params string[] parts`) -- distinguishing it by its position in the value parameter list from a `params` modifier inside a type-parameter list (which is a Type_Parameter_Pack).
7. WHEN the Parser encounters a flat type-argument list such as `Foo<int, string>`, THE Parser SHALL parse it through the existing type-argument-list rule and SHALL produce a flat type-argument-list node, leaving resolution of the flat form into bracketed pack slots (for example folding `Foo<int, string>` into the canonical `Foo<[int, string]>`) to the Binding_Rule at a later stage.
8. WHEN the Parser encounters more than one `params` modifier in a single type-parameter or value-parameter list (for example `Foo<params A, params B>`), THE Parser SHALL parse each `params` modifier as a valid pack declaration without emitting a parse-error diagnostic, leaving the at-most-one-Unbracketed_Pack check (diagnostic MIST_VAR_0001) to the Program_Model_Builder.

### Requirement 2: Pretty-Printer Round-Trip for Variadic Surface Forms

**User Story:** As a Mist tooling developer, I want the pretty-printer to faithfully reproduce every variadic surface form, so that parse-print-parse round-trips are stable.

#### Acceptance Criteria

1. WHEN the Pretty_Printer encounters a pack declared with the `params` modifier, THE Pretty_Printer SHALL emit the `params TArgs` form, preserving the declared pack identifier (for example, a pack named `CtorArgs` prints as `params CtorArgs`).
2. WHEN the Pretty_Printer encounters a pack declared with an explicit domain annotation, THE Pretty_Printer SHALL emit the `Type[]:TArgs` form for an unknown-length domain and the `Type[N]:TArgs` form for a fixed-size domain, preserving the declared pack identifier and, for a fixed-size domain, the Comptime length `N`.
3. WHEN the Pretty_Printer encounters a Spread, THE Pretty_Printer SHALL emit the Spread operand immediately followed by the `...` token, preserving the operand text (for example a type-pack operand prints as `TArgs...` and a tuple-value operand prints as `args...`).
4. WHEN the Pretty_Printer encounters a value-side `params` parameter, THE Pretty_Printer SHALL emit the `params` modifier followed by the declared type and parameter name, preserving the target form so that a Value_Pack prints as `params TArgs name` (for example `params TArgs args`) and a Value_Level_Params prints as `params T[] name` (for example `params string[] parts`).
5. WHEN the Pretty_Printer encounters a use-site type-argument list, THE Pretty_Printer SHALL emit a flat argument list (for example `Foo<int, string>`) in flat form and a bracketed pack argument list (for example `Foo<[int, string]>`) in bracketed form, preserving the flat-versus-bracketed distinction so that neither surface form is normalized into the other.
6. FOR ALL valid Mist ASTs containing pack declarations (in both the `params` and `Type[]:`/`Type[N]:` spellings), Spreads, flat type-argument lists, bracketed type-argument lists, Value_Pack parameters (`params TArgs args`), and Value_Level_Params parameters (`params T[] name`), pretty-printing the AST and then parsing the printed text SHALL produce a second AST that is structurally equal to the original, meaning identical node kinds, nesting, declared identifiers, element types, fixed-size domain lengths, and modifiers, and the same flat-versus-bracketed choice for each argument list, while disregarding source positions and formatting trivia (round-trip property).

### Requirement 3: Pack Declaration Rules and Arity

**User Story:** As a Mist developer, I want clear rules for declaring packs, so that I can write single-pack and multi-pack families unambiguously.

#### Acceptance Criteria

1. WHERE a type parameter is declared with the `params` modifier, THE Program_Model_Builder SHALL treat `params TArgs` as sugar for the dependent type parameter `Type[]:TArgs` and SHALL classify `TArgs` as an Unbracketed_Pack, granting bracket-free construction at the use site.
2. WHERE the only type-array parameter in a type-parameter list is written in the explicit `Type[]:` form, THE Program_Model_Builder SHALL treat that lone pack as an Unbracketed_Pack (inverse sugar), so that a single-pack family never requires brackets.
3. THE Program_Model_Builder SHALL permit more than one `Type[]:` pack in a single type-parameter list, and SHALL classify each such pack that is not an Unbracketed_Pack as a Bracketed_Pack whose arguments are supplied in brackets `[...]` at the use site.
4. THE Program_Model_Builder SHALL permit at most one Unbracketed_Pack per type-parameter list.
5. IF a type-parameter list declares more than one Unbracketed_Pack, THEN THE Program_Model_Builder SHALL emit diagnostic MIST_VAR_0001.
6. THE Program_Model_Builder SHALL compute arity as the count of declared slots, counting each type-array pack slot (whether spelled `params` or `Type[]:`) as exactly one slot regardless of the number of types the slot later absorbs (for example `Foo<TVal, Type[]:TArgs, params CtorArgs, Type[]:Args3, TRes>` has arity 5).

### Requirement 4: Binding Rule

**User Story:** As a Mist developer, I want a single pack to bind consistently whether it appears at the start, middle, or end of a parameter list, so that I can place the pack wherever the design reads best.

#### Acceptance Criteria

1. WHERE a type-parameter or value-parameter list contains a single Unbracketed_Pack, THE Type_Checker SHALL permit that pack in leading, middle, or trailing position.
2. WHEN binding a flat use-site argument list to a declared slot list containing at most one Unbracketed_Pack, THE Type_Checker SHALL bind the leading arguments one-to-one and in declaration order to the fixed scalar and Bracketed_Pack slots that precede the Unbracketed_Pack, SHALL bind the trailing arguments one-to-one and in declaration order to the fixed scalar and Bracketed_Pack slots that follow it, and SHALL assign the remaining contiguous middle arguments (which may contain zero arguments) to the Unbracketed_Pack as an ordered Type array, where the count of leading arguments equals the count of slots before the Unbracketed_Pack and the count of trailing arguments equals the count of slots after it.
3. THE Type_Checker SHALL permit an empty Unbracketed_Pack, binding it to the empty type array so that an arity-0 pack is well-formed (for example `Func<string>` binds `TArgs` to `[]` and `TResult` to `string`).
4. THE Type_Checker SHALL treat the flat construction form and the bracketed canonical form of the same pack as denoting the same type (for example `Foo<int, string>` and `Foo<[int, string]>`).
5. WHEN binding the declaration `class Between<TFirst, params TMid, TLast>` at the use site `Between<int, string, bool, double>`, THE Type_Checker SHALL bind `TFirst` to `int`, `TLast` to `double`, and `TMid` to `[string, bool]`.
6. IF a flat argument list supplies fewer arguments than the number of fixed scalar slots plus the number of Bracketed_Pack slots (the minimum required to fill every non-pack slot while the Unbracketed_Pack absorbs zero arguments), THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0005 indicating too few arguments to fill the fixed scalar and Bracketed_Pack slots.

### Requirement 5: Type Identity of Constructed Variadic Types

**User Story:** As a Mist developer, I want variadic type identity to depend on both arity and the ordered element types, so that differently-shaped constructions are distinct types.

#### Acceptance Criteria

1. THE Type_Checker SHALL identify a Constructed_Variadic_Type by the pair of its type family and the Comptime `Type[]` value bound to its pack slot.
2. THE Type_Checker SHALL treat two Constructed_Variadic_Types as equal if and only if they share the same type family and their pack slots bind equal `Type[]` values; two `Type[]` values are equal exactly when they have the same length and, at each index, their element `Type` values are equal under the type-equality relation used for ordinary type arguments.
3. WHERE two constructions of the same family bind pack `Type[]` values that contain the same element types in a different order, THE Type_Checker SHALL treat them as distinct types (for example `Foo<[int, string]>` and `Foo<[string, int]>`).
4. WHERE two constructions of the same family bind pack `Type[]` values of different length (different pack arity), THE Type_Checker SHALL treat them as distinct types (for example `Foo<[int, string]>`, `Foo<[int, string, bool]>`, and the empty-pack `Foo<[]>` are pairwise distinct).

### Requirement 6: Position Semantics and Capability Ladder

**User Story:** As a Mist developer, I want the meaning of a pack identifier to depend on its position and on what is proven about its length, so that I can use packs safely without unproven array access.

#### Acceptance Criteria

1. WHERE a pack identifier appears in expression position, THE Type_Checker SHALL treat it as the Comptime `Type[]` value.
2. THE Type_Checker SHALL make the size-agnostic operations length query (`.Length`), iteration, and Spread available for a pack regardless of whether its length is proven.
3. IF a pack identifier appears in bare type position (for example `public TArgs Item;`), THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0002.
4. IF a pack with the unknown-length Type_Array_Domain `Type[]` is indexed as `TArgs[i]` in type position, where its length is not proven to be at least `i + 1`, THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0003.
5. WHERE a pack has the fixed-size Type_Array_Domain `Type[N]`, THE Type_Checker SHALL permit indexing `TArgs[i]` for a Comptime index `i` in the range `0` through `N - 1`, yielding the Comptime `Type` value at that index.
6. IF a pack with the fixed-size Type_Array_Domain `Type[N]` is indexed as `TArgs[i]` with a Comptime index `i` outside the range `0` through `N - 1` (for example `TArgs[N]` or a negative index), THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0004.
7. WHEN a guard narrows an unknown-length pack to a fixed-size domain of size `N` (for example `if (TArgs is Type[3] fixed)`), THE Type_Checker SHALL permit indexing the narrowed binding with a Comptime index in the range `0` through `N - 1` within that branch.
8. WHERE a pack identifier is followed by the Spread suffix `TArgs...`, THE Type_Checker SHALL expand the pack in place into a count of positions equal to the pack length, substituting each element type in order, so that an empty pack expands to zero positions.

### Requirement 7: Value Side of a Pack

**User Story:** As a Mist developer, I want the value counterpart of a pack to be a real first-class value, so that I can store, return, forward, and inspect a heterogeneous argument list.

#### Acceptance Criteria

1. WHERE a method parameter is declared `params TArgs args`, THE Program_Model_Builder SHALL give `args` the tuple type `(TArgs...)` (equivalently `ValueTuple<TArgs>`), SHALL represent it as exactly one parameter slot introduced by the `params` modifier, and SHALL mark the parameter with the Flattened_Call_Convention.
2. THE Program_Model_Builder SHALL count method parameter arity as the number of declared parameter slots, counting a value pack `params TArgs args` as exactly one slot, so that `Foo(int number, params TArgs args, bool enabled)` has arity 3 (mirroring the type-parameter side, where `class Foo<TValue, params TArgs, TResult>` has arity 3).
3. THE Type_Checker SHALL permit a Value_Pack value of tuple type `(TArgs...)` to be stored in a local of that tuple type, returned from a method whose declared return type is that tuple type, and passed as an argument to a parameter of that tuple type.
4. WHEN a method that declares fixed parameters positioned before, after, or on both sides of a value pack `params TArgs args` is invoked with a flat argument list, THE Type_Checker SHALL bind the fixed parameters positionally inward from both ends per the Binding_Rule and SHALL collect the remaining contiguous middle arguments into `args` as the tuple `(TArgs...)` (for example, given `Foo(int number, params TArgs args, bool enabled)`, the call `Foo(5, a, b, c, true)` binds `number` to `5`, `enabled` to `true`, and `args` to `(a, b, c)`).
5. WHERE a Value_Pack value is followed by the Spread suffix `args...` in argument position, THE Type_Checker SHALL expand the tuple into exactly N individual argument slots in element order, where N is the number of elements of the tuple `(TArgs...)` (for example, a Value_Pack whose pack is bound to `[int, string]` expands into two argument slots).
6. IF a method declaring a value pack `params TArgs args` is invoked with a flat argument list containing too few arguments to fill the method's fixed parameters, THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0005.
7. WHERE a value parameter is declared as a plain tuple parameter `(TArgs...) args` without the `params` modifier, THE Type_Checker SHALL treat it as an ordinary single-tuple parameter that receives one tuple argument at the call site rather than a flat argument list, so that the `params` modifier is the sole marker of the Flattened_Call_Convention.
8. THE Program_Model_Builder SHALL permit at most one value-side `params` parameter per value-parameter list, counting both a Value_Pack (`params TArgs args`, target tuple `(TArgs...)`) and a Value_Level_Params (`params T[] xs`, target array) as a `params` parameter, and SHALL require any additional pack in the same list to be declared as an explicit plain tuple parameter `(TArgs...) name` (for example `Foo(params TArgs first, (TArgs2...) second)` and `Foo((TArgs...) a, (TArgs2...) b, params string[] names)`).
9. IF a value-parameter list declares more than one value-side `params` parameter (whether targeting tuples, arrays, or a mix), THEN THE Program_Model_Builder SHALL emit diagnostic MIST_VAR_0001.

### Requirement 8: Spread, Forward, and Zip

**User Story:** As a Mist developer, I want spreading to work uniformly for types and values and to support parallel expansion, so that I can forward packs and correlate two packs safely.

#### Acceptance Criteria

1. WHERE a Comptime `Type[]` operand is spread (for example `TArgs...`), THE Type_Checker SHALL expand the operand into a number of type slots equal to the operand's length (`.Length`), preserving element order so that the element at each position of the operand occupies the corresponding expanded type slot, and expanding an empty operand into zero type slots.
2. WHERE any tuple value is spread (for example the value pack `args...`, or an ordinary tuple `(7, "foo", false)...`), THE Type_Checker SHALL decompose the tuple into a number of argument slots equal to the tuple's element count, preserving element order so that the element at each position occupies the corresponding argument slot, and expanding an empty tuple into zero argument slots.
3. THE Type_Checker SHALL apply tuple decomposition (the value-side Spread) to every tuple value, not only to value packs, so that a Value_Pack `args...` is simply the case where the tuple originated from a pack.
4. WHEN a tuple value is decomposed into argument or parameter slots (for example `SomeMethod(x...)`), THE Type_Checker SHALL treat the decomposition as well-typed if and only if the tuple's ordered element types match the target slots positionally under assignability, and SHALL otherwise emit diagnostic MIST_VAR_0011.
5. WHERE two operands are spread in parallel as a Zip (for example `field... = args...`), THE Type_Checker SHALL expand them element by element into `N` paired positions, where `N` is the common length of the two operands, pairing the operands position by position so that each left-hand slot receives exactly one right-hand value.
6. THE Type_Checker SHALL permit a Zip only when the two operands are proven to have equal length, either because they originate from the same pack or because equal length has otherwise been established.
7. IF a Zip is attempted between operands not proven to have equal length, THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0006.

### Requirement 9: Spread-Accepting Positions

**User Story:** As a Mist developer, I want spreads to be accepted in every position the design allows, so that I can expand a pack wherever a list of types or values is expected.

#### Acceptance Criteria

1. WHERE a Comptime `Type[]` Spread appears in a type-argument list (for example `Func<TArgs..., void>` in `type Action<params TArgs> = Func<TArgs..., void>;`), THE Type_Checker SHALL treat the Spread as well-formed in that position, SHALL expand it into one type-argument per element of the spread `Type[]` (so a pack of length 2 contributes 2 type-arguments), and SHALL NOT emit a diagnostic attributable to the Spread's position.
2. WHERE a Comptime `Type[]` Spread appears in a field declaration (for example `public TArgs... Item;`), THE Type_Checker SHALL treat the Spread as well-formed in that position and SHALL NOT emit a diagnostic attributable to the Spread's position.
3. WHERE a Comptime `Type[]` Spread appears in a base-type list (including implemented abstract types) (for example `: TArgs...`), THE Type_Checker SHALL treat the Spread as well-formed in that position, SHALL expand it into one base-type entry per element of the spread `Type[]`, and SHALL NOT emit a diagnostic attributable to the Spread's position.
4. WHERE a Comptime `Type[]` Spread appears in a tuple-type element list, including a tuple type used as a return type (for example `(TArgs...)`), THE Type_Checker SHALL treat the Spread as well-formed in that position, SHALL expand it into one tuple element per element of the spread `Type[]`, and SHALL NOT emit a diagnostic attributable to the Spread's position.
5. THE Type_Checker SHALL NOT treat a value-parameter declaration as a Spread position, because a value pack parameter is declared with the `params` modifier (`params TArgs args`, per Requirement 7) rather than with a `...` spread suffix.

### Requirement 10: Spread-Generated Members

**User Story:** As a Mist developer, I want a spread in a member-declaring position to generate an indexed family of members, so that a variadic type can expose one member per pack element.

#### Acceptance Criteria

1. WHERE a Spread of a Type_Parameter_Pack appears in a member-declaring position with Stem `S` (for example `public TArgs... Item;`), THE Program_Model_Builder SHALL declare an aggregate member `S` of tuple type `(TArgs...)` (the whole ValueTuple) together with exactly `N` element-projection members `S1` through `SN` (1-based), where `N` is the pack length, each projection `Sk` has the pack's element type at 0-based index `k - 1` and denotes the k-th element of `S`, and the aggregate and all projections take the same accessibility and member kind (field or property) as the Spread declaration, so that an instance exposes both the tuple `S` and its elements `Sk`.
2. THE Program_Model_Builder SHALL reserve the Stem namespace so that the Spread owns the aggregate name `S` and every `S{i}` projection name; no other member may use the Stem `S` or any `S{i}` name, and no two Spread Stems may prefix one another.
3. IF a member outside the Spread family uses the Stem `S` or an `S{i}` name, or two Spread Stems prefix one another, THEN THE Program_Model_Builder SHALL emit diagnostic MIST_VAR_0007.
4. THE Program_Model_Builder SHALL require a non-empty Stem.
5. IF a Spread declares an empty Stem, THEN THE Program_Model_Builder SHALL emit diagnostic MIST_VAR_0008.
6. WHERE the pack is empty, THE Program_Model_Builder SHALL produce the empty aggregate and zero element projections.
7. WHERE the pack has length 1, THE Program_Model_Builder SHALL produce the aggregate `S` of type `(T)` and the single projection `S1` (the numeric suffix is always present).
8. WHERE two Spreads of the same pack appear in one type (for example `Key...` and `Value...`), THE Program_Model_Builder SHALL produce for each Spread index-aligned element projections at the same count `N` (for example `Key1 .. KeyN` and `Value1 .. ValueN`).
9. THE Type_Checker SHALL permit Spread-Generated members to be re-spread, so that `S...` spreads the aggregate's elements.

### Requirement 11: Whole-Pack Constraints

**User Story:** As a Mist developer, I want a constraint on a pack to apply to every element, so that I can call constrained members on each element and on each `args[i]`.

#### Acceptance Criteria

1. WHERE a Type_Parameter_Pack is declared with a Whole_Pack_Constraint `where TArgs : IConstraint`, THE Type_Checker SHALL require every element type bound to the pack to independently satisfy the constraint.
2. WHERE a Type_Parameter_Pack is declared with a Whole_Pack_Constraint, THE Type_Checker SHALL permit any member declared by the constraint to be used on each element type of the pack and on each element value `args[i]` of the corresponding Value_Pack.
3. IF any element type bound to a Type_Parameter_Pack that declares a Whole_Pack_Constraint does not satisfy the constraint, THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0009 for each such element type and SHALL treat each offending element as an error type so that downstream analysis continues.
4. WHERE a Type_Parameter_Pack that declares a Whole_Pack_Constraint is bound to the empty type array `[]`, THE Type_Checker SHALL treat the Whole_Pack_Constraint as satisfied and SHALL NOT emit diagnostic MIST_VAR_0009.

### Requirement 12: Method-Level Packs

**User Story:** As a Mist developer, I want a method to declare its own pack independent of the enclosing type, so that I can write variadic methods that forward their arguments.

#### Acceptance Criteria

1. THE Program_Model_Builder SHALL permit a method to declare its own Type_Parameter_Pack (for example `int SomeMethod<params TArgs>(params TArgs args)`) whether or not the enclosing type declares a Type_Parameter_Pack, treating the method's pack as a slot distinct from, and not constrained by, any pack on the enclosing type.
2. WHERE a method declares the value parameter `params TArgs args`, THE Type_Checker SHALL treat `args` within the method body as a Value_Pack whose type is the tuple `(TArgs...)`.
3. WHERE a method body passes `args...` in an argument position of a call (for example `Consume(args...)`), THE Type_Checker SHALL expand the Value_Pack into one argument per pack element, in declaration order, filling the callee's corresponding argument slots.
4. THE Program_Model_Builder SHALL permit at most one Unbracketed_Pack per method type-parameter list.
5. IF a method type-parameter list declares more than one Unbracketed_Pack, THEN THE Program_Model_Builder SHALL emit diagnostic MIST_VAR_0001.
6. WHEN the Type_Checker checks a variadic method body, THE Type_Checker SHALL require the body to be well-typed for every instantiation the signature and constraints permit, applying the same open-generic type-checking discipline used for any type parameter (for example, in `class Foo<TValue> { int Bar(TValue value) { return Double(value); } }`, `Double(value)` is rejected because `TValue` is not proven to be `int`).
7. WHERE a variadic method body forwards a value pack with `args...`, THE Type_Checker SHALL accept the spread when its destination provably accepts the pack `(TArgs...)` for every `TArgs` -- another `params TArgs` or `(TArgs...)` parameter, or a `Func<TArgs..., R>` value.
8. IF a variadic method body spreads a value pack `args...` into a fixed-arity destination that is not provably `(TArgs...)`-compatible for every instantiation (for example `Sum3(args...)` where `Sum3` takes `(int, int, int)`), THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0011.
9. WHERE a variadic method body guards a value pack with `args is T x` for a concrete tuple type `T` (for example `args is (int, int, int) triple`), THE Type_Checker SHALL narrow the pack to `T` within that branch so that spreading `x...` into a destination whose slots match `T` is well-typed (recovering the forwarding that criterion 8 rejects for an unnarrowed symbolic pack), and IF a spread within such a branch targets a destination that does not match the narrowed shape `T`, THEN THE Type_Checker SHALL emit diagnostic MIST_VAR_0011.

### Requirement 13: Value-Level params

**User Story:** As a Mist developer, I want a homogeneous, C#-style `params` value parameter, so that I can accept a variable number of same-typed arguments with a real array and a flat call site.

#### Acceptance Criteria

1. WHERE a value parameter is declared `params T[] xs`, THE Program_Model_Builder SHALL represent `xs` as a homogeneous Value_Level_Params whose parameter type is the array type `T[]` and SHALL mark it with the Flattened_Call_Convention.
2. WHEN a method with a Value_Level_Params parameter `params T[] xs` is invoked with a flat argument list whose trailing arguments (those supplied after the fixed leading parameters) are each assignable to the element type `T`, THE Type_Checker SHALL collect those trailing arguments in left-to-right source order into a single `T[]` value and bind it to `xs`.
3. WHEN a method with a Value_Level_Params parameter `params T[] xs` is invoked with zero trailing arguments, THE Type_Checker SHALL bind `xs` to a non-null `T[]` of length zero.
4. WHERE `params` appears in a value parameter list on a `T[]`-typed parameter, THE Program_Model_Builder SHALL interpret it as a Value_Level_Params, distinguishing it by position from a Type_Parameter_Pack declared inside a type-parameter list.
5. THE Program_Model_Builder SHALL count a Value_Level_Params as a value-side `params` parameter for the purpose of the at-most-one-`params`-per-value-parameter-list rule defined in Requirement 7, so that a value-parameter list may not contain a Value_Level_Params together with any other value-side `params` parameter (whether a Value_Pack or another Value_Level_Params), subject to diagnostic MIST_VAR_0001 when violated.
6. THE Backend SHALL emit a Value_Level_Params parameter as a C# `params T[]` array parameter so that the callee receives a real array.
7. IF a method with a Value_Level_Params parameter `params T[] xs` is invoked with a trailing argument that is not assignable to the element type `T`, THEN THE Type_Checker SHALL reject the invocation with a type error indicating that the argument is not assignable to `T` and SHALL NOT bind `xs`.

### Requirement 14: Call-Site Pack Inference

**User Story:** As a Mist developer, I want the compiler to infer a type pack from value arguments, so that I can call variadic methods without writing explicit type arguments.

#### Acceptance Criteria

1. WHEN a variadic method whose sole type parameter is a pack (such as `M<params TArgs>(params TArgs args)`) is invoked with value arguments and without explicit type arguments (for example `M(1, "x", true)`), THE Type_Checker SHALL infer the pack so that its elements are the static types of the value arguments in left-to-right source order (for example `TArgs = [int, string, bool]`).
2. WHEN a variadic method that declares one or more fixed scalar type parameters together with a pack is invoked without explicit type arguments, THE Type_Checker SHALL resolve the fixed scalar type parameters and the pack together in a single binding using the Binding_Rule (the same rule that resolves the flat form into the bracketed form), binding the fixed scalar type parameters positionally inward from both ends and binding the pack to the remaining contiguous value arguments.
3. WHERE explicit type arguments are supplied (for example `M<[int, string, bool]>()`), THE Type_Checker SHALL bind the pack from the explicit bracketed type arguments instead of inferring it from the value arguments.
4. WHEN a variadic method whose sole type parameter is a pack (such as `M<params TArgs>(params TArgs args)`) is invoked with zero value arguments and without explicit type arguments, THE Type_Checker SHALL infer the empty pack (for example `TArgs = []`).

### Requirement 15: Func / Action Bootstrap Rewrite and Invocation Generalization

**User Story:** As a Mist standard-library maintainer, I want the callable family written once using a pack, so that the 34 hand-written `Func`/`Action` declarations collapse to one declaration each.

#### Acceptance Criteria

1. THE Standard_Library SHALL declare the callable family in `Source/Standard/Bootstrap/Func.mist` as exactly one intrinsic class `Func<params TArgs, TResult>`, in which the trailing `TResult` slot is the return type and the `TArgs` pack absorbs the parameter types, so that the previously hand-written per-arity `Func` class declarations are collapsed into this single declaration.
2. THE Standard_Library SHALL declare `Action` in `Source/Standard/Bootstrap/Action.mist` as exactly one alias `type Action<params TArgs> = Func<TArgs..., void>;`, so that the previously hand-written per-arity `Action` aliases are collapsed into this single alias.
3. WHEN `Func<int, string>` is constructed, THE Type_Checker SHALL bind `TArgs` to `[int]` and `TResult` to `string` per the Binding_Rule.
4. WHEN `Func<string>` is constructed, THE Type_Checker SHALL bind `TArgs` to the empty type array `[]` and `TResult` to `string` per the Binding_Rule, so that the empty pack yields the zero-argument callable.
5. WHEN `Action<int, string>` is constructed, THE Type_Checker SHALL resolve the alias to `Func<int, string, void>` and bind `TArgs` to `[int, string]` and `TResult` to `void`.
6. WHEN a value of a constructed `Func<params TArgs, TResult>` type is followed by an invocation suffix whose argument list has the same length as `TArgs` and where each argument is assignable to the corresponding element type of `TArgs`, THE Type_Checker SHALL treat the invocation as well-typed and SHALL give the invocation the result type `TResult`.
7. IF a value of a constructed `Func<params TArgs, TResult>` type is followed by an invocation suffix whose argument list differs in length from `TArgs`, or where an argument at some position is not assignable to the corresponding element type of `TArgs`, THEN THE Type_Checker SHALL reject the invocation with a type error indicating that the argument list does not match `TArgs`, and SHALL NOT give the invocation the result type `TResult`.
8. THE Type_Checker SHALL apply the same generalized `Func` invocation rule for a `Func` value of any arity, including the empty pack (a zero-argument invocation), and SHALL NOT retain any per-arity invocation recognition.

### Requirement 16: Backend Arity Ceiling

**User Story:** As a Mist developer, I want core variadic semantics to be unbounded while the C# backend clearly reports when a construction exceeds a target CLR limit, so that arity limits are a backend concern rather than a language limit.

#### Acceptance Criteria

1. THE Compiler SHALL, within the Grammar, Program_Model_Builder, and Type_Checker stages, accept a Type_Parameter_Pack or Value_Pack of any element count (0 or greater) and SHALL NOT emit MIST_VAR_0010 or reject a construction on the basis of pack element count at those stages, so that pack arity is unbounded in the front end and any Arity_Ceiling is confined to the Backend.
2. IF the Backend maps a construction to a `System.Func` or `System.Action` shape that would require more than 16 input parameter types, THEN THE Backend SHALL emit diagnostic MIST_VAR_0010 identifying the offending construction and the exceeded target CLR capacity.
3. WHEN the Backend maps a Value_Pack of more than 7 elements to `System.ValueTuple`, THE Backend SHALL represent the construction as nested `System.ValueTuple` values and SHALL NOT emit MIST_VAR_0010 for that construction.
4. IF the Backend cannot represent a construction within the target CLR type capacity (a `System.Func` or `System.Action` over 16 input parameter types, or a Value_Pack over 7 elements that cannot be represented as nested `System.ValueTuple` values), THEN THE Backend SHALL emit diagnostic MIST_VAR_0010 at the Backend stage, identifying the offending construction and the exceeded target CLR capacity, without imposing any constraint or emitting any diagnostic at the Grammar, Program_Model_Builder, or Type_Checker stages.
5. WHEN the Backend maps a construction that fits the target CLR type capacity (a `System.Func` or `System.Action` with 16 or fewer input parameter types, or a Value_Pack of 7 or fewer elements), THE Backend SHALL emit the corresponding C# shape without emitting MIST_VAR_0010.

### Requirement 17: TextMate Grammar Update

**User Story:** As a Mist developer using VS Code, I want pack declarations and spreads to be syntax-highlighted correctly, so that I get clear visual cues for variadic syntax.

#### Acceptance Criteria

1. THE TextMate_Grammar SHALL highlight the `params` modifier in a type-parameter or value-parameter position as a storage modifier (scope `storage.modifier.mist`).
2. THE TextMate_Grammar SHALL highlight the Spread suffix `...` following a pack or tuple identifier with an appropriate operator or punctuation scope distinct from the surrounding identifier.
3. THE TextMate_Grammar SHALL highlight the `Type[]:` and `Type[N]:` domain annotation on a pack declaration so that the pack name is highlighted consistently with other type parameters (scope `entity.name.type.parameter.mist`).

### Requirement 18: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating variadics, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/Variadics/Program.mist` with a corresponding `samples/Variadics/mist.toml` project file whose `name` field is set to `"Variadics"`.
2. The sample SHALL demonstrate every usage axis defined in the preceding requirements, including but not limited to: the single-declaration `Func` family (arity 1) with invocation, the empty-pack `Func<TResult>` case, the `Action` alias spreading a pack into `Func`, a method-level type pack with call-site inference and expression-position `TArgs.Length`, explicit bracketed construction `M<[...]>(...)`, a middle pack bound by the Binding_Rule, multiple bracketed `Type[]:` packs disambiguated at the use site, forwarding a value pack with `args...` into a `Func` of the same pack, value-pack narrowing with `args is (concrete tuple)` that recovers fixed-arity forwarding across branches of differing length and element type, general tuple decomposition of a plain tuple value, spread-generated members (the aggregate tuple and the indexed projections, in both field and property kinds) populated by a zip constructor and re-spread into a Func, a fixed-size `Type[N]` pack with a whole-pack constraint used elementwise, homogeneous value-level `params`, a variadic abstract type declared and implemented (via a base-type-list spread), and a variadic struct.
3. Each demonstrated pattern SHALL print labeled output to stdout so correctness is observable.
4. The sample SHALL include a `public static void RunDemo()` entry point that exercises the demonstrated patterns.
5. `samples/AllFeatures/Program.mist` SHALL call `Variadics.Program.RunDemo()` and `samples/AllFeatures/mist.toml` SHALL include a reference entry for the Variadics project, so that the feature is exercised in the full-suite sample run.
6. The sample SHALL compile and run without errors or warnings using the Mist compiler.
7. The sample SHALL contain the proof code block below verbatim and produce the specified expected output, matched line-by-line exactly.

#### Proof Code

```mist
using Standard;

namespace Variadics;

public abstract IShape
{
    int Area();
}

public struct Circle : IShape
{
    public int Area()
    {
        return 3;
    }
}

public struct Square : IShape
{
    public int Area()
    {
        return 4;
    }
}

// PROOF: spread-generated members. `public TArgs... Item;` surfaces the aggregate `Item`
//        (the whole (TArgs...) tuple) and the element projections Item1..ItemN. The
//        constructor zips the value pack into the members; Map re-spreads them (R10.9).
public class Bundle<params TArgs>
{
    public TArgs... Item;

    public Bundle(params TArgs values)
    {
        Item... = values...;
    }

    public TResult Map<TResult>(Func<TArgs..., TResult> f)
    {
        return f(Item...);
    }
}

// PROOF: spread-generated members as a property kind -- `public TArgs... Slot { get; }`
//        generates get-only property projections Slot1..SlotN.
public class ReadOnlyRow<params TArgs>
{
    public TArgs... Slot { get; }

    public ReadOnlyRow(params TArgs values)
    {
        Slot... = values...;
    }
}

// PROOF: fixed-size pack Type[2] + whole-pack constraint. `where TShapes : IShape` makes
//        Area() callable on each spread-generated member (Shape1, Shape2) even though their
//        types are the symbolic pack elements TShapes[0] and TShapes[1].
// PROOF: a variadic STRUCT (value type) carrying a fixed-size pack and a whole-pack constraint.
public struct ShapePair<Type[2]:TShapes> where TShapes : IShape
{
    public TShapes... Shape;

    public ShapePair(params TShapes shapes)
    {
        Shape... = shapes...;
    }

    public int TotalArea()
    {
        return Shape1.Area() + Shape2.Area();
    }
}

// PROOF: a base whose only constructor takes a value pack is callable with nothing exactly when
//        the base clause closes the pack empty (R4.3), so a derived constructor may omit its chain;
//        the omitted chain passes the empty tuple the flattened convention packs (R7.1).
public class Counted<params TArgs>
{
    public int Width;

    public Counted(params TArgs values)
    {
        Width = TArgs.Length;
    }
}

public class Uncounted : Counted<[]>
{
    public Uncounted()
    {
    }
}

public struct Sized<params TArgs>
{
    public int Width;

    public Sized(params TArgs values)
    {
        Width = TArgs.Length;
    }
}

public struct Unsized : Sized<[]>
{
    public Unsized()
    {
    }
}

// PROOF: a variadic abstract type (Mist's interface-like construct) declared with a pack.
abstract ICountable<params TArgs>
{
    int Count();
}

// PROOF: a variadic class implements the variadic abstract type by spreading its own pack
//        into the base-type list (ICountable<TArgs...>).
public class Tup<params TArgs> : ICountable<TArgs...>
{
    public int Count()
    {
        return TArgs.Length;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== Variadics Sample ===");

        // PROOF: the single Func<params TArgs, TResult> declaration covers arity 1; invoking it
        //        is well-typed because the argument list matches TArgs=[int] and returns TResult=string.
        Func<int, string> label = n => $"n={n}";
        Console.WriteLine(label(5));

        // PROOF: the empty pack TArgs=[] yields Func<string>, the zero-argument case.
        Func<string> greet = () => "empty-pack";
        Console.WriteLine(greet());

        // PROOF: Action<params TArgs> = Func<TArgs..., void> spreads the pack into Func.
        Action<int> act = n => Console.WriteLine($"act={n}");
        act(7);

        // PROOF: method-level pack with call-site inference (TArgs=[int, string, bool]);
        //        TArgs.Length reads the arity in expression position.
        var arity = Describe(1, "x", true);
        Console.WriteLine($"arity={arity}");

        // PROOF: explicit bracketed construction supplies the pack directly instead of inferring.
        var explicitArity = Describe<[int, string, bool]>(1, "x", true);
        Console.WriteLine($"explicit={explicitArity}");

        // PROOF: a middle pack bound by the Binding_Rule -- first and last bind inward from the
        //        ends, the pack absorbs the middle, so TMid=[string, string] and its length is 2.
        var middle = MiddleCount(1, "a", "b", true);
        Console.WriteLine($"middle={middle}");

        // PROOF: two bracketed Type[]: packs disambiguated at the use site. A=[int, int], B=[string].
        var twoPacks = TwoPacks<[int, int], [string]>();
        Console.WriteLine($"twoPacks={twoPacks}");

        // PROOF: forwarding a value pack into a Func whose parameter list IS the pack; provable for
        //        every TArgs (a fixed-arity target would be MIST_VAR_0011, see Requirement 21).
        Func<int, int, int, int> add = (a, b, c) => a + b + c;
        var forward = Apply(add, 2, 3, 4);
        Console.WriteLine($"forward={forward}");

        // PROOF: value-pack narrowing. Each `is` branch proves a concrete shape, so the matching
        //        fixed-arity forward type-checks; branches narrow to different length AND element
        //        type; the else branch uses only the size-agnostic TArgs.Length.
        var narrowInt = Categorize(2, 3, 4);
        Console.WriteLine($"narrowInt={narrowInt}");
        var narrowStr = Categorize("ab", "cd");
        Console.WriteLine($"narrowStr={narrowStr}");
        var narrowElse = Categorize(true);
        Console.WriteLine($"narrowElse={narrowElse}");

        // PROOF: general tuple decomposition -- `...` spreads any tuple value, not only a pack.
        var pair = (10, 20);
        var tupleSpread = Add2(pair...);
        Console.WriteLine($"tupleSpread={tupleSpread}");

        // PROOF: spread-generated members. Read the aggregate `Item` (the whole ValueTuple)
        //        and the projection `Item1` (its first element) from the same instance.
        var bundle = new Bundle<int, string, bool>(42, "hi", true);
        var whole = bundle.Item;
        Console.WriteLine($"bundleAgg={whole.Item1}:{whole.Item2}:{whole.Item3}");
        Console.WriteLine($"bundleItem1={bundle.Item1}");

        // PROOF: re-spread the generated members (Item...) into a Func of the same pack.
        Func<int, string, bool, string> fmt = (a, b, c) => $"{a}-{b}-{c}";
        var mapped = bundle.Map(fmt);
        Console.WriteLine($"bundleMap={mapped}");

        // PROOF: spread-generated members as a property kind (Slot1, Slot2 are get properties).
        var row = new ReadOnlyRow<int, string>(7, "z");
        Console.WriteLine($"prop={row.Slot1}:{row.Slot2}");

        // PROOF: fixed-size pack + whole-pack constraint; Area() is callable on each element
        //        because every pack element is proven to implement IShape.
        var shapePair = new ShapePair<Circle, Square>(new Circle(), new Square());
        Console.WriteLine($"area={shapePair.TotalArea()}");

        // PROOF: a variadic abstract type implemented by a variadic class, used through the
        //        abstract type. Count() returns the pack arity (3).
        ICountable<int, string, bool> counter = new Tup<int, string, bool>();
        Console.WriteLine($"abstractCount={counter.Count()}");

        // PROOF: value-level params is homogeneous; xs is a real int[] and the call site is flat.
        var sum = SumAll(1, 2, 3, 4);
        Console.WriteLine($"sum={sum}");
        var sumEmpty = SumAll();
        Console.WriteLine($"sumEmpty={sumEmpty}");

        // PROOF: one family closed at several widths, two closings of the same width among them and the
        //        one and zero widths C#'s tuple syntax cannot spell; a variadic abstract reached at a second
        //        width through the family naming it; a variadic method called flat at the one and zero widths.
        var twoWide = new Tup<int, string>();
        var alsoTwoWide = new Tup<bool, char>();
        var oneWide = new Tup<bool>();
        var zeroWide = new Tup();
        Console.WriteLine($"widths={twoWide.Count()}{alsoTwoWide.Count()}{oneWide.Count()}{zeroWide.Count()}");
        ICountable<char> single = new Tup<char>();
        Console.WriteLine($"abstractCountOne={single.Count()} describeOne={Describe(5)} describeZero={Describe()}");

        // PROOF: an omitted chain to a pack-only base closed empty runs that constructor with the
        //        empty tuple, for a class and for a flattened struct alike.
        Console.WriteLine($"emptyPackChain={new Uncounted().Width} {new Unsized().Width}");

        Console.WriteLine("=== Variadics Done ===");
    }

    // PROOF: <params TArgs> declares a type pack; TArgs.Length is the size-agnostic arity.
    public static int Describe<params TArgs>(params TArgs args)
    {
        return TArgs.Length;
    }

    // PROOF: middle pack -- TFirst and TLast bind inward from the ends, TMid absorbs the middle.
    public static int MiddleCount<TFirst, params TMid, TLast>(TFirst first, params TMid mid, TLast last)
    {
        return TMid.Length;
    }

    // PROOF: two bracketed Type[]: packs; each is supplied in brackets at the use site.
    public static int TwoPacks<Type[]:A, Type[]:B>()
    {
        return A.Length + B.Length;
    }

    // PROOF: args... forwards the value pack into f, whose parameter list IS TArgs, so the spread
    //        type-checks for every TArgs, not just this call's shape.
    public static TResult Apply<params TArgs, TResult>(Func<TArgs..., TResult> f, params TArgs args)
    {
        return f(args...);
    }

    // PROOF: narrowing the value pack with `is` re-enables provable fixed-arity forwarding.
    public static int Categorize<params TArgs>(params TArgs args)
    {
        if (args is (int, int, int) triple)
        {
            return Sum3(triple...);
        }
        else if (args is (string, string) pair)
        {
            return Join(pair...).Length;
        }
        else
        {
            return TArgs.Length;
        }
    }

    public static int Sum3(int a, int b, int c)
    {
        return a + b + c;
    }

    public static string Join(string a, string b)
    {
        return a + b;
    }

    public static int Add2(int a, int b)
    {
        return a + b;
    }

    // PROOF: value-level params yields a real array; foreach iterates the collected values.
    public static int SumAll(params int[] xs)
    {
        var total = 0;
        foreach (var x in xs)
        {
            total = total + x;
        }

        return total;
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== Variadics Sample ===
n=5
empty-pack
act=7
arity=3
explicit=3
middle=2
twoPacks=3
forward=9
narrowInt=9
narrowStr=4
narrowElse=1
tupleSpread=30
bundleAgg=42:hi:True
bundleItem1=42
bundleMap=42-hi-True
prop=7:z
area=7
abstractCount=3
sum=10
sumEmpty=0
widths=2210
abstractCountOne=1 describeOne=1 describeZero=0
emptyPackChain=0 0
=== Variadics Done ===
```

### Requirement 19: Book Documentation

**User Story:** As a Mist developer, I want the language book to document variadics, so that I can learn the syntax and semantics from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a chapter or section covering variadics with compiled code examples, placed under an appropriate section (for example the type-system or generics section).
2. THE book chapter SHALL demonstrate the following concepts: type-parameter packs and the `params`/`Type[]:` spellings, the pack-as-dependent-type keystone, the Binding_Rule for leading, middle, and trailing packs, order-sensitive and arity-sensitive type identity, position semantics and the capability ladder, the value side (`(TArgs...)`, forwarding with `args...`), spread-generated members, whole-pack constraints, value-level `params`, call-site inference, and the `Func`/`Action` rewrite.
3. All code examples in the chapter SHALL be compiled snippets following the Book Snippet Policy, with corresponding `.mist` files under `book/snippets/`.
4. THE book's `SUMMARY.md` SHALL reference the new chapter in the appropriate section.

### Requirement 20: Feature Coverage Documentation Update

**User Story:** As a Mist project maintainer, I want the `CSHARP_FEATURES.md` document updated to reflect value-level `params`, so that the feature tracking document stays accurate.

#### Acceptance Criteria

1. WHEN value-level `params` is implemented, THE Compiler team SHALL update the `params arrays` row in `CSHARP_FEATURES.md` from unimplemented to implemented, with a note describing homogeneous value-level `params T[]` support.
2. THE Compiler team SHALL update the `Summary` table in `CSHARP_FEATURES.md` to reflect the new implementation counts.

### Requirement 21: Negative Proof -- Spread Destination Enforcement

**User Story:** As a Mist developer, I want a locked negative test proving the type checker rejects an unprovable spread, so that an implementation cannot silently bypass destination typing to obtain a green build.

#### Acceptance Criteria

1. A negative test fixture SHALL contain the Negative Proof Code below verbatim, and the Compiler SHALL reject it, emitting diagnostic MIST_VAR_0011 on the `Sum3(args...)` spread.
2. THE negative fixture SHALL NOT be added to the runnable sample at `samples/Variadics/Program.mist` (which must compile without errors); it SHALL live as a type-checker rejection test.
3. IF the fixture compiles successfully, THEN the test SHALL fail, because a successful compile means the Type_Checker is not enforcing destination typing on spread.

#### Negative Proof Code

```mist
using Standard;

namespace Variadics;

public class NegativeProofs
{
    // NEGATIVE PROOF: this MUST NOT compile. args is a symbolic pack (TArgs...), so
    // Sum3(args...) cannot be proven well-typed for every TArgs -- it would fail for
    // TArgs = [string]. This is ordinary open-generic checking: the same reason
    // Double(value) is rejected in Foo<TValue>. The type checker MUST reject the spread
    // with MIST_VAR_0011.
    public static int ForwardSumBad<params TArgs>(params TArgs args)
    {
        return Sum3(args...);
    }

    public static int Sum3(int a, int b, int c)
    {
        return a + b + c;
    }
}
```

**Expected:** compilation fails with diagnostic MIST_VAR_0011 reported on `Sum3(args...)`.

### Requirement 22: Pack-Bearing Declaration Kinds and Variadic Abstract Types

**User Story:** As a Mist developer, I want to declare a pack on any generic-capable declaration kind and to write variadic abstract types, so that variadics work uniformly across the language's type system.

#### Acceptance Criteria

1. THE Program_Model_Builder SHALL accept a pack type-parameter list (`params TArgs` or `Type[]:TArgs`) on every generic-capable declaration kind Mist provides -- a `class`, a `struct`, an `abstract` type, a type alias (`type Name<...> = ...;`), and a newtype (`type Name<...> = new Source;`) -- as well as on a method type-parameter list, applying the same pack rules (the Binding_Rule, arity counted in slots, and at most one `params` per list) uniformly across all of them. (Mist has no `interface` or `delegate` keyword; abstract types -- similar to, but not identical to, C#'s interfaces -- fill that role, and the `Func` class family fills the callable role.)
2. WHERE an `abstract` type declares a pack (for example `abstract IApplier<params TArgs, TResult> { TResult Apply(params TArgs args); }`), THE Type_Checker SHALL permit a type to implement it either at a concrete arity, where the variadic member slots monomorphize to that arity (implementing `IApplier<int, int, int>` requires `int Apply(int a, int b)`), or generically, where the implementer spreads its own pack into the base-type list (for example `class Boxed<params TArgs> : IApplier<TArgs..., int>`), reusing the base-type-list spread.
3. WHERE a type alias or a newtype declares a pack (for example `type Action<params TArgs> = Func<TArgs..., void>;` or `type Row<params TArgs> = new (TArgs...);`), THE Program_Model_Builder SHALL bind the pack per the Binding_Rule at the use site, and a newtype SHALL clone the (possibly variadic) source type's members into a nominally distinct type.

## Non-Goals / Deferred

The following are explicitly out of scope for this feature, mirroring the deferred items in the design:

- **Range- or refinement-bounded pack sizes** such as `Type[2..8]`, and array slicing or range operations on packs. The unknown-length (`Type[]`) and exact-fixed-size (`Type[N]`) rungs of the capability ladder are in scope; the range rung depends on deferred array-refinement work and is not addressed here.
- **Per-element / per-index type constraints** such as `TArgs[0] : Foo`. Only whole-pack constraints, which apply elementwise, are supported.
- **Overload resolution across differing pack shapes.** Distinguishing overloads by how their packs are shaped is not addressed by this feature.
