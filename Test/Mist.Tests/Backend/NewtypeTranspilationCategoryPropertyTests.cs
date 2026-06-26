// Feature: newtypes, Property 9: Transpilation preserves type category
//
// **Validates: Requirements 5.1, 5.2**
//
// For any newtype whose source is a class, the transpiled C# output SHALL be
// a class declaration; for any newtype whose source is a struct, the transpiled
// C# output SHALL be a struct declaration.
//
// Uses in-process compilation via CSharpEmitter.EmitToMemory (no disk I/O,
// no process spawning).

using System.Collections.Immutable;
using System.Text;
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
/// Property-based tests verifying that transpilation preserves type category
/// for newtypes.
///
/// Property 9: For any newtype whose source is a class, the transpiled C#
/// output SHALL be a class declaration; for any newtype whose source is a
/// struct, the transpiled C# output SHALL be a struct declaration.
///
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public sealed class NewtypeTranspilationCategoryPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared infrastructure
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario with a source type (class or struct) and a
    /// newtype declared from it, along with the expected C# type category.
    /// </summary>
    public sealed record TranspilationCategoryScenario(
        string Source,
        string NewtypeName,
        string ExpectedCategory,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] SourceTypeNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] NewtypeNames =
        ["MyAlpha", "MyBeta", "MyGamma", "MyDelta", "MyEpsilon"];

    private static readonly string[] FieldNames =
        ["x", "y", "z", "w", "count", "value", "data", "flag"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    /// <summary>
    /// Generates a scenario with a class source type and a newtype from it.
    /// Expected C# output: <c>public class {newtypeName}</c>.
    /// </summary>
    private static Gen<TranspilationCategoryScenario> GenClassScenario()
    {
        return from sourceIdx in Gen.Choose(0, SourceTypeNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               from fieldCount in Gen.Choose(0, 3)
               from methodCount in Gen.Choose(0, 3)
               from hasConstructor in Gen.Elements(true, false)
               let sourceName = SourceTypeNames[sourceIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               where sourceName != newtypeName
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => $"    public int {FieldNames[i]};")
                   .ToArray()
               let methods = Enumerable.Range(0, methodCount)
                   .Select(i => $"    public int {MethodNames[i]}() {{ return 0; }}")
                   .ToArray()
               let ctorLines = hasConstructor
                   ? new[] { $"    public {sourceName}() {{ }}" }
                   : Array.Empty<string>()
               let allMembers = string.Join("\n", fields.Concat(methods).Concat(ctorLines))
               let source = $"namespace TestNs;\n\npublic class {sourceName}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {sourceName};\n"
               select new TranspilationCategoryScenario(
                   source,
                   newtypeName,
                   "class",
                   $"class {sourceName} ({fieldCount}F, {methodCount}M) -> {newtypeName}");
    }

    /// <summary>
    /// Generates a scenario with a struct source type and a newtype from it.
    /// Expected C# output: <c>public struct {newtypeName}</c>.
    /// </summary>
    private static Gen<TranspilationCategoryScenario> GenStructScenario()
    {
        return from sourceIdx in Gen.Choose(0, SourceTypeNames.Length - 1)
               from newtypeIdx in Gen.Choose(0, NewtypeNames.Length - 1)
               from fieldCount in Gen.Choose(1, 3)
               from methodCount in Gen.Choose(0, 3)
               from hasConstructor in Gen.Elements(true, false)
               let sourceName = SourceTypeNames[sourceIdx]
               let newtypeName = NewtypeNames[newtypeIdx]
               where sourceName != newtypeName
               let fields = Enumerable.Range(0, fieldCount)
                   .Select(i => $"    public int {FieldNames[i]};")
                   .ToArray()
               let methods = Enumerable.Range(0, methodCount)
                   .Select(i => $"    public int {MethodNames[i]}() {{ return 0; }}")
                   .ToArray()
               let ctorParams = string.Join(", ", Enumerable.Range(0, fieldCount).Select(i => $"int p{i}"))
               let ctorBody = string.Join("\n", Enumerable.Range(0, fieldCount).Select(i => $"        this.{FieldNames[i]} = p{i};"))
               let ctorLines = hasConstructor
                   ? new[] { $"    public {sourceName}({ctorParams})\n    {{\n{ctorBody}\n    }}" }
                   : Array.Empty<string>()
               let allMembers = string.Join("\n", fields.Concat(methods).Concat(ctorLines))
               let source = $"namespace TestNs;\n\npublic struct {sourceName}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {sourceName};\n"
               select new TranspilationCategoryScenario(
                   source,
                   newtypeName,
                   "struct",
                   $"struct {sourceName} ({fieldCount}F, {methodCount}M) -> {newtypeName}");
    }

    /// <summary>
    /// Combined generator covering both class and struct source types.
    /// </summary>
    public static Arbitrary<TranspilationCategoryScenario> ArbitraryTranspilationCategoryScenario()
    {
        return Gen.OneOf(GenClassScenario(), GenStructScenario()).ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // In-memory compilation (no disk I/O)
    // ═══════════════════════════════════════════════════════════════════════

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
            return (false, "", FormatDiagnostics(lexBag));

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors || parseResult.Root is null)
            return (false, "", FormatDiagnostics(parseBag));

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
            return (false, "", FormatDiagnostics(semBag));

        // Type checker (ignore entry point diagnostic — test programs don't need Main)
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        if(tcBag.ToImmutableArray().Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Id.Value != "MIST_TC_0017"))
            return (false, "", FormatDiagnostics(tcBag));

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if(emitBag.HasErrors)
            return (false, "", FormatDiagnostics(emitBag));

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
            sb.AppendLine(content);

        return (true, sb.ToString(), "");
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Transpilation preserves type category
    //
    // **Validates: Requirements 5.1, 5.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any newtype whose source is a class, the transpiled C# output SHALL
    /// be a class declaration; for any newtype whose source is a struct, the
    /// transpiled C# output SHALL be a struct declaration.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(NewtypeTranspilationCategoryPropertyTests) })]
    public Property TranspilationPreservesTypeCategory(TranspilationCategoryScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileInMemory(scenario.Source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed:\n{diagnosticText}")
                .Label($"Source:\n{scenario.Source}");
        }

        // Verify the emitted C# contains the expected type category declaration
        var expectedPattern = $"public {scenario.ExpectedCategory} {scenario.NewtypeName}";
        var containsExpected = csContent.Contains(expectedPattern);

        if(!containsExpected)
        {
            // Check if the opposite category was emitted (a clear violation)
            var oppositeCategory = scenario.ExpectedCategory == "class" ? "struct" : "class";
            var oppositePattern = $"public {oppositeCategory} {scenario.NewtypeName}";
            var containsOpposite = csContent.Contains(oppositePattern);

            var detail = containsOpposite
                ? $"Found WRONG category: '{oppositePattern}' instead of '{expectedPattern}'"
                : $"Expected pattern '{expectedPattern}' not found in emitted C#";

            return false.ToProperty()
                .Label(detail)
                .Label($"Emitted C#:\n{csContent}")
                .Label($"Source:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Correctly emitted '{expectedPattern}'");
    }
}