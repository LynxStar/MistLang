// Feature: structured-concurrency, Property 7: Task Storage Restriction
// **Validates: Requirements 7.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that <c>Task&lt;T&gt;</c> stored in fields, properties,
/// or collections (array element type) produces diagnostic <c>MIST_TC_0110</c>.
///
/// **Validates: Requirements 7.5**
///
/// For any program that declares a field, property, or collection with element type
/// <c>Task&lt;T&gt;</c>, the type checker SHALL emit diagnostic <c>MIST_TC_0110</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class TaskStorageRestrictionPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileWithStdlibAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The kind of storage location where Task&lt;T&gt; is placed.
    /// </summary>
    public enum TaskStorageKind
    {
        /// <summary>A field of type <c>Task&lt;T&gt;</c></summary>
        Field,

        /// <summary>A property of type <c>Task&lt;T&gt;</c></summary>
        Property,

        /// <summary>A field of type <c>Task&lt;T&gt;[]</c> (array/collection)</summary>
        ArrayField,

        /// <summary>A property of type <c>Task&lt;T&gt;[]</c> (array/collection)</summary>
        ArrayProperty,
    }

    /// <summary>
    /// A scenario describing a program with Task&lt;T&gt; stored in a restricted location.
    /// </summary>
    public record TaskStorageScenario(
        TaskStorageKind StorageKind,
        string ClassName,
        string MemberName,
        string TypeArgument)
    {
        public override string ToString() =>
            $"{StorageKind}: class {ClassName} {{ {TypeArgument} {MemberName} }}";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Foo", "Bar", "Service", "Handler", "Cache", "Store"];

    private static readonly string[] MemberNames =
        ["myTask", "pending", "work", "operation", "job", "computation"];

    private static readonly string[] TypeArguments =
        ["int", "string", "bool", "void"];

    public static Gen<TaskStorageScenario> GenScenario()
    {
        return from storageKind in Gen.Elements(
                   TaskStorageKind.Field,
                   TaskStorageKind.Property,
                   TaskStorageKind.ArrayField,
                   TaskStorageKind.ArrayProperty)
               from className in Gen.Elements(ClassNames)
               from memberName in Gen.Elements(MemberNames)
               from typeArg in Gen.Elements(TypeArguments)
               select new TaskStorageScenario(storageKind, className, memberName, typeArg);
    }

    public static Arbitrary<TaskStorageScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildSource(TaskStorageScenario scenario)
    {
        var taskType = $"Task<{scenario.TypeArgument}>";
        var member = scenario.StorageKind switch
        {
            TaskStorageKind.Field =>
                $"    {taskType} {scenario.MemberName};",
            TaskStorageKind.Property =>
                $"    {taskType} {scenario.MemberName} {{ get {{ return default; }} }}",
            TaskStorageKind.ArrayField =>
                $"    {taskType}[] {scenario.MemberName};",
            TaskStorageKind.ArrayProperty =>
                $"    {taskType}[] {scenario.MemberName} {{ get {{ return default; }} }}",
            _ => throw new ArgumentOutOfRangeException(),
        };

        return $$"""
            using Standard;
            class {{scenario.ClassName}} {
            {{member}}
            }
            class Program { public static void Main() {} }
            """;
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Task Storage Restriction
    ///
    /// For any program that declares a field, property, or collection with element type
    /// <c>Task&lt;T&gt;</c>, the type checker SHALL emit diagnostic <c>MIST_TC_0110</c>.
    ///
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TaskStorageRestrictionPropertyTests) }, MaxTest = 100)]
    public Property TaskStoredInFieldOrProperty_EmitsMIST_TC_0110(TaskStorageScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var storageRestrictionDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0110")
            .ToList();

        var hasDiagnostic = storageRestrictionDiags.Count > 0;

        if (!hasDiagnostic)
        {
            return false.ToProperty()
                .Label($"Expected MIST_TC_0110 but got none.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Verify the diagnostic message mentions the correct storage kind
        var expectedKindWord = scenario.StorageKind switch
        {
            TaskStorageKind.Field or TaskStorageKind.ArrayField => "field",
            TaskStorageKind.Property or TaskStorageKind.ArrayProperty => "property",
            _ => throw new ArgumentOutOfRangeException(),
        };

        var mentionsKind = storageRestrictionDiags.Any(d =>
            d.Message.Contains(expectedKindWord, StringComparison.OrdinalIgnoreCase));

        return mentionsKind.ToProperty()
            .Label($"Diagnostic emitted but does not mention '{expectedKindWord}'.\n" +
                   $"Scenario: {scenario}\n" +
                   $"Diagnostic messages: [{string.Join(", ", storageRestrictionDiags.Select(d => d.Message))}]");
    }
}
