// Feature: definite-assignment
// Property 3: Stampability classification correctness
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for stampability classification in the semantic model.
///
/// Property 3: A struct is classified <see cref="StructType.IsStampable"/>
/// if and only if every instance field's type is transitively a primitive or
/// a stampable struct; any reference-typed field (class, string) or any
/// type-parameter-typed field makes it non-stampable. Classes are never
/// stampable.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public class StampabilityClassificationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer SharedLexer = new MistLexer();
    private static readonly IParser SharedParser = new MistParser();

    /// <summary>
    /// Parses source and builds a semantic model in-process. No process
    /// spawn. Uses lex → parse → SemanticModelBuilder directly.
    /// </summary>
    private static ISemanticModel BuildModelFromSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var lexBag = new DiagnosticBag();
        var tokens = SharedLexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = SharedParser.Parse(tokens, parseBag);

        if (parseResult.Root is null)
            throw new InvalidOperationException(
                $"Parse failed: {string.Join("; ", parseBag.ToImmutableArray().Select(d => d.Message))}");

        parseResult.Root.SetParentLinks();

        var semanticBag = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        return builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);
    }

    // ─── Field type enum for generation ──────────────────────────────────

    /// <summary>
    /// Represents the category of a field type for stampability testing.
    /// </summary>
#pragma warning disable CA1034
    public enum FieldTypeKind
    {
        /// <summary>A primitive type (int, bool, float, etc.) — stampable.</summary>
        Primitive,
        /// <summary>A nested struct with all-primitive fields — stampable.</summary>
        StampableStruct,
        /// <summary>A class type — makes the containing struct non-stampable.</summary>
        ClassField,
        /// <summary>A string field (reference type) — non-stampable.</summary>
        StringField,
        /// <summary>A type parameter field — non-stampable.</summary>
        TypeParameterField,
    }

    /// <summary>
    /// Represents a generated struct scenario for stampability testing.
    /// </summary>
    public record StampabilityScenario(
        string Source,
        string StructName,
        ImmutableArray<(string FieldName, FieldTypeKind Kind)> Fields,
        bool ExpectedStampable)
    {
        public override string ToString()
        {
            var fields = string.Join(", ",
                Fields.Select(f => $"{f.Kind} {f.FieldName}"));
            return $"struct {StructName} {{ {fields} }} " +
                   $"→ IsStampable={ExpectedStampable}";
        }
    }
