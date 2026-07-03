// Feature: inferred-namespaces, Property 6: File-Scoped Namespace Type Association
// For any file containing one or more file-scoped namespace declarations, each type
// declaration following a file-scoped namespace shall be registered under that namespace
// until the next namespace declaration or end of file.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that types following a file-scoped namespace
/// declaration are registered under that namespace until the next declaration or EOF.
/// </summary>
public sealed class FileScopedNamespaceAssociationPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidNamespaceName(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly string[] NamespaceNames =
    {
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Foo.Bar", "App.Models", "Core.Utils", "Data.IO", "Net.Http"
    };

    public static Arbitrary<ValidNamespaceName> ArbitraryValidNamespaceName()
    {
        return Gen.Elements(NamespaceNames)
            .Select(s => new ValidNamespaceName(s))
            .ToArbitrary();
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

    private static FileScopedNamespaceNamespaceDeclarationNode MakeFileScopedNs(
        string nsName, ImmutableArray<TypeDeclarationNode> body, int sourceOffset = 0)
    {
        // The IDENTIFIER token gets the first segment, but the QualifiedIdentifier
        // span covers the full name. We set the span relative to sourceOffset so
        // ExtractNamespaceName can read the full name from source text.
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

    private static TypeBodyDeclarationNode MakeClass(string name)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static TypeBodyDeclarationNode MakeStruct(string name)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static ISemanticModel BuildModel(
        CompilationUnitNode ast,
        string source)
    {
        var sourceText = new SourceText("/project/File.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);
    }

    private static string? GetContainingNamespace(ISemanticModel model, string typeName)
    {
        var symbol = model.AllSymbols.FirstOrDefault(
            s => s.Name == typeName &&
                 (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct || s.Kind == SymbolKind.Abstract));
        if(symbol is null)
        {
            return null;
        }

        return symbol.DeclaredType switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            AbstractType it => it.ContainingNamespace,
            _ => null
        };
    }

    // ─── Property 6a: Single file-scoped namespace with types ───────────

    /// <summary>
    /// For any file-scoped namespace declaration followed by type declarations,
    /// all types should be registered under that namespace.
    /// **Validates: Requirements 4.3, 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(FileScopedNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool FileScopedNamespace_TypesRegisteredUnderDeclaredNamespace(ValidNamespaceName nsName)
    {
        // Build source text: "namespace <nsName>; class MyClass { } struct MyStruct { }"
        var source = $"namespace {nsName.Value}; class MyClass {{ }} struct MyStruct {{ }}";
        var nsOffset = "namespace ".Length;

        var classDecl = MakeClass("MyClass");
        var structDecl = MakeStruct("MyStruct");

        var nsNode = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl },
                new BodyTypeTypeDeclarationNode { TypeBody = structDecl }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode }),
        };

        var model = BuildModel(ast, source);

        var classNs = GetContainingNamespace(model, "MyClass");
        var structNs = GetContainingNamespace(model, "MyStruct");

        return classNs == nsName.Value && structNs == nsName.Value;
    }

    // ─── Property 6b: Second file-scoped namespace overrides first ──────

    /// <summary>
    /// When two file-scoped namespace declarations appear in sequence, types after
    /// the second declaration should be registered under the second namespace.
    /// **Validates: Requirements 4.3, 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(FileScopedNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool SecondFileScopedNamespace_OverridesFirst(
        ValidNamespaceName ns1, ValidNamespaceName ns2)
    {
        if(ns1.Value == ns2.Value)
        {
            return true; // skip trivial case
        }

        // Build source: "namespace <ns1>; class ClassA { } namespace <ns2>; class ClassB { }"
        var part1 = $"namespace {ns1.Value}; class ClassA {{ }} ";
        var part2 = $"namespace {ns2.Value}; class ClassB {{ }}";
        var source = part1 + part2;

        var ns1Offset = "namespace ".Length;
        var ns2Offset = part1.Length + "namespace ".Length;

        var classA = MakeClass("ClassA");
        var classB = MakeClass("ClassB");

        var nsNode1 = MakeFileScopedNs(ns1.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classA }),
            sourceOffset: ns1Offset);

        var nsNode2 = MakeFileScopedNs(ns2.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classB }),
            sourceOffset: ns2Offset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var model = BuildModel(ast, source);

        var classANs = GetContainingNamespace(model, "ClassA");
        var classBNs = GetContainingNamespace(model, "ClassB");

        return classANs == ns1.Value && classBNs == ns2.Value;
    }

    // ─── Property 6c: File-scoped namespace with top-level type after ───

    /// <summary>
    /// When a file-scoped namespace is followed by a top-level type item (outside
    /// the namespace body), the top-level type should be registered under the
    /// file-scoped namespace (since file-scoped updates the current namespace).
    /// **Validates: Requirements 4.3, 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(FileScopedNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool FileScopedNamespace_SubsequentTopLevelType_UnderFileScopedNamespace(
        ValidNamespaceName nsName)
    {
        // Build source: "namespace <nsName>; class InBody { } class AfterNs { }"
        var source = $"namespace {nsName.Value}; class InBody {{ }} class AfterNs {{ }}";
        var nsOffset = "namespace ".Length;

        var classInBody = MakeClass("InBody");
        var nsNode = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classInBody }),
            sourceOffset: nsOffset);

        var classAfter = MakeClass("AfterNs");

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new BodyTypeTypeDeclarationNode { TypeBody = classAfter }
                }),
        };

        var model = BuildModel(ast, source);

        var inBodyNs = GetContainingNamespace(model, "InBody");
        var afterNs = GetContainingNamespace(model, "AfterNs");

        // Both should be under the file-scoped namespace since it updates current namespace
        return inBodyNs == nsName.Value && afterNs == nsName.Value;
    }
}