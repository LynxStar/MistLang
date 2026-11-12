// Feature: dependent-type-refinement-constraints, Property 6: Completeness Silence on Full Consumption

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// **Validates: Requirements 3.2**
public class CompletenessSilencePropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();

    private static ImmutableArray<Diagnostic> ParseAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        _ = Parser.Parse(tokens, parseBag);

        return parseBag.ToImmutableArray();
    }

#pragma warning disable CA1034
    public record ValidSourceScenario(string Description, string Source)
    {
        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

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

    private static Gen<ValidSourceScenario> GenEmptyFile()
    {
        return Gen.Constant(new ValidSourceScenario("empty file", ""));
    }

    private static Gen<ValidSourceScenario> GenNamespaceOnly()
    {
        return from ns in Gen.Elements(Namespaces)
               select new ValidSourceScenario($"namespace {ns}", $"namespace {ns};");
    }

    private static Gen<ValidSourceScenario> GenNamespaceWithClass()
    {
        return from ns in Gen.Elements(Namespaces)
               from cls in Gen.Elements(ClassNames)
               select new ValidSourceScenario(
                   $"namespace {ns} + class {cls}",
                   $"namespace {ns}; class {cls} {{ }}");
    }

    private static Gen<ValidSourceScenario> GenClassWithFields()
    {
        return from cls in Gen.Elements(ClassNames)
               from fields in (from name in Gen.Elements(FieldNames)
                               from typ in Gen.Elements(PrimitiveTypes)
                               select $"    {typ} {name};").ListOf()
                   .Where(list => list.Count is >= 1 and <= 3)
               let fieldBlock = string.Join("\n", fields.Distinct())
               select new ValidSourceScenario(
                   $"class {cls} with {fields.Count} fields",
                   $"class {cls} {{\n{fieldBlock}\n}}");
    }

    private static Gen<ValidSourceScenario> GenClassWithMethod()
    {
        return from cls in Gen.Elements(ClassNames)
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
    }

    private static Gen<ValidSourceScenario> GenGenericClass()
    {
        return from cls in Gen.Elements(ClassNames)
               from tp in Gen.Elements(TypeParamNames)
               select new ValidSourceScenario(
                   $"generic class {cls}<{tp}>",
                   $"class {cls}<{tp}> {{ }}");
    }

    private static Gen<ValidSourceScenario> GenStruct()
    {
        return from name in Gen.Elements(StructNames)
               from field in Gen.Elements(FieldNames)
               from typ in Gen.Elements("int", "bool", "string")
               select new ValidSourceScenario(
                   $"struct {name}",
                   $"struct {name} {{\n    {typ} {field};\n}}");
    }

    private static Gen<ValidSourceScenario> GenNamespaceWithMultipleClasses()
    {
        return from ns in Gen.Elements(Namespaces)
               from classes in Gen.Elements(ClassNames).ListOf()
                   .Where(list => list.Count is >= 2 and <= 3)
               let distinctClasses = classes.Distinct().ToArray()
               where distinctClasses.Length >= 2
               let classDecls = string.Join("\n", distinctClasses.Select(c => $"class {c} {{ }}"))
               select new ValidSourceScenario(
                   $"namespace {ns} with {distinctClasses.Length} classes",
                   $"namespace {ns};\n{classDecls}");
    }

    private static Gen<ValidSourceScenario> GenProgramWithMain()
    {
        return from ns in Gen.Elements(Namespaces)
               select new ValidSourceScenario(
                   "program with Main",
                   $"namespace {ns};\nclass Program {{\n    public static void Main() {{\n    }}\n}}");
    }

    private static Gen<ValidSourceScenario> GenDependentTypeClass()
    {
        return from cls in Gen.Elements(ClassNames)
               from param in Gen.Elements("Size", "Count", "Length")
               from prim in Gen.Elements("int", "uint", "long", "bool", "char")
               select new ValidSourceScenario(
                   $"dependent type class {cls}<{param}:{prim}>",
                   $"class {cls}<{param}:{prim}> {{ }}");
    }

    private static Gen<ValidSourceScenario> GenUsingDirective()
    {
        return from ns in Gen.Elements(Namespaces)
               from cls in Gen.Elements(ClassNames)
               select new ValidSourceScenario(
                   $"using {ns} + class {cls}",
                   $"using {ns};\nclass {cls} {{ }}");
    }

    private static Gen<ValidSourceScenario> GenValidSource()
    {
        return Gen.OneOf(
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
    }

    public static Arbitrary<ValidSourceScenario> ArbitraryValidSource()
    {
        return GenValidSource().ToArbitrary();
    }

    [Property(
        MaxTest = 200,
        Arbitrary = new[] { typeof(CompletenessSilencePropertyTests) })]
    public bool FullyValidSource_NeverEmitsIncomplete(ValidSourceScenario scenario)
    {
        var diagnostics = ParseAndGetDiagnostics(scenario.Source);

        var incompleteDiags = diagnostics
            .Where(d => d.Number.Value == "MIST0123")
            .ToList();

        return incompleteDiags.Count == 0;
    }

    /// Curated structural patterns the random generator might not exercise.
    [Fact]
    public void KnownValidSources_NeverEmitIncomplete()
    {
        var validSources = new[]
        {
            "",
            "// just a comment",
            "namespace MyApp;",
            "class Foo { }",
            "namespace MyApp; class Foo { }",
            "class Foo { public int GetValue() { return 42; } }",
            "class Foo { } class Bar { }",
            "class Container<T> { }",
            "struct Point { int x; int y; }",
            "class Program { public static void Main() { } }",
            "using Standard; namespace MyApp; class Foo { }",
            "class Fixed<Size:int> { }",
            "class Map<TKey, TValue> { }",
            "type PositiveInt = int >= 0;\nclass Arr<Length:PositiveInt> { }",
            "class Foo : IDisposable { }",
            "class Counter {\n    int count;\n    public int GetCount() {\n        return count;\n    }\n}",
        };

        foreach(var source in validSources)
        {
            var diagnostics = ParseAndGetDiagnostics(source);
            var incompleteDiags = diagnostics
                .Where(d => d.Number.Value == "MIST0123")
                .ToList();

            Assert.True(
                incompleteDiags.Count == 0,
                $"Source produced MIST0123 but is valid:\n---\n{source}\n---\nDiagnostic: {(incompleteDiags.Count > 0 ? incompleteDiags[0].Message : "")}");
        }
    }
}