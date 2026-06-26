// Feature: enum-readonly-restriction, Property 2: Readonly base type acceptance
// **Validates: Requirements 2.1, 3.1, 5.1, 5.2, 5.3, 5.4**

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
/// Describes a scenario for testing that an enum with a readonly base type
/// produces zero MIST_TC_0102 diagnostics.
/// </summary>
public sealed class EnumReadonlyAcceptanceScenario
{
    public string MistSource { get; }
    public string EnumName { get; }
    public string BaseTypeName { get; }
    public string Description { get; }

    public EnumReadonlyAcceptanceScenario(
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

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for readonly base type acceptance.
///
/// **Validates: Requirements 2.1, 3.1, 5.1, 5.2, 5.3, 5.4**
///
/// For any enum declaration whose resolved underlying type is a readonly type
/// (struct, class, abstract type), the type checker
/// SHALL emit zero MIST_TC_0102 diagnostics for that enum.
/// </summary>
public sealed class EnumReadonlyAcceptancePropertyTests
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

    private static readonly string[] AbstractNames =
        ["IShape", "IValue", "IToken", "IEntry", "INode"];

    private static readonly string[] MemberPools =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    public static Arbitrary<EnumReadonlyAcceptanceScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<EnumReadonlyAcceptanceScenario> GenScenario()
    {
        return Gen.OneOf(
            GenStructBaseScenario(),
            GenClassBaseScenario(),
            GenAbstractBaseScenario());
    }

    /// <summary>
    /// Generates a scenario with a readonly struct as the enum base type.
    /// The struct is declared WITH the readonly modifier.
    /// </summary>
    private static Gen<EnumReadonlyAcceptanceScenario> GenStructBaseScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from structName in Gen.Elements(StructNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let source = BuildStructBaseSource(enumName, structName, members)
               select new EnumReadonlyAcceptanceScenario(
                   source,
                   enumName,
                   structName,
                   $"enum {enumName} : {structName} (readonly struct, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a readonly class as the enum base type.
    /// The class is declared WITH the readonly modifier.
    /// </summary>
    private static Gen<EnumReadonlyAcceptanceScenario> GenClassBaseScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from className in Gen.Elements(ClassNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let source = BuildClassBaseSource(enumName, className, members)
               select new EnumReadonlyAcceptanceScenario(
                   source,
                   enumName,
                   className,
                   $"enum {enumName} : {className} (readonly class, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a readonly abstract type as the enum base type.
    /// A readonly struct implementing the abstract type is used for member values.
    /// </summary>
    private static Gen<EnumReadonlyAcceptanceScenario> GenAbstractBaseScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from abstractName in Gen.Elements(AbstractNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let implName = string.Concat(abstractName.AsSpan(1), "Impl")
               let source = BuildAbstractBaseSource(enumName, abstractName, implName, members)
               select new EnumReadonlyAcceptanceScenario(
                   source,
                   enumName,
                   abstractName,
                   $"enum {enumName} : {abstractName} (readonly abstract type, {memberCount} members)");
    }

    // ─── Source construction helpers ─────────────────────────────────────

    private static string BuildStructBaseSource(
        string enumName, string structName, string[] memberNames)
    {
        var members = string.Join(",\n    ",
            memberNames.Select(m => $"{m} = new {structName}(0)"));

        return $$"""
            public readonly struct {{structName}} {
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
            public readonly class {{className}} {
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

    private static string BuildAbstractBaseSource(
        string enumName, string abstractName, string implName, string[] memberNames)
    {
        var members = string.Join(",\n    ",
            memberNames.Select(m => $"{m} = new {implName}(0)"));

        return $$"""
            public readonly abstract {{abstractName}} {
                int GetValue();
            }

            public readonly struct {{implName}} : {{abstractName}} {
                public int value;

                public {{implName}}(int value)
                {
                    this.value = value;
                }

                public int GetValue()
                {
                    return this.value;
                }
            }

            public enum {{enumName}} : {{abstractName}} {
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
            return lexBag.ToImmutableArray();

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
            return parseBag.ToImmutableArray();

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
    // Property 2: Readonly base type acceptance
    //
    // **Validates: Requirements 2.1, 3.1, 5.1, 5.2, 5.3, 5.4**
    //
    // For any enum declaration whose resolved underlying type is a readonly
    // type (struct, class, abstract type), the
    // type checker SHALL emit zero MIST_TC_0102 diagnostics for that enum.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(EnumReadonlyAcceptancePropertyTests) })]
    public Property ReadonlyBaseType_ProducesZeroDiagnostics(
        EnumReadonlyAcceptanceScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        // Filter to MIST_TC_0102 diagnostics only.
        var rejectionDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0102")
            .ToList();

        // Must have zero MIST_TC_0102 diagnostics.
        if(rejectionDiags.Count != 0)
        {
            return false.ToProperty()
                .Label($"Expected zero MIST_TC_0102 diagnostics, got {rejectionDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"Diagnostics: [{string.Join("; ", rejectionDiags.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' with readonly base '{scenario.BaseTypeName}' " +
                   $"correctly accepted with zero MIST_TC_0102 diagnostics");
    }
}