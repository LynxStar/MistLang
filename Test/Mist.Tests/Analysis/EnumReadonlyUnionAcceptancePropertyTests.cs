// Feature: enum-readonly-restriction, Property 4: All-readonly union acceptance
// **Validates: Requirements 2.3, 3.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Describes a scenario for testing that an enum with a union base type
/// where all constituents are readonly produces zero MIST_TC_0103 diagnostics.
/// </summary>
public sealed class EnumReadonlyUnionAcceptanceScenario
{
    public string MistSource { get; }
    public string EnumName { get; }
    public IReadOnlyList<string> ConstituentTypeNames { get; }
    public string Description { get; }

    public EnumReadonlyUnionAcceptanceScenario(
        string mistSource,
        string enumName,
        IReadOnlyList<string> constituentTypeNames,
        string description)
    {
        MistSource = mistSource;
        EnumName = enumName;
        ConstituentTypeNames = constituentTypeNames;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests for all-readonly union acceptance.
///
/// **Validates: Requirements 2.3, 3.3**
///
/// For any enum declaration whose resolved underlying type is a union type
/// where every constituent is readonly, the type checker SHALL emit zero
/// MIST_TC_0103 diagnostics for that enum.
/// </summary>
public sealed class EnumReadonlyUnionAcceptancePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Shape", "Token", "Result", "Event", "Action"];

    private static readonly string[] StructNamePool =
        ["Point", "Vector", "Coord", "Offset", "Range"];

    private static readonly string[] ClassNamePool =
        ["Label", "Config", "Wrapper", "Container", "Holder"];

    private static readonly string[] MemberPools =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<EnumReadonlyUnionAcceptanceScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<EnumReadonlyUnionAcceptanceScenario> GenScenario()
    {
        return Gen.OneOf(
            GenTwoReadonlyStructsScenario(),
            GenThreeReadonlyStructsScenario(),
            GenMixedReadonlyTypesScenario());
    }

    /// <summary>
    /// Generates a scenario with a union of two readonly structs as the enum base type.
    /// </summary>
    private static Gen<EnumReadonlyUnionAcceptanceScenario> GenTwoReadonlyStructsScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from struct1 in Gen.Elements(StructNamePool)
               from struct2 in Gen.Elements(StructNamePool).Where(s => s != struct1)
               from memberCount in Gen.Choose(2, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let typeNames = new[] { struct1, struct2 }
               let source = BuildUnionSource(enumName, typeNames, "struct", members)
               select new EnumReadonlyUnionAcceptanceScenario(
                   source,
                   enumName,
                   typeNames,
                   $"enum {enumName} : {struct1} | {struct2} (two readonly structs, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a union of three readonly structs as the enum base type.
    /// </summary>
    private static Gen<EnumReadonlyUnionAcceptanceScenario> GenThreeReadonlyStructsScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from shuffled in Gen.Shuffle(StructNamePool)
               let picked = shuffled.Take(3).ToArray()
               from memberCount in Gen.Choose(3, 5)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let source = BuildUnionSource(enumName, picked, "struct", members)
               select new EnumReadonlyUnionAcceptanceScenario(
                   source,
                   enumName,
                   picked,
                   $"enum {enumName} : {string.Join(" | ", picked)} (three readonly structs, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a union of a readonly struct and a readonly class.
    /// </summary>
    private static Gen<EnumReadonlyUnionAcceptanceScenario> GenMixedReadonlyTypesScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from structName in Gen.Elements(StructNamePool)
               from className in Gen.Elements(ClassNamePool)
               from memberCount in Gen.Choose(2, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let typeNames = new[] { structName, className }
               let source = BuildMixedUnionSource(enumName, structName, className, members)
               select new EnumReadonlyUnionAcceptanceScenario(
                   source,
                   enumName,
                   typeNames,
                   $"enum {enumName} : {structName} | {className} (readonly struct + readonly class, {memberCount} members)");
    }

    // ─── Source construction helpers ─────────────────────────────────────

    private static string BuildUnionSource(
        string enumName, string[] typeNames, string typeKind, string[] memberNames)
    {
        // Declare all types as readonly
        var typeDecls = string.Join("\n\n",
            typeNames.Select(t => $$"""
            public readonly {{typeKind}} {{t}} {
                public int value;

                public {{t}}(int value)
                {
                    this.value = value;
                }
            }
            """));

        // Build union type annotation
        var unionType = string.Join(" | ", typeNames);

        // Each member uses a round-robin assignment to union constituents
        var members = string.Join(",\n    ",
            memberNames.Select((m, i) =>
            {
                var typeName = typeNames[i % typeNames.Length];
                return $"{m} = new {typeName}({i})";
            }));

        return $$"""
            {{typeDecls}}

            public enum {{enumName}} : {{unionType}} {
                {{members}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;
    }

    private static string BuildMixedUnionSource(
        string enumName, string structName, string className, string[] memberNames)
    {
        var source = $$"""
            public readonly struct {{structName}} {
                public int value;

                public {{structName}}(int value)
                {
                    this.value = value;
                }
            }

            public readonly class {{className}} {
                public int value;

                public {{className}}(int value)
                {
                    this.value = value;
                }
            }

            public enum {{enumName}} : {{structName}} | {{className}} {
                {{string.Join(",\n    ",
                    memberNames.Select((m, i) =>
                    {
                        var typeName = i % 2 == 0 ? structName : className;
                        return $"{m} = new {typeName}({i})";
                    }))}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;

        return source;
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
    // Property 4: All-readonly union acceptance
    //
    // **Validates: Requirements 2.3, 3.3**
    //
    // For any enum declaration whose resolved underlying type is a union type
    // where every constituent is readonly, the type checker SHALL emit zero
    // MIST_TC_0103 diagnostics for that enum.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(EnumReadonlyUnionAcceptancePropertyTests) })]
    public Property AllReadonlyUnionBaseType_ProducesZeroDiagnostics(
        EnumReadonlyUnionAcceptanceScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        // Filter to MIST_TC_0103 diagnostics only.
        var unionDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0103")
            .ToList();

        // Must have zero MIST_TC_0103 diagnostics.
        if(unionDiags.Count != 0)
        {
            return false.ToProperty()
                .Label($"Expected zero MIST_TC_0103 diagnostics, got {unionDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"Diagnostics: [{string.Join("; ", unionDiags.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' with all-readonly union base " +
                   $"'{string.Join(" | ", scenario.ConstituentTypeNames)}' correctly accepted");
    }
}