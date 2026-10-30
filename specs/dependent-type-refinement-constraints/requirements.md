# Requirements Document

## Introduction

This feature extends the Mist compiler to allow refinement types as dependent type parameter constraints, adds parser completeness checking so silently dropped content is detected, and removes synthetic workarounds that exist only because `Array.mist` and `Span.mist` could not parse their type parameter constraints.

Today, the `TypeParameter` grammar rule only accepts `PrimitiveType` as a dependent constraint (e.g., `Length:int`). Refinement types like `ZeroOrPositiveInt` — whose base type is an allowed primitive — are rejected at the grammar level, causing stdlib declarations like `Array<TValue, Length:ZeroOrPositiveInt>` to silently fail to parse. The PEG parser's `*` repetition drops the unparseable content without emitting any diagnostic, leaving users with confusing downstream "type not found" errors.

## Glossary

- **Storm_Parser**: The PEG parser generated from `Mist.storm` grammar rules. Produces syntax tree nodes from token streams.
- **MistParser**: The C# wrapper (`MistParser.cs`) that bridges the Storm-generated parser to the compiler pipeline via the `IParser` interface.
- **TypeParameter_Rule**: The grammar rule that defines how type parameters are parsed, producing either `DependentTypeParameter` or `RegularTypeParameter` nodes.
- **PrimitiveType**: A grammar rule matching one of the built-in type keywords: int, uint, long, ulong, short, ushort, byte, sbyte, float, double, decimal, bool, char, string, regex.
- **Refinement_Type**: A user-defined type alias declared with a refinement predicate (e.g., `type ZeroOrPositiveInt = int >= 0;`). Has a base type that is one of the primitive types.
- **DependentTypeParameter**: A type parameter annotated with a value-type constraint (e.g., `Length:int` or `Length:ZeroOrPositiveInt`), indicating the parameter carries a compile-time value rather than a type.
- **Allowed_Dependent_Primitives**: The set of primitive types permitted as the resolved base type for dependent type parameters: int, uint, long, ulong, short, ushort, byte, sbyte, bool, char.
- **SemanticModelBuilder**: The compiler component that constructs the semantic model from parsed ASTs, resolving types, symbols, and scopes.
- **EnsureSpanAliasesRegistered**: A synthetic workaround method in SemanticModelBuilder that registers arity-1 `Span<T>` and `ReadOnlySpan<T>` type aliases because the full declarations fail to parse.
- **Parse_Completeness_Check**: A post-parse validation that verifies all tokens in the source file were consumed by the parser.
- **MIST0123**: A diagnostic emitted when the parser fails to consume all tokens in a source file.

## Requirements

### Requirement 1: Extend TypeParameter Grammar to Accept Identifier Constraints

**User Story:** As a language designer, I want dependent type parameters to accept refinement type names as constraints, so that stdlib types like `Array<TValue, Length:ZeroOrPositiveInt>` parse correctly.

#### Acceptance Criteria

1. WHEN a type parameter uses the syntax `Name:Identifier` where `Identifier` is not a primitive type keyword, THE TypeParameter_Rule SHALL parse it as a DependentTypeParameter node with the identifier stored as the constraint reference.
2. WHEN a type parameter uses the syntax `Name:PrimitiveKeyword` (e.g., `Size:int`), THE TypeParameter_Rule SHALL continue to parse it as a DependentTypeParameter node with the primitive type as the constraint.
3. WHEN a type parameter uses the syntax `Name` without a colon, THE TypeParameter_Rule SHALL parse it as a RegularTypeParameter node.
4. THE Storm_Parser SHALL produce a DependentTypeParameter node for `Length:ZeroOrPositiveInt` that preserves both the parameter name `Length` and the constraint name `ZeroOrPositiveInt`.

### Requirement 2: Validate Refinement Type Constraints in the Semantic Model

**User Story:** As a compiler developer, I want the semantic model builder to resolve refinement type constraints and validate their base types, so that only refinement types with allowed primitive base types are accepted as dependent type parameter constraints.

#### Acceptance Criteria

1. WHEN a DependentTypeParameter node has an identifier constraint that resolves to a Refinement_Type whose base type is in the Allowed_Dependent_Primitives set, THE SemanticModelBuilder SHALL create a DependentTypeParameterType with the resolved base primitive type as the value type.
2. WHEN a DependentTypeParameter node has an identifier constraint that resolves to a Refinement_Type whose base type is NOT in the Allowed_Dependent_Primitives set, THE SemanticModelBuilder SHALL emit diagnostic MIST0407 with a message identifying the disallowed base type.
3. WHEN a DependentTypeParameter node has an identifier constraint that does not resolve to any known type, THE SemanticModelBuilder SHALL emit a diagnostic indicating the constraint type could not be found.
4. WHEN a DependentTypeParameter node has a primitive keyword constraint (e.g., `int`), THE SemanticModelBuilder SHALL continue to validate it using the existing AllowedDependentTypeAnnotations check.

### Requirement 3: Parser Completeness Checking

**User Story:** As a Mist developer, I want the parser to report when it fails to consume all tokens in a source file, so that I get immediate feedback about unparseable content instead of confusing downstream errors.

