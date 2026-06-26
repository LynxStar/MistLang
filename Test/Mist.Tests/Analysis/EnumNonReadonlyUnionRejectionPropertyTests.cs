// Feature: enum-readonly-restriction, Property 5: Non-readonly union constituent rejection
// **Validates: Requirements 2.4, 3.4, 4.3, 4.4, 4.5**

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
/// containing non-readonly constituents produces the correct MIST_TC_0103 diagnostics.
/// </summary>
public sealed class EnumNonReadonlyUnionRejectionScenario
{
    public string MistSource { get; }
    public string EnumName { get; }
    public IReadOnlyList<string> NonReadonlyTypeNames { get; }
    public IReadOnlyList<string> AllConstituentTypeNames { get; }
    public string Description { get; }

    public EnumNonReadonlyUnionRejectionScenario(
        string mistSource,
        string enumName,
        IReadOnlyList<string> nonReadonlyTypeNames,
        IReadOnlyList<string> allConstituentTypeNames,
        string description)
    {
        MistSource = mistSource;
        EnumName = enumName;
        NonReadonlyTypeNames = nonReadonlyTypeNames;
        AllConstituentTypeNames = allConstituentTypeNames;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests for non-readonly union constituent rejection.
///
/// **Validates: Requirements 2.4, 3.4, 4.3, 4.4, 4.5**
///
/// For any enum declaration whose resolved underlying type is a union type
/// containing at least one non-readonly constituent, the type checker SHALL emit
/// exactly one MIST_TC_0103 diagnostic per non-readonly constituent, each message
/// containing the enum's display name and the specific non-readonly constituent's
/// display name, with severity Error.
/// </summary>
public sealed class EnumNonReadonlyUnionRejectionPropertyTests
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

    public static Arbitrary<EnumNonReadonlyUnionRejectionScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static Gen<EnumNonReadonlyUnionRejectionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenOneNonReadonlyOfTwoScenario(),
            GenAllNonReadonlyOfTwoScenario(),
            GenOneNonReadonlyOfThreeScenario(),
            GenTwoNonReadonlyOfThreeScenario());
    }

    /// <summary>
    /// Generates a scenario with a union of two structs where one is non-readonly.
    /// </summary>
    private static Gen<EnumNonReadonlyUnionRejectionScenario> GenOneNonReadonlyOfTwoScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from readonlyName in Gen.Elements(StructNamePool)
               from nonReadonlyName in Gen.Elements(ClassNamePool)
               from memberCount in Gen.Choose(2, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let allTypes = new[] { readonlyName, nonReadonlyName }
               let nonReadonlyTypes = new[] { nonReadonlyName }
               let source = BuildMixedUnionSource(
                   enumName, new[] { readonlyName }, new[] { nonReadonlyName }, "struct", members)
               select new EnumNonReadonlyUnionRejectionScenario(
                   source,
                   enumName,
                   nonReadonlyTypes,
                   allTypes,
                   $"enum {enumName} : {readonlyName} | {nonReadonlyName} " +
                   $"(1 non-readonly of 2, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a union of two structs where both are non-readonly.
    /// </summary>
    private static Gen<EnumNonReadonlyUnionRejectionScenario> GenAllNonReadonlyOfTwoScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from name1 in Gen.Elements(StructNamePool)
               from name2 in Gen.Elements(ClassNamePool)
               from memberCount in Gen.Choose(2, 4)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let allTypes = new[] { name1, name2 }
               let nonReadonlyTypes = new[] { name1, name2 }
               let source = BuildAllNonReadonlyUnionSource(
                   enumName, name1, "struct", name2, "class", members)
               select new EnumNonReadonlyUnionRejectionScenario(
                   source,
                   enumName,
                   nonReadonlyTypes,
                   allTypes,
                   $"enum {enumName} : {name1} | {name2} " +
                   $"(2 non-readonly of 2, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a union of three structs where one is non-readonly.
    /// </summary>
    private static Gen<EnumNonReadonlyUnionRejectionScenario> GenOneNonReadonlyOfThreeScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from shuffledStructs in Gen.Shuffle(StructNamePool)
               let readonlyNames = shuffledStructs.Take(2).ToArray()
               from nonReadonlyName in Gen.Elements(ClassNamePool)
               from memberCount in Gen.Choose(3, 5)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let allTypes = readonlyNames.Append(nonReadonlyName).ToArray()
               let nonReadonlyTypes = new[] { nonReadonlyName }
               let source = BuildMixedUnionSource(
                   enumName, readonlyNames, new[] { nonReadonlyName }, "struct", members)
               select new EnumNonReadonlyUnionRejectionScenario(
                   source,
                   enumName,
                   nonReadonlyTypes,
                   allTypes,
                   $"enum {enumName} : {string.Join(" | ", allTypes)} " +
                   $"(1 non-readonly of 3, {memberCount} members)");
    }

    /// <summary>
    /// Generates a scenario with a union of three types where two are non-readonly.
    /// </summary>
    private static Gen<EnumNonReadonlyUnionRejectionScenario> GenTwoNonReadonlyOfThreeScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from readonlyName in Gen.Elements(StructNamePool)
               from nonReadonly1 in Gen.Elements(ClassNamePool)
               from nonReadonly2 in Gen.Elements(StructNamePool).Where(s => s != readonlyName)
               from memberCount in Gen.Choose(3, 5)
               from members in Gen.Shuffle(MemberPools)
                   .Select(m => m.Take(memberCount).ToArray())
               let allTypes = new[] { readonlyName, nonReadonly1, nonReadonly2 }
               let nonReadonlyTypes = new[] { nonReadonly1, nonReadonly2 }
               let source = BuildTwoNonReadonlyOfThreeSource(
                   enumName, readonlyName, nonReadonly1, nonReadonly2, members)
               select new EnumNonReadonlyUnionRejectionScenario(
                   source,
                   enumName,
                   nonReadonlyTypes,
                   allTypes,
                   $"enum {enumName} : {readonlyName} | {nonReadonly1} | {nonReadonly2} " +
                   $"(2 non-readonly of 3, {memberCount} members)");
    }

    // ─── Source construction helpers ─────────────────────────────────────

    /// <summary>
    /// Builds source with a mix of readonly and non-readonly types in a union.
    /// Readonly types are declared with the readonly modifier; non-readonly types are not.
    /// </summary>
    private static string BuildMixedUnionSource(
        string enumName,
        string[] readonlyNames,
        string[] nonReadonlyNames,
        string typeKind,
        string[] memberNames)
    {
        // Declare readonly types with the readonly modifier
        var readonlyDecls = string.Join("\n\n",
            readonlyNames.Select(t => $$"""
            public readonly {{typeKind}} {{t}} {
                public int value;

                public {{t}}(int value)
                {
                    this.value = value;
                }
            }
            """));

        // Declare non-readonly types WITHOUT the readonly modifier
        var nonReadonlyDecls = string.Join("\n\n",
            nonReadonlyNames.Select(t => $$"""
            public class {{t}} {
                public int value;

                public {{t}}(int value)
                {
                    this.value = value;
                }
            }
            """));

        // Build union type annotation with all types
        var allTypes = readonlyNames.Concat(nonReadonlyNames).ToArray();
        var unionType = string.Join(" | ", allTypes);

        // Each member uses a round-robin assignment to union constituents
        var members = string.Join(",\n    ",
            memberNames.Select((m, i) =>
            {
                var typeName = allTypes[i % allTypes.Length];
                return $"{m} = new {typeName}({i})";
            }));

        return $$"""
            {{readonlyDecls}}

            {{nonReadonlyDecls}}

            public enum {{enumName}} : {{unionType}} {
                {{members}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;
    }

    /// <summary>
    /// Builds source with two non-readonly types in a union (no readonly types).
    /// </summary>
    private static string BuildAllNonReadonlyUnionSource(
        string enumName,
        string name1, string kind1,
        string name2, string kind2,
        string[] memberNames)
    {
        var members = string.Join(",\n    ",
            memberNames.Select((m, i) =>
            {
                var typeName = i % 2 == 0 ? name1 : name2;
                return $"{m} = new {typeName}({i})";
            }));

        return $$"""
            public {{kind1}} {{name1}} {
                public int value;

                public {{name1}}(int value)
                {
                    this.value = value;
                }
            }

            public {{kind2}} {{name2}} {
                public int value;

                public {{name2}}(int value)
                {
                    this.value = value;
                }
            }

            public enum {{enumName}} : {{name1}} | {{name2}} {
                {{members}}
            }

            public class Program {
                public static void Main() {
                }
            }
            """;
    }

    /// <summary>
    /// Builds source with one readonly struct and two non-readonly types in a union.
    /// </summary>
    private static string BuildTwoNonReadonlyOfThreeSource(
        string enumName,
        string readonlyName,
        string nonReadonly1,
        string nonReadonly2,
        string[] memberNames)
    {
        var allTypes = new[] { readonlyName, nonReadonly1, nonReadonly2 };
        var members = string.Join(",\n    ",
            memberNames.Select((m, i) =>
            {
                var typeName = allTypes[i % allTypes.Length];
                return $"{m} = new {typeName}({i})";
            }));

        return $$"""
            public readonly struct {{readonlyName}} {
                public int value;

                public {{readonlyName}}(int value)
                {
                    this.value = value;
                }
            }

            public class {{nonReadonly1}} {
                public int value;

                public {{nonReadonly1}}(int value)
                {
                    this.value = value;
                }
            }

            public struct {{nonReadonly2}} {
                public int value;

                public {{nonReadonly2}}(int value)
                {
                    this.value = value;
                }
            }

            public enum {{enumName}} : {{readonlyName}} | {{nonReadonly1}} | {{nonReadonly2}} {
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
    // Property 5: Non-readonly union constituent rejection
    //
    // **Validates: Requirements 2.4, 3.4, 4.3, 4.4, 4.5**
    //
    // For any enum declaration whose resolved underlying type is a union type
    // containing at least one non-readonly constituent, the type checker SHALL
    // emit exactly one MIST_TC_0103 diagnostic per non-readonly constituent,
    // each message containing the enum's display name and the specific
    // non-readonly constituent's display name, with severity Error.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(EnumNonReadonlyUnionRejectionPropertyTests) })]
    public Property NonReadonlyUnionConstituent_ProducesDiagnosticPerConstituent(
        EnumNonReadonlyUnionRejectionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.MistSource);

        // Filter to MIST_TC_0103 diagnostics only.
        var unionDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0103")
            .ToList();

        // Must have exactly one MIST_TC_0103 diagnostic per non-readonly constituent.
        var expectedCount = scenario.NonReadonlyTypeNames.Count;
        if(unionDiags.Count != expectedCount)
        {
            return false.ToProperty()
                .Label($"Expected {expectedCount} MIST_TC_0103 diagnostic(s), got {unionDiags.Count}.\n" +
                       $"Scenario: {scenario.Description}\n" +
                       $"Non-readonly types: [{string.Join(", ", scenario.NonReadonlyTypeNames)}]\n" +
                       $"All diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{scenario.MistSource}");
        }

        // Verify each diagnostic has severity Error.
        var nonErrorDiags = unionDiags.Where(d => d.Severity != DiagnosticSeverity.Error).ToList();
        if(nonErrorDiags.Count > 0)
        {
            return false.ToProperty()
                .Label($"Expected all diagnostics to have severity Error, but found: " +
                       $"[{string.Join(", ", nonErrorDiags.Select(d => d.Severity.ToString()))}].\n" +
                       $"Scenario: {scenario.Description}");
        }

        // Verify each diagnostic message contains the enum name.
        var missingEnumName = unionDiags
            .Where(d => !d.Message.Contains(scenario.EnumName))
            .ToList();
        if(missingEnumName.Count > 0)
        {
            return false.ToProperty()
                .Label($"Some diagnostics do not contain enum name '{scenario.EnumName}'.\n" +
                       $"Messages: [{string.Join("; ", missingEnumName.Select(d => d.Message))}]\n" +
                       $"Scenario: {scenario.Description}");
        }

        // Verify each non-readonly type name appears in at least one diagnostic message.
        var missingTypeNames = scenario.NonReadonlyTypeNames
            .Where(typeName => !unionDiags.Any(d => d.Message.Contains(typeName)))
            .ToList();
        if(missingTypeNames.Count > 0)
        {
            return false.ToProperty()
                .Label($"Diagnostics do not mention non-readonly type(s): " +
                       $"[{string.Join(", ", missingTypeNames)}].\n" +
                       $"Diagnostic messages: [{string.Join("; ", unionDiags.Select(d => d.Message))}]\n" +
                       $"Scenario: {scenario.Description}");
        }

        return true.ToProperty()
            .Label($"Enum '{scenario.EnumName}' with non-readonly union constituents " +
                   $"[{string.Join(", ", scenario.NonReadonlyTypeNames)}] correctly rejected " +
                   $"with {expectedCount} MIST_TC_0103 diagnostic(s)");
    }
}