// Feature: inferred-namespaces, Property 5: Multi-Namespace Parsing
// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**

using FsCheck;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

public class MultiNamespaceParsingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool MultiNamespaceSource_ParsesWithoutErrors(
        MultiNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        if(result.Root is not CompilationUnitNode)
        {
            return false;
        }

        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return parseErrors.Count == 0;
    }

    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool MixedNamespaces_ParsesWithoutErrors(
        MixedNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        if(result.Root is not CompilationUnitNode)
        {
            return false;
        }

        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return parseErrors.Count == 0;
    }

    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool ParsedItems_AreValidTopLevelItems(
        MultiNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        if(result.Root is not CompilationUnitNode cu)
        {
            return true; // vacuously true if parse fails
        }

        foreach(var item in cu.Items)
        {
            if(item is not (NamespaceItemTopLevelItemNode or TypeItemTopLevelItemNode))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void EmptySource_ParsesWithoutErrors()
    {
        var result = ParseSource("");
        Assert.NotNull(result);
        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.Empty(cu.Items);
        Assert.Empty(cu.Usings);
    }

    [Fact]
    public void SingleFileScopedNamespace_ParsesCorrectly()
    {
        var source = "namespace MyApp;\nclass Program { }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.True(cu.Items.Length >= 1);
    }

    [Fact]
    public void SingleBlockNamespace_ParsesCorrectly()
    {
        var source = "namespace MyApp {\n    class Program { }\n}";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.True(cu.Items.Length >= 1);
    }

    [Fact]
    public void MultipleNamespaces_ParseCorrectly()
    {
        var source = @"
namespace First;
class A { }

namespace Second {
    class B { }
}

class C { }
";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        // The file-scoped namespace consumes all subsequent items as its body,
        // including the block namespace.
        Assert.True(cu.Items.Length >= 1,
            $"Expected at least 1 item (file-scoped namespace), got {cu.Items.Length}");

        var nsItem = Assert.IsType<NamespaceItemTopLevelItemNode>(cu.Items[0]);
        var fileScopedNs = Assert.IsType<FileScopedNamespaceNamespaceDeclarationNode>(nsItem.NamespaceDeclaration);
        Assert.Equal(3, fileScopedNs.Body.Length);
    }

    [Fact]
    public void TypesOnly_ParsesCorrectly()
    {
        var source = "class A { }\nstruct B { }\nabstract IC { }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        _ = Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.Equal(3, cu.Items.Length);
        Assert.All(cu.Items, item => Assert.IsType<TypeItemTopLevelItemNode>(item));
    }
}