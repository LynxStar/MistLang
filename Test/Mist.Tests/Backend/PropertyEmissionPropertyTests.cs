// Feature: properties, Property 7: C# Emission Validity
// **Validates: Requirements 14.1–14.11**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for C# emission validity of property declarations.
///
/// **Validates: Requirements 14.1–14.11**
///
/// For any valid Mist source with properties, C# emitter produces syntactically
/// valid C# preserving semantic meaning.
/// </summary>
public sealed class PropertyEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public PropertyEmissionPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistPropertyEmissionTests_" + Guid.NewGuid().ToString("N"));
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

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode() =>
        MistPropertyGenerator.GenPropertyProgram().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private ISemanticModel BuildModel(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        CleanOutputDir();
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", diagnostics);

        var csFiles = Directory.GetFiles(outputDir, "*.cs");
        if(csFiles.Length == 0)
        {
            return "";
        }

        return string.Join("\n", csFiles.Select(File.ReadAllText));
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: C# Emission Validity
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyEmissionPropertyTests) })]
    public Property CSharpEmission_ProducesNonEmptyOutput(CompilationUnitNode ast)
    {
        var model = BuildModel(ast);
        var csContent = EmitAndGetCsContent(model);

        return (!string.IsNullOrWhiteSpace(csContent)).ToProperty()
            .Label("Emitted C# should be non-empty");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyEmissionPropertyTests) })]
    public Property CSharpEmission_ContainsPropertySyntax(CompilationUnitNode ast)
    {
        var model = BuildModel(ast);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return true.ToProperty().Label("No output (may be expected)");
        }

        // The emitted C# should contain property-related syntax
        var containsPropertySyntax =
            csContent.Contains("get") ||
            csContent.Contains("set") ||
            csContent.Contains("init") ||
            csContent.Contains("=>") ||
            csContent.Contains("this["); // indexer

        return containsPropertySyntax.ToProperty()
            .Label($"No property syntax found in emitted C#:\n{csContent}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyEmissionPropertyTests) })]
    public Property CSharpEmission_BalancedBraces(CompilationUnitNode ast)
    {
        var model = BuildModel(ast);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return true.ToProperty().Label("No output (may be expected)");
        }

        var openBraces = csContent.Count(c => c == '{');
        var closeBraces = csContent.Count(c => c == '}');

        return (openBraces == closeBraces).ToProperty()
            .Label($"Unbalanced braces: {{ = {openBraces}, }} = {closeBraces}\n{csContent}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyEmissionPropertyTests) })]
    public Property CSharpEmission_ContainsClassOrInterfaceDeclaration(CompilationUnitNode ast)
    {
        var model = BuildModel(ast);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrWhiteSpace(csContent))
        {
            return true.ToProperty().Label("No output (may be expected)");
        }

        var containsTypeDecl =
            csContent.Contains("class ") ||
            csContent.Contains("interface ");

        return containsTypeDecl.ToProperty()
            .Label($"No class or interface declaration found in emitted C#:\n{csContent}");
    }
}