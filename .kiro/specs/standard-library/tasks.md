# Implementation Plan: Mist Standard Library

## Overview

Ship a first-class Mist Standard Library under the `Standard` namespace with two tiers (Bootstrap + Pure-Mist), first-class user-definable operators, a dual-level `intrinsic` marker, and primitive-keyword aliases that bypass scoping. The migration follows the staged cutover in design § *Migration Plan* — six stages chosen to keep the compiler buildable at every checkpoint.

Implementation language: **C#** (the compiler's existing language; the design uses C# throughout). Stdlib source files are written in Mist (`.mist`). Property-based tests use FsCheck (design § *Testing Strategy*).

Stages:

1. Grammar + intrinsic plumbing (no behavior change).
2. Operator declarations + overload resolution (behind a feature flag).
3. Stand up stdlib files + `StdlibLoader` (singletons still live).
4. Flip primitive keyword resolution to stdlib (shim preserves reference equality).
5. Migrate C# backend (Backing_Type_Map, intrinsic emission registry, ambient resolver rewrite).
6. Remove `PrimitiveType`, `StringType`, shim, cleanup.

## Tasks

### Stage 1 — Grammar and intrinsic plumbing

- [x] 1. Add `intrinsic` and `operator` reserved keyword tokens
    Add two new reserved keyword tokens to `Source/Mist/Grammar/keywords.storm`: `INTRINSIC = "intrinsic";` and `OPERATOR = "operator";`. Place them adjacent to existing modifier keywords (`PUBLIC`, `STATIC`, etc.). Both are reserved (not contextual), so they can never be used as identifiers. Requirement 5.14 mandates that `intrinsic` is defined in exactly one place — `keywords.storm` is that place. Regenerate the lexer so the new tokens are recognized. See design § *Components and Interfaces → 1. Grammar changes*.
    - [x] 1.1 Unit test: lexer tokenizes `intrinsic` as `INTRINSIC`, `operator` as `OPERATOR`, and rejects them as identifiers.
    _Requirements: 5.14, 4.1_

- [x] 2. Extend `Modifier` grammar rule to accept `intrinsic`
    In `Source/Mist/Grammar/Mist.storm`, add `| INTRINSIC as IntrinsicModifier` to the `Modifier` sum type (currently accepting `PUBLIC`, `PRIVATE`, `STATIC`). This single rule change lets `intrinsic` appear at both type level (because `ClassDeclaration`/`StructDeclaration` prefix `modifiers:Modifier*`) and member level (because `MethodDeclaration`/`ConstructorDeclaration` and the new `OperatorDeclaration` all prefix `modifiers:Modifier*`). No separate grammar rule is needed for type vs member placement. Regenerate the parser. See design § 1.
    - [x] 2.1 Unit test: parser accepts `intrinsic public struct Int32 { }` and `intrinsic public static Int32 operator +(Int32 a, Int32 b);` and produces AST nodes containing `IntrinsicModifier` in the modifier list.
    _Requirements: 5.1, 5.11_

- [x] 3. Add `OperatorDeclaration` grammar rule and wire it into `MemberDeclaration`
    In `Source/Mist/Grammar/Mist.storm`, add new rules per design § 1:
    - `OperatorDeclaration = modifiers:Modifier* returnType:TypeReference OPERATOR symbol:OperatorSymbol LEFT_PARENTHESIS parameters:ParameterList? RIGHT_PARENTHESIS body:OperatorBody`
    - `OperatorBody = block:Block as OperatorBlockBody | SEMICOLON as OperatorNoBody`
    - `OperatorSymbol` covering 14 symbols: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||`, `!`.
    Add `| operator:OperatorDeclaration as OperatorMember` to the `MemberDeclaration` rule. Regenerate the parser.
    - [x] 3.1 Unit test: parser accepts binary operators (`public static Int32 operator +(Int32 a, Int32 b) { return a; }`) and unary operators (`public static Int32 operator -(Int32 a) { return a; }`), producing `OperatorDeclaration` AST nodes with the correct `OperatorSymbol` variant.
    - [x] 3.2 Unit test: parser accepts `operator +(...);` (semicolon-terminated, no body) as `OperatorNoBody`.
    _Requirements: 4.1, 4.3, 4.4, 4.5_

- [x] 4. Relax `MethodDeclaration` and `ConstructorDeclaration` bodies to allow `;`
    In `Source/Mist/Grammar/Mist.storm`, replace `body:Block` on `MethodDeclaration` and `ConstructorDeclaration` with `body:MethodBody`, where `MethodBody = block:Block as MethodBlockBody | SEMICOLON as MethodNoBody`. Whether `;` is valid on a given declaration (i.e., requires `intrinsic`) is enforced in the semantic model, not the grammar (see Task 8). Interface methods continue to use the existing `InterfaceMethodDeclaration` rule unchanged. Regenerate the parser.
    - [x] 4.1 Unit test: parser accepts `intrinsic public static Int32 operator +(Int32 a, Int32 b);` and `intrinsic public Int32();` with `MethodNoBody`/`OperatorNoBody`.
    _Requirements: 5.2_

- [x] 5. Add `IsBackendBacked` flag to `ClassType` and `StructType`
    Add `public bool IsBackendBacked { get; }` as an immutable readonly property to `Source/Mist/Semantics/Model/Types/ClassType.cs` and `Source/Mist/Semantics/Model/Types/StructType.cs`. Default to `false`. Expose it via a constructor parameter. The flag tells the emitter the type has a target-language backing participating in the `Backing_Type_Map`. See design § 2.
    _Requirements: 5.11, 9.7_

- [x] 6. Add `IsIntrinsic` flag to `Symbol`
    Add `public bool IsIntrinsic { get; }` as an immutable readonly property to `Source/Mist/Semantics/Model/SymbolTable.cs` (the `Symbol` class). Default to `false`. Expose it via a constructor parameter. The flag tells the emitter the member's body comes from the `IntrinsicEmissionTable` rather than from a Mist-bodied method. See design § 2.
    _Requirements: 5.1, 5.7_

- [x] 7. Create `KeywordAliases.cs` with the primitive-keyword → stdlib-type mapping
    Create `Source/Mist/Semantics/Model/KeywordAliases.cs` with `public static class KeywordAliases` exposing `public static readonly ImmutableDictionary<string, string> Map` containing all 14 entries per design § 2: `int → Standard.Int32`, `uint → Standard.UInt32`, `long → Standard.Int64`, `ulong → Standard.UInt64`, `short → Standard.Int16`, `ushort → Standard.UInt16`, `byte → Standard.Byte`, `sbyte → Standard.SByte`, `float → Standard.Single`, `double → Standard.Double`, `decimal → Standard.Decimal`, `bool → Standard.Boolean`, `char → Standard.Char`, `string → Standard.String`. Requirement 7.5 mandates a single location; this is it. Nothing reads it yet (keyword resolution is flipped in Stage 4).
    - [x] 7.1 Unit test: `KeywordAliases.Map` contains exactly 14 entries with the values specified in Requirement 7.1.
    _Requirements: 7.1, 7.5_

- [x] 8. Wire modifier and body translation into the AST → semantic-model pass
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, extend the Pass 1 member-registration code so that when a `ClassDeclaration`/`StructDeclaration` or a `MethodDeclaration`/`ConstructorDeclaration`/`OperatorDeclaration` is translated:
    - `IntrinsicModifier` on the type sets `IsBackendBacked = true` on the resulting `ClassType`/`StructType`.
    - `IntrinsicModifier` on a member sets `IsIntrinsic = true` on the resulting `Symbol`.
    - A `MethodNoBody`/`OperatorNoBody` body is recorded on the symbol (e.g., via a `HasBody` flag) for downstream validation in Task 9.
    No diagnostics are emitted yet; just propagate the information.
    - [x] 8.1 Unit test: `intrinsic public struct Int32 { }` produces a `StructType` with `IsBackendBacked == true`; a member `intrinsic public static Int32 operator +(...);` on that type produces a `Symbol` with `IsIntrinsic == true` and `HasBody == false`.
    _Requirements: 5.1, 5.2, 5.11_

- [x] 9. Add `Pass1e_ValidateIntrinsicMarkers` with diagnostics MIST_SB_0030–0032
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a new Pass 1e immediately after symbol registration. For every type and member, emit:
    - `MIST_SB_0030` when a member with `IsIntrinsic == true` has a body (Requirement 5.3). Message: `Intrinsic member '{type}.{member}' must not have a body; use ';' to mark it as backend-provided.`
    - `MIST_SB_0031` when `IsIntrinsic` (member-level) or `IsBackendBacked` (type-level) appears on a declaration whose `CompilationUnit.IsStdlib == false` (Requirements 5.6, 5.13). `CompilationUnit.IsStdlib` is added in Task 26. For now, treat every unit as non-stdlib — this task's diagnostic emission is already correct once Task 26 lands the flag. Message: `The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.`
    - `MIST_SB_0032` when a member has `IsIntrinsic == true` but its containing type has `IsBackendBacked == false` (Requirement 5.15). Message: `Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{type}' is not intrinsic.`
    Use the next unallocated `MIST_SB_*` IDs (see design § *Error Handling and Diagnostics*; last used is `MIST_SM_0022`).
    - [x] 9.1 Unit test: each of the three diagnostics fires on the corresponding adversarial snippet from design § *Error Handling and Diagnostics* and does NOT fire on the correct-usage counter-example.
    _Requirements: 5.3, 5.6, 5.13, 5.15, 14.3, 14.4, 14.5_

