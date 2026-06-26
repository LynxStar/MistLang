// Feature: dependent-type-refinement-constraints, Property 6: Completeness Silence on Full Consumption

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests verifying that the parser does NOT emit
/// <c>MIST_PARSE_INCOMPLETE</c> for fully valid source files.
///
/// For all source files where the Storm parser returns a non-null root and
/// consumes all default-channel tokens, no <c>MIST_PARSE_INCOMPLETE</c>
/// diagnostic is emitted.
///
/// **Validates: Requirements 3.2**
/// </summary>
public class CompletenessSilencePropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();

    // --- Helpers ---

    private static ImmutableArray<Diagnostic> ParseAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        _ = Parser.Parse(tokens, parseBag);

        return parseBag.ToImmutableArray();
    }

    // --- Test data model ---

#pragma warning disable CA1034
    public record ValidSourceScenario(string Description, string Source)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // --- Generators ---

    private static readonly string[] ClassNames =
        ["Foo", "Bar", "Widget", "Container", "Handler", "Service", "Builder"];

    private static readonly string[] StructNames =
        ["Point", "Coord", "Entry", "Slot", "Cell", "Pair"];

    private static readonly string[] MethodNames =
        ["Run", "Execute", "Process", "Compute", "GetValue", "Apply"];

    private static readonly string[] FieldNames =
        ["value", "data", "count", "name", "items", "result"];

    private static readonly string[] TypeParamNames =
        ["T", "U", "V", "W", "TValue", "TKey"];

    private static readonly string[] PrimitiveTypes =
        ["int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "bool", "char", "string", "float", "double"];

    private static readonly string[] Namespaces =
        ["MyApp", "Core", "Utils", "Domain", "Services"];

    /// <summary>
    /// Generates an empty source file (trivially valid).
    /// </summary>
    private static Gen<ValidSourceScenario> GenEmptyFile() =>
        Gen.Constant(new ValidSourceScenario("empty file", ""));

    /// <summary>
    /// Generates a namespace-only source file.
    /// </summary>
    private static Gen<ValidSourceScenario> GenNamespaceOnly() =>
        from ns in Gen.Elements(Namespaces)
        select new ValidSourceScenario($"namespace {ns}", $"namespace {ns};");

    /// <summary>
    /// Generates a namespace with a single empty class.
    /// </summary>
    private static Gen<ValidSourceScenario> GenNamespaceWithClass() =>
        from ns in Gen.Elements(Namespaces)
        from cls in Gen.Elements(ClassNames)
        select new ValidSourceScenario(
            $"namespace {ns} + class {cls}",
            $"namespace {ns}; class {cls} {{ }}");

    /// <summary>
    /// Generates a class with fields.
    /// </summary>
    private static Gen<ValidSourceScenario> GenClassWithFields() =>
        from cls in Gen.Elements(ClassNames)
        from fields in (from name in Gen.Elements(FieldNames)
                        from typ in Gen.Elements(PrimitiveTypes)
                        select $"    {typ} {name};").ListOf()
            .Where(list => list.Count >= 1 && list.Count <= 3)
        let fieldBlock = string.Join("\n", fields.Distinct())
        select new ValidSourceScenario(
            $"class {cls} with {fields.Count} fields",
            $"class {cls} {{\n{fieldBlock}\n}}");

    /// <summary>
    /// Generates a class with a method that has a return statement.
    /// </summary>
    private static Gen<ValidSourceScenario> GenClassWithMethod() =>
        from cls in Gen.Elements(ClassNames)
        from method in Gen.Elements(MethodNames)
        from retType in Gen.Elements("int", "bool", "string")
        let body = retType switch
        {
            "int" => "return 0;",
            "bool" => "return true;",
            _ => "return \"hello\";"
        }
        select new ValidSourceScenario(
            $"class {cls} with method {method}",
            $"class {cls} {{\n    public {retType} {method}() {{\n        {body}\n    }}\n}}");

    /// <summary>
    /// Generates a class with a generic type parameter.
    /// </summary>
    private static Gen<ValidSourceScenario> GenGenericClass() =>
        from cls in Gen.Elements(ClassNames)
        from tp in Gen.Elements(TypeParamNames)
        select new ValidSourceScenario(
            $"generic class {cls}<{tp}>",
            $"class {cls}<{tp}> {{ }}");

    /// <summary>
    /// Generates a struct declaration.
    /// </summary>
    private static Gen<ValidSourceScenario> GenStruct() =>
        from name in Gen.Elements(StructNames)
        from field in Gen.Elements(FieldNames)
        from typ in Gen.Elements("int", "bool", "string")
        select new ValidSourceScenario(
            $"struct {name}",
            $"struct {name} {{\n    {typ} {field};\n}}");

    /// <summary>
    /// Generates a namespace with multiple classes.
    /// </summary>
    private static Gen<ValidSourceScenario> GenNamespaceWithMultipleClasses() =>
        from ns in Gen.Elements(Namespaces)
        from classes in Gen.Elements(ClassNames).ListOf()
            .Where(list => list.Count >= 2 && list.Count <= 3)
        let distinctClasses = classes.Distinct().ToArray()
        where distinctClasses.Length >= 2
        let classDecls = string.Join("\n", distinctClasses.Select(c => $"class {c} {{ }}"))
        select new ValidSourceScenario(
            $"namespace {ns} with {distinctClasses.Length} classes",
            $"namespace {ns};\n{classDecls}");

    /// <summary>
    /// Generates a class with a static void Main entry point.
    /// </summary>
    private static Gen<ValidSourceScenario> GenProgramWithMain() =>
        from ns in Gen.Elements(Namespaces)
        select new ValidSourceScenario(
            "program with Main",
            $"namespace {ns};\nclass Program {{\n    public static void Main() {{\n    }}\n}}");

    /// <summary>
    /// Generates a class with a dependent type parameter using a primitive constraint.
    /// </summary>
    private static Gen<ValidSourceScenario> GenDependentTypeClass() =>
        from cls in Gen.Elements(ClassNames)
        from param in Gen.Elements("Size", "Count", "Length")
        from prim in Gen.Elements("int", "uint", "long", "bool", "char")
        select new ValidSourceScenario(
            $"dependent type class {cls}<{param}:{prim}>",
            $"class {cls}<{param}:{prim}> {{ }}");

    /// <summary>
    /// Generates a using directive with a namespace.
    /// </summary>
    private static Gen<ValidSourceScenario> GenUsingDirective() =>
        from ns in Gen.Elements(Namespaces)
        from cls in Gen.Elements(ClassNames)
        select new ValidSourceScenario(
            $"using {ns} + class {cls}",
            $"using {ns};\nclass {cls} {{ }}");

    /// <summary>
    /// Combined generator producing a variety of valid Mist source files.
    /// </summary>
    private static Gen<ValidSourceScenario> GenValidSource() =>
        Gen.OneOf(
            GenEmptyFile(),
            GenNamespaceOnly(),
            GenNamespaceWithClass(),
            GenClassWithFields(),
            GenClassWithMethod(),
            GenGenericClass(),
            GenStruct(),
            GenNamespaceWithMultipleClasses(),
            GenProgramWithMain(),
            GenDependentTypeClass(),
            GenUsingDirective());

    public static Arbitrary<ValidSourceScenario> ArbitraryValidSource() =>
        GenValidSource().ToArbitrary();

    // --- Property 6: Completeness Silence on Full Consumption ---

    /// <summary>
    /// Property 6: Completeness Silence on Full Consumption.
    ///
    /// For all source files where the Storm parser returns a non-null root and
    /// consumes all default-channel tokens, no <c>MIST_PARSE_INCOMPLETE</c>
    /// diagnostic is emitted.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(
        MaxTest = 200,
        Arbitrary = new[] { typeof(CompletenessSilencePropertyTests) })]
    public bool FullyValidSource_NeverEmitsIncomplete(ValidSourceScenario scenario)
    {
        var diagnostics = ParseAndGetDiagnostics(scenario.Source);

        var incompleteDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_PARSE_INCOMPLETE")
            .ToList();

        return incompleteDiags.Count == 0;
    }

    // --- Deterministic validation with known-good inputs ---

    /// <summary>
    /// Validates that a curated set of known-valid programs produce no
    /// MIST_PARSE_INCOMPLETE diagnostics. These cover structural patterns
    /// that the random generator might not exercise.
    /// </summary>
    [Fact]
    public void KnownValidSources_NeverEmitIncomplete()
    {
        var validSources = new[]
        {
            // Empty file
            "",
            // Comment-only file
            "// just a comment",
            // Namespace only
            "namespace MyApp;",
            // Empty class
            "class Foo { }",
            // Namespace + class
            "namespace MyApp; class Foo { }",
            // Class with method
            "class Foo { public int GetValue() { return 42; } }",
            // Multiple classes
            "class Foo { } class Bar { }",
            // Generic class
            "class Container<T> { }",
            // Struct
            "struct Point { int x; int y; }",
            // Class with static method
            "class Program { public static void Main() { } }",
            // Namespace + using + class
            "using Standard; namespace MyApp; class Foo { }",
            // Dependent type parameter
            "class Fixed<Size:int> { }",
            // Multiple type parameters
            "class Map<TKey, TValue> { }",
            // Dependent type with identifier constraint
            "type PositiveInt = int >= 0;\nclass Arr<Length:PositiveInt> { }",
            // Class with abstract type
            "class Foo : IDisposable { }",
            // Class with field and method
            "class Counter {\n    int count;\n    public int GetCount() {\n        return count;\n    }\n}",
        };

        foreach(var source in validSources)
        {
            var diagnostics = ParseAndGetDiagnostics(source);
            var incompleteDiags = diagnostics
                .Where(d => d.Id.Value == "MIST_PARSE_INCOMPLETE")
                .ToList();

            Assert.True(
                incompleteDiags.Count == 0,
                $"Source produced MIST_PARSE_INCOMPLETE but is valid:\n---\n{source}\n---\nDiagnostic: {(incompleteDiags.Count > 0 ? incompleteDiags[0].Message : "")}");
        }
    }
}