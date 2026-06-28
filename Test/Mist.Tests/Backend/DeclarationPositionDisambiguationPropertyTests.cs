// Feature: attributes-and-test-command, Property 2: Declaration-Position Disambiguation
// **Validates: Requirements 1.10**
//
// For any valid Mist program containing both attribute annotations on declarations and
// bracket expressions in expression position (array literals, index access), the parser
// SHALL produce AttributeListNode only in declaration context and ArrayLiteralExpression/
// IndexSuffix nodes in expression context -- never confusing the two.

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for declaration-position disambiguation.
///
/// Property 2: Declaration-Position Disambiguation
/// Generates programs containing BOTH attribute annotations on class/method
/// declarations (e.g., [Serializable], [Obsolete("msg")]) AND bracket expressions
/// in method bodies (array literals like [1, 2, 3], index access like arr[0]).
/// Compiles in-memory and verifies no parse errors occur, proving the parser
/// correctly disambiguates [ in declaration position vs expression position.
///
/// Uses batched in-memory compilation via shared CompilerDriver (no process spawning).
/// Minimum 100 iterations.
///
/// **Validates: Requirements 1.10**
/// </summary>
public sealed class DeclarationPositionDisambiguationPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // --- Shared driver (reused across all iterations) ---
    // No analyzers needed -- we're testing parsing/disambiguation, not type checking.

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray<Mist.Analysis.Contracts.IAnalyzer>.Empty,
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public DeclarationPositionDisambiguationPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_disambig_" + Guid.NewGuid().ToString("N"));
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

    // =========================================================================
    // Scenario model
    // =========================================================================

    /// <summary>
    /// Describes a single attribute annotation to place on a declaration.
    /// </summary>
#pragma warning disable CA1034
    public record AttributeAnnotation(string Name, string? Argument)
    {
        public override string ToString()
        {
            return Argument is not null ? $"[{Name}({Argument})]" : $"[{Name}]";
        }
    }

    /// <summary>
    /// Describes a bracket expression to use in a method body.
    /// </summary>
    public enum BracketExprKind
    {
        ArrayLiteral,
        IndexAccess,
        NewArray,
    }

    /// <summary>
    /// A bracket expression scenario placed inside a method body.
    /// </summary>
    public record BracketExpression(BracketExprKind Kind, string Code)
    {
        public override string ToString()
        {
            return $"[{Kind}] {Code}";
        }
    }

    /// <summary>
    /// Full disambiguation scenario: attributes on a declaration plus bracket
    /// expressions in a method body.
    /// </summary>
    public record DisambiguationScenario(
        IReadOnlyList<AttributeAnnotation> ClassAttributes,
        IReadOnlyList<AttributeAnnotation> MethodAttributes,
        IReadOnlyList<BracketExpression> BodyExpressions)
    {
        public override string ToString()
        {
            return $"ClassAttrs={ClassAttributes.Count}, " +
                   $"MethodAttrs={MethodAttributes.Count}, " +
                   $"BodyExprs={BodyExpressions.Count}";
        }
    }