- [x] 10. Checkpoint — Stage 1 complete
    Ensure the full solution builds and all existing unit tests pass. Grammar accepts new forms; no behavior change has shipped. Ask the user if questions arise.
    _Requirements: (none — gate)_

### Stage 2 — Operator declarations and overload resolution (behind a feature flag)

- [x] 11. Register operator declarations as static methods during semantic model building
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` Pass 1 member-registration, handle `OperatorDeclaration` AST nodes by registering them as `Symbol` instances with:
    - `Kind = SymbolKind.Method`
    - `Name = "operator" + <op lexeme>` (e.g., `"operator+"`, `"operator=="`, `"operator-"` — unary `-` and binary `-` share the same name; overload resolution disambiguates by arity)
    - `IsIntrinsic` set from modifiers (via Task 8's translation)
    - `DeclaredType` set to the return type
    - Parameter signatures extracted like for methods
    Naming operators `operator+` prevents collision with user-defined methods named `plus`. See design § 3(c).
    - [x] 11.1 Unit test: an operator declaration on a user struct becomes a `Symbol` with `Name == "operator+"`, `Kind == Method`, the expected parameter types, and `IsIntrinsic == false`.
    _Requirements: 4.6_

- [x] 12. Add operator-declaration shape diagnostics MIST_SB_0033–0035
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, validate operator declarations during Pass 1 and emit:
    - `MIST_SB_0033` — operator declared outside a class/struct body (Requirement 4.7). Message: `Operators must be declared as 'public static' members of a class or struct.`
    - `MIST_SB_0034` — binary operator without exactly 2 parameters OR unary operator without exactly 1 parameter (Requirements 4.8, 4.9). Message (binary): `Binary operator '{op}' must declare exactly 2 parameters; found {n}.` Message (unary): `Unary operator '{op}' must declare exactly 1 parameter; found {n}.`
    - `MIST_SB_0035` — operator not declared `public static` (Requirement 4.2). Message: `Operator '{op}' must be declared 'public static'.`
    Binary vs unary arity for each of the 14 operator symbols is fixed in the grammar (Requirement 4.3–4.4); `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `>`, `<=`, `>=`, `&&`, `||` are binary; `!` is unary; `-` is overloaded between binary and unary based on declared parameter count.
    - [x] 12.1 Unit test: each diagnostic fires on the corresponding adversarial snippet and does NOT fire on the correct-usage counter-example.
    _Requirements: 4.2, 4.7, 4.8, 4.9, 14.6, 14.7_

- [x] 13. Add `RecordResolvedOperator` / `GetResolvedOperator` to `ISemanticModel`
    Extend `Source/Mist/Semantics/Contracts/ISemanticModel.cs` and `Source/Mist/Semantics/Model/SemanticModel.cs` with two methods:
    - `void RecordResolvedOperator(SyntaxNode node, ISymbol op)` — called by the type checker after overload resolution.
    - `ISymbol? GetResolvedOperator(SyntaxNode node)` — called by the emitter to retrieve which operator was selected.
    Backed by a private `Dictionary<SyntaxNode, ISymbol>`. See design § 4.
    - [x] 13.1 Unit test: `RecordResolvedOperator` then `GetResolvedOperator` round-trips the symbol; `GetResolvedOperator` on an unrecorded node returns `null`.
    _Requirements: 6.1, 6.2_

