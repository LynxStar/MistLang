// Feature: newtypes, Property 5: Member access resolves on cloned set
//
// **Validates: Requirements 2.2**
//
// For any newtype whose source type has at least one accessible member,
// accessing that member on a newtype instance SHALL resolve successfully
// (no "member not found" error).
//
// Uses the batched pattern: generate all N scenarios upfront, build a single
// Mist program containing all scenarios, compile once via the shared
// CompilerDriver (in-process), and check diagnostics per-scenario.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that member access on newtype instances
/// resolves against the cloned member set without producing diagnostics.
///
/// Property 5: For any newtype whose source type has at least one accessible
/// member, accessing that member on a newtype instance SHALL resolve
/// successfully (no "member not found" error).
///
/// **Validates: Requirements 2.2**
/// </summary>
public sealed class NewtypeMemberAccessPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles Mist source in-process and returns all diagnostics
    /// (parse + semantic model + type checker).
    /// </summary>
    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());
        }

        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        return lexDiag.ToImmutableArray()
            .AddRange(parseDiag.ToImmutableArray())
            .AddRange(buildDiag.ToImmutableArray())
            .AddRange(tcDiag.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// Represents a batched scenario: a complete Mist program containing
    /// multiple source types with newtypes, where each newtype instance
    /// has its members accessed.
    /// </summary>
    public sealed record MemberAccessBatchScenario(
        string Source,
        int ScenarioCount,
        string Description)
    {
        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] FieldNames =
        ["x", "y", "z", "w", "count", "value"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    private static readonly string[] FieldTypes = ["int", "string", "bool"];

    private static readonly string[] MethodReturnTypes = ["int", "string", "bool"];

    /// <summary>
    /// Generates a batched scenario with N source types (classes and structs),
    /// each with accessible members, and corresponding newtypes that access
    /// those members.
    /// </summary>
    public static Arbitrary<MemberAccessBatchScenario> ArbitraryMemberAccessBatchScenario()
    {
        var gen =
            from scenarioCount in Gen.Choose(3, 8)
            from scenarios in Gen.ArrayOf(GenSingleScenario(), scenarioCount)
            let indexedScenarios = scenarios.Select((s, i) => (s, i)).ToList()
            let typeDecls = string.Join("\n\n", indexedScenarios.Select(x =>
                GenerateSourceTypeDecl(x.i, x.s)))
            let newtypeDecls = string.Join("\n", indexedScenarios.Select(x =>
                GenerateNewtypeDecl(x.i, x.s)))
            let memberAccesses = string.Join("\n", indexedScenarios.Select(x =>
                GenerateMemberAccess(x.i, x.s)))
            let source = $@"namespace TestNs;

{typeDecls}

{newtypeDecls}

class Program {{
    public static void Main() {{
{memberAccesses}
    }}
}}"
            select new MemberAccessBatchScenario(
                source,
                scenarioCount,
                $"Batch of {scenarioCount} member access scenarios");

        return gen.ToArbitrary();
    }

    // ─── Single scenario components ──────────────────────────────────────

    private enum SourceKind { Class, Struct }

    private sealed record SingleScenario(
        SourceKind Kind,
        int FieldCount,
        int MethodCount,
        int[] FieldTypeIndices,
        int[] FieldNameIndices,
        int[] MethodReturnTypeIndices,
        int[] MethodNameIndices);

    private static Gen<SingleScenario> GenSingleScenario()
    {
        return from kind in Gen.Elements(SourceKind.Class, SourceKind.Struct)
               from fieldCount in Gen.Choose(1, 3)
               from methodCount in Gen.Choose(1, 3)
               from fieldTypeIdxs in Gen.ArrayOf(Gen.Choose(0, FieldTypes.Length - 1), fieldCount)
               from fieldNameIdxs in Gen.ArrayOf(
                   Gen.Choose(0, FieldNames.Length - 1), fieldCount)
               from methodRetTypeIdxs in Gen.ArrayOf(
                   Gen.Choose(0, MethodReturnTypes.Length - 1), methodCount)
               from methodNameIdxs in Gen.ArrayOf(
                   Gen.Choose(0, MethodNames.Length - 1), methodCount)
               select new SingleScenario(
                   kind,
                   fieldCount,
                   methodCount,
                   fieldTypeIdxs,
                   fieldNameIdxs,
                   methodRetTypeIdxs,
                   methodNameIdxs);
    }

    // ─── Code generation helpers ─────────────────────────────────────────

    private static string GenerateSourceTypeDecl(int index, SingleScenario scenario)
    {
        var typeName = $"Source{index}";
        var keyword = scenario.Kind == SourceKind.Class ? "class" : "struct";

        var fields = new List<string>();
        for(var i = 0; i < scenario.FieldCount; i++)
        {
            var fieldType = FieldTypes[scenario.FieldTypeIndices[i]];
            var fieldName = $"{FieldNames[scenario.FieldNameIndices[i]]}{index}_{i}";
            fields.Add($"    public {fieldType} {fieldName};");
        }

        var methods = new List<string>();
        for(var i = 0; i < scenario.MethodCount; i++)
        {
            var retType = MethodReturnTypes[scenario.MethodReturnTypeIndices[i]];
            var methodName = $"{MethodNames[scenario.MethodNameIndices[i]]}{index}_{i}";
            var returnValue = retType switch
            {
                "int" => "0",
                "string" => "\"hello\"",
                "bool" => "true",
                _ => "0",
            };
            methods.Add($"    public {retType} {methodName}() {{ return {returnValue}; }}");
        }

        // Add a constructor for struct types so we can instantiate them
        var ctorParams = new List<string>();
        var ctorBody = new List<string>();
        for(var i = 0; i < scenario.FieldCount; i++)
        {
            var fieldType = FieldTypes[scenario.FieldTypeIndices[i]];
            var fieldName = $"{FieldNames[scenario.FieldNameIndices[i]]}{index}_{i}";
            ctorParams.Add($"{fieldType} p{i}");
            ctorBody.Add($"        this.{fieldName} = p{i};");
        }

        var ctor = $"    public {typeName}({string.Join(", ", ctorParams)})\n    {{\n{string.Join("\n", ctorBody)}\n    }}";

        var allMembers = string.Join("\n", fields.Concat(methods).Append(ctor));
        return $"public {keyword} {typeName}\n{{\n{allMembers}\n}}";
    }

    private static string GenerateNewtypeDecl(int index, SingleScenario scenario)
    {
        _ = scenario; // Used for consistency; the newtype name is derived from index.
        return $"type Newtype{index} = new Source{index};";
    }

    private static string GenerateMemberAccess(int index, SingleScenario scenario)
    {
        var lines = new List<string>();
        var newtypeName = $"Newtype{index}";

        // Create an instance of the newtype via constructor
        var ctorArgs = new List<string>();
        for(var i = 0; i < scenario.FieldCount; i++)
        {
            var fieldType = FieldTypes[scenario.FieldTypeIndices[i]];
            var defaultVal = fieldType switch
            {
                "int" => "0",
                "string" => "\"test\"",
                "bool" => "false",
                _ => "0",
            };
            ctorArgs.Add(defaultVal);
        }

        lines.Add($"        var inst{index} = new {newtypeName}({string.Join(", ", ctorArgs)});");

        // Access each field
        for(var i = 0; i < scenario.FieldCount; i++)
        {
            var fieldType = FieldTypes[scenario.FieldTypeIndices[i]];
            var fieldName = $"{FieldNames[scenario.FieldNameIndices[i]]}{index}_{i}";
            lines.Add($"        {fieldType} f{index}_{i} = inst{index}.{fieldName};");
        }

        // Access each method
        for(var i = 0; i < scenario.MethodCount; i++)
        {
            var retType = MethodReturnTypes[scenario.MethodReturnTypeIndices[i]];
            var methodName = $"{MethodNames[scenario.MethodNameIndices[i]]}{index}_{i}";
            lines.Add($"        {retType} m{index}_{i} = inst{index}.{methodName}();");
        }

        return string.Join("\n", lines);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Member access resolves on cloned set
    //
    // **Validates: Requirements 2.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any newtype whose source type has at least one accessible member,
    /// accessing that member on a newtype instance SHALL resolve successfully
    /// (no "member not found" error).
    ///
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeMemberAccessPropertyTests) })]
    public Property MemberAccess_OnNewtypeInstance_ResolvesSuccessfully(
        MemberAccessBatchScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);

        // Filter for member-resolution-related diagnostics
        var memberErrors = diagnostics
            .Where(d => d.Id.Value is "MIST_UNDEFINED_MEMBER" or "MIST_TC_0073"
                or "MIST_TC_0062" or "MIST_TC_0065")
            .ToList();

        if(memberErrors.Count > 0)
        {
            var errorMessages = string.Join("\n",
                memberErrors.Select(d => $"  [{d.Id.Value}] {d.Message}"));
            return false.ToProperty()
                .Label($"Member access produced {memberErrors.Count} diagnostic(s):\n{errorMessages}")
                .Label($"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"All {scenario.ScenarioCount} member accesses resolved successfully");
    }
}