#pragma warning restore CA1034

    // =========================================================================
    // Generators
    // =========================================================================

    private static readonly string[] AttrNames =
        ["Serializable", "Obsolete", "Fact", "Theory", "Required", "Display"];

    private static readonly string[] AttrStringArgs =
        ["\"deprecated\"", "\"use V2\"", "\"test\"", "\"info\""];

    private static readonly string[] AttrIntArgs =
        ["0", "1", "42", "100"];

    /// <summary>
    /// Generates an attribute with no arguments, a string argument, or an int argument.
    /// </summary>
    private static Gen<AttributeAnnotation> GenAttribute()
    {
        var noArg = Gen.Elements(AttrNames)
            .Select(n => new AttributeAnnotation(n, null));

        var stringArg = from name in Gen.Elements(AttrNames)
                        from arg in Gen.Elements(AttrStringArgs)
                        select new AttributeAnnotation(name, arg);

        var intArg = from name in Gen.Elements(AttrNames)
                     from arg in Gen.Elements(AttrIntArgs)
                     select new AttributeAnnotation(name, arg);

        return Gen.Frequency(
            (2, noArg),
            (2, stringArg),
            (1, intArg));
    }

    /// <summary>
    /// Generates a bracket expression for use in a method body.
    /// </summary>
    private static Gen<BracketExpression> GenBracketExpression()
    {
        // Array literals: [1, 2, 3] etc.
        var arrayLiteral = from count in Gen.Choose(1, 5)
                           from values in Gen.ArrayOf(Gen.Choose(0, 99), count)
                           let code = $"var arr{count} = [{string.Join(", ", values)}];"
                           select new BracketExpression(BracketExprKind.ArrayLiteral, code);

        // Index access: arr[i]
        var indexAccess = from idx in Gen.Choose(0, 4)
                          let code = $"var val = nums[{idx}];"
                          select new BracketExpression(BracketExprKind.IndexAccess, code);

        // new int[N] allocation
        var newArray = from size in Gen.Choose(1, 10)
                       let code = $"var buf = new int[{size}];"
                       select new BracketExpression(BracketExprKind.NewArray, code);

        return Gen.Frequency(
            (3, arrayLiteral),
            (2, indexAccess),
            (2, newArray));
    }

    /// <summary>
    /// Generates a full disambiguation scenario.
    /// </summary>
    private static Gen<DisambiguationScenario> GenScenario()
    {
        return from classAttrCount in Gen.Choose(1, 3)
               from methodAttrCount in Gen.Choose(1, 3)
               from bodyExprCount in Gen.Choose(1, 4)
               from classAttrs in Gen.ArrayOf(GenAttribute(), classAttrCount)
               from methodAttrs in Gen.ArrayOf(GenAttribute(), methodAttrCount)
               from bodyExprs in Gen.ArrayOf(GenBracketExpression(), bodyExprCount)
               select new DisambiguationScenario(classAttrs, methodAttrs, bodyExprs);
    }

    // =========================================================================
    // Source builder
    // =========================================================================

    /// <summary>
    /// Builds a Mist program containing both attribute annotations on declarations
    /// and bracket expressions in a method body.
    /// </summary>
    private static string BuildProgram(DisambiguationScenario scenario, int index)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"namespace DisambigTest{index};");
        _ = sb.AppendLine();

        // Class-level attributes
        foreach(var attr in scenario.ClassAttributes)
        {
            _ = sb.AppendLine(attr.ToString());
        }

        _ = sb.AppendLine($"class Target{index}");
        _ = sb.AppendLine("{");

        // Method-level attributes
        foreach(var attr in scenario.MethodAttributes)
        {
            _ = sb.AppendLine($"    {attr}");
        }

        _ = sb.AppendLine($"    public void Run()");
        _ = sb.AppendLine("    {");

        // Always declare a nums array so index access expressions have something to index
        _ = sb.AppendLine("        var nums = new int[5];");

        // Body bracket expressions
        foreach(var expr in scenario.BodyExpressions)
        {
            _ = sb.AppendLine($"        {expr.Code}");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    // =========================================================================
    // Compilation helper
    // =========================================================================

    /// <summary>
    /// Compiles Mist source in-process and returns any parse errors.
    /// We only check for parse-stage errors -- type-check or backend errors
    /// are irrelevant to the disambiguation property.
    /// </summary>
    private (bool HasParseErrors, IReadOnlyList<Diagnostic> ParseErrors) CompileAndCheckParse(
        string mistSource, int index)
    {
        var iterDir = Path.Combine(outputDir, $"iter_{index}");
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
                ProjectName: $"DisambigTest{index}",
                NamespaceRoot: $"DisambigTest{index}",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                OutputType: ProjectOutputType.Library);

            var result = SharedDriver.Compile(options);

            // Filter to only parse-stage errors (and lexing errors which would also
            // indicate bracket confusion)
            var parseErrors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Where(d => d.Stage is PipelineStage.Parsing or PipelineStage.Lexing)
                .ToList();

            return (parseErrors.Count > 0, parseErrors);
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // =========================================================================
    // Property test
    // =========================================================================

    /// <summary>
    /// Property 2: Declaration-Position Disambiguation.
    ///
    /// Any program where [ appears both before declarations (as attributes) and
    /// inside method bodies (as arrays/indexing) should compile without parse errors
    /// (no ambiguity between the two contexts).
    ///
    /// Generates 100 random disambiguation scenarios, compiles each in-memory,
    /// and verifies no parse errors occur.
    ///
    /// **Validates: Requirements 1.10**
    /// </summary>
    [Fact]
    [Trait("Category", "PropertyBased")]
    public void DeclarationPosition_Disambiguation_NoParseErrors()
    {
        var scenarios = GenScenario().Sample(IterationCount).ToList();

        var failures = new List<string>();

        for(var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var source = BuildProgram(scenario, i);

            var (hasParseErrors, parseErrors) = CompileAndCheckParse(source, i);

            if(hasParseErrors)
            {
                var errorDetails = string.Join("\n",
                    parseErrors.Select(d => $"  {d.Stage} {d.Id.Value}: {d.Message} at {d.Location}"));

                failures.Add(
                    $"Scenario {i} had parse errors (disambiguation failure):\n" +
                    $"  Scenario: {scenario}\n" +
                    $"  Errors:\n{errorDetails}\n" +
                    $"  Source:\n{source}");
            }
        }

        if(failures.Count > 0)
        {
            var firstFailure = failures[0];
            throw new Exception(
                $"Property failed: {failures.Count}/{scenarios.Count} scenarios had parse errors.\n\n" +
                $"First failure:\n{firstFailure}");
        }
    }
}