- [x] 14. Add `ResolveBinaryOperator` and `ResolveUnaryOperator` helpers in `TypeCheckerAnalyzer`, gated by a feature flag
    In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`, add two new private helpers per design § 4:
    - `ResolveBinaryOperator(string op, ExpressionNode left, ExpressionNode right, SyntaxNode surrounding, ISemanticModel model, DiagnosticBag diagnostics)` — gathers operator-member candidates from BOTH operand types, calls `OverloadResolver.Resolve`, emits `MIST_TC_0003`/`MIST_TC_0005`/`MIST_TC_0006` on `NoApplicableCandidate` (selecting the ID by which `Check*Node` wrapper is calling), emits `MIST_TC_0032` on `AmbiguousCall`, calls `RecordResolvedOperator` on `Success`, sets the expression type to the selected candidate's `DeclaredType`.
    - `ResolveUnaryOperator(string op, ExpressionNode operand, SyntaxNode surrounding, ISemanticModel model, DiagnosticBag diagnostics)` — analogous, one operand.
    Add a static boolean `internal static bool UseOperatorOverloadResolution = false;` at the top of `TypeCheckerAnalyzer`. The existing `CheckAdditiveNode`/`CheckMultiplicativeNode`/`CheckRelationalNode`/`CheckEqualityNode` methods and `CheckUnaryNode` cases (`LogicalNotUnaryNode`, `NegateUnaryNode`) are NOT modified yet — do not branch on the flag from them in this task. The flag controls a single new test in Task 16. Production traffic still uses the old path.
    - [x] 14.1 Unit test: `ResolveBinaryOperator` called with a user-declared `Vector3` type and its `operator +` returns `Success` with the correct symbol, sets the result type to `Vector3`, and populates `GetResolvedOperator`.
    - [x] 14.2 Unit test: `ResolveBinaryOperator` on types with no applicable operator emits `MIST_TC_0003` (or the appropriate ID).
    - [x] 14.3 Unit test: `ResolveBinaryOperator` on types with two equally-applicable operators emits `MIST_TC_0032`.
    _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

- [x] 15. Extend `OverloadResolver` with left-operand precedence tiebreaker
    In `Source/Mist/Analysis/Builtin/OverloadResolver.cs`, add a new overload of `Resolve` (or an optional parameter `IReadOnlyList<int>? candidateOrigin = null`) per design § 5. When `candidateOrigin` is provided (0 = left operand's type, 1 = right operand's type, `null` for non-operator calls), and `MoreSpecific` yields an otherwise-ambiguous result, prefer the lower-origin candidate. Existing method-call call sites pass `null` and preserve existing behavior. `ResolveBinaryOperator` (Task 14) passes the origin vector.
    - [x] 15.1 Unit test: with two candidates `A.operator+(A, B)` (origin 0) and `B.operator+(A, B)` (origin 1) and no specificity difference, the left-origin candidate wins. Reverse operand order: the right-hand type candidate (now origin 0) wins — confirming determinism across commutation.
    _Requirements: 12.5, 15.4 (resolved)_

- [x] 16. Integration test: compile a user-defined `Vector3` struct with `operator +` under the feature flag
    Add an integration test that sets `TypeCheckerAnalyzer.UseOperatorOverloadResolution = true` for the duration of the test, compiles a Mist source defining `public struct Vector3 { public float x; public float y; public float z; public static Vector3 operator +(Vector3 a, Vector3 b) { ... } }` plus a call site `v1 + v2`, and asserts:
    - The semantic model records the user's operator as the resolved operator for the `+` expression.
    - The expression type is `Vector3`.
    - No diagnostics.
    The production flag stays `false` — this test flips it locally. Other tests and the driver continue on the old path.
    _Requirements: 4.1, 4.6, 6.1, 6.3_

- [x] 17. Property-based test: operator overload resolution is deterministic
    Add an FsCheck property test per design § *Correctness Properties → Property 4*. The generator produces an arbitrary set of operator-member candidates applicable to a fixed operand-type pair, plus an arbitrary permutation of the candidate list. Assert: for every permutation, `OverloadResolver.Resolve` (with origin vector) selects the same symbol. Runs ≥100 iterations (Requirement 12.8).
    _Requirements: 12.5, 12.8_

- [x] 18. Checkpoint — Stage 2 complete
    Ensure all tests pass. The feature flag is off by default; samples and all existing tests unchanged. User-defined operator resolution proven via Task 16. Ask the user if questions arise.
    _Requirements: (none — gate)_

### Stage 3 — Stand up stdlib files and `StdlibLoader` (singletons still live)

- [x] 19. Create stdlib source tree and README
    Create the directory `Source/Standard/` with a `Bootstrap/` subdirectory. Add `Source/Standard/README.md` describing the tier layout per Requirement 1.3 and 1.6: `Bootstrap/` for Backend_Backed_Types (numeric primitives, `Boolean`, `Char`, `String`, `Array<T>`); files at the `Source/Standard/` root for Pure-Mist tier types. Mention the single `Standard` root namespace (Requirement 1.1).
    _Requirements: 1.1, 1.2, 1.3, 1.6_

- [x] 20. Write `Source/Standard/Bootstrap/Int32.mist` as the prototype bootstrap type
    Write the `Int32` type in Mist per Requirements 2.1, 3.1, 3.2, 3.3, 3.10, 5.11:
    - `namespace Standard;`
    - `intrinsic public struct Int32 { }` — type-level `intrinsic`, zero instance fields (permitted by Requirement 2.6).
    - Binary arithmetic operators (`+`, `-`, `*`, `/`, `%`) — each `intrinsic public static Int32 operator <op>(Int32 a, Int32 b);` with no body.
    - Comparison operators (`<`, `>`, `<=`, `>=`, `==`, `!=`) — each `intrinsic public static Boolean operator <op>(Int32 a, Int32 b);`.
    - Unary `-` — `intrinsic public static Int32 operator -(Int32 a);`.
    Once this file lexes/parses/builds its semantic model without errors (validated by Task 27's smoke test), the other numerics in Task 21 follow the same shape.
    _Requirements: 2.1, 2.4, 2.6, 2.7, 3.1, 3.2, 3.3, 3.10, 5.11_

- [x] 21. Write the remaining numeric bootstrap types
    Write one `.mist` file per numeric primitive in `Source/Standard/Bootstrap/`: `UInt32.mist`, `Int64.mist`, `UInt64.mist`, `Int16.mist`, `UInt16.mist`, `Byte.mist`, `SByte.mist`, `Single.mist`, `Double.mist`, `Decimal.mist`. Each mirrors `Int32.mist`'s shape (Task 20), with the following Requirement 3.4 carve-out: `UInt32`, `UInt64`, `UInt16`, and `Byte` MUST NOT declare unary `-`. Comparison operators return `Standard.Boolean`.
    _Requirements: 2.1, 3.1, 3.2, 3.3, 3.4, 3.10, 5.11_

- [x] 22. Write `Source/Standard/Bootstrap/Boolean.mist` and `Char.mist`
    - `Boolean.mist`: `intrinsic public struct Boolean { }` with `intrinsic public static Boolean operator &&(Boolean a, Boolean b);`, `operator ||`, unary `operator !`, plus `operator ==` and `operator !=` (Requirement 3.5, 3.6).
    - `Char.mist`: `intrinsic public struct Char { }` with `operator ==`, `!=`, `<`, `>`, `<=`, `>=` (all returning `Standard.Boolean`) per Requirement 3.7.
    _Requirements: 2.1, 3.5, 3.6, 3.7, 3.10, 5.11_

- [x] 23. Write `Source/Standard/Bootstrap/String.mist`
    Write `String` as a class (Requirement 2.2): `intrinsic public class String { }`. Include intrinsic members per Requirements 3.8, 3.9, 9.12:
    - `intrinsic public static String operator +(String a, String b);` (concat)
    - `intrinsic public static Boolean operator ==(String a, String b);`
    - `intrinsic public static Boolean operator !=(String a, String b);`
    - `intrinsic public Int32 Length();` (modeled as a method for v1 simplicity, matching the existing ambient resolver; see design § 7 for future upgrade to property)
    - `intrinsic public String Substring(Int32 startIndex);`
    - `intrinsic public Boolean Contains(String value);`
    _Requirements: 2.2, 3.8, 3.9, 3.10, 5.11, 9.1, 9.12_

- [x] 24. Write `Source/Standard/Bootstrap/Array.mist`
    Write `Array<T>` as a generic class (Requirement 2.3): `intrinsic public class Array<T> { }`. Include per Requirement 9.13: `intrinsic public Int32 Length();`.
    _Requirements: 2.3, 5.11, 9.2, 9.13_

- [x] 25. Write `Source/Standard/Optional.mist` as the pure-Mist tier demo
    Write `Optional<T>` per design § *Pure-Mist Tier for v1* — a pure-Mist generic class (no `intrinsic` markers anywhere) with a private `T value` field, a private `bool hasValue` field, two constructors (parameterless and `Optional(T value)`), and two expression-bodied properties (`HasValue`, `Value`). Path: `Source/Standard/Optional.mist` (directly under the root, visually distinct from `Bootstrap/`).
    _Requirements: 1.3, 1.5, 4.10_

- [x] 26. Add `IsStdlib` flag on `CompilationUnit` and implement `StdlibLoader`
    - In `Source/Mist/Driver/Contracts/CompilationUnit.cs`, add `public bool IsStdlib { get; init; } = false;`.
    - Create `Source/Mist/Driver/StdlibLoader.cs` per design § 8 and § *Stdlib Loading Strategy*: a static class with `public static ImmutableArray<CompilationUnit> LoadFor(IBackend backend, DiagnosticBag diagnostics)` that:
        1. Computes the stdlib root as `Path.Combine(AppContext.BaseDirectory, "Standard")`.
        2. Enumerates every `*.mist` file under that directory recursively.
        3. Reads each file, wraps it in a `SourceText` and then a `CompilationUnit` with `IsStdlib = true`.
        4. Emits `MIST_STDLIB_PARSE` (severity Error) with the file path and underlying message on I/O failure.
        5. Returns an `ImmutableArray<CompilationUnit>`.
    Do NOT wire the loader into `CompilerDriver` yet — that happens in Task 28.
    - [x] 26.1 Unit test: `StdlibLoader.LoadFor` with a temp directory containing two `.mist` files returns both as `CompilationUnit`s with `IsStdlib == true`.
    - [x] 26.2 Unit test: `StdlibLoader.LoadFor` with a missing directory emits `MIST_STDLIB_PARSE`.
    _Requirements: 10.1, 10.2, 10.3, 10.9_

- [x] 27. Add stdlib compile smoke test
    Add a new test `StdlibCompilesTest` in the existing unit test project. It constructs the same stdlib `CompilationUnit` list `StdlibLoader` would produce (by calling `StdlibLoader.LoadFor` against the repo's `Source/Standard/` directory directly, or by enumerating the files manually), runs lex → parse → `SemanticModelBuilder.Build` on them, and asserts that the resulting diagnostic bag contains zero diagnostics of severity `Error`. This catches any malformed stdlib file before it's wired into the driver. Requirement 1.4 and 1.5 naming conventions are verified implicitly (the test reads files from the prescribed paths).
    _Requirements: 1.4, 1.5, 2.4_

- [x] 28. Wire `StdlibLoader` into `CompilerDriver.Compile` and publish stdlib source
    Modify `Source/Mist/Driver/CompilerDriver.cs` per design § 8: immediately after `var compilation = new MistCompilation(units.ToImmutable());` (current Step 1 end), call `StdlibLoader.LoadFor(backend, allDiagnostics)` and prepend the returned units to `compilation.Units`. If the loader produced any Error diagnostics, abort before lex (compilation fails with `MIST_STDLIB_PARSE`). If no Errors, proceed with the combined unit list through the existing Lex → Parse → SemanticModelBuilder pipeline.
    Edit `Source/Mist.CLI/Mist.CLI.csproj` (create it if needed) to add `<Content Include="..\..\Source\Standard\**\*.mist" CopyToPublishDirectory="PreserveNewest" TargetPath="Standard\%(RecursiveDir)%(Filename)%(Extension)" />` so `dotnet publish` packages the stdlib into `publish/Standard/` alongside the binary (resolves Requirement 15.5).
    At this point stdlib types coexist with the old `PrimitiveType.*` singletons — nothing routes primitive keywords to stdlib yet.
    - [x] 28.1 Integration test: compile a trivial user program (`namespace X; public class Y { public static void Main() { } }`) and assert the built semantic model contains `Standard.Int32` (discoverable via namespace lookup) in addition to the existing `PrimitiveType.Int`.
    - [x] 28.2 Unit test: `dotnet publish` output (or a simulation of `AppContext.BaseDirectory + Standard/`) contains every file from `Source/Standard/` (exercise with a test that invokes the publish target and enumerates the result, or asserts the MSBuild item group is correctly declared).
    _Requirements: 10.1, 10.2, 10.3, 10.4, 10.9, 15.5_

- [x] 29. Checkpoint — Stage 3 complete
    Ensure all tests pass. Stdlib source compiles in isolation (Task 27) and co-compiles alongside user source (Task 28.1). Primitive keyword resolution is unchanged; samples unchanged. Ask the user if questions arise.
    _Requirements: (none — gate)_

### Stage 4 — Flip primitive keyword resolution to stdlib

- [x] 30. Add `LookupQualifiedStdlibType` helper and implement `ResolvePrimitiveKeyword`
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs`, add a new private helper per design § 3(b):
    - `private IType LookupQualifiedStdlibType(string qualifiedName)` — splits `"Standard.Int32"` into namespace + simple name, walks directly from the global scope to the `Standard` namespace scope, finds the type symbol by simple name, returns its `IType`. Throws a fatal `MIST_STDLIB_MISSING` diagnostic if the stdlib hasn't been loaded or the type isn't present.
    - `private IType ResolvePrimitiveKeyword(PrimitiveTypeNode keyword)` — extracts the keyword lexeme (e.g., `"int"`), looks it up in `KeywordAliases.Map`, passes the qualified name to `LookupQualifiedStdlibType`, returns the result.
    Crucially, `ResolvePrimitiveKeyword` bypasses all scope-chain, using-directive, and user-type machinery (Requirement 7.6).
    - [x] 30.1 Unit test: `LookupQualifiedStdlibType("Standard.Int32")` returns the stdlib `Standard.Int32` `StructType`; `LookupQualifiedStdlibType("Standard.NonExistent")` throws `MIST_STDLIB_MISSING`.
    _Requirements: 7.1, 7.6_

