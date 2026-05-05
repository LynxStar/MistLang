// Feature: inferred-namespaces, Property 8: Top-Level Types Default to Inferred Namespace
// For any file containing both explicit namespace declarations and top-level type
// declarations outside any namespace, the top-level types shall be registered under
// the file's inferred namespace.

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
/// Property-based tests verifying that top-level types outside any explicit namespace
/// are registered under the inferred namespace for the file.
/// </summary>
public sealed class TopLevelTypeInferredNamespacePropertyTests
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

#pragma warning disable CA1819
    public record FolderSegments(string[] Segments)
    {
        public override string ToString() => string.Join("/", Segments);
    }
#pragma warning restore CA1819
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

    private static readonly char[] FolderChars =
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

    public static Arbitrary<FolderSegments> ArbitraryFolderSegments()
    {
        var segmentGen = from len in Gen.Choose(1, 10)
                         from chars in Gen.ArrayOf(Gen.Elements(FolderChars), len)
                         select new string(chars);

        var gen = from count in Gen.Choose(0, 3)
                  from segments in Gen.ArrayOf(segmentGen, count)
                  select new FolderSegments(segments);
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
            Body = body.AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(sourceOffset > 0 ? sourceOffset - 10 : 0, nsName.Length + 20),
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
            Body = body.AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(sourceOffset > 0 ? sourceOffset - 10 : 0, nsName.Length + 20),
        };
    }

    private static ISemanticModel BuildModelForFile(
        CompilationUnitNode ast,
        string namespaceRoot,
        string projectFolder,
        string filePath,
        string source = "")
    {
        var sourceText = new SourceText(filePath, source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: namespaceRoot,
            projectFolder: projectFolder,
            diagnostics);
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

    // ─── Property 8a: Top-level type with block namespace ───────────────

    /// <summary>
    /// When a file has a block namespace and a top-level type outside it,
    /// the top-level type should be under the inferred namespace.
    /// **Validates: Requirements 5.4, 9.1, 9.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TopLevelTypeInferredNamespacePropertyTests) }, MaxTest = 100)]
    public bool TopLevelType_WithBlockNamespace_UnderInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidNamespaceName explicitNs, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedInferred = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var source = $"class TopLevel {{ }} namespace {explicitNs.Value} {{ class InBlock {{ }} }}";
        var nsOffset = $"class TopLevel {{ }} namespace ".Length;

        var classInBlock = MakeClass("InBlock");
        var classTopLevel = MakeClass("TopLevel");

        var blockNs = MakeBlockNs(explicitNs.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classInBlock }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classTopLevel }
                },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs }),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath, source);

        var topLevelNs = GetContainingNamespace(model, "TopLevel");
        var inBlockNs = GetContainingNamespace(model, "InBlock");

        return topLevelNs == expectedInferred && inBlockNs == explicitNs.Value;
    }

    // ─── Property 8b: Top-level type before file-scoped namespace ───────

    /// <summary>
    /// When a top-level type appears before a file-scoped namespace declaration,
    /// it should be under the inferred namespace (since the file-scoped namespace
    /// hasn't been declared yet).
    /// **Validates: Requirements 5.4, 9.1, 9.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TopLevelTypeInferredNamespacePropertyTests) }, MaxTest = 100)]
    public bool TopLevelType_BeforeFileScopedNamespace_UnderInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidNamespaceName explicitNs, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedInferred = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var source = $"class TopLevel {{ }} namespace {explicitNs.Value}; class InNamespace {{ }}";
        var nsOffset = $"class TopLevel {{ }} namespace ".Length;

        var classTopLevel = MakeClass("TopLevel");
        var classInNs = MakeClass("InNamespace");

        var fileScopedNs = MakeFileScopedNs(explicitNs.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classInNs }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classTopLevel }
                },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = fileScopedNs }),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath, source);

        var topLevelNs = GetContainingNamespace(model, "TopLevel");
        var inNsNs = GetContainingNamespace(model, "InNamespace");

        return topLevelNs == expectedInferred && inNsNs == explicitNs.Value;
    }

    // ─── Property 8c: Top-level type after block namespace ──────────────

    /// <summary>
    /// When a top-level type appears after a block namespace, it should still
    /// be under the inferred namespace (block namespaces don't change current namespace).
    /// **Validates: Requirements 5.4, 9.1, 9.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TopLevelTypeInferredNamespacePropertyTests) }, MaxTest = 100)]
    public bool TopLevelType_AfterBlockNamespace_UnderInferredNamespace(
        ValidNamespaceRoot nsRoot, ValidNamespaceName explicitNs, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = folders.Segments.Length > 0
            ? Path.Combine(folders.Segments)
            : "";
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var expectedInferred = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var source = $"namespace {explicitNs.Value} {{ class InBlock {{ }} }} class AfterBlock {{ }}";
        var nsOffset = "namespace ".Length;

        var classInBlock = MakeClass("InBlock");
        var classAfterBlock = MakeClass("AfterBlock");

        var blockNs = MakeBlockNs(explicitNs.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classInBlock }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classAfterBlock }
                }),
        };

        var model = BuildModelForFile(ast, nsRoot.Value, projectFolder, filePath, source);

        var inBlockNs = GetContainingNamespace(model, "InBlock");
        var afterBlockNs = GetContainingNamespace(model, "AfterBlock");

        return inBlockNs == explicitNs.Value && afterBlockNs == expectedInferred;
    }
}
