# Requirements Document

## Introduction

This document specifies the requirements for adding type argument inference to generic method calls in the Mist programming language. Currently, callers of generic methods must always supply explicit type arguments in angle brackets (e.g., `Identity<int>(42)`). This feature allows the compiler to deduce type arguments from the types of the arguments passed at the call site, so that callers can write `Identity(42)` and the compiler infers `Identity<int>(42)`.

Type argument inference applies to generic method invocations (both instance and static methods). It does not apply to generic type construction (`new Box<T>(...)`) — type arguments on constructors of generic types remain explicit.

The inference algorithm examines each method parameter whose declared type mentions a type parameter and unifies it with the corresponding argument's resolved type. If all type parameters can be uniquely determined, the call succeeds without explicit type arguments. Where several arguments give one type parameter different types, it is fixed to the one of those types that all the others implicitly convert to, as C# does. The compiler never infers a type no argument has: a union or a shared base type is stated explicitly, as in `Pick<string | int>("x", 1)`. If inference fails (conflicting deductions or insufficient information), the compiler emits a diagnostic requiring the programmer to supply explicit type arguments.

**Interaction with existing features:**
- Explicit type arguments remain supported and take precedence — inference is only attempted when type arguments are omitted.
- Overload resolution interacts with inference: when multiple overloads exist, inference is attempted for each candidate independently.
- Constraint satisfaction is checked after inference — inferred type arguments must satisfy all `where` clause constraints.
- The C# emitter must emit explicit type arguments in the generated C# code even when the Mist source omitted them, because C# type inference rules differ from Mist's.

## Glossary

- **Compiler**: The Mist compiler pipeline, including lexing, parsing, semantic model construction, analysis, and C# code emission.
- **Type_Checker**: The analysis component (`TypeCheckerAnalyzer`) that validates type rules against the semantic model.
- **Inference_Engine**: The new component responsible for deducing type arguments from argument types at a generic method call site.
- **Semantic_Model_Builder**: The component (`SemanticModelBuilder`) that constructs the semantic model from the AST.
- **Emitter**: The C# code emission component (`CSharpEmitter`) that translates the Mist AST into C# source files.
- **Overload_Resolver**: The component (`OverloadResolver`) that selects the best-matching method from an overload set.
- **Type_Parameter**: A placeholder type declared in angle brackets on a generic method declaration (e.g., `T` in `void Foo<T>(T item)`).
- **Type_Argument**: A concrete type deduced or explicitly supplied for a type parameter at a call site.
- **Inference_Context**: The mapping from type parameter names to their inferred concrete types, built during inference for a single call site.
- **Unification**: The process of matching a declared parameter type (which may contain type parameters) against an argument type to determine type parameter bindings.
- **Callable_Argument**: An argument that is a lambda, or a method name used as a value (a method group). It has no type of its own until it is given parameter types, so it is typed by the call rather than read by it like any other argument (Requirement 11).
- **Bound**: A type unification records for a type parameter from one argument position. A type parameter appearing in several positions collects several bounds, and is fixed to one type from them per Requirement 3.
- **ConstructedType**: A generic type with all type parameters substituted by concrete type arguments (e.g., `List<int>`).
- **TypeParameterType**: The semantic model representation of an unsubstituted type parameter.
- **Symbol**: The semantic model representation of a named entity (method, class, variable, etc.) carrying type parameter information.

## Requirements

### Requirement 1: Basic Type Argument Inference

**User Story:** As a Mist developer, I want to call generic methods without specifying type arguments when the compiler can deduce them from the arguments I pass, so that my code is less verbose.

#### Acceptance Criteria