- [x] 31. Flip `Pass2_ResolveTypes` to use `ResolvePrimitiveKeyword` for primitive keyword references
    In `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` Pass 2, when resolving a `TypeReference` whose grammar variant is `PrimitiveTypeRef` (the grammar rule that matches `int`, `string`, `bool`, etc.), call `ResolvePrimitiveKeyword` and return its result instead of `PrimitiveType.Int` / `StringType.Instance` / etc. Do NOT delete `PrimitiveType.cs` or `StringType.cs` yet — the compat shim (Task 32) preserves existing direct references.
    _Requirements: 7.1, 7.2, 7.6, 7.7_

- [ ] 32. Add `PrimitiveType.Int` compat shim preserving reference equality
    Per design § *Migration Plan → Stage 4* and Requirement 8.3, 8.5:
    - Replace every static readonly field in `Source/Mist/Semantics/Model/Types/PrimitiveType.cs` (e.g., `public static readonly PrimitiveType Int = new("int");`) with a static readonly property whose getter returns the corresponding stdlib `IType` looked up from a static semantic-model handle (registered by `CompilerDriver` after semantic model construction, or a static service-locator style singleton kept for the migration window).
    - Similarly rewrite `Source/Mist/Semantics/Model/Types/StringType.cs` so `StringType.Instance` returns the stdlib `Standard.String` type.
    - The return type of these properties is widened to `IType` (not `PrimitiveType` / `StringType`) so the returned value is literally the stdlib type instance — preserving reference equality (Requirement 8.5).
    This keeps intermediate pipeline stages working while individual call sites are migrated in later stages. Existing direct references to `PrimitiveType.Int` continue to compile.
    - [x] 32.1 Unit test: `PrimitiveType.Int` and `KeywordAliases.Map["int"]`-lookup return reference-equal `IType` instances (Requirement 8.5). Two separate Mist source occurrences of `int` resolve to the same `IType` instance.
    _Requirements: 8.3, 8.5, 15.3 (resolved)_

