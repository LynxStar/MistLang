// Feature: inferred-namespaces, Property 9: Cross-File Namespace Scope Merging
// For any set of source files where two or more files declare the same namespace name
// (whether explicitly or via inference), all types from those files shall be registered
// in the same namespace scope.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that types from multiple files declaring the same
/// namespace (explicitly or via inference) are merged into the same namespace scope.
/// </summary>
public sealed class NamespaceMergingPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidNamespaceName(string Value)
    {
        public override string ToString() => Value;
    }

    public record ValidNamespaceRoot(string Value)
    {
        public override string ToString() => Value;
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly string[] NamespaceNames =
    {
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Foo.Bar", "App.Models", "Core.Utils", "Data.IO", "Net.Http"
    };

    private static readonly char[] IdentifierStartChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();

    private static readonly char[] IdentifierChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static Arbitrary<ValidNamespaceName> ArbitraryValidNamespaceName()
    {
        return Gen.Elements(NamespaceNames)
            .Select(s => new ValidNamespaceName(s))
            .ToArbitrary();
    }

    public static Arbitrary<ValidNamespaceRoot> ArbitraryValidNamespaceRoot()
    {
        var gen = from first in Gen.Elements(IdentifierStartChars)
                  from restLen in Gen.Choose(1, 15)
                  from rest in Gen.ArrayOf(Gen.Elements(IdentifierChars), restLen)
                  select new ValidNamespaceRoot(first + new string(rest));
        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static ClassDeclarationNode MakeClass(string name)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static FileScopedNamespaceNamespaceDeclarationNode MakeFileScopedNs(
        string nsName, ImmutableArray<TypeDeclarationNode> body, int sourceOffset = 0)
    {
        var firstSegment = nsName.Contains('.') ? nsName[..nsName.IndexOf('.')] : nsName;
        return new FileScopedNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(firstSegment, start: sourceOffset),
                Span = new Storm.Runtime.SourceSpan(sourceOffset, nsName.Length),
            },
            Body = body,
            Span = new Storm.Runtime.SourceSpan(sourceOffset > 0 ? sourceOffset - 10 : 0, nsName.Length + 20),
        };
    }

    private static BlockNamespaceNamespaceDeclarationNode MakeBlockNs(
        string nsName, ImmutableArray<TypeDeclarationNode> body, int sourceOffset = 0)
    {
        var firstSegment = nsName.Contains('.') ? nsName[..nsName.IndexOf('.')] : nsName;
        return new BlockNamespaceNamespaceDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(firstSegment, start: sourceOffset),
                Span = new Storm.Runtime.SourceSpan(sourceOffset, nsName.Length),
            },
            Body = body,
            Span = new Storm.Runtime.SourceSpan(sourceOffset > 0 ? sourceOffset - 10 : 0, nsName.Length + 20),
        };
    }

    private static string? GetContainingNamespace(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(
            s => s.Name == typeName &&
                 (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct || s.Kind == SymbolKind.Interface));
        if (symbol is null) return null;
        return symbol.DeclaredType switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            InterfaceType it => it.ContainingNamespace,
            _ => null
        };
    }

    // ─── Property 9a: Two files with same explicit namespace ────────────

    /// <summary>
    /// When two files declare the same namespace explicitly, all types from both
    /// files should be registered under the same namespace.
    /// **Validates: Requirements 5.1, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceMergingPropertyTests) }, MaxTest = 100)]
    public bool TwoFiles_SameExplicitNamespace_TypesMerged(ValidNamespaceName nsName)
    {
        var source1 = $"namespace {nsName.Value}; class ClassA {{ }}";
        var source2 = $"namespace {nsName.Value}; class ClassB {{ }}";
        var nsOffset = "namespace ".Length;

        var classA = MakeClass("ClassA");
        var classB = MakeClass("ClassB");

        var nsNode1 = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA }),
            sourceOffset: nsOffset);

        var nsNode2 = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }),
            sourceOffset: nsOffset);

        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2)));
        var diagnostics = new DiagnosticBag();

        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var classANs = GetContainingNamespace(model, "ClassA");
        var classBNs = GetContainingNamespace(model, "ClassB");

        return classANs == nsName.Value && classBNs == nsName.Value;
    }

    // ─── Property 9b: Two files with same inferred namespace ────────────

    /// <summary>
    /// When two files in the same directory have no explicit namespace, all types
    /// from both files should be registered under the same inferred namespace.
    /// **Validates: Requirements 5.1, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceMergingPropertyTests) }, MaxTest = 100)]
    public bool TwoFiles_SameDirectory_SameInferredNamespace(ValidNamespaceRoot nsRoot)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var filePath1 = Path.Combine(projectFolder, "File1.mist");
        var filePath2 = Path.Combine(projectFolder, "File2.mist");

        var expectedNs = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath1);

        var classA = MakeClass("ClassA");
        var classB = MakeClass("ClassB");

        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA }).AsTopLevelItems(),
        };

        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }).AsTopLevelItems(),
        };

        var sourceText1 = new SourceText(filePath1, "");
        var sourceText2 = new SourceText(filePath2, "");
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2)));
        var diagnostics = new DiagnosticBag();

        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: nsRoot.Value,
            projectFolder: projectFolder,
            diagnostics);

        var classANs = GetContainingNamespace(model, "ClassA");
        var classBNs = GetContainingNamespace(model, "ClassB");

        return classANs == expectedNs && classBNs == expectedNs;
    }

    // ─── Property 9c: Explicit and inferred same namespace merge ────────

    /// <summary>
    /// When one file uses an explicit namespace and another file's inferred namespace
    /// matches, types from both should be in the same namespace scope.
    /// **Validates: Requirements 5.1, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceMergingPropertyTests) }, MaxTest = 100)]
    public bool ExplicitAndInferred_SameNamespace_TypesMerged(ValidNamespaceRoot nsRoot)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var filePath1 = Path.Combine(projectFolder, "File1.mist");
        var filePath2 = Path.Combine(projectFolder, "File2.mist");

        var source1 = $"namespace {nsRoot.Value}; class ClassExplicit {{ }}";
        var nsOffset = "namespace ".Length;

        // File 1: explicit namespace matching the namespace root
        var classA = MakeClass("ClassExplicit");
        var nsNode = MakeFileScopedNs(nsRoot.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA }),
            sourceOffset: nsOffset);

        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        // File 2: no namespace (inferred = nsRoot since at project root)
        var classB = MakeClass("ClassInferred");
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }).AsTopLevelItems(),
        };

        var sourceText1 = new SourceText(filePath1, source1);
        var sourceText2 = new SourceText(filePath2, "");
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2)));
        var diagnostics = new DiagnosticBag();

        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: nsRoot.Value,
            projectFolder: projectFolder,
            diagnostics);

        var classANs = GetContainingNamespace(model, "ClassExplicit");
        var classBNs = GetContainingNamespace(model, "ClassInferred");

        // Both should be under the same namespace (nsRoot)
        return classANs == nsRoot.Value && classBNs == nsRoot.Value;
    }

    // ─── Property 9d: Block namespace merges across files ───────────────

    /// <summary>
    /// When two files declare the same namespace using block syntax, types from
    /// both blocks should be registered under the same namespace.
    /// **Validates: Requirements 5.1, 5.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceMergingPropertyTests) }, MaxTest = 100)]
    public bool TwoFiles_SameBlockNamespace_TypesMerged(ValidNamespaceName nsName)
    {
        var source1 = $"namespace {nsName.Value} {{ class BlockClassA {{ }} }}";
        var source2 = $"namespace {nsName.Value} {{ class BlockClassB {{ }} }}";
        var nsOffset = "namespace ".Length;

        var classA = MakeClass("BlockClassA");
        var classB = MakeClass("BlockClassB");

        var blockNs1 = MakeBlockNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA }),
            sourceOffset: nsOffset);

        var blockNs2 = MakeBlockNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }),
            sourceOffset: nsOffset);

        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs1 }),
        };

        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2)));
        var diagnostics = new DiagnosticBag();

        var model = Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var classANs = GetContainingNamespace(model, "BlockClassA");
        var classBNs = GetContainingNamespace(model, "BlockClassB");

        return classANs == nsName.Value && classBNs == nsName.Value;
    }
}
