// Feature: type-unions, Property 5: Semantic Model Resolution with Flattening and Deduplication
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Represents a generated union type scenario for semantic model resolution testing.
/// The scenario includes the source type annotation and the expected distinct primitive
/// type names after flattening and deduplication.
/// </summary>
public sealed class UnionSemanticScenario
{
    /// <summary>
    /// The union type annotation as it appears in source (e.g., "(int | string) | bool").
    /// </summary>
    public string TypeAnnotation { get; }

    /// <summary>
    /// The expected distinct primitive type names after flattening and deduplication,
    /// in alphabetical order (canonical ordering by DisplayName).
    /// </summary>
    public ImmutableArray<string> ExpectedDistinctTypes { get; }

    public UnionSemanticScenario(string typeAnnotation, ImmutableArray<string> expectedDistinctTypes)
    {
        TypeAnnotation = typeAnnotation;
        ExpectedDistinctTypes = expectedDistinctTypes;
    }

    public override string ToString()
    {
        return $"{TypeAnnotation} → [{string.Join(", ", ExpectedDistinctTypes)}]";
    }
}

/// <summary>
/// Property-based tests for semantic model resolution of union types with
/// flattening and deduplication.
///
/// Property 5: For any union type reference, the semantic model SHALL resolve it to
/// a UnionType whose constituents are the flattened, deduplicated set of resolved
/// types — nested unions are inlined, duplicates are removed, and a single remaining
/// constituent reduces to that type directly.
/// </summary>
public class UnionSemanticModelPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder builder = new();

    // ─── Known primitive types for generation ────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    // ─── Generator ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates union type scenarios that exercise:
    /// - Simple unions: "int | string"
    /// - Nested unions via parenthesized grouping: "(int | string) | bool"
    /// - Duplicate types: "int | int | string"
    /// - Combinations of nesting and duplication
    /// </summary>
    public static Arbitrary<UnionSemanticScenario> ArbitraryUnionSemanticScenario()
    {
        var gen = Gen.Frequency(
            (3, GenSimpleUnion()),
            (3, GenNestedUnion()),
            (2, GenDuplicateUnion()),
            (2, GenNestedWithDuplicates()),
            (1, GenAllDuplicatesReduceToSingle()));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates a simple union: "T1 | T2 | ... | Tn" with 2-4 distinct types.
    /// </summary>
    private static Gen<UnionSemanticScenario> GenSimpleUnion()
    {
        return from count in Gen.Choose(2, 4)
               from types in GenDistinctTypes(count)
               let annotation = string.Join(" | ", types)
               let expected = types.OrderBy(t => MapToDisplayName(t), StringComparer.Ordinal).ToImmutableArray()
               select new UnionSemanticScenario(annotation, expected);
    }

    /// <summary>
    /// Generates a nested union via parenthesized grouping:
    /// "(T1 | T2) | T3" which should flatten to T1 | T2 | T3.
    /// </summary>
    private static Gen<UnionSemanticScenario> GenNestedUnion()
    {
        return from innerCount in Gen.Choose(2, 3)
               from outerCount in Gen.Choose(1, 2)
               from allTypes in GenDistinctTypes(innerCount + outerCount)
               let innerTypes = allTypes.Take(innerCount).ToArray()
               let outerTypes = allTypes.Skip(innerCount).ToArray()
               let innerAnnotation = $"({string.Join(" | ", innerTypes)})"
               let annotation = string.Join(" | ", new[] { innerAnnotation }.Concat(outerTypes))
               let expected = allTypes.Select(MapToDisplayName).OrderBy(t => t, StringComparer.Ordinal).ToImmutableArray()
               select new UnionSemanticScenario(annotation, expected);
    }

    /// <summary>
    /// Generates a union with duplicate types: "T1 | T1 | T2" which should
    /// deduplicate to T1 | T2.
    /// </summary>
    private static Gen<UnionSemanticScenario> GenDuplicateUnion()
    {
        return from distinctCount in Gen.Choose(2, 3)
               from types in GenDistinctTypes(distinctCount)
               from dupIndex in Gen.Choose(0, distinctCount - 1)
               let duplicated = types.Append(types[dupIndex]).ToArray()
               let annotation = string.Join(" | ", duplicated)
               let expected = types.Select(MapToDisplayName).OrderBy(t => t, StringComparer.Ordinal).ToImmutableArray()
               select new UnionSemanticScenario(annotation, expected);
    }

    /// <summary>
    /// Generates a nested union with duplicates: "(T1 | T2) | T1" which should
    /// flatten and deduplicate to T1 | T2.
    /// </summary>
    private static Gen<UnionSemanticScenario> GenNestedWithDuplicates()
    {
        return from distinctCount in Gen.Choose(2, 3)
               from types in GenDistinctTypes(distinctCount)
               from dupIndex in Gen.Choose(0, distinctCount - 1)
               let innerTypes = types.Take(2).ToArray()
               let outerTypes = types.Skip(2).Append(types[dupIndex]).ToArray()
               let innerAnnotation = $"({string.Join(" | ", innerTypes)})"
               let annotation = outerTypes.Length > 0
                   ? string.Join(" | ", new[] { innerAnnotation }.Concat(outerTypes))
                   : $"{innerAnnotation} | {types[dupIndex]}"
               let expected = types.Select(MapToDisplayName).OrderBy(t => t, StringComparer.Ordinal).ToImmutableArray()
               select new UnionSemanticScenario(annotation, expected);
    }

    /// <summary>
    /// Generates a union where all constituents are the same type: "T | T | T"
    /// which should deduplicate to just T (single-constituent reduction).
    /// </summary>
    private static Gen<UnionSemanticScenario> GenAllDuplicatesReduceToSingle()
    {
        return from typeName in Gen.Elements(PrimitiveTypes)
               from count in Gen.Choose(2, 4)
               let annotation = string.Join(" | ", Enumerable.Repeat(typeName, count))
               let expected = ImmutableArray.Create(MapToDisplayName(typeName))
               select new UnionSemanticScenario(annotation, expected);
    }

    /// <summary>
    /// Generates an array of N distinct primitive type names.
    /// </summary>
    private static Gen<string[]> GenDistinctTypes(int count)
    {
        return Gen.Shuffle(PrimitiveTypes)
            .Select(shuffled => shuffled.Take(count).ToArray());
    }

    /// <summary>
    /// Maps a Mist primitive keyword to its semantic model DisplayName.
    /// </summary>
    private static string MapToDisplayName(string keyword)
    {
        return keyword switch
        {
            "int" => "int",
            "string" => "string",
            "bool" => "bool",
            "float" => "float",
            "double" => "double",
            _ => keyword,
        };
    }

    // ─── Semantic model helpers ─────────────────────────────────────────

    private ISemanticModel? BuildSemanticModelFromSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if(bag.HasErrors)
        {
            return null;
        }

        var result = Parser.Parse(tokens, bag);
        if(bag.HasErrors || result.Root is null)
        {
            return null;
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semanticBag = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(result.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);
    }

    /// <summary>
    /// Wraps a type annotation in a minimal Mist program and returns the resolved
    /// type of the local variable declared with that annotation.
    /// </summary>
    private IType? GetResolvedType(string typeAnnotation)
    {
        var source = $"class T {{ public void M() {{ {typeAnnotation} x = default; }} }}";
        var model = BuildSemanticModelFromSource(source);
        if(model is null)
        {
            return null;
        }

        var localVar = model.AllSymbols
            .FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);
        return localVar?.DeclaredType;
    }

    // ─── Property Tests ─────────────────────────────────────────────────

    /// <summary>
    /// Property 5a: Nested unions are flattened — no UnionType contains another
    /// UnionType as a constituent.
    ///
    /// **Validates: Requirements 5.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionSemanticModelPropertyTests) })]
    public bool NestedUnions_AreFlattenedInSemanticModel(UnionSemanticScenario scenario)
    {
        var resolvedType = GetResolvedType(scenario.TypeAnnotation);
        if(resolvedType is null)
        {
            return false;
        }

        // If only one distinct type remains after dedup, it reduces to that type (not a union).
        if(scenario.ExpectedDistinctTypes.Length == 1)
        {
            return resolvedType is not UnionType;
        }

        if(resolvedType is not UnionType union)
        {
            return false;
        }

        // No constituent should itself be a UnionType (flattening).
        return union.Constituents.All(c => c is not UnionType);
    }

    /// <summary>
    /// Property 5b: Duplicates are removed — all constituents have distinct DisplayNames.
    ///
    /// **Validates: Requirements 5.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionSemanticModelPropertyTests) })]
    public bool Duplicates_AreRemovedInSemanticModel(UnionSemanticScenario scenario)
    {
        var resolvedType = GetResolvedType(scenario.TypeAnnotation);
        if(resolvedType is null)
        {
            return false;
        }

        // If only one distinct type remains after dedup, it reduces to that type.
        if(scenario.ExpectedDistinctTypes.Length == 1)
        {
            return resolvedType is not UnionType;
        }

        if(resolvedType is not UnionType union)
        {
            return false;
        }

        // All constituents should have distinct DisplayNames.
        var displayNames = union.Constituents.Select(c => c.DisplayName).ToList();
        return displayNames.Distinct(StringComparer.Ordinal).Count() == displayNames.Count;
    }

    /// <summary>
    /// Property 5c: The resolved union has exactly the expected distinct constituents
    /// (matching the flattened, deduplicated set).
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionSemanticModelPropertyTests) })]
    public bool ResolvedUnion_HasExpectedConstituents(UnionSemanticScenario scenario)
    {
        var resolvedType = GetResolvedType(scenario.TypeAnnotation);
        if(resolvedType is null)
        {
            return false;
        }

        // If only one distinct type remains after dedup, it reduces to that type.
        if(scenario.ExpectedDistinctTypes.Length == 1)
        {
            return resolvedType is not UnionType
                && resolvedType.DisplayName == scenario.ExpectedDistinctTypes[0];
        }

        if(resolvedType is not UnionType union)
        {
            return false;
        }

        // Constituents should match expected (sorted by DisplayName).
        var actualNames = union.Constituents
            .Select(c => c.DisplayName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if(actualNames.Count != scenario.ExpectedDistinctTypes.Length)
        {
            return false;
        }

        for(var i = 0; i < actualNames.Count; i++)
        {
            if(actualNames[i] != scenario.ExpectedDistinctTypes[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Property 5d: Single-constituent result after deduplication is reduced to
    /// the type itself (not a UnionType).
    ///
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionSemanticModelPropertyTests) })]
    public bool SingleConstituent_AfterDedup_ReducesToTypeItself(UnionSemanticScenario scenario)
    {
        // This property only applies to scenarios that reduce to a single type.
        // For multi-type scenarios, we just return true (vacuously true).
        if(scenario.ExpectedDistinctTypes.Length != 1)
        {
            return true;
        }

        var resolvedType = GetResolvedType(scenario.TypeAnnotation);
        if(resolvedType is null)
        {
            return false;
        }

        // Should NOT be a UnionType — reduced to the single type.
        return resolvedType is not UnionType
            && resolvedType.DisplayName == scenario.ExpectedDistinctTypes[0];
    }

    // ─── Property 6 ─────────────────────────────────────────────────────

    // Feature: type-unions, Property 6: Union Type Order Equivalence

    /// <summary>
    /// Generates a pair of type annotations that are permutations of the same
    /// distinct type set (2-4 types). Used to verify order equivalence.
    /// </summary>
    public static Arbitrary<UnionOrderEquivalenceScenario> ArbitraryUnionOrderEquivalenceScenario()
    {
        var gen =
            from count in Gen.Choose(2, 4)
            from types in GenDistinctTypes(count)
            from shuffled in Gen.Shuffle(types)
            let annotation1 = string.Join(" | ", types)
            let annotation2 = string.Join(" | ", shuffled)
            select new UnionOrderEquivalenceScenario(annotation1, annotation2, types);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 6: For any set of distinct types, all permutations of those types
    /// in a union type annotation SHALL resolve to the same UnionType (same canonical
    /// constituent list, same DisplayName).
    ///
    /// **Validates: Requirements 6.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionSemanticModelPropertyTests) })]
    public bool UnionTypeOrder_IsEquivalent(UnionOrderEquivalenceScenario scenario)
    {
        var type1 = GetResolvedType(scenario.Annotation1);
        var type2 = GetResolvedType(scenario.Annotation2);

        if(type1 is null || type2 is null)
        {
            return false;
        }

        if(type1 is not UnionType union1 || type2 is not UnionType union2)
        {
            return false;
        }

        // Same DisplayName (canonical ordering ensures this).
        if(union1.DisplayName != union2.DisplayName)
        {
            return false;
        }

        // Same Constituents (count and display names).
        if(union1.Constituents.Count != union2.Constituents.Count)
        {
            return false;
        }

        for(var i = 0; i < union1.Constituents.Count; i++)
        {
            if(union1.Constituents[i].DisplayName != union2.Constituents[i].DisplayName)
            {
                return false;
            }
        }

        // Structural equality via Equals.
        return union1.Equals(union2);
    }
}

/// <summary>
/// Represents a pair of union type annotations that are permutations of the same
/// distinct type set, used for testing order equivalence (Property 6).
/// </summary>
public sealed class UnionOrderEquivalenceScenario
{
    /// <summary>
    /// First permutation of the type set as a union annotation (e.g., "int | string | bool").
    /// </summary>
    public string Annotation1 { get; }

    /// <summary>
    /// Second (different) permutation of the type set as a union annotation (e.g., "bool | int | string").
    /// </summary>
    public string Annotation2 { get; }

    /// <summary>
    /// The original distinct types used to generate both annotations.
    /// </summary>
    public ImmutableArray<string> Types { get; }

    public UnionOrderEquivalenceScenario(string annotation1, string annotation2, string[] types)
    {
        Annotation1 = annotation1;
        Annotation2 = annotation2;
        Types = [.. types];
    }

    public override string ToString()
    {
        return $"[{Annotation1}] vs [{Annotation2}]";
    }
}