- [x] 33. Flip `TypeCheckerAnalyzer.UseOperatorOverloadResolution` to `true` and route existing check methods through the new helpers
    In `Source/Mist/Analysis/Builtin/TypeCheckerAnalyzer.cs`:
    - Change `UseOperatorOverloadResolution` default to `true`.
    - Rewrite `CheckAdditiveNode`, `CheckMultiplicativeNode`, `CheckRelationalNode`, `CheckEqualityNode` as three-line wrappers that call `ResolveBinaryOperator` with the appropriate operator lexeme and the appropriate `MIST_TC_*` ID for `NoApplicableCandidate`. Rewrite the `LogicalNotUnaryNode` and `NegateUnaryNode` cases of `CheckUnaryNode` to call `ResolveUnaryOperator`.
    - Delete the `NumericTypes` HashSet (lines 26–34).
    - Delete `IsNumeric` (line ~3186).
    - Delete the `StringType`-for-`+` special case and the `PrimitiveType.Bool`-for-`&&`/`||`/`!` special case.
    After this task lands, operator expressions are typed exclusively via overload resolution against stdlib operator declarations. Requirements 6.7, 6.8, 6.9 are enforced by the deletion itself.
    _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8, 6.9_

- [x] 34. Capture sample output baselines
    Before Task 35 runs, capture a baseline: for every `.mist` file under `samples/` that compiles and runs today, save its stdout to `samples/__baselines__/<sample-path>.out` (or an equivalent location). This is a one-time data capture, not ongoing production code. This baseline is consumed by Task 35 and serves as the regression oracle for Requirement 11.2, 11.4, 12.6.
    _Requirements: 11.2, 11.4, 12.6_

- [x] 35. Sample regression guard — `SamplesCompileAndMatchBaselineTest`
    Add an integration test that, for every `.mist` file under `samples/`:
    1. Invokes the real `CompilerDriver.Compile` against the sample.
    2. Runs the emitted artifact (for the C# backend: `dotnet run` on the emitted project) and captures stdout.
    3. Diffs captured stdout against the baseline saved in Task 34.
    4. Fails if any sample fails to compile, fails to run, or produces diverging stdout.
    Covers Requirements 11.1, 11.2, 11.3, 11.4, 11.6 and Property-based Property 12.6.
    _Requirements: 11.1, 11.2, 11.3, 11.4, 11.6, 12.6_

- [x] 36. Property-based test: Property 1 — keyword-alias reference identity
    Add an FsCheck property test per design § *Correctness Properties → Property 1*. The generator produces an arbitrary valid Mist program with one or more occurrences of primitive keywords. Assert: every occurrence of keyword `k` resolves to an `IType` reference-equal to `LookupQualifiedStdlibType(KeywordAliases.Map[k])`, and all occurrences of the same keyword in the same compilation resolve to the same instance. Runs ≥100 iterations.
    _Requirements: 7.1, 7.2, 8.5, 12.8_

- [x] 37. Property-based test: Property 2 — keyword bypasses scoping (adversarial user-declared `Int32`)
    Add an FsCheck property test per design § *Correctness Properties → Property 2* and Requirement 12.9. The generator produces an adversarial Mist program where the user declares `class Int32 { }` (or `String`, `Boolean`, etc.) in an arbitrary user namespace, with/without `using Standard;`, with/without user members that shadow stdlib members. Assert: every occurrence of the primitive keyword `int` in that program resolves to `Standard.Int32`, NEVER to the user's type. Also assert: the unqualified simple name `Int32` follows ordinary scoping (resolves to user's type when shadowed, to `Standard.Int32` when `using Standard;` without shadowing, and is an unresolved-type error otherwise). Runs ≥100 iterations.
    _Requirements: 7.6, 7.7, 7.8, 12.9, 12.8_

