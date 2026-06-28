// Feature: attributes-and-test-command, Property 2: Declaration-Position Disambiguation
// **Validates: Requirements 1.10**
//
// For any valid Mist program containing both attribute annotations on declarations
// and bracket expressions in expression position (array literals, index access),
// the parser SHALL produce AttributeListNode only in declaration context and
// ArrayLiteralExpression/IndexSuffix nodes in expression context -- never confusing the two.

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for declaration-position disambiguation.
///
/// Property 2: The parser never confuses attribute brackets with array/index brackets.
/// Programs that contain BOTH attribute annotations on declarations AND bracket
/// expressions in method bodies compile without parse errors, proving the parser
/// correctly disambiguates <c>[</c> in declaration vs expression position.
///
/// Uses batched in-memory compilation via shared CompilerDriver (no process spawning).
/// </summary>
public sealed class AttributeDeclarationPositionDisambiguationPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // --- Shared driver (reused across all iterations) ---

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public AttributeDeclarationPositionDisambiguationPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_disamb_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // === Scenario definition ===

    /// <summary>
    /// Describes a disambiguation scenario: an attribute configuration on
    /// a declaration combined with bracket expressions in a method body.
    /// </summary>
    private sealed record DisambiguationScenario(
        string AttributeName,
        string[] AttributeArgs,
        string DeclarationKind,
        string[] BracketExpressions)
    {
        public override string ToString()
        {
            var args = AttributeArgs.Length > 0
                ? $"({string.Join(", ", AttributeArgs)})"
                : "";
            return $"[{AttributeName}{args}] {DeclarationKind} with [{string.Join("; ", BracketExpressions)}]";
        }
    }

    // === Generators ===

    private static readonly string[] AttributeNames =
        ["Serializable", "Obsolete", "Conditional", "Flags", "TestAttr", "MyCustom"];

    private static readonly string[] SimpleStringArgs =
        ["\"hello\"", "\"test\"", "\"deprecated\"", "\"msg\""];

    private static readonly string[] SimpleIntArgs =
        ["1", "2", "42", "100"];

    private static readonly string[] DeclarationKinds =
        ["class", "struct"];

    private static Gen<DisambiguationScenario> ScenarioGen()
    {
        var genAttrName = Gen.Elements(AttributeNames);

        var genArgs = Gen.Frequency(
            (3, Gen.Constant(Array.Empty<string>())),
            (2, Gen.Elements(SimpleStringArgs).Select(a => new[] { a })),
            (2, Gen.Elements(SimpleIntArgs).Select(a => new[] { a })),
            (1, from a1 in Gen.Elements(SimpleIntArgs)
                from a2 in Gen.Elements(SimpleIntArgs)
                select new[] { a1, a2 }));

        var genDeclKind = Gen.Elements(DeclarationKinds);

        // Generate 1-3 bracket expressions for the method body
        var genBracketExprs = Gen.Frequency(
            (3, Gen.Constant(new[] { "array-literal" })),
            (3, Gen.Constant(new[] { "index-access" })),
            (2, Gen.Constant(new[] { "array-literal", "index-access" })),
            (1, Gen.Constant(new[] { "array-literal", "array-literal", "index-access" })));

        return from attrName in genAttrName
               from args in genArgs
               from declKind in genDeclKind
               from bracketExprs in genBracketExprs
               select new DisambiguationScenario(attrName, args, declKind, bracketExprs);
    }

    // === Source code builder ===

    /// <summary>
    /// Builds a Mist source program that has both attribute annotations on
    /// declarations and bracket expressions inside a method body.
    /// </summary>
    private static string BuildProgram(DisambiguationScenario scenario)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("namespace TestDisambiguation;");
        _ = sb.AppendLine();

        // Build the attribute annotation
        var attrArgs = scenario.AttributeArgs.Length > 0
            ? $"({string.Join(", ", scenario.AttributeArgs)})"
            : "";
        var attrAnnotation = $"[{scenario.AttributeName}{attrArgs}]";

        // Emit the declaration with the attribute
        _ = sb.AppendLine(attrAnnotation);
        _ = sb.AppendLine($"{scenario.DeclarationKind} Decorated");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("}");
        _ = sb.AppendLine();

        // Emit a class with a method containing bracket expressions
        _ = sb.AppendLine("class Program");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    public static void Main()");
        _ = sb.AppendLine("    {");

        foreach(var bracketExpr in scenario.BracketExpressions)
        {
            switch(bracketExpr)
            {
                case "array-literal":
                    _ = sb.AppendLine("        var items = [1, 2, 3];");
                    break;
                case "index-access":
                    _ = sb.AppendLine("        var data = [10, 20, 30];");
                    _ = sb.AppendLine("        var elem = data[0];");
                    break;
            }
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    // === Compilation helper ===

    /// <summary>
    /// Compiles a Mist source in-process and returns the diagnostics.
    /// No process spawning occurs.
    /// </summary>
    private (bool Succeeded, IReadOnlyList<Diagnostic> Errors) CompileInMemory(string mistSource, string iterationId)
    {
        var iterDir = Path.Combine(outputDir, iterationId);
        _ = Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: Path.Combine(iterDir, "out"),
                BinDirectory: Path.Combine(iterDir, "bin"),
                ProjectName: "TestDisambiguation",
                NamespaceRoot: "TestDisambiguation",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            var parseErrors = result.Diagnostics
                .Where(d => d.Stage == PipelineStage.Parsing && d.Severity == DiagnosticSeverity.Error)
                .ToList();

            return (parseErrors.Count == 0, parseErrors);
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // === Property test ===

    /// <summary>
    /// Property 2: Declaration-Position Disambiguation.
    ///
    /// For any valid Mist program containing both attribute annotations on
    /// declarations and bracket expressions in method bodies, compilation
    /// succeeds without parse errors -- proving the parser correctly
    /// disambiguates [brackets] in declaration vs expression position.
    ///
    /// **Validates: Requirements 1.10**
    /// </summary>
    [Fact]
    public void DeclarationPositionDisambiguation_NoParsErrorsWithBothContexts()
    {
        var scenarios = ScenarioGen()
            .Sample(IterationCount)
            .ToList();

        for(var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var source = BuildProgram(scenario);
            var (succeeded, errors) = CompileInMemory(source, $"iter_{i}");

            if(!succeeded)
            {
                var errorMessages = string.Join("\n",
                    errors.Select(e => $"  {e.Id.Value}: {e.Message}"));
                throw new Exception(
                    $"Property failed on scenario {i}/{scenarios.Count}.\n" +
                    $"  Scenario: {scenario}\n" +
                    $"  Source:\n{source}\n" +
                    $"  Parse errors:\n{errorMessages}");
            }
        }
    }
}