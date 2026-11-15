using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;
using Storm.SourceGenerator.Validate;

namespace Mist.StormIntegration.Tests;

[Trait("Tier", "4")]
public class MistGrammarIntegrationTests
{
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

    private static Func<string, string?> FileSystemReader(string grammarDir)
    {
        return path =>
        {
            // The resolver hands over canonical forward-slash paths.
            var fsPath = path.Replace('/', Path.DirectorySeparatorChar);

            if(Path.IsPathRooted(fsPath) && File.Exists(fsPath))
            {
                return File.ReadAllText(fsPath);
            }

            var fullPath = Path.GetFullPath(Path.Combine(grammarDir, fsPath));
            if(File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }

            return null;
        };
    }

    /// <summary>
    /// Includes the trait IR, as the real generator pipeline does.
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

    private static readonly Lazy<EmissionContext> Context = new(BuildMistEmissionContext);
    private static readonly Lazy<string> AstOutput = new(() => AstEmitter.EmitAstNodes(Context.Value));

    [Fact]
    public void MistGrammar_ParameterListNode_ContainsImmutableArrayOfParameterNode()
    {
        var output = AstOutput.Value;

        Assert.Contains("ImmutableArray<ParameterNode>", output);
    }

    [Fact]
    public void MistGrammar_ParameterListNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class ParameterListNode : SyntaxNode", output);
    }

    [Fact]
    public void MistGrammar_ArgumentListNode_ContainsImmutableArrayOfExpressionNode()
    {
        var output = AstOutput.Value;

        Assert.Contains("ImmutableArray<ExpressionNode>", output);
    }

    [Fact]
    public void MistGrammar_ArgumentListNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class ArgumentListNode : SyntaxNode", output);
    }

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_ContainsImmutableArrayOfToken()
    {
        var output = AstOutput.Value;

        Assert.Contains("ImmutableArray<Token>", output);
    }

    [Fact]
    public void MistGrammar_QualifiedIdentifierNode_IsSealedClass()
    {
        var output = AstOutput.Value;

        Assert.Contains("public sealed class QualifiedIdentifierNode : SyntaxNode", output);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_ContainsExpressionNodeNullableProperty()
    {
        var output = AstOutput.Value;

        Assert.Contains("ExpressionNode?", output);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_DoesNotHaveTokenOnlyInitializer()
    {
        var output = AstOutput.Value;

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

        Assert.Contains("ExpressionNode?", fieldNodeSection);
        Assert.DoesNotContain("public Token? Initializer", fieldNodeSection);
    }

    [Fact]
    public void MistGrammar_FieldDeclarationNode_ChildrenYieldsInitializerWhenPresent()
    {
        var output = AstOutput.Value;

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

        Assert.Contains("if (Initializer != null)", fieldNodeSection);
        Assert.Contains("yield return Initializer", fieldNodeSection);
    }

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

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_NoTraitValidationErrors()
    {
        // BuildMistEmissionContext asserts there is no trait validation error.
        var context = Context.Value;
        Assert.NotNull(context);
    }

    [Fact]
    public void MistGrammar_ZeroDownstreamImpact_LoweringSucceeds()
    {
        // BuildMistEmissionContext asserts that lowering succeeded.
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

    /// <summary>
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