#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "double", "float", "char", "long", "short", "byte"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] StructNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    // Note: Nested struct and class names are generated dynamically
    // (Inner0, Inner1, ..., Service0, Service1, ...) to guarantee
    // uniqueness across fields and avoid declaration collisions.

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a stampability scenario: a struct with a randomized mix of
    /// field types. The expected stampability is computed from the field
    /// types: stampable iff all fields are primitive or stampable struct.
    /// </summary>
    private static Gen<StampabilityScenario> GenStampabilityScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldCount in Gen.Choose(1, 5)
               from fields in GenFields(fieldCount)
               let expectedStampable = fields.All(f =>
                   f.Kind is FieldTypeKind.Primitive
                         or FieldTypeKind.StampableStruct)
               let source = BuildSource(structName, fields)
               select new StampabilityScenario(
                   source, structName, fields, expectedStampable);
    }

    private static Gen<ImmutableArray<(string FieldName, FieldTypeKind Kind)>>
        GenFields(int count)
    {
        if (count == 0)
            return Gen.Constant(
                ImmutableArray<(string, FieldTypeKind)>.Empty);

        Gen<ImmutableArray<(string, FieldTypeKind)>> result =
            from kind in GenFieldKind()
            select ImmutableArray.Create((FieldNames[0], kind));

        for (var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from kind in GenFieldKind()
                     select arr.Add((FieldNames[idx], kind));
        }

        return result;
    }

    private static Gen<FieldTypeKind> GenFieldKind()
    {
        return Gen.Elements(
            FieldTypeKind.Primitive,
            FieldTypeKind.StampableStruct,
            FieldTypeKind.ClassField,
            FieldTypeKind.StringField,
            FieldTypeKind.TypeParameterField);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<StampabilityScenario>
        ArbitraryStampabilityScenario() =>
        GenStampabilityScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source code for a stampability scenario. Generates all
    /// necessary supporting type declarations (nested structs, classes).
    /// </summary>
    private static string BuildSource(
        string structName,
        ImmutableArray<(string FieldName, FieldTypeKind Kind)> fields)
    {
        var declarations = new List<string>();
        var structFields = new List<string>();

        // Track which supporting types we need to declare
        var nestedStructsNeeded = new HashSet<string>();
        var classesNeeded = new HashSet<string>();
        var hasTypeParam = false;

        // Choose concrete type names for each field — use unique indexed
        // names to avoid collisions when multiple fields share a kind.
        var fieldSpecs = new List<(string FieldName, FieldTypeKind Kind, string TypeName)>();
        var nestedIdx = 0;
        var classIdx = 0;
        var primIdx = 0;

        foreach (var (fieldName, kind) in fields)
        {
            var typeName = kind switch
            {
                FieldTypeKind.Primitive =>
                    PrimitiveTypes[primIdx++ % PrimitiveTypes.Length],
                FieldTypeKind.StampableStruct =>
                    $"Inner{nestedIdx++}",
                FieldTypeKind.ClassField =>
                    $"Service{classIdx++}",
                FieldTypeKind.StringField => "string",
                FieldTypeKind.TypeParameterField => "T",
                _ => "int",
            };

            fieldSpecs.Add((fieldName, kind, typeName));

            switch (kind)
            {
                case FieldTypeKind.StampableStruct:
                    nestedStructsNeeded.Add(typeName);
                    break;
                case FieldTypeKind.ClassField:
                    classesNeeded.Add(typeName);
                    break;
                case FieldTypeKind.TypeParameterField:
                    hasTypeParam = true;
                    break;
            }
        }

        // Declare nested stampable structs (with a single int field)
        foreach (var name in nestedStructsNeeded)
        {
            declarations.Add($$"""
                public struct {{name}}
                {
                    public int value;
                }
                """);
        }

        // Declare classes referenced by class fields
        foreach (var name in classesNeeded)
        {
            declarations.Add($$"""
                public class {{name}}
                {
                    public int id;
                }
                """);
        }

        // Build the struct under test
        foreach (var (fieldName, _, typeName) in fieldSpecs)
        {
            structFields.Add($"    public {typeName} {fieldName};");
        }

        var typeParamSuffix = hasTypeParam ? "<T>" : "";
        declarations.Add($$"""
            public struct {{structName}}{{typeParamSuffix}}
            {
            {{string.Join("\n", structFields)}}
            }
            """);

        return $$"""
            namespace TestProject;

            {{string.Join("\n\n", declarations)}}
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Stampability classification correctness
    //
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    //
    // A struct is classified IsStampable if and only if every instance
    // field's type is transitively a primitive or a stampable struct; any
    // reference-typed field or any type-parameter-typed field makes it
    // non-stampable. Classes are never stampable.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 3: Stampability classification matches the recursive
    /// predicate. A struct with only primitive or nested-stampable-struct
    /// fields is stampable; any class, string, or type-parameter field
    /// makes it non-stampable.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(StampabilityClassificationPropertyTests) },
        MaxTest = 100)]
    public Property StampabilityMatchesRecursivePredicate(
        StampabilityScenario scenario)
    {
        var model = BuildModelFromSource(scenario.Source);

        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s =>
                s.Name == scenario.StructName &&
                s.Kind == SymbolKind.Struct);

        if (typeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find struct '{scenario.StructName}' " +
                       $"in semantic model. Scenario: {scenario}");
        }

        if (typeSymbol.DeclaredType is not StructType structType)
        {
            return false.ToProperty()
                .Label($"Symbol '{scenario.StructName}' is not a " +
                       $"StructType. Scenario: {scenario}");
        }

        // For generic structs (those with type parameter fields), the
        // ComputeStampability pass skips them (they have unbound type
        // params), so IsStampable remains false — which matches our
        // expectation since type-parameter fields make a struct
        // non-stampable.
        var actualStampable = structType.IsStampable;

        return (actualStampable == scenario.ExpectedStampable)
            .ToProperty()
            .Label($"Expected IsStampable={scenario.ExpectedStampable}" +
                   $" but got {actualStampable}. " +
                   $"Scenario: {scenario}");
    }
}
