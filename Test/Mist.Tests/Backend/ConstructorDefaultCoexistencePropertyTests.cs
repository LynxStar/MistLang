// Feature: optional-defaultability, Property 11: Both constructor and default block coexistence
// For any type that has both user-defined constructors and a `default {}` block, the compiler
// SHALL classify it as defaultable (Explicit), emit `__CreateDefault()`, AND emit the constructors.
// Both `new T(...)` and `default(T)` SHALL be valid.
// **Validates: Requirements 4.1, 12.4**

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for constructor + default block coexistence in emitted C#.
///
/// Property 11: Both constructor and default block coexistence
/// For any type that has both user-defined constructors and a <c>default {}</c> block,
/// the compiler SHALL classify it as defaultable (<c>Explicit</c>), emit
/// <c>__CreateDefault()</c>, AND emit the constructors. Both <c>new T(...)</c> and
/// <c>default(T)</c> SHALL be valid.
///
/// Uses in-process compilation via lex → parse → semantic model → type check → emit
/// in memory. Zero disk I/O, zero process spawns.
///
/// **Validates: Requirements 4.1, 12.4**
/// </summary>
public sealed partial class ConstructorDefaultCoexistencePropertyTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario describing a type with both a constructor and a default block.
    /// </summary>
    public record CoexistenceScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType, string DefaultValue)> Fields,
        int ConstructorParamCount)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} + ctor({ConstructorParamCount}) + default {{ }} {{ {fields} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta"];

    private static readonly (string Type, string DefaultValue)[] FieldTypes =
    [
        ("int", "0"),
        ("bool", "false"),
        ("string", "\"\""),
    ];

    // ─── FsCheck generators ─────────────────────────────────────────────

    private static Gen<CoexistenceScenario> GenCoexistenceScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenFields(fieldCount)
               from ctorParamCount in Gen.Choose(1, fieldCount)
               select new CoexistenceScenario(isStruct, typeName, fields, ctorParamCount);
    }

    private static Gen<ImmutableArray<(string FieldName, string FieldType, string DefaultValue)>> GenFields(
        int count)
    {
        Gen<ImmutableArray<(string, string, string)>> result =
            Gen.Constant(ImmutableArray<(string, string, string)>.Empty);

        for(var i = 0; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(FieldTypes)
                     select arr.Add((FieldNames[idx], fieldType.Type, fieldType.DefaultValue));
        }

        return result;
    }

    public static Arbitrary<CoexistenceScenario> ArbitraryCoexistenceScenario() =>
        GenCoexistenceScenario().ToArbitrary();

    // ─── Mist source builder ─────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source text for a type with both a constructor and a default block.
    /// </summary>
    private static string BuildMistSource(CoexistenceScenario scenario)
    {
        var sb = new StringBuilder();
        var kind = scenario.IsStruct ? "struct" : "class";

        _ = sb.AppendLine($"public {kind} {scenario.TypeName}");
        _ = sb.AppendLine("{");

        // Field declarations
        foreach(var (fieldName, fieldType, _) in scenario.Fields)
        {
            _ = sb.AppendLine($"    public {fieldType} {fieldName};");
        }

        _ = sb.AppendLine();

        // Constructor — takes the first N fields as params and assigns them
        var ctorParams = scenario.Fields
            .Take(scenario.ConstructorParamCount)
            .Select(f => $"{f.FieldType} {f.FieldName}Param");
        _ = sb.AppendLine($"    public {scenario.TypeName}({string.Join(", ", ctorParams)})");
        _ = sb.AppendLine("    {");
        foreach(var (fieldName, _, _) in scenario.Fields.Take(scenario.ConstructorParamCount))
        {
            _ = sb.AppendLine($"        this.{fieldName} = {fieldName}Param;");
        }
        // Assign remaining fields with their default values
        foreach(var (fieldName, _, defaultValue) in scenario.Fields.Skip(scenario.ConstructorParamCount))
        {
            _ = sb.AppendLine($"        this.{fieldName} = {defaultValue};");
        }
        _ = sb.AppendLine("    }");

        _ = sb.AppendLine();

        // Default block
        _ = sb.AppendLine("    default");
        _ = sb.AppendLine("    {");
        foreach(var (fieldName, _, defaultValue) in scenario.Fields)
        {
            _ = sb.AppendLine($"        {fieldName} = {defaultValue}");
        }
        _ = sb.AppendLine("    }");

        _ = sb.AppendLine("}");

        // Program class with Main entry point
        _ = sb.AppendLine();
        _ = sb.AppendLine("class Program");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public static void Main()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    // ─── In-memory compilation ───────────────────────────────────────────

    private static (bool Succeeded, string CsContent, string DiagnosticText) CompileInMemory(
        string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(lexBag));
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return (false, "", FormatDiagnostics(parseBag));
        }

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);

        if(semBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(semBag));
        }

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        if(tcBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return (false, "", FormatDiagnostics(tcBag));
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if(emitBag.HasErrors)
        {
            return (false, "", FormatDiagnostics(emitBag));
        }

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Regex patterns ──────────────────────────────────────────────────

    /// <summary>
    /// Matches a <c>public static TypeName __CreateDefault()</c> method declaration.
    /// </summary>
    [GeneratedRegex(@"public\s+static\s+(\w+)\s+__CreateDefault\s*\(\s*\)", RegexOptions.Multiline)]
    private static partial Regex CreateDefaultMethodPattern();

    /// <summary>
    /// Matches a constructor declaration for the given type name.
    /// Captures the type name and the parameter list.
    /// </summary>
    [GeneratedRegex(@"public\s+(\w+)\s*\(([^)]*)\)\s*[:{]", RegexOptions.Multiline)]
    private static partial Regex ConstructorPattern();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Both constructor and default block coexistence
    //
    // **Validates: Requirements 4.1, 12.4**
    //
    // For any type that has both user-defined constructors and a default {}
    // block, the compiler SHALL classify it as defaultable (Explicit), emit
    // __CreateDefault(), AND emit the constructors.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: Types with both constructors and default blocks emit BOTH
    /// <c>__CreateDefault()</c> AND the constructor in the C# output.
    ///
    /// **Validates: Requirements 4.1, 12.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ConstructorDefaultCoexistencePropertyTests) })]
    public Property BothConstructorAndDefaultBlock_EmitsBothCreateDefaultAndConstructor(
        CoexistenceScenario scenario)
    {
        var source = BuildMistSource(scenario);
        var (succeeded, csContent, diagnosticText) = CompileInMemory(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed for: {scenario}\nSource:\n{source}\nDiagnostics:\n{diagnosticText}");
        }

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty for: {scenario}");
        }

        // 1. Verify __CreateDefault() method declaration exists for the type
        var createDefaultMatches = CreateDefaultMethodPattern().Matches(csContent);
        var hasCreateDefault = createDefaultMatches
            .Cast<Match>()
            .Any(m => m.Groups[1].Value == scenario.TypeName);

        if(!hasCreateDefault)
        {
            return false.ToProperty()
                .Label($"__CreateDefault() not found for type '{scenario.TypeName}'\nC# output:\n{csContent}");
        }

        // 2. Verify a constructor with parameters exists for the type
        var ctorMatches = ConstructorPattern().Matches(csContent);
        var hasConstructor = ctorMatches
            .Cast<Match>()
            .Any(m => m.Groups[1].Value == scenario.TypeName
                      && !string.IsNullOrWhiteSpace(m.Groups[2].Value));

        if(!hasConstructor)
        {
            return false.ToProperty()
                .Label($"Constructor not found for type '{scenario.TypeName}'\nC# output:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"Both __CreateDefault and constructor emitted for: {scenario}");
    }
}