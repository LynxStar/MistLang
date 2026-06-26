using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Integration tests that parse the full Mist grammar through the source generator
/// pipeline and verify the generated AST output for key node types affected by the
/// repetition and optional-sequence bugfixes.
/// </summary>
public class MistGrammarIntegrationTests
{
    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Locates the Mist grammar directory by walking up from the test assembly's
    /// output directory until we find the repository root (contains Mist.slnx).
    /// </summary>
    private static string FindGrammarDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while(dir != null)
        {
            var slnxPath = Path.Combine(dir, "Mist.slnx");
            if(File.Exists(slnxPath))
            {
                var grammarDir = Path.Combine(dir, "Source", "Mist", "Grammar");
                if(Directory.Exists(grammarDir))
                {
                    return grammarDir;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate the Mist grammar directory. " +
            "Ensure the test is run from within the repository tree.");
    }

    /// <summary>
    /// Creates a file reader delegate that reads files from the filesystem,
    /// resolving paths relative to the grammar directory.
    /// </summary>
    private static Func<string, string?> FileSystemReader(string grammarDir)
    {
        return path =>
        {
            // The resolver uses canonical forward-slash paths.
            // Map them back to the filesystem.
            var fsPath = path.Replace('/', Path.DirectorySeparatorChar);

            // If the path is already absolute, try it directly
            if(Path.IsPathRooted(fsPath) && File.Exists(fsPath))
            {
                return File.ReadAllText(fsPath);
            }

            // Otherwise resolve relative to the grammar directory
            var fullPath = Path.GetFullPath(Path.Combine(grammarDir, fsPath));
            if(File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }

            return null;
        };
    }

    /// <summary>
    /// Processes the full Mist grammar through the source generator pipeline
    /// and returns the EmissionContext.
    /// </summary>
    private static EmissionContext BuildMistEmissionContext()
    {
        var grammarDir = FindGrammarDirectory();
        var mainGrammarPath = Path.Combine(grammarDir, "Mist.storm")
            .Replace(Path.DirectorySeparatorChar, '/');

        var reader = FileSystemReader(grammarDir);
        var resolver = new StormImportResolver(reader);
        var resolution = resolver.Resolve(mainGrammarPath);

        Assert.True(resolution.Succeeded,
            "Mist grammar import resolution failed: " +
            string.Join(", ", resolution.Diagnostics.Select(d => d.Message)));

        var stormGrammar = StormGrammar.Build(resolution);
        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded,
            "Mist grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        return new EmissionContext("Mist.Generated", "Mist", loweringResult.Grammar!);
    }

    // Cache the context and AST output across tests in this class
    private static readonly Lazy<EmissionContext> context = new(BuildMistEmissionContext);
    private static readonly Lazy<string> astOutput = new(() => AstEmitter.EmitAstNodes(context.Value));

    // ---------------------------------------------------------------
    //  7.1 — ParameterListNode has ImmutableArray<ParameterNode>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_ParameterListNode_ContainsImmutableArrayOfParameterNode()
    {
        var output = astOutput.Value;

        // ParameterList rule: Parameter (COMMA Parameter)*
        // Should generate a list property for repeated Parameters
        Assert.Contains("ImmutableArray<ParameterNode>", output);
    }

    [Fact]
    public void MistGrammar_ParameterListNode_IsSealedClass()
    {
        var output = astOutput.Value;

        Assert.Contains("public sealed class ParameterListNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.2 — ArgumentListNode has ImmutableArray<ExpressionNode>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_ArgumentListNode_ContainsImmutableArrayOfExpressionNode()
    {
        var output = astOutput.Value;

        // ArgumentList rule: Expression (COMMA Expression)*
        // Should generate a list property for repeated Expressions
        Assert.Contains("ImmutableArray<ExpressionNode>", output);
    }

    [Fact]
    public void MistGrammar_ArgumentListNode_IsSealedClass()
    {
        var output = astOutput.Value;

        Assert.Contains("public sealed class ArgumentListNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.3 — QualifiedIdentifierNode has ImmutableArray<Token>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_ContainsImmutableArrayOfToken()
    {
        var output = astOutput.Value;

        // QualifiedIdentifier rule: IDENTIFIER (DOT IDENTIFIER)*
        // Should generate a list property for repeated IDENTIFIER tokens
        Assert.Contains("ImmutableArray<Token>", output);
    }

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_IsSealedClass()
    {
        var output = astOutput.Value;

        Assert.Contains("public sealed class QualifiedIdentifierNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.4 — FieldDeclarationNode preserves ExpressionNode from
    //         the optional initializer group
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_FieldDeclarationNode_ContainsExpressionNodeNullableProperty()
    {
        var output = astOutput.Value;

        // FieldDeclaration rule: ... initializer:(EQUAL Expression)? SEMICOLON
        // The labeled optional (EQUAL Expression)? should produce an ExpressionNode? property
        Assert.Contains("ExpressionNode?", output);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_DoesNotHaveTokenOnlyInitializer()
    {
        var output = astOutput.Value;

        // The Initializer property should NOT be typed as Token? — it should be ExpressionNode?
        // Extract the FieldDeclarationNode section to check
        var fieldNodeStart = output.IndexOf("public sealed class FieldDeclarationNode");
        Assert.True(fieldNodeStart >= 0, "Expected FieldDeclarationNode in AST output");

        // Find the end of the class (next "public sealed class" or "public abstract class")
        var fieldNodeEnd = output.IndexOf("public sealed class", fieldNodeStart + 1);
        if(fieldNodeEnd < 0)
        {
            fieldNodeEnd = output.IndexOf("public abstract class", fieldNodeStart + 1);
        }

        if(fieldNodeEnd < 0)
        {
            fieldNodeEnd = output.Length;
        }

        var fieldNodeSection = output[fieldNodeStart..fieldNodeEnd];

        // The Initializer property should be ExpressionNode?, not Token?
        Assert.Contains("ExpressionNode?", fieldNodeSection);
        Assert.DoesNotContain("public Token? Initializer", fieldNodeSection);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_ChildrenYieldsInitializerWhenPresent()
    {
        var output = astOutput.Value;

        // Extract the FieldDeclarationNode section
        var fieldNodeStart = output.IndexOf("public sealed class FieldDeclarationNode");
        Assert.True(fieldNodeStart >= 0, "Expected FieldDeclarationNode in AST output");

        var fieldNodeEnd = output.IndexOf("public sealed class", fieldNodeStart + 1);
        if(fieldNodeEnd < 0)
        {
            fieldNodeEnd = output.IndexOf("public abstract class", fieldNodeStart + 1);
        }

        if(fieldNodeEnd < 0)
        {
            fieldNodeEnd = output.Length;
        }

        var fieldNodeSection = output[fieldNodeStart..fieldNodeEnd];

        // Children() should yield the Initializer when non-null
        Assert.Contains("if (Initializer != null)", fieldNodeSection);
        Assert.Contains("yield return Initializer", fieldNodeSection);
    }
}