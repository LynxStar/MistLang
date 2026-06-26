// Feature: inferred-namespaces, Property 5: Multi-Namespace Parsing
// For any syntactically valid Mist source file containing zero or more namespace
// declarations (file-scoped, block, or mixed) interleaved with type declarations,
// the parser SHALL produce a valid AST without errors.
//
// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**

using FsCheck;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for multi-namespace parsing.
/// Verifies that the parser correctly handles files with 0–3 namespace declarations
/// of mixed styles (file-scoped and block) interleaved with type declarations.
/// </summary>
public class MultiNamespaceParsingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    // ─── Property 5a: Random multi-namespace source files parse without errors ──

    /// <summary>
    /// Property 5: Multi-Namespace Parsing — random multi-namespace source files.
    /// For any syntactically valid Mist source file with 0+ namespace declarations
    /// interleaved with type declarations, the parser produces a valid AST without errors.
    /// **Validates: Requirements 4.1, 4.2, 4.5, 4.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool MultiNamespaceSource_ParsesWithoutErrors(
        MultiNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        // Parser should return a non-null result with a CompilationUnitNode root
        if(result.Root is not CompilationUnitNode)
            return false;

        // No parse errors should be reported
        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return parseErrors.Count == 0;
    }

    // ─── Property 5b: Mixed file-scoped and block namespaces parse without errors ──

    /// <summary>
    /// Property 5: Multi-Namespace Parsing — mixed file-scoped and block namespaces.
    /// **Validates: Requirements 4.1, 4.2, 4.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool MixedNamespaces_ParsesWithoutErrors(
        MixedNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        if(result.Root is not CompilationUnitNode)
            return false;

        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return parseErrors.Count == 0;
    }

    // ─── Property 5c: AST items are valid TopLevelItem types ──

    /// <summary>
    /// Property 5: Multi-Namespace Parsing — AST structure validation.
    /// Each TopLevelItem in the parsed AST is either a NamespaceItem or TypeItem.
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistMultiNamespaceGenerator) }, MaxTest = 100)]
    public bool ParsedItems_AreValidTopLevelItems(
        MultiNamespaceSource input)
    {
        var result = ParseSource(input.Source);

        if(result.Root is not CompilationUnitNode cu)
            return true; // vacuously true if parse fails

        foreach(var item in cu.Items)
        {
            if(item is not (NamespaceItemTopLevelItemNode or TypeItemTopLevelItemNode))
                return false;
        }

        return true;
    }

    // ─── Unit tests for specific edge cases ──

    /// <summary>
    /// Empty source file parses without errors (Requirement 4.6).
    /// </summary>
    [Fact]
    public void EmptySource_ParsesWithoutErrors()
    {
        var result = ParseSource("");
        Assert.NotNull(result);
        Assert.NotNull(result.Root);
        Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.Empty(cu.Items);
        Assert.Empty(cu.Usings);
    }

    /// <summary>
    /// Single file-scoped namespace with a class parses correctly.
    /// </summary>
    [Fact]
    public void SingleFileScopedNamespace_ParsesCorrectly()
    {
        var source = "namespace MyApp;\nclass Program { }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        // Should have namespace item followed by type item (file-scoped namespace body
        // contains the class, so it may be 1 item or 2 depending on grammar)
        Assert.True(cu.Items.Length >= 1);
    }

    /// <summary>
    /// Single block namespace with a class parses correctly.
    /// </summary>
    [Fact]
    public void SingleBlockNamespace_ParsesCorrectly()
    {
        var source = "namespace MyApp {\n    class Program { }\n}";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.True(cu.Items.Length >= 1);
    }

    /// <summary>
    /// Multiple namespaces in one file parse correctly (Requirement 4.1).
    /// After the nested-scoping grammar change, a file-scoped namespace followed by
    /// a block namespace results in the block namespace being nested inside the
    /// file-scoped namespace's body (NamespaceNamespaceItem). So the top-level
    /// has only 1 item: the file-scoped namespace containing everything.
    /// </summary>
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
        Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        // The file-scoped namespace consumes all subsequent items as its body,
        // including the block namespace (now a NamespaceNamespaceItem).
        Assert.True(cu.Items.Length >= 1,
            $"Expected at least 1 item (file-scoped namespace), got {cu.Items.Length}");

        // Verify the file-scoped namespace contains the expected body items
        var nsItem = Assert.IsType<NamespaceItemTopLevelItemNode>(cu.Items[0]);
        var fileScopedNs = Assert.IsType<FileScopedNamespaceNamespaceDeclarationNode>(nsItem.NamespaceDeclaration);
        // Body should contain: class A, namespace Second { class B }, class C
        Assert.Equal(3, fileScopedNs.Body.Length);
    }

    /// <summary>
    /// Types-only file (no namespaces) parses correctly (Requirement 4.6).
    /// </summary>
    [Fact]
    public void TypesOnly_ParsesCorrectly()
    {
        var source = "class A { }\nstruct B { }\nabstract IC { }";
        var result = ParseSource(source);

        Assert.NotNull(result.Root);
        Assert.IsType<CompilationUnitNode>(result.Root);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var cu = (CompilationUnitNode)result.Root;
        Assert.Equal(3, cu.Items.Length);
        Assert.All(cu.Items, item => Assert.IsType<TypeItemTopLevelItemNode>(item));
    }
}