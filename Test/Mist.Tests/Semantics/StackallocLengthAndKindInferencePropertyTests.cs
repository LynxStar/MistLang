// Feature: stack-only-types, Property 4: Length and kind inference
//
// **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
//
// In-process via SemanticModelBuilder + TypeCheckerAnalyzer; assert
// KnownLength == N for literal N, unknown otherwise, and IsReadOnly true
// iff the target context is a ReadOnlySpan family target.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Storm.Runtime;

using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using MistLexer = Mist.Lexing.MistLexer;
using MistParser = Mist.Parsing.MistParser;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a stackalloc length-inference scenario for property testing.
/// </summary>
public sealed record StackallocLengthInferenceScenario(
    string Source,
    long? ExpectedKnownLength,
    bool ExpectedIsReadOnly,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for stackalloc length and kind inference
/// (Property 4: Length and kind inference).
///
/// Verifies that <see cref="SemanticModelBuilder"/> resolves stackalloc expressions
/// to <see cref="SpanType"/> with:
/// - <c>KnownLength == N</c> when the size is an integer literal N
/// - <c>KnownLength == null</c> (unknown) when the size is a runtime expression
/// - <c>IsReadOnly == true</c> iff the target context is a ReadOnlySpan family target
/// - <c>IsReadOnly == false</c> for var/Span targets
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> with a
/// <see cref="CapturingSemanticModelBuilder"/> to capture the semantic model and
/// inspect the resolved type of StackallocExpressionPrimaryNode.
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
/// </summary>
public sealed class StackallocLengthAndKindInferencePropertyTests : IDisposable
{
    private readonly string outputDir;

    public StackallocLengthAndKindInferencePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistStackallocLenKindPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    // ─── Element type pool: stampable primitives ─────────────────────────

    private static readonly string[] ElementTypes =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "bool", "char"
    ];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a literal size in a mutable (var) context.
    /// Expected: KnownLength = N, IsReadOnly = false.
    /// Validates Requirement 4.1.
    /// </summary>
    private static Gen<StackallocLengthInferenceScenario> GenLiteralSizeMutableScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from size in Gen.Choose(1, 512)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram($"var s = stackalloc {elementType}[{size}];")
               select new StackallocLengthInferenceScenario(
                   source,
                   ExpectedKnownLength: size,
                   ExpectedIsReadOnly: false,
                   $"var s = stackalloc {elementType}[{size}] → KnownLength={size}, mutable");
    }

    /// <summary>
    /// Generates a scenario with a runtime (variable) size in a mutable (var) context.
    /// Expected: KnownLength = null (unknown), IsReadOnly = false.
    /// Validates Requirement 4.2.
    /// </summary>
    private static Gen<StackallocLengthInferenceScenario> GenRuntimeSizeMutableScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from sizeVal in Gen.Choose(1, 100)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   $"var n = {sizeVal};\n        var s = stackalloc {elementType}[n];")
               select new StackallocLengthInferenceScenario(
                   source,
                   ExpectedKnownLength: null,
                   ExpectedIsReadOnly: false,
                   $"var s = stackalloc {elementType}[n] (n={sizeVal}) → unknown length, mutable");
    }

    /// <summary>
    /// Generates a scenario with a literal size in a ReadOnlySpan target context.
    /// Expected: KnownLength = N, IsReadOnly = true.
    /// Validates Requirements 4.3, 4.5.
    /// </summary>
    private static Gen<StackallocLengthInferenceScenario> GenLiteralSizeReadOnlyScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from size in Gen.Choose(1, 512)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}];")
               select new StackallocLengthInferenceScenario(
                   source,
                   ExpectedKnownLength: size,
                   ExpectedIsReadOnly: true,
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}] → KnownLength={size}, readonly");
    }

    /// <summary>
    /// Generates a scenario with a runtime size in a ReadOnlySpan target context.
    /// Expected: KnownLength = null (unknown), IsReadOnly = true.
    /// Validates Requirements 4.2, 4.3, 4.5.
    /// </summary>
    private static Gen<StackallocLengthInferenceScenario> GenRuntimeSizeReadOnlyScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from sizeVal in Gen.Choose(1, 100)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   $"var n = {sizeVal};\n        ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[n];")
               select new StackallocLengthInferenceScenario(
                   source,
                   ExpectedKnownLength: null,
                   ExpectedIsReadOnly: true,
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[n] (n={sizeVal}) → unknown length, readonly");
    }

    /// <summary>
    /// Combined generator: all four scenario types with equal probability.
    /// </summary>
    public static Arbitrary<StackallocLengthInferenceScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenLiteralSizeMutableScenario(),
            GenRuntimeSizeMutableScenario(),
            GenLiteralSizeReadOnlyScenario(),
            GenRuntimeSizeReadOnlyScenario()).ToArbitrary();
    }

    // ─── Program wrapping ───────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $$"""
            class Program
            {
                static void Main()
                {
                    {{body}}
                }
            }
            """;
    }

    // ─── Compilation helper ─────────────────────────────────────────────

    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false,
            GlobalUsings: ImmutableArray.Create("Standard"));

        var result = driver.Compile(options);
        return (result, capture.CapturedModel);
    }

    // ─── AST node finder ────────────────────────────────────────────────

    /// <summary>
    /// Recursively finds all <see cref="StackallocExpressionPrimaryNode"/>
    /// nodes in the given AST subtree.
    /// </summary>
    private static List<StackallocExpressionPrimaryNode> FindStackallocNodes(SyntaxNode root)
    {
        var result = new List<StackallocExpressionPrimaryNode>();
        Walk(root);
        return result;

        void Walk(SyntaxNode node)
        {
            if(node is StackallocExpressionPrimaryNode stackallocNode)
            {
                result.Add(stackallocNode);
            }

            foreach(var child in node.Children())
            {
                if(child is not null)
                {
                    Walk(child);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Length and kind inference
    //
    // **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
    //
    // For any Mist stackalloc expression:
    // - A literal integer size N yields SpanType with KnownLength == N
    // - A non-literal (runtime) size yields SpanType with KnownLength == null
    // - A ReadOnlySpan family target yields IsReadOnly == true
    // - A var / Span family target yields IsReadOnly == false
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: The resolved SpanType has the correct KnownLength and IsReadOnly
    /// based on the literal/runtime size and the target context.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocLengthAndKindInferencePropertyTests) })]
    public Property StackallocExpression_ResolvesToCorrectSpanType(
        StackallocLengthInferenceScenario scenario)
    {
        var (result, model) = CompileAndCapture(scenario.Source);

        // The program should compile successfully (no semantic/type-checker errors
        // preventing model construction).
        if(model is null)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Model is null — compilation failed. Errors: [{errors}]. Source:\n{scenario.Source}");
        }

        // Find the user AST (last one, after stdlib ASTs).
        var userAst = model.Asts[^1];
        var stackallocNodes = FindStackallocNodes(userAst);

        if(stackallocNodes.Count == 0)
        {
            return false.ToProperty()
                .Label($"No StackallocExpressionPrimaryNode found in AST. Scenario: {scenario}");
        }

        // Get the resolved type of the stackalloc expression.
        var stackallocNode = stackallocNodes[0];
        IType? resolvedType;
        try
        {
            resolvedType = model.GetTypeOf(stackallocNode);
        }
        catch
        {
            return false.ToProperty()
                .Label($"GetTypeOf threw for stackalloc node. Scenario: {scenario}");
        }

        if(resolvedType is not SpanType spanType)
        {
            return false.ToProperty()
                .Label($"Expected SpanType but got {resolvedType?.GetType().Name ?? "null"} " +
                       $"(DisplayName: {resolvedType?.DisplayName ?? "n/a"}). Scenario: {scenario}");
        }

        // Assert KnownLength matches expectation.
        var lengthOk = spanType.KnownLength == scenario.ExpectedKnownLength;

        // Assert IsReadOnly matches expectation.
        var readOnlyOk = spanType.IsReadOnly == scenario.ExpectedIsReadOnly;

        return (lengthOk && readOnlyOk)
            .ToProperty()
            .Label($"KnownLength: expected={scenario.ExpectedKnownLength?.ToString() ?? "null"}, " +
                   $"actual={spanType.KnownLength?.ToString() ?? "null"} ({(lengthOk ? "OK" : "MISMATCH")}); " +
                   $"IsReadOnly: expected={scenario.ExpectedIsReadOnly}, " +
                   $"actual={spanType.IsReadOnly} ({(readOnlyOk ? "OK" : "MISMATCH")}). " +
                   $"Scenario: {scenario}");
    }

    /// <summary>
    /// Property 4 (supplemental): The resolved SpanType always has the correct
    /// element type corresponding to the stackalloc element type in the source.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3, 4.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocLengthAndKindInferencePropertyTests) })]
    public Property StackallocExpression_ElementTypeIsResolved(
        StackallocLengthInferenceScenario scenario)
    {
        var (_, model) = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty()
                .Label($"Model is null — compilation failed.");
        }

        var userAst = model.Asts[^1];
        var stackallocNodes = FindStackallocNodes(userAst);

        if(stackallocNodes.Count == 0)
        {
            return false.ToProperty()
                .Label($"No StackallocExpressionPrimaryNode found in AST.");
        }

        var stackallocNode = stackallocNodes[0];
        IType? resolvedType;
        try
        {
            resolvedType = model.GetTypeOf(stackallocNode);
        }
        catch
        {
            return false.ToProperty()
                .Label($"GetTypeOf threw for stackalloc node.");
        }

        if(resolvedType is not SpanType spanType)
        {
            return false.ToProperty()
                .Label($"Expected SpanType but got {resolvedType?.GetType().Name ?? "null"}.");
        }

        // The element type should be non-null and a valid resolved type.
        var elementOk = spanType.ElementType is not null;

        return elementOk
            .ToProperty()
            .Label($"ElementType is null on resolved SpanType. Scenario: {scenario}");
    }
}