1. WHEN a generic method is invoked without explicit type arguments and the method has one or more type parameters, THE Inference_Engine SHALL attempt to infer each type parameter from the types of the arguments at the call site. Inference SHALL apply only to method invocations, not to generic type construction (e.g., `new Box<T>(...)`).
2. WHEN a method parameter's declared type is a TypeParameterType (e.g., parameter `T item`), THE Inference_Engine SHALL infer that type parameter to be the resolved type of the corresponding argument (matched by position: argument 0 to parameter 0, argument 1 to parameter 1, and so on).
3. WHEN every type parameter of the method is fixed to one type per Requirement 3, THE Compiler SHALL treat the call as equivalent to an explicit invocation with those inferred type arguments.
4. IF one or more type parameters cannot be inferred (because they do not appear in any method parameter type, or because no argument provides a binding), THEN THE Inference_Engine SHALL report inference as failed for that call site.
5. WHEN explicit type arguments are provided at the call site (e.g., `Foo<int>(42)`), THE Compiler SHALL use the explicit type arguments and SHALL NOT attempt inference.
6. THE Inference_Engine SHALL support inference for both instance methods and static methods.

### Requirement 2: Inference from Constructed Type Arguments

**User Story:** As a Mist developer, I want type inference to work when I pass generic container types as arguments, so that the compiler can deduce type parameters from nested type structure.

#### Acceptance Criteria

1. WHEN a method parameter's declared type is a ConstructedType containing type parameters (e.g., `List<T>`) and the argument's type is a ConstructedType with the same GenericDefinition, THE Inference_Engine SHALL match type argument positions pairwise and infer each type parameter from the type argument at its corresponding position in the argument's ConstructedType.
2. WHEN a method parameter's declared type is `List<T>` and the argument type is `List<int>`, THE Inference_Engine SHALL infer `T = int`.
3. WHEN a method parameter's declared type contains multiple type parameters in a constructed type (e.g., `Map<K, V>`) and the argument type is a ConstructedType with the same GenericDefinition and the same number of type arguments, THE Inference_Engine SHALL infer each type parameter from its corresponding position in the argument's type arguments.
4. WHEN a method parameter's declared type contains a nested constructed type (e.g., `List<List<T>>`), THE Inference_Engine SHALL recursively unify each type argument position, descending into nested ConstructedTypes until a TypeParameterType is reached and can be bound to the corresponding concrete type from the argument.
5. IF the argument's type is not a ConstructedType or its GenericDefinition differs from the parameter's declared ConstructedType GenericDefinition, THEN THE Inference_Engine SHALL produce no inference bindings from that parameter (inference may still succeed from other parameters or fail per Requirement 4 rules).

### Requirement 3: Multiple Parameter Inference

**User Story:** As a Mist developer, I want type inference to work across multiple parameters, so that different type parameters can be inferred from different arguments.

#### Acceptance Criteria

1. WHEN a generic method has multiple type parameters and each appears in a different parameter position, THE Inference_Engine SHALL infer each type parameter from the resolved type of its corresponding argument, producing an independent entry in the Inference_Context for each type parameter.
2. WHEN the same type parameter appears in multiple parameter positions and all corresponding arguments resolve to the same concrete type (by exact type identity), THE Inference_Engine SHALL record that single concrete type in the Inference_Context and allow the call to proceed.
3. IF the same type parameter appears in multiple parameter positions, the corresponding arguments resolve to different types, and no bound qualifies under 3.5 or 3.6, THEN THE Compiler SHALL emit a diagnostic error naming the type parameter and the conflicting types. `F<T>(T a, T b)` called `F("x", 1)` is refused, and so is `F(new Dog(), new Cat())` even where `Dog` and `Cat` both derive from `Animal`: no type is inferred that no argument has, whether a union of the bounds or a base type they share. The caller states it instead, as in `F<string | int>("x", 1)` or `F<Animal>(new Dog(), new Cat())`.
4. IF a type parameter appears in multiple parameter positions and inference succeeds for at least one position but yields no type information from another position (e.g., the argument provides no resolvable type), THEN THE Inference_Engine SHALL use the successfully inferred type from the other position(s) without reporting a conflict.
5. IF the same type parameter has bounds of different types and one of them is a type every other bound implicitly converts to -- by identity, numeric widening, inheritance or conformance, a type parameter's constraint, a refinement's base, into `unknown`, or through a user-declared implicit conversion -- THEN THE Inference_Engine SHALL fix the type parameter to that bound, the first in argument order where several qualify. `F(1, 2L)` fixes `T` to `long`, `F(new Dog(), new Animal())` to `Animal`, and `F(n, 4)` with `n` of a refinement type over `int` to `int`.
6. IF no bound qualifies under 3.5 but a bound is a refinement type whose base type every other bound implicitly converts to, THEN THE Inference_Engine SHALL fix the type parameter to the first such bound in argument order, whether each argument satisfies its predicate being proven where the argument meets the parameter, as for any refinement-typed parameter.