- [x] 38. Checkpoint — Stage 4 complete
    Ensure all tests pass. `SamplesCompileAndMatchBaselineTest` is green; property tests 1 and 2 pass. Primitive keywords now resolve to stdlib types; shim preserves existing direct references. Ask the user if questions arise.
    _Requirements: (none — gate)_

### Stage 5 — Migrate the C# backend

- [x] 39. Rewrite `CSharpTypeMap.cs` into the Backing_Type_Map form
    Rewrite `Source/Mist/Backends/CSharpTranspiler/CSharpTypeMap.cs` per design § 7. Replace the `PrimitiveType`-keyed dictionary with a `Dictionary<string, string>` keyed on fully-qualified stdlib type name (`"Standard.Int32" → "int"`, `"Standard.String" → "string"`, etc. — all 14 entries from design § 7). Rewrite `GetTypeName(IType type, bool isValueContext)` so its `switch` handles:
    - `ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace == "Standard"` → look up `"Standard." + ct.DisplayName` in the dictionary; return the C# source keyword.
    - `StructType st when st.IsBackendBacked && st.ContainingNamespace == "Standard"` → same.
    - `VoidType` → unchanged.
    - `ArrayType` → recursive call on `ElementType` plus `"[]"` (kept structural per design § 2; `Array<T>` is not a dictionary entry because the element type is generic).
    - `ConstructedType`, `TypeParameterType`, generic `ClassType`/`StructType` → unchanged.
    The file remains `internal` and lives in `Source/Mist/Backends/CSharpTranspiler/` (Requirement 9.11).
    - [x] 39.1 Unit test: `CSharpTypeMap.GetTypeName(standardInt32Type)` returns `"int"`; `CSharpTypeMap.GetTypeName(standardStringType)` returns `"string"`; `CSharpTypeMap.GetTypeName(new ArrayType(standardInt32Type))` returns `"int[]"`.
    _Requirements: 9.7, 9.8, 9.11_

- [x] 40. Implement `IIntrinsicEmissionRegistry` and `IntrinsicEmissionTable`
    Create `Source/Mist/Backends/CSharpTranspiler/Intrinsics/IntrinsicEmission.cs` with the internal `IntrinsicEmissionTemplate` record and `IIntrinsicEmissionRegistry` interface per design § 6:
    - `TryGetBinaryOperator(string declaringType, string op)`
    - `TryGetUnaryOperator(string declaringType, string op)`
    - `TryGetInstanceMethod(string declaringType, string method, ImmutableArray<string> paramTypes)`
    - `TryGetConstructor(string declaringType, ImmutableArray<string> paramTypes)`
    Create `Source/Mist/Backends/CSharpTranspiler/Intrinsics/IntrinsicEmissionTable.cs` implementing the registry. Populate dictionaries in a static constructor with entries for every intrinsic declared by stdlib per design § *Data Models → Emission template registry*:
    - Binary arithmetic `{0} <op> {1}` for each of the 11 numeric types × 5 arithmetic ops.
    - Binary comparison `{0} <op> {1}` for each numeric type × 6 comparison ops.
    - `Standard.Boolean`: `&&`, `||` as `{0} && {1}` / `{0} || {1}`; `!` unary as `!{0}`; `==`, `!=`.
    - `Standard.Char`: 6 comparison ops.
    - `Standard.String`: `+` as `{0} + {1}`, `==` as `{0} == {1}`, `!=`, `Length` as `{0}.Length`, `Substring(Int32)` as `{0}.Substring({1})`, `Contains(String)` as `{0}.Contains({1})`.
    - `Standard.Array`: `Length` as `{0}.Length`.
    - Unary `-` on signed numerics: `-{0}` (keyed with a distinct mangled name like `"negate"` to distinguish from binary `-`).
    - [x] 40.1 Unit test: the registry returns the expected template for a representative sample (e.g., `TryGetBinaryOperator("Standard.Int32", "+")` returns `"{0} + {1}"`; `TryGetInstanceMethod("Standard.String", "Substring", ["Standard.Int32"])` returns `"{0}.Substring({1})"`).
    _Requirements: 5.7, 5.8_

- [x] 41. Rewrite `CSharpEmitter` operator emission to dispatch through the resolved-operator symbol
    Modify `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` per design § 7. Rewrite the following methods so they:
    1. Look up `model.GetResolvedOperator(node)` to retrieve the `ISymbol` selected by the type checker.
    2. If the symbol has `IsIntrinsic == true`, look up the emission template via the registry (`TryGetBinaryOperator` / `TryGetUnaryOperator`) and emit via `string.Format(template, left, right)`.
    3. If the symbol has `IsIntrinsic == false`, emit as a static method call on the declaring type (e.g., `Vector3.op_Addition(a, b)` via a `CSharpOperatorName` helper that maps `+` → `op_Addition`, etc.).
    4. If the template lookup fails on an intrinsic, throw `MIST_BE_0002` per Task 43 and emit no output.
    Methods to rewrite: `EmitAdditive` (lines ~1663), `EmitMultiplicative` (~1672), `EmitRelational`, `EmitEquality`, and the `LogicalNotUnaryNode`/`NegateUnaryNode` cases of `EmitUnary`.
    Also rewrite method-call and member-access emission (e.g., `x.Substring(0)`, `s.Length`, `arr.Length`) to consult the registry when the resolved member has `IsIntrinsic == true`, so stdlib member calls emit `{receiver}.Substring(...)` rather than a Mist static-method dispatch.
    - [x] 41.1 Unit test: emitted C# for `int a = 1; int b = 2; int c = a + b;` equals `int a = 1; int b = 2; int c = a + b;` (the intrinsic template produces byte-identical output; Requirement 11.5).
    - [x] 41.2 Unit test: emitted C# for a user-defined `Vector3.operator+` call emits `Vector3.op_Addition(a, b)`.
    _Requirements: 5.7, 5.8, 6.10, 11.5_

