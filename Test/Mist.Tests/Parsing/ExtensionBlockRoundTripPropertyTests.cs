// Feature: extension-methods, Property 1: Extension Block Round-Trip
//
// **Validates: Requirements 2.7, 4.4, 5.6, 6.7**
//
// For any valid extension block AST (including generic extensions, mixed
// static/instance members, various receiver types, and multiple blocks in one
// file), pretty-printing the AST and then parsing the resulting source text
// SHALL produce an AST that, when pretty-printed again, yields identical
// source text.
//
// All work runs in-process — no `dotnet` process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for extension block round-trip.
///
/// **Validates: Requirements 2.7, 4.4, 5.6, 6.7**
///
/// For any valid extension block AST (generic, non-generic, mixed static/instance,
/// various receiver types, multiple blocks), pretty-printing → parsing →
/// pretty-printing SHALL produce identical output.
/// </summary>
public class ExtensionBlockRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034
    public record ExtensionBlockScenario(
        string Description,
        string Source)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Building blocks for source generation ───────────────────────────

    private static readonly string[] ReceiverTypes =
    [
        "string",
        "int",
        "bool",
        "double",
        "unknown",
    ];

    private static readonly string[] NamedReceiverTypes =
    [
        "MyClass",
        "Widget",
        "Container",
    ];

    private static readonly string[] ReceiverNames =
    [
        "self",
        "value",
        "item",
    ];

    private static readonly string[] MethodNames =
    [
        "DoWork",
        "Transform",
        "Compute",
        "GetValue",
    ];

    private static readonly string[] PropertyNames =
    [
        "Length",
        "Count",
        "IsEmpty",
        "Name",
    ];

    private static readonly string[] ReturnTypes =
    [
        "int",
        "string",
        "bool",
        "void",
        "double",
    ];

    private static readonly string[] ParamTypes =
    [
        "int",
        "string",
        "bool",
        "double",
    ];

    // ─── Member generators ───────────────────────────────────────────────

    private static Gen<string> GenInstanceMethod()
    {
        return from retType in Gen.Elements(ReturnTypes)
               from name in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(ParamTypes)
               from paramName in Gen.Elements("x", "y", "arg")
               let paramList = hasParam ? $"{paramType} {paramName}" : ""
               let body = retType == "void"
                   ? "{ }"
                   : retType == "bool"
                       ? "{ return true; }"
                       : retType == "string"
                           ? "{ return \"hello\"; }"
                           : "{ return 0; }"
               select $"    {retType} {name}({paramList}) {body}";
    }

    private static Gen<string> GenStaticMethod()
    {
        return from retType in Gen.Elements(ReturnTypes)
               from name in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(ParamTypes)
               from paramName in Gen.Elements("x", "y", "arg")
               let paramList = hasParam ? $"{paramType} {paramName}" : ""
               let body = retType == "void"
                   ? "{ }"
                   : retType == "bool"
                       ? "{ return true; }"
                       : retType == "string"
                           ? "{ return \"hello\"; }"
                           : "{ return 0; }"
               select $"    static {retType} {name}({paramList}) {body}";
    }

    private static Gen<string> GenExpressionProperty()
    {
        return from propType in Gen.Elements("int", "string", "bool", "double")
               from name in Gen.Elements(PropertyNames)
               let expr = propType == "bool"
                   ? "true"
                   : propType == "string"
                       ? "\"value\""
                       : "0"
               select $"    {propType} {name} => {expr};";
    }

    private static Gen<string> GenBlockProperty()
    {
        return from propType in Gen.Elements("int", "string", "bool")
               from name in Gen.Elements(PropertyNames)
               let expr = propType == "bool"
                   ? "true"
                   : propType == "string"
                       ? "\"value\""
                       : "0"
               select $"    {propType} {name} {{ get => {expr}; }}";
    }

    private static Gen<string> GenMember()
    {
        return Gen.OneOf(
            GenInstanceMethod(),
            GenStaticMethod(),
            GenExpressionProperty(),
            GenBlockProperty());
    }

    // ─── Extension block generators ──────────────────────────────────────

    /// <summary>Non-generic extension block with primitive/named receiver.</summary>
    private static Gen<ExtensionBlockScenario> GenNonGenericExtensionBlock()
    {
        return from receiverType in Gen.OneOf(
                   Gen.Elements(ReceiverTypes),
                   Gen.Elements(NamedReceiverTypes))
               from receiverName in Gen.Elements(ReceiverNames)
               from memberCount in Gen.Choose(1, 3)
               from members in Gen.ArrayOf(GenMember(), memberCount)
               let memberBlock = string.Join("\n\n", members)
               let source = $"extension({receiverType} {receiverName}) {{\n{memberBlock}\n}}\n"
               select new ExtensionBlockScenario(
                   $"extension({receiverType} {receiverName}) with {memberCount} members",
                   source);
    }

    /// <summary>Generic extension block with generic receiver type.</summary>
    private static Gen<ExtensionBlockScenario> GenGenericExtensionBlock()
    {
        // Pick a generic receiver pattern and matching type params
        var genSingleTypeParam = from receiver in Gen.Elements("List<T>", "Optional<T>")
                                 from receiverName in Gen.Elements(ReceiverNames)
                                 from memberCount in Gen.Choose(1, 3)
                                 from members in Gen.ArrayOf(GenMember(), memberCount)
                                 let memberBlock = string.Join("\n\n", members)
                                 let source = $"<T> extension({receiver} {receiverName}) {{\n{memberBlock}\n}}\n"
                                 select new ExtensionBlockScenario(
                                     $"<T> extension({receiver} {receiverName}) with {memberCount} members",
                                     source);

        var genTwoTypeParams = from receiverName in Gen.Elements(ReceiverNames)
                               from memberCount in Gen.Choose(1, 2)
                               from members in Gen.ArrayOf(GenMember(), memberCount)
                               let memberBlock = string.Join("\n\n", members)
                               let source = $"<T, U> extension(Map<T, U> {receiverName}) {{\n{memberBlock}\n}}\n"
                               select new ExtensionBlockScenario(
                                   $"<T, U> extension(Map<T, U> {receiverName}) with {memberCount} members",
                                   source);

        return Gen.OneOf(genSingleTypeParam, genTwoTypeParams);
    }

    /// <summary>Extension block with mixed static and instance members.</summary>
    private static Gen<ExtensionBlockScenario> GenMixedStaticInstanceBlock()
    {
        return from receiverType in Gen.Elements(ReceiverTypes)
               from receiverName in Gen.Elements(ReceiverNames)
               from instanceMember in GenInstanceMethod()
               from staticMember in GenStaticMethod()
               from property in GenExpressionProperty()
               let memberBlock = string.Join("\n\n", instanceMember, staticMember, property)
               let source = $"extension({receiverType} {receiverName}) {{\n{memberBlock}\n}}\n"
               select new ExtensionBlockScenario(
                   $"mixed static/instance extension({receiverType} {receiverName})",
                   source);
    }

    /// <summary>Multiple extension blocks in one file.</summary>
    private static Gen<ExtensionBlockScenario> GenMultipleExtensionBlocks()
    {
        return from type1 in Gen.Elements(ReceiverTypes)
               from type2 in Gen.Elements(NamedReceiverTypes)
               from name1 in Gen.Elements(ReceiverNames)
               from name2 in Gen.Elements(ReceiverNames)
               from member1 in GenInstanceMethod()
               from member2 in GenInstanceMethod()
               from member3 in GenExpressionProperty()
               let block1 = $"extension({type1} {name1}) {{\n{member1}\n}}\n"
               let block2 = $"extension({type2} {name2}) {{\n{member2}\n\n{member3}\n}}\n"
               let source = $"{block1}\n{block2}"
               select new ExtensionBlockScenario(
                   $"multiple blocks: extension({type1} ...) + extension({type2} ...)",
                   source);
    }

    /// <summary>Extension block with array receiver type.</summary>
    private static Gen<ExtensionBlockScenario> GenArrayReceiverBlock()
    {
        return from elemType in Gen.Elements("int", "string", "bool")
               from receiverName in Gen.Elements(ReceiverNames)
               from member in GenInstanceMethod()
               let source = $"extension({elemType}[] {receiverName}) {{\n{member}\n}}\n"
               select new ExtensionBlockScenario(
                   $"extension({elemType}[] {receiverName})",
                   source);
    }

    // ─── Combined scenario generator ─────────────────────────────────────

    private static Gen<ExtensionBlockScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNonGenericExtensionBlock(),
            GenGenericExtensionBlock(),
            GenMixedStaticInstanceBlock(),
            GenMultipleExtensionBlocks(),
            GenArrayReceiverBlock());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ExtensionBlockScenario> ArbitraryExtensionBlockScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Extension Block Round-Trip
    //
    // **Validates: Requirements 2.7, 4.4, 5.6, 6.7**
    //
    // For any valid extension block AST (generic, non-generic, mixed
    // static/instance, various receiver types, multiple blocks),
    // pretty-printing → parsing → pretty-printing SHALL produce identical
    // output.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ExtensionBlockRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesExtensionBlocks(ExtensionBlockScenario scenario)
    {
        var source = scenario.Source;

        // Parse₁
        var sourceText1 = new Mist.Common.Text.SourceText("test.mist", source);
        var bag1 = new DiagnosticBag();
        var tokens1 = Lexer.Lex(sourceText1, bag1);
        var result1 = Parser.Parse(tokens1, bag1);

        var errors1 = bag1.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors1.Count > 0)
            return false.ToProperty()
                .Label($"First-parse errors for {scenario}:\n  {string.Join("\n  ", errors1.Select(e => e.Message))}\nSource:\n{source}");

        if(result1.Root is not CompilationUnitNode ast1)
            return false.ToProperty().Label($"First-parse root was null for {scenario}\nSource:\n{source}");

        // Print₁
        var printed1 = MistPrettyPrinter.Print(ast1, source);
        if(string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label($"printed1 was empty for {scenario}");

        // Sanity: the printed output must contain the `extension` keyword
        if(!printed1.Contains("extension"))
            return false.ToProperty()
                .Label($"printed1 lost the `extension` keyword for {scenario}\nSource:\n{source}\nPrinted:\n{printed1}");

        // Parse₂
        var sourceText2 = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag2 = new DiagnosticBag();
        var tokens2 = Lexer.Lex(sourceText2, bag2);
        var result2 = Parser.Parse(tokens2, bag2);

        var errors2 = bag2.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors2.Count > 0)
            return false.ToProperty()
                .Label($"Reparse errors for {scenario}:\n  {string.Join("\n  ", errors2.Select(e => e.Message))}\nPrinted:\n{printed1}");

        if(result2.Root is not CompilationUnitNode ast2)
            return false.ToProperty().Label($"Reparse root was null for {scenario}\nPrinted:\n{printed1}");

        // Print₂
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Assert: Print₁ == Print₂
        return (printed1 == printed2).ToProperty()
            .Label($"Mismatch for {scenario}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}