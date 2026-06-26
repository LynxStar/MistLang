// Feature: enum-readonly-restriction, Property 3: Non-readonly base type rejection
// **Validates: Requirements 2.2, 3.2, 4.1, 4.2, 4.4, 4.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario for testing that an enum with a non-readonly base type
/// produces exactly one MIST_TC_0102 diagnostic.
/// </summary>
public sealed class EnumNonReadonlyRejectionScenario
{
    public string MistSource { get; }
    public string EnumName { get; }
    public string BaseTypeName { get; }
    public string Description { get; }

    public EnumNonReadonlyRejectionScenario(
        string mistSource,
        string enumName,
        string baseTypeName,
        string description)
    {
        MistSource = mistSource;
        EnumName = enumName;
        BaseTypeName = baseTypeName;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests for non-readonly base type rejection.
///
/// **Validates: Requirements 2.2, 3.2, 4.1, 4.2, 4.4, 4.5**
///
/// For any enum declaration whose resolved underlying type is not a readonly type
/// and is not a union type, the type checker SHALL emit exactly one MIST_TC_0102
/// diagnostic whose message contains both the enum's display name and the
/// non-readonly type's display name, with severity Error.
/// </summary>
public sealed class EnumNonReadonlyRejectionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Status", "Priority", "Level", "Mode", "Signal"];

    private static readonly string[] StructNames =
        ["Point", "Vector", "Coord", "Offset", "Range"];

    private static readonly string[] ClassNames =
        ["Label", "Config", "Wrapper", "Container", "Holder"];

    private static readonly string[] MemberPools =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<EnumNonReadonlyRejectionScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<EnumNonReadonlyRejectionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenStructBaseScenario(),
            GenClassBaseScenario());
    }

    /// <summary>
    /// Generates a scenario with a non-readonly struct as the enum base type.
    /// The struct is declared WITHOUT the readonly modifier.
    /// </summary>
    private static Gen<EnumNonReadonlyRejectionScenario> GenStructBaseScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from structName in Gen.Elements(StructNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let source = BuildStructBaseSource(enumName, structName, members)
               select new EnumNonReadonlyRejectionScenario(
                   source,
                   enumName,
                   structName,
                   $"enum {enumName} : {structName} (non-readonly struct, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a non-readonly class as the enum base type.
    /// The class is declared WITHOUT the readonly modifier.
    /// </summary>
    private static Gen<EnumNonReadonlyRejectionScenario> GenClassBaseScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from className in Gen.Elements(ClassNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let source = BuildClassBaseSource(enumName, className, members)
               select new EnumNonReadonlyRejectionScenario(
                   source,
                   enumName,
                   className,
                   $"enum {enumName} : {className} (non-readonly class, {memberCount} members)");
    }

    // ─── Source construction helpers ─────────────────────────────────────

    private static string BuildStructBaseSource(
        string enumName, string structName, string[] memberNames)
    {
        var members = string.Join(",\n    ",
            memberNames.Select(m => $"{m} = new {structName}(0)"));

        return $$"""
            public struct {{structName}} {
                public int value;

                public {{structName}}(int value)
                {
                    this.value = value;
                }
            }

            public enum {{enumName}} : {{structName}} {
                {{members}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;
    }

    private static string BuildClassBaseSource(
        string enumName, string className, string[] memberNames)
    {
        var members = string.Join(",\n    ",
            memberNames.Select(m => $"{m} = new {className}(0)"));

        return $$"""
            public class {{className}} {
                public int value;

                public {{className}}(int value)
                {
                    this.value = value;
                }
            }

            public enum {{enumName}} : {{className}} {
                {{members}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return lexBag.ToImmutableArray();
        }

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
        {
            return parseBag.ToImmutableArray();
        }

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Non-readonly base type rejection
    //
    // **Validates: Requirements 2.2, 3.2, 4.1, 4.2, 4.4, 4.5**
    //
    // For any enum declaration whose resolved underlying type is not a readonly
    // type and is not a union type, the type checker SHALL emit exactly one
    // MIST_TC_0102 diagnostic whose message contains both the enum's display
    // name and the non-readonly type's display name, with severity Error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(EnumNonReadonlyRejectionPropertyTests) })]
    public Property NonReadonlyBaseType_ProducesExactlyOneDiagnostic(
        EnumNonReadonlyRejectionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        // Filter to MIST_TC_0102 diagnostics only.
        var rejectionDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0102")
            .ToList();

        // Must have exactly one MIST_TC_0102 diagnostic.
        if(rejectionDiags.Count != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 MIST_TC_0102 diagnostic, got {rejectionDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        var diag = rejectionDiags[0];

        // Verify severity is Error.
        if(diag.Severity != DiagnosticSeverity.Error)
        {
            return false.ToProperty()
                .Label($"Expected severity Error, got {diag.Severity}.\n" +
                       $"Scenario: {scenario.Description}");
        }

        // Verify message contains the enum name.
        if(!diag.Message.Contains(scenario.EnumName))
        {
            return false.ToProperty()
                .Label($"Diagnostic message does not contain enum name '{scenario.EnumName}'.\n" +
                       $"Message: {diag.Message}\n" +
                       $"Scenario: {scenario.Description}");
        }

        // Verify message contains the non-readonly base type name.
        if(!diag.Message.Contains(scenario.BaseTypeName))
        {
            return false.ToProperty()
                .Label($"Diagnostic message does not contain base type name '{scenario.BaseTypeName}'.\n" +
                       $"Message: {diag.Message}\n" +
                       $"Scenario: {scenario.Description}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' with non-readonly base '{scenario.BaseTypeName}' " +
                   $"correctly rejected with MIST_TC_0102");
    }
}