- [x] 42. Rewrite `CSharpAmbientTypeResolver` to consume an `IStdlibTypeProvider`
    Per design § 7 and Requirements 9.3, 9.4, 9.5, 9.6, 9.9, 9.10, 9.12, 9.13:
    - Add a new public interface `IStdlibTypeProvider` in `Source/Mist/Semantics/Contracts/` with one method: `IType LookupStandardType(string simpleName)`.
    - Have `SemanticModel` (or a small adapter) implement `IStdlibTypeProvider` by walking into the `Standard` namespace scope.
    - Rewrite `Source/Mist/Backends/CSharpTranspiler/CSharpAmbientTypeResolver.cs`:
        1. Constructor takes an `IStdlibTypeProvider stdlib`. Cache the reference.
        2. Delete `BuildString()` (lines ~140–160) and `BuildArray()` (lines ~185–200) entirely — per Requirement 9.1, 9.2, `String` and `Array<T>` are stdlib types now. Remove their `RegisterType` registrations from the constructor.
        3. Rewrite `BuildConsole`, `BuildMath`, `BuildConvert` to reference stdlib types: every `PrimitiveType.Int` becomes `stdlib.LookupStandardType("Int32")`; every `StringType.Instance` becomes `stdlib.LookupStandardType("String")`; every `PrimitiveType.Bool` becomes `stdlib.LookupStandardType("Boolean")`.
    - Update `CompilerDriver` constructor order so the semantic model is constructed first (giving the stdlib type provider), then the ambient resolver is built with the provider, then the full `SemanticModelBuilder.Build` re-runs with the resolver in place. (Alternative: two-phase build where stdlib ASTs are registered first, ambient resolver is constructed, then user ASTs are built on top. Implementor chooses; design § 7 and § 8 lay out the order.)
    - [x] 42.1 Unit test: `CSharpAmbientTypeResolver.ResolveType("System", "Console")` returns a type whose `WriteLine(string)` member has parameter type equal to `stdlib.LookupStandardType("String")` (reference equality).
    - [x] 42.2 Unit test: `CSharpAmbientTypeResolver.ResolveType("System", "String")` returns `null` (no longer exposed per Requirement 9.6).
    _Requirements: 9.3, 9.4, 9.5, 9.6, 9.9, 9.10, 9.12, 9.13_