#### Acceptance Criteria

1. WHEN the Storm_Parser returns a root node and the parser position is not at the end of the token stream, THE MistParser SHALL emit diagnostic MIST0123 with a message indicating the line where unparseable content begins.
2. WHEN the Storm_Parser returns a root node and the parser position is at the end of the token stream, THE MistParser SHALL not emit the MIST0123 diagnostic.
3. THE MIST0123 diagnostic SHALL have severity Error and pipeline stage Parsing.
4. THE MIST0123 diagnostic SHALL include the file path and line position of the first unconsumed token.

### Requirement 4: Stdlib Files Parse Completely

**User Story:** As a compiler developer, I want `Array.mist` and `Span.mist` to fully parse their type declarations, so that the semantic model is built from actual parsed ASTs rather than synthetic workarounds.

#### Acceptance Criteria

1. WHEN `Array.mist` is parsed, THE Storm_Parser SHALL consume all tokens and produce a complete CompilationUnit node containing the `Array<TValue, Length:ZeroOrPositiveInt>` class declaration.
2. WHEN `Span.mist` is parsed, THE Storm_Parser SHALL consume all tokens and produce a complete CompilationUnit node containing the `Span<T, Length:ZeroOrPositiveInt>` and `ReadOnlySpan<T, Length:ZeroOrPositiveInt>` struct declarations and the arity-1 type alias declarations.
3. WHEN `Array.mist` is processed by the SemanticModelBuilder, THE SemanticModelBuilder SHALL register the `Array` type in the Standard namespace scope with its full type parameter list and member declarations.
4. WHEN `Span.mist` is processed by the SemanticModelBuilder, THE SemanticModelBuilder SHALL register `Span` and `ReadOnlySpan` types in the Standard namespace scope with their full type parameter lists and member declarations.

### Requirement 5: Remove Synthetic Span Alias Workaround

**User Story:** As a compiler developer, I want to remove the `EnsureSpanAliasesRegistered` workaround, so that the codebase relies on parsed declarations rather than hardcoded synthetic registrations.

#### Acceptance Criteria

1. WHEN `Span.mist` declares `type Span<T> = Span<T, unknown>;` and parses completely, THE SemanticModelBuilder SHALL register the arity-1 `Span<T>` type alias from the parsed declaration.
2. WHEN `Span.mist` declares `type ReadOnlySpan<T> = ReadOnlySpan<T, unknown>;` and parses completely, THE SemanticModelBuilder SHALL register the arity-1 `ReadOnlySpan<T>` type alias from the parsed declaration.
3. THE SemanticModelBuilder SHALL not contain the `EnsureSpanAliasesRegistered` method after this feature is complete.
4. THE SemanticModelBuilder SHALL not contain the `RegisterSyntheticSpanAlias` helper method after this feature is complete.

### Requirement 6: Remove SpanType Constructor Special Case

**User Story:** As a compiler developer, I want `new Span<T>(arr)` constructor validation to use the constructor declared in `Span.mist` rather than hardcoded logic, so that the stdlib declaration is the source of truth for Span construction semantics.

#### Acceptance Criteria

1. WHEN `Span.mist` declares an intrinsic constructor and the declaration is fully parsed, THE SemanticModelBuilder SHALL register a constructor symbol in the Span type's member scope.
2. WHEN user code writes `new Span<int>(arr)`, THE TypeCheckerAnalyzer SHALL resolve the constructor through normal member-scope constructor lookup rather than the dedicated `if (createdType is SpanType)` branch.
3. WHEN user code writes `new Span<int>(arr)` with a non-array argument, THE TypeCheckerAnalyzer SHALL emit MIST_TC_0011 (historical) with the same error message content as before this change.
4. WHEN user code writes `new Span<int>()` with wrong argument count, THE TypeCheckerAnalyzer SHALL emit MIST_TC_0011 (historical) with the same error message content as before this change.
5. THE TypeCheckerAnalyzer SHALL not contain the dedicated `if (createdType is SpanType)` constructor validation branch after this feature is complete.

### Requirement 7: Remove Foreach Fast-Path for Array and Span

**User Story:** As a compiler developer, I want foreach iteration over arrays and spans to resolve the element type through the parsed `IEnumerable<T>` interface implementation rather than hardcoded fast-path checks, so that the stdlib declarations control foreach behavior.

#### Acceptance Criteria

1. WHEN `Array.mist` declares `: IEnumerable<TValue>` and is fully parsed, THE SemanticModelBuilder SHALL register the IEnumerable interface implementation on the Array type.
2. WHEN `Span.mist` declares `: IEnumerable<T>` and is fully parsed, THE SemanticModelBuilder SHALL register the IEnumerable interface implementation on the Span and ReadOnlySpan types.
3. WHEN user code iterates over an array with foreach, THE TypeCheckerAnalyzer SHALL resolve the element type through the standard `FindIEnumerableImplementation` interface lookup path.
4. WHEN user code iterates over a span with foreach, THE TypeCheckerAnalyzer SHALL resolve the element type through the standard `FindIEnumerableImplementation` interface lookup path.
5. THE `FindIEnumerableImplementation` method SHALL not contain the dedicated `if (type is ArrayType)` or `if (type is SpanType)` fast-path branches after this feature is complete.