### Requirement 4: Inference Failure Diagnostics

**User Story:** As a Mist developer, I want clear error messages when type inference fails, so that I know to add explicit type arguments.

#### Acceptance Criteria

1. WHEN a type parameter cannot be inferred because it does not appear in any parameter position (e.g., only in the return type), THE Compiler SHALL emit a diagnostic error at the call site indicating the method name and the name of each type parameter that could not be inferred.
2. WHEN inference produces conflicting types for the same type parameter (i.e., two or more argument positions infer different types, and none is fixed per Requirement 3.5 or 3.6), THE Compiler SHALL emit a diagnostic error at the call site naming the type parameter and listing the conflicting inferred types. WHERE two distinct type parameters the diagnostic shows share a spelling -- the callee's `T` and an enclosing method's or type's `T` among the conflicting types, at any depth of a constructed type -- THE Compiler SHALL name each of them with its declaration (`T of method Api.Pick`, `List<T of type Box>`), and SHALL otherwise name a type parameter by its bare name. (Diagnostic wording only, 2026-10-21: the diagnostic's ID, location, the inference and what is admitted are unchanged.)
3. IF inference fails for any reason (uninferrable type parameter or conflicting inference), THEN THE Compiler SHALL emit exactly one diagnostic per call site with severity Error, preventing compilation from succeeding.
4. IF inference succeeds but one or more inferred type arguments violate a constraint on the type parameter, THEN THE Type_Checker SHALL emit the same constraint violation diagnostic (same diagnostic ID and message format) as when explicit type arguments violate constraints.
5. WHEN explicit type arguments are provided at the call site and inference is not attempted, THE Compiler SHALL NOT emit inference failure diagnostics regardless of whether the type parameters would have been inferrable.

### Requirement 5: Interaction with Overload Resolution

**User Story:** As a Mist developer, I want type inference to work correctly when generic methods have overloads, so that the compiler picks the right overload even without explicit type arguments.

#### Acceptance Criteria