- [x] 43. Emit `MIST_BE_0002` on missing intrinsic emission template
    Per Requirement 5.9 and 5.10, the backend MUST emit a hard error when an intrinsic is invoked but the registry has no template, MUST NOT emit any fallback or stub, and MUST produce no output artifact.
    - Add diagnostic ID `MIST_BE_0002` to the backend diagnostic catalog with message template: `No emission template found for intrinsic '{type}.{member}' in backend '{backend}'.` (design § *Error Handling*).
    - In `CSharpEmitter` (Task 41), when an intrinsic template lookup returns null, create a `MIST_BE_0002` diagnostic (severity Error) with the call-site source span (resolved open question — include the AST node's source span) and add it to the backend's `DiagnosticBag`. Abort emission of the current compilation unit; do NOT write any `.cs` file.
    - In `CSharpTranspilerBackend.Emit`, if any `MIST_BE_0002` is in the bag, return a `BackendResult(Succeeded: false, PrimaryArtifactPath: null, ...)` with the diagnostic, ensuring no output artifact is produced.
    - [x] 43.1 Unit test: construct a stdlib file with an intrinsic whose key is intentionally absent from `IntrinsicEmissionTable`. Compile a user program that calls it. Assert: `MIST_BE_0002` is emitted, and the `BackendResult.Succeeded == false`, and no `.cs` file is written.
    - [x] 43.2 Unit test: at test-project startup, iterate every `Symbol` with `IsIntrinsic == true` in the stdlib semantic model and assert the registry has a matching template. Ensures no stdlib intrinsic ships without its template.
    _Requirements: 5.9, 5.10_

- [x] 44. Property-based test: Property 5 — bootstrap numeric operators match C# semantics
    Add an FsCheck property test per design § *Correctness Properties → Property 5* and Requirements 12.1, 12.2, 12.3. For each bootstrap numeric type `N` and each operator `op ∈ {+, -, *, /, %, <, >, <=, >=, ==, !=}` legal on `N`, the generator produces operand pairs `(a, b)` (filtering `b == 0` for `/` and `%`). The test compiles and executes a Mist program that evaluates `a op b` and compares the output to the C# evaluation of the equivalent expression on the backing type. Covers at least `Int32`, `Int64`, `UInt32`, `UInt64`, `Double`, `Decimal` at ≥100 iterations each.
    _Requirements: 12.1, 12.2, 12.3, 12.8_

- [x] 45. Property-based test: Property 3 — every operator expression is typed via OverloadResolver
    Add an FsCheck property test per design § *Correctness Properties → Property 3* and Requirements 6.1, 6.2, 6.3, 6.7, 6.8, 6.9. The generator produces an arbitrary well-typed operator expression (binary or unary, user-defined or stdlib-intrinsic). Assert: the symbol recorded via `RecordResolvedOperator` equals the symbol returned by a direct call to `OverloadResolver.Resolve` on the same candidate set and argument types. Confirms the type checker contains no special-case operator logic that bypasses `OverloadResolver`.
    _Requirements: 6.1, 6.2, 6.3, 6.7, 6.8, 6.9, 12.8_

- [x] 46. Property-based test: Property 6 — operator round-trip inverse
    Add an FsCheck property test per design § *Correctness Properties → Property 6* and Requirement 12.7. For each bootstrap numeric type `N` with both `+` and `-` declared, generator produces operand pairs `(a, b)` filtered so that `a + b` does not overflow. Assert: Mist expression `(a + b) - b` evaluates to `a`. Covers `Int32`, `Int64`, `UInt32`, `UInt64`, `Double`, `Decimal`.
    _Requirements: 12.7, 12.8_

- [x] 47. Property-based test: Property 7 — no backing type leak into the semantic model
    Add an FsCheck property test per design § *Correctness Properties → Property 7* and Requirement 12.10. The generator produces Mist source that exercises `Console.WriteLine("hi")`, `Math.Abs(-1)`, `array.Length`, string literals, numeric literals, member access on primitives (`x.ToString()` where `ToString` is ambient-exposed), and calls whose CLR signatures reference `System.String` / `System.Array` / CLR primitives. The test builds the `ISemanticModel`, then traverses every reachable `IType` (parameter types, return types, local variable types, field types, expression types via `GetTypeOf`, generic type arguments, members produced by every `IAmbientTypeResolver`) and asserts each is either (a) a user-declared Mist type, (b) a `Standard.*` type, or (c) an ambient synthesized type whose own member signatures recursively satisfy the property. Failure if any reachable `IType`'s `DisplayName` or `ContainingNamespace` matches a CLR-type pattern (`System.*`).
    _Requirements: 9.6, 9.9, 9.10, 12.10, 12.8_

- [x] 48. Checkpoint — Stage 5 complete
    Ensure all tests pass. Samples produce byte-identical output vs baseline. Property tests 1–7 all green. The backend now fully uses the Backing_Type_Map and emission registry; no target-language type leaks into the semantic model. Ask the user if questions arise.
    _Requirements: (none — gate)_

### Stage 6 — Remove the shim and the ghost singletons

- [x] 49. Delete `PrimitiveType.cs` and fix all fallout
    Delete `Source/Mist/Semantics/Model/Types/PrimitiveType.cs`. Compile; for each reference that breaks, replace the direct `PrimitiveType.*` reference with a call into the stdlib type lookup (`semanticModel.LookupStandardType("Int32")` or the equivalent helper), or — where the caller already has access to an `IType` via normal type resolution — drop the indirection entirely. This flushes out any call site that was silently riding the compat shim.
    _Requirements: 8.1, 8.2, 8.4_

- [x] 50. Delete `StringType.cs` and fix all fallout
    Delete `Source/Mist/Semantics/Model/Types/StringType.cs`. Compile; for each reference to `StringType.Instance` that breaks, replace with a stdlib lookup for `Standard.String`. Verify `CSharpAmbientTypeResolver` has no remaining `StringType` references after Task 42 — if any slipped through, fix them here.
    _Requirements: 8.1, 8.2, 8.4, 9.1, 9.6_

- [x] 51. Audit `ArrayType` and decide: keep structural or fold into `ConstructedType(Standard.Array<T>)`
    Per design § 2, `ArrayType` is kept as a structural marker representing `Standard.Array<T>`. No deletion is required for v1. Perform an audit: search the codebase for any reference that treats `ArrayType` as a target-language type rather than a Mist stdlib type (e.g., any code that maps `ArrayType` straight to `T[]` without consulting the Backing_Type_Map). Any such reference should route through `CSharpTypeMap`. Document the decision to defer folding to v1.x in a comment on the `ArrayType` class.
    _Requirements: 8.1, 9.2_

- [x] 52. Final grep for orphaned primitive-singleton references
    Run `grepSearch` across the entire `Source/` tree for `PrimitiveType\.`, `StringType\.Instance`, `PrimitiveType_Singleton`, `NumericTypes`, `IsNumeric`. Expected count: zero (after Tasks 49, 50, and the Stage 4 deletions in Task 33). Any remaining hit is a missed reference — fix it here, not in a follow-up.
    _Requirements: 8.4_

- [x] 53. Final end-to-end validation checkpoint
    Run the full test suite. Verify:
    - `StdlibCompilesTest` (Task 27) passes.
    - `SamplesCompileAndMatchBaselineTest` (Task 35) passes — every sample compiles and produces byte-identical stdout vs baseline.
    - All seven property-based tests pass (Tasks 17, 36, 37, 44, 45, 46, 47).
    - All diagnostic unit tests (Tasks 9, 12, 43) pass.
    - `dotnet build` on the full solution produces zero warnings and zero errors.
    - `dotnet publish` on `Mist.CLI` produces a `publish/Standard/` directory containing every stdlib `.mist` file (Task 28).
    If any test fails, fix it before marking this task complete. Ask the user if questions arise.
    _Requirements: 8.4, 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 12.1, 12.2, 12.3, 12.5, 12.6, 12.7, 12.9, 12.10_

## Requirement coverage matrix

Each requirement ID from `requirements.md` appears on at least one task above:

- **Req 1** (Stdlib structure): Tasks 19, 20, 21, 22, 23, 24, 25, 27 (Req 1.4 / 1.5 verified via path structure).
- **Req 2** (Bootstrap tier membership): Tasks 20, 21, 22, 23, 24.
- **Req 3** (Bootstrap numeric operators): Tasks 20, 21, 22, 23.
- **Req 4** (First-class operators): Tasks 1, 2, 3, 4, 11, 12, 16, 25.
- **Req 5** (Intrinsic marker): Tasks 1, 2, 4, 5, 6, 8, 9, 20, 22, 23, 24, 40, 41, 43.
- **Req 6** (Operator overload resolution): Tasks 13, 14, 15, 33, 41, 45.
- **Req 7** (Primitive keyword aliases): Tasks 7, 30, 31, 36, 37.
- **Req 8** (PrimitiveType migration): Tasks 32, 49, 50, 52, 53.
- **Req 9** (Ambient-type migration): Tasks 23, 24, 39, 40, 42, 47, 50.
- **Req 10** (Stdlib distribution): Tasks 26, 28.
- **Req 11** (Sample backwards compatibility): Tasks 34, 35, 41, 53.
- **Req 12** (Correctness properties): Tasks 17, 35, 36, 37, 44, 45, 46, 47, 53.
- **Req 13** (Non-goals): Respected implicitly — no task introduces bitwise / compound-assignment / increment / conversion operators or generic constraints. No audit task needed because the work list above contains no line item implementing any of them.
- **Req 14** (Diagnostic IDs): Tasks 9, 12, 43.
- **Req 15** (Open questions): Resolved in design; Tasks 15 (15.4), 28 (15.5), 25 (15.6), 32 (15.3).

## Notes

- **No tasks are marked `*` (optional).** Per the user's guidance in the prompt, property-based tests are required deliverables (Requirement 12 explicitly lists them as acceptance criteria), and regression guards (Tasks 34, 35) are load-bearing for Requirement 11. Testing is integrated into each stage rather than saved for the end.
- **Checkpoints (Tasks 10, 18, 29, 38, 48, 53)** mark stage boundaries where the compiler must be green before proceeding.
- **Dependency ordering**: Stage 1 before Stage 2 (grammar before semantic model), Stage 2 before Stage 3 (flag mechanism before stdlib compiles), Stage 3 before Stage 4 (stdlib types must exist before keyword flip), Stage 4 before Stage 5 (semantic model uses stdlib types before emitter migrates to Backing_Type_Map), Stage 5 before Stage 6 (emitter migrated before old singletons are deleted).
- **Implementation language**: C# (compiler itself); Mist for stdlib source files.
- **Property-based testing library**: FsCheck (per design § *Testing Strategy* and Requirement 12.8).
- **Regression baselines** (Task 34) must be captured BEFORE any work in Stage 4 begins — if Stage 4 has already altered emitter output, the baseline is no longer authoritative. Plan this capture as the first act of Stage 4 in real calendar time.