**Note — what remains unchanged:** The dedicated `ArrayType` and `SpanType` type representations, their construction during type resolution (e.g., `new int[3]` → `ArrayType`, `stackalloc` → `SpanType`), Slice length inference, bounds checking (uses `KnownLength`/`SymbolicLengthId` directly), and type substitution all remain. These rely on structural properties of the dedicated types that cannot be expressed through parsed declarations today.

### Requirement 8: Existing Test Suite Passes

**User Story:** As a compiler developer, I want all existing tests to continue passing after these changes, so that the grammar extension and workaround removal do not introduce regressions.

#### Acceptance Criteria

1. THE compiler SHALL pass all existing test cases (currently 4,257) after the grammar extension, completeness check, and workaround removal are applied.
2. WHEN a dependent type parameter uses a primitive keyword constraint (e.g., `Size:int`), THE compiler SHALL produce identical semantic model results as before this change.
3. WHEN user code declares a dependent type parameter with an identifier constraint whose base type is not in the Allowed_Dependent_Primitives set, THE compiler SHALL emit MIST0407 diagnostic as before this change.

### Requirement 9: Round-Trip Property for TypeParameter Parsing

**User Story:** As a compiler developer, I want a round-trip property test that verifies TypeParameter nodes with refinement type constraints can be pretty-printed and re-parsed to produce equivalent ASTs.

#### Acceptance Criteria

1. FOR ALL valid TypeParameter nodes with identifier constraints (refinement type names), parsing the pretty-printed form SHALL produce an equivalent DependentTypeParameter node with the same parameter name and constraint identifier.
2. FOR ALL valid TypeParameter nodes with primitive keyword constraints, parsing the pretty-printed form SHALL produce an equivalent DependentTypeParameter node with the same parameter name and primitive type.
3. FOR ALL valid TypeParameter nodes without constraints, parsing the pretty-printed form SHALL produce an equivalent RegularTypeParameter node with the same parameter name.

### Requirement 10: Sample Application

**User Story:** As a Mist developer, I want a sample application demonstrating refinement type constraints on dependent parameters, so that I can see the feature working end-to-end and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/DependentTypeRefinementConstraints/Program.mist`.
2. The sample SHALL demonstrate: refinement type declared and used as a dependent parameter constraint, a class with a refinement-constrained dependent parameter, a struct with a refinement-constrained dependent parameter, construction with literal values that satisfy the refinement, using the dependent parameter value inside the type body, and foreach iteration over a type that uses the refinement-constrained dependent (e.g., a custom collection).
3. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
4. `samples/AllFeatures/Program.mist` SHALL call `DependentTypeRefinementConstraints.Program.RunDemo()` to include the feature in the full-suite sample run.
5. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
using Standard;

namespace DependentTypeRefinementConstraints;

// PROOF: Refinement type declaration parses correctly
type PositiveInt = int >= 1;

// PROOF: Refinement type used as dependent parameter constraint parses and resolves
// PROOF: The base type of PositiveInt (int) is in the allowed primitives set
public class SizedCollection<Count:PositiveInt>
{
    // PROOF: Dependent parameter value is accessible inside the type body
    public int GetCount()
    {
        return Count;
    }

    public string Describe()
    {
        return $"SizedCollection<{Count}>";
    }
}

// PROOF: Struct with refinement-constrained dependent parameter
public struct FixedSlot<Capacity:PositiveInt>
{
    public int GetCapacity()
    {
        return Capacity;
    }
}

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("=== Dependent Type Refinement Constraints ===");

        // PROOF: Construction with literal value satisfying the refinement (>= 1)
        var coll = new SizedCollection<5>();
        Console.WriteLine($"  coll.Describe() = {coll.Describe()}");
        Console.WriteLine($"  coll.GetCount() = {coll.GetCount()}");

        // PROOF: Struct construction with refinement-constrained dependent
        var slot = new FixedSlot<3>();
        Console.WriteLine($"  slot.GetCapacity() = {slot.GetCapacity()}");

        // PROOF: Different literal values produce distinct types
        var coll10 = new SizedCollection<10>();
        Console.WriteLine($"  coll10.Describe() = {coll10.Describe()}");

        Console.WriteLine("=== Done ===");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
=== Dependent Type Refinement Constraints ===
  coll.Describe() = SizedCollection<5>
  coll.GetCount() = 5
  slot.GetCapacity() = 3
  coll10.Describe() = SizedCollection<10>
=== Done ===
```

### Requirement 11: Book Documentation

**User Story:** As a Mist developer, I want the dependent types book chapter to document refinement type constraints, so that I can learn how to use refinement types as dependent parameter annotations.

#### Acceptance Criteria

1. THE book's dependent types chapter SHALL include a section on refinement type constraints explaining that a refinement type whose base type is an allowed primitive can be used as a dependent parameter annotation.
2. THE section SHALL include a compiled snippet demonstrating a refinement type used as a dependent parameter constraint.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
