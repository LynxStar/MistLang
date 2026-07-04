using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;
using Storm.SourceGenerator.Validate;

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
    /// and returns the EmissionContext (including trait IR, mirroring the real pipeline).
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

        var traitValidationDiags = TraitValidationPass.Run(stormGrammar);
        Assert.DoesNotContain(traitValidationDiags,
            d => d.Severity == StormDiagnosticSeverity.Error);

        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded,
            "Mist grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        var traitBuildResult = TraitIRBuilder.Build(stormGrammar);

        return new EmissionContext(
            "Mist.Generated", "Mist", loweringResult.Grammar!,
            traits: traitBuildResult.Traits,
            ruleTraitBindings: traitBuildResult.RuleTraitBindings);
    }

    // Cache the Context and AST output across tests in this class
    private static readonly Lazy<EmissionContext> Context = new(BuildMistEmissionContext);
    private static readonly Lazy<string> AstOutput = new(() => AstEmitter.EmitAstNodes(Context.Value));

    // ---------------------------------------------------------------
    //  7.1 — ParameterListNode has ImmutableArray<ParameterNode>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_ParameterListNode_ContainsImmutableArrayOfParameterNode()
    {
        var output = AstOutput.Value;

        // ParameterList rule: Parameter (COMMA Parameter)*
        // Should generate a list property for repeated Parameters
        Assert.Contains("ImmutableArray<ParameterNode>", output);
    }

    [Fact]
    public void MistGrammar_ParameterListNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class ParameterListNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.2 — ArgumentListNode has ImmutableArray<ExpressionNode>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_ArgumentListNode_ContainsImmutableArrayOfExpressionNode()
    {
        var output = AstOutput.Value;

        // ArgumentList rule: Expression (COMMA Expression)*
        // Should generate a list property for repeated Expressions
        Assert.Contains("ImmutableArray<ExpressionNode>", output);
    }

    [Fact]
    public void MistGrammar_ArgumentListNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class ArgumentListNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.3 — QualifiedIdentifierNode has ImmutableArray<Token>
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_ContainsImmutableArrayOfToken()
    {
        var output = AstOutput.Value;

        // QualifiedIdentifier rule: IDENTIFIER (DOT IDENTIFIER)*
        // Should generate a list property for repeated IDENTIFIER tokens
        Assert.Contains("ImmutableArray<Token>", output);
    }

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class QualifiedIdentifierNode : SyntaxNode", output);
    }

    // ---------------------------------------------------------------
    //  7.4 — FieldDeclarationNode preserves ExpressionNode from
    //         the optional initializer group
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_FieldDeclarationNode_ContainsExpressionNodeNullableProperty()
    {
        var output = AstOutput.Value;

        // FieldDeclaration rule: ... initializer:(EQUAL Expression)? SEMICOLON
        // The labeled optional (EQUAL Expression)? should produce an ExpressionNode? property
        Assert.Contains("ExpressionNode?", output);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_DoesNotHaveTokenOnlyInitializer()
    {
        var output = AstOutput.Value;

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
        var output = AstOutput.Value;

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

    // ---------------------------------------------------------------
    //  11.2 — Delegate/FullDelegate trait interface emission
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_IHasAttributesNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IHasAttributesNode", output);
    }

    [Fact]
    public void MistGrammar_IHasAttributesNode_HasAttributesProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IHasAttributesNode");
        Assert.Contains("ImmutableArray<AttributeListNode> Attributes { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_IHasModifiersNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IHasModifiersNode", output);
    }

    [Fact]
    public void MistGrammar_IHasModifiersNode_HasModifiersProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IHasModifiersNode");
        Assert.Contains("ImmutableArray<ModifierNode> Modifiers { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_IHasParametersNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IHasParametersNode", output);
    }

    [Fact]
    public void MistGrammar_IHasParametersNode_HasParametersProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IHasParametersNode");
        Assert.Contains("ParameterListNode? Parameters { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_IHasBodyNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IHasBodyNode", output);
    }

    [Fact]
    public void MistGrammar_IHasBodyNode_HasBodyProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IHasBodyNode");
        Assert.Contains("CallableBodyNode Body { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_IDelegateNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IDelegateNode", output);
    }

    [Fact]
    public void MistGrammar_IDelegateNode_ExtendsAllFourTraits()
    {
        var output = AstOutput.Value;

        var marker = "public interface IDelegateNode";
        var start = output.IndexOf(marker);
        Assert.True(start >= 0, "Expected IDelegateNode in AST output");
        var lineEnd = output.IndexOf('\n', start);
        var line = output[start..lineEnd];

        Assert.Contains("IHasAttributesNode", line);
        Assert.Contains("IHasModifiersNode", line);
        Assert.Contains("IHasParametersNode", line);
        Assert.Contains("IHasBodyNode", line);
    }

    [Fact]
    public void MistGrammar_IFullDelegateNode_InterfaceIsEmitted()
    {
        var output = AstOutput.Value;

        Assert.Contains("public interface IFullDelegateNode", output);
    }

    [Fact]
    public void MistGrammar_IFullDelegateNode_ExtendsDelegateNode()
    {
        var output = AstOutput.Value;

        var marker = "public interface IFullDelegateNode";
        var start = output.IndexOf(marker);
        Assert.True(start >= 0, "Expected IFullDelegateNode in AST output");
        var lineEnd = output.IndexOf('\n', start);
        var line = output[start..lineEnd];

        Assert.Contains("IDelegateNode", line);
    }

    [Fact]
    public void MistGrammar_IFullDelegateNode_HasWithClausesProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IFullDelegateNode");
        Assert.Contains("ImmutableArray<WithClauseNode> WithClauses { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_IFullDelegateNode_HasConstraintsProperty()
    {
        var output = AstOutput.Value;

        var interfaceSection = ExtractInterfaceSection(output, "IFullDelegateNode");
        Assert.Contains("ImmutableArray<WhereClauseNode> Constraints { get; }", interfaceSection);
    }

    [Fact]
    public void MistGrammar_ConstructorDeclarationNode_ImplementsIFullDelegateNode()
    {
        var output = AstOutput.Value;

        var classDecl = ExtractClassDeclarationLine(output, "ConstructorDeclarationNode");
        Assert.Contains("IFullDelegateNode", classDecl);
    }

    [Fact]
    public void MistGrammar_MethodDeclarationNode_ImplementsIFullDelegateNode()
    {
        var output = AstOutput.Value;

        var classDecl = ExtractClassDeclarationLine(output, "MethodDeclarationNode");
        Assert.Contains("IFullDelegateNode", classDecl);
    }

    [Fact]
    public void MistGrammar_OperatorDeclarationNode_ImplementsIDelegateNode()
    {
        var output = AstOutput.Value;

        var classDecl = ExtractClassDeclarationLine(output, "OperatorDeclarationNode");
        Assert.Contains("IDelegateNode", classDecl);
    }

    [Fact]
    public void MistGrammar_CastOperatorDeclarationNode_ImplementsIDelegateNode()
    {
        var output = AstOutput.Value;

        var classDecl = ExtractClassDeclarationLine(output, "CastOperatorDeclarationNode");
        Assert.Contains("IDelegateNode", classDecl);
    }

    // ---------------------------------------------------------------
    //  12.2 — Property 9: Zero downstream impact
    //
    //  **Validates: Requirement 14**
    //
    //  The Mist grammar, when processed through the Storm source
    //  generator pipeline with traits enabled, SHALL continue to
    //  produce a valid GrammarIR with zero trait validation errors
    //  and emit parseable C# source code.
    // ---------------------------------------------------------------

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_NoTraitValidationErrors()
    {
        // BuildMistEmissionContext() asserts zero trait validation errors internally.
        // If it throws, the pipeline has trait validation errors.
        var context = Context.Value;
        Assert.NotNull(context);
    }

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_LoweringSucceeds()
    {
        // BuildMistEmissionContext() asserts lowering succeeded internally.
        // Verify the lowered grammar is present and has rules.
        var context = Context.Value;
        Assert.NotNull(context.Grammar);
        Assert.True(context.Grammar.Rules.Count > 0,
            "Expected lowered GrammarIR to contain at least one rule.");
    }

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_EmittedAstIsNonEmpty()
    {
        var output = AstOutput.Value;
        Assert.False(string.IsNullOrWhiteSpace(output),
            "Expected emitted AST source to be non-empty.");
    }

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_EmittedAstContainsKnownNodeTypes()
    {
        var output = AstOutput.Value;

        // Verify known node types that must always be present in the Mist grammar output
        Assert.Contains("IDelegateNode", output);
        Assert.Contains("IFullDelegateNode", output);
        Assert.Contains("MethodDeclarationNode", output);
        Assert.Contains("ConstructorDeclarationNode", output);
        Assert.Contains("ExpressionNode", output);
        Assert.Contains("StatementNode", output);
    }

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_EmittedAstIsParseableCSharp()
    {
        var output = AstOutput.Value;

        var syntaxTree = CSharpSyntaxTree.ParseText(output);
        var diagnostics = syntaxTree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(diagnostics.Count == 0,
            "Emitted AST source has C# parse errors:\n" +
            string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    // ---------------------------------------------------------------
    //  Helpers for trait integration tests
    // ---------------------------------------------------------------

    /// <summary>
    /// Extracts the body section of an interface from the emitted output.
    /// Tracks brace nesting to handle <c>{ get; }</c> inside properties.
    /// </summary>
    private static string ExtractInterfaceSection(string output, string interfaceName)
    {
        var marker = $"public interface {interfaceName}";
        var start = output.IndexOf(marker);
        Assert.True(start >= 0, $"Expected {interfaceName} in AST output");

        var braceStart = output.IndexOf('{', start);
        Assert.True(braceStart >= 0, $"Expected opening brace for {interfaceName}");

        var depth = 1;
        var i = braceStart + 1;
        while(i < output.Length && depth > 0)
        {
            if(output[i] == '{')
            {
                depth++;
            }
            else if(output[i] == '}')
            {
                depth--;
            }

            i++;
        }

        return output[braceStart..i];
    }

    /// <summary>
    /// Extracts the class declaration line (up to the opening brace) for a given class name.
    /// </summary>
    private static string ExtractClassDeclarationLine(string output, string className)
    {
        var marker = $"public sealed class {className}";
        var start = output.IndexOf(marker);
        Assert.True(start >= 0, $"Expected {className} in AST output");

        var lineEnd = output.IndexOf('\n', start);
        if(lineEnd < 0)
        {
            lineEnd = output.Length;
        }

        return output[start..lineEnd];
    }
}