1. WHEN a generic method call without type arguments has multiple overload candidates, THE Compiler SHALL attempt inference independently for each candidate and SHALL exclude from further consideration any candidate for which inference fails.
2. WHEN inference succeeds for exactly one candidate and that candidate's inferred types satisfy all constraints, THE Overload_Resolver SHALL select that candidate.
3. WHEN inference succeeds for multiple candidates, THE Overload_Resolver SHALL apply the specificity rules (a candidate is more specific if its parameter types are assignable to another candidate's parameter types but not vice versa for at least one position) to choose among the successfully-inferred candidates.
4. IF inference succeeds for multiple candidates and the specificity rules cannot identify a single most-specific candidate, THEN THE Compiler SHALL emit a diagnostic indicating an ambiguous overload call.
5. WHEN inference fails for all candidates, THE Compiler SHALL emit a diagnostic indicating no applicable overload was found.
6. IF inference succeeds for exactly one candidate but the inferred type arguments violate a constraint on a type parameter, THEN THE Compiler SHALL emit the constraint violation diagnostic rather than selecting that candidate.

### Requirement 6: Constraint Satisfaction After Inference

**User Story:** As a Mist developer, I want inferred type arguments to be validated against constraints, so that type safety is preserved even when I omit explicit type arguments.

#### Acceptance Criteria

1. WHEN type arguments are inferred for a generic method call, THE Type_Checker SHALL validate each inferred type argument against the corresponding type parameter's constraints, including interface constraints, trait constraints, base class constraints, class/struct constraints, and constructor constraints.
2. IF an inferred type argument violates one or more constraints, THEN THE Type_Checker SHALL emit the same diagnostic error code and message format for each violation as when an explicit type argument violates the same constraint.
3. WHEN a type parameter has multiple constraints and the inferred type argument violates more than one, THE Type_Checker SHALL emit a separate diagnostic for each violated constraint.
4. WHEN inference succeeds but constraint validation fails and no other overload candidates remain, THE Compiler SHALL report the constraint violation diagnostics to the developer rather than silently discarding the candidate.

### Requirement 7: C# Emitter Support

**User Story:** As a Mist developer, I want programs using inferred type arguments to compile to correct C# code, so that the feature works end-to-end.

#### Acceptance Criteria

1. WHEN a generic method call in Mist source omits type arguments and inference succeeds, THE Emitter SHALL emit the method call in C# with explicit type arguments in angle-bracket syntax (e.g., Mist `Identity(42)` emits as C# `Identity<int>(42)`).
2. WHEN a generic method call has multiple inferred type arguments, THE Emitter SHALL emit all inferred type arguments in declaration order, separated by commas (e.g., Mist `Pair(1, "two")` emits as C# `Pair<int, string>(1, "two")`).
3. WHEN the inferred type is a primitive type, THE Emitter SHALL use the C# mapped type name (e.g., `int` not `Int32`, `string` not `String`).
4. WHEN the inferred type is a constructed generic type, THE Emitter SHALL emit the full type argument list recursively (e.g., `List<int>`).
5. WHEN a generic method call in Mist source provides explicit type arguments, THE Emitter SHALL emit those explicit type arguments unchanged and SHALL NOT substitute inferred types.

### Requirement 8: Return Type Substitution for Inferred Calls

**User Story:** As a Mist developer, I want the return type of a generic method to be correctly resolved when type arguments are inferred, so that downstream type checking works correctly.

#### Acceptance Criteria

1. WHEN type arguments are inferred for a generic method call, THE Type_Checker SHALL substitute the inferred type arguments into the method's return type.
2. WHEN the result of a generic method call with inferred type arguments is used in a downstream expression (variable assignment, argument to another method call, or member access), THE Compiler SHALL use the substituted return type as the resolved type of that expression.
3. WHEN the return type of a generic method is a TypeParameterType (e.g., `T Identity<T>(T value)`), THE Type_Checker SHALL resolve the return type to the inferred concrete type.
4. WHEN the return type of a generic method is a ConstructedType containing type parameters (e.g., `List<T> Wrap<T>(T value)`), THE Type_Checker SHALL substitute the inferred type arguments into the constructed return type.

### Requirement 9: Book Update

**User Story:** As a Mist developer reading the language book, I want the generics chapter to document type argument inference, so that I learn the feature exists and understand when I can omit type arguments.

#### Acceptance Criteria

1. THE book chapter at `book/src/type-system/generics.md` SHALL remove the statement "Mist does not yet support type argument inference" and replace it with documentation explaining that the compiler infers type arguments from call-site argument types when type arguments are omitted.
2. THE book chapter SHALL add a new section titled "Type Argument Inference" that explains the inference behavior, when it applies (method calls only, not type construction), and when explicit type arguments are still required (inference failure, return-type-only type parameters).
3. THE book chapter SHALL include a tabbed snippet (Mist + Emitted C#) demonstrating inference in action, stored at `book/snippets/type-system/generics/inference.mist` and `book/snippets/type-system/generics/inference.cs`.
4. THE snippet SHALL show at least: a generic method called without type arguments, the same method called with explicit type arguments for comparison, and inference from a constructed type argument.
5. THE `.cs` file SHALL be generated by running `dotnet run --project tools/SnippetEmitter` after the `.mist` file compiles successfully.

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating type argument inference, so that I can see every supported usage pattern in one place and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/TypeArgumentInference/Program.mist` with an accompanying `mist.toml` project file.
2. The sample SHALL contain the Proof Code verbatim and SHALL demonstrate every syntax variant and usage pattern defined in the preceding requirements, including but not limited to: single type parameter inference from a direct argument, multiple type parameter inference from multiple arguments, inference from constructed type arguments (e.g., `List<T>`), inference with nested generics, inference on instance methods, inference on static methods, inference interacting with overload resolution, inference with constrained type parameters, and explicit type arguments still working alongside inferred calls.
3. Each demonstrated permutation SHALL print output to stdout in `label=value` format so correctness is verifiable against the expected output specified in the Proof Code section.
4. The sample SHALL expose a `public static void RunDemo()` entry point, and `samples/AllFeatures/Program.mist` SHALL call `TypeArgumentInference.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler, producing output that exactly matches the expected output defined in the Proof Code section.

#### Proof Code

```mist
// TypeArgumentInference — Demonstrates type argument inference for generic method calls.
// Each permutation prints labeled output to stdout for correctness verification.

using Standard;

namespace TypeArgumentInference;

// ─── Supporting types ────────────────────────────────────────────────────

// Minimal List<T> to support the proof code (Mist stdlib does not include List<T>).
public class List<T> {
    public T item0;
    public int count;

    public List() {
        count = 0;
    }

    public void Add(T item) {
        if (count == 0) {
            item0 = item;
        }
        count = count + 1;
    }

    public T this[int index] {
        get {
            return item0;
        }
    }
}

// PROOF: Inference on instance method
public class Wrapper {
    public T Echo<T>(T value) {
        return value;
    }
}

// ─── Entry point ─────────────────────────────────────────────────────────

public class Program {
    // PROOF: Single type parameter inferred from argument type (int)
    // PROOF: Return type correctly substituted to int
    public static T Identity<T>(T value) {
        return value;
    }

    // PROOF: Multiple type parameters inferred from different arguments
    public static string Pair<A, B>(A first, B second) {
        return $"({first}, {second})";
    }

    // PROOF: Type parameter inferred from constructed type argument (List<T>)
    public static T FirstOf<T>(List<T> items) {
        return items[0];
    }

    public static void RunDemo() {
        var x = Identity(42);
        Console.WriteLine($"identity_int={x}");

        // PROOF: Single type parameter inferred from argument type (string)
        var s = Identity("hello");
        Console.WriteLine($"identity_string={s}");

        // PROOF: Multiple type parameters inferred from different arguments
        var p = Pair(1, "two");
        Console.WriteLine($"pair={p}");

        // PROOF: Type parameter inferred from constructed type argument (List<T>)
        var nums = new List<int>();
        nums.Add(10);
        var first = FirstOf(nums);
        Console.WriteLine($"first_of_list={first}");

        // PROOF: Inference on instance method
        var w = new Wrapper();
        var echoed = w.Echo(99);
        Console.WriteLine($"instance_echo={echoed}");

        // PROOF: Explicit type arguments still work (no inference attempted)
        var explicit_result = Program.Identity<string>("explicit");
        Console.WriteLine($"explicit={explicit_result}");
    }

    public static void Main(string[] args) {
        RunDemo();
    }
}
```

**Expected output:**
```
identity_int=42
identity_string=hello
pair=(1, two)
first_of_list=10
instance_echo=99
explicit=explicit
```

### Requirement 11: Inference from Lambda and Method Group Arguments

**User Story:** As a Mist developer, I want to pass a lambda or a method name to a generic method without stating its type arguments, so that `Apply(1, x => x + 1)` and `Apply(4, Next)` infer what `Apply<int, int>(1, x => x + 1)` and `Apply<int, int>(4, Next)` state.

#### Acceptance Criteria

1. WHEN a call infers its type arguments, THE Inference_Engine SHALL collect bounds per Requirements 1-3 from every argument except a Callable_Argument whose parameter is a function type, and from such a lambda only its explicitly typed parameters, each matched against the function type's parameter type at its position: `Call((string s) => s.Length)` over `Call<T, R>(func<T, R> f)` gives `T` the bound `string`.
2. AFTER those bounds are collected, THE Inference_Engine SHALL type the Callable_Arguments, repeating until none remains that can be typed. A Callable_Argument can be typed once every type parameter mentioned by its function type's parameter types -- at the positions a lambda does not state itself -- has at least one bound. Those type parameters are then fixed per Requirement 3, the argument is typed with the parameter types they give, and the type it produces is matched against the function type's result, adding bounds to the type parameters not yet fixed. `Apply(1, x => x + 1)` over `R Apply<T, R>(T value, func<T, R> f)` fixes `T` to `int`, types the body as `int`, and infers `R = int`; `Pipe(1, x => x + 1, y => $"{y}")` over `C Pipe<A, B, C>(A a, func<A, B> f, func<B, C> g)` infers `A` from the first argument, `B` from the first lambda and `C` from the second.
3. THE type a lambda produces SHALL be its expression body's type, or for a block body the type of its `return` values (`void` where it returns none), wrapped in `Task<T>` for an async lambda (first-class-functions Requirement 9). THE type a method group produces SHALL be the return type of the one overload whose parameter types are identical, position by position, to the types it is given -- a generic overload closing its own type parameters from those types alone, and a Candidate declaring no type parameters preferred per method-overloading Requirement 3.8. A group with no such overload, or with several, produces nothing. `Apply(4, Show)` with `Show(int)` and `Show(string)` infers `R` from the return type of `Show(int)`.
4. A type parameter fixed so that a Callable_Argument could be typed SHALL take no further bound. The argument was typed by it, so a type it later produces that disagrees is checked where the argument is bound, as it would be had the type argument been stated: `Grow(1, x => x * 2L)` over `T Grow<T>(T seed, func<T, T> step)` infers `T = int` and refuses the body's `long` where an `int` is expected, exactly as `Grow<int>(1, x => x * 2L)` does.
5. IF a type parameter that a Callable_Argument needs fixed has bounds none of which qualifies under Requirement 3.5 or 3.6, THEN THE Compiler SHALL report that conflict (Requirement 4.2) rather than the type parameters the argument would have taught: `Apply2(1, "x", x => x)` over `Apply2<T, R>(T a, T b, func<T, R> f)` is a conflict on `T`, not an uninferred `R`.
6. IF a Callable_Argument is never typed because a type parameter its parameter types need has no bound, THEN THE Compiler SHALL report the type parameters left without bounds as not inferred (Requirement 4.1), and the argument SHALL add no diagnostic of its own: `Fixed(x => x)` over `Fixed<T>(func<T, T> f)` reports that `T` must be stated.
7. WHEN inference succeeds, a lambda SHALL be bound once, against the selected method's parameter with the inferred type arguments substituted, and a method group SHALL be converted to that parameter per first-class-functions Requirement 7.7. The call therefore binds exactly as the call stating those type arguments binds, and the Emitter states them (Requirement 7).
8. A method group SHALL be typed by the call only where it is the argument's whole value, parenthesized or not. A method group nested inside an argument -- an operand of `??`, the result of a switch arm -- is converted where it stands, against the type expected there: `Apply(4, maybe ?? Next)` with `maybe` a `func<int, int>?` converts `Next` to `func<int, int>` and infers `T = int`, `R = int`.

For lambda arguments in Requirement 11, a declared union with exactly one callable constituent supplies that constituent's signature after receiver substitution and alias expansion, per first-class-functions Requirements 6.9-6.10. This does not change method-group inference or choose among multiple callable constituents.
