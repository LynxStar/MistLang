// Feature: inferred-namespaces, Property 7: Block Namespace Type Association
// For any file containing block namespace declarations, only the type declarations
// enclosed within the braces of a block namespace shall be registered under that
// namespace. Types declared outside the block shall not be affected.

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
/// Property-based tests verifying that only types enclosed within a block namespace
/// are registered under that namespace, and types outside are not affected.
/// </summary>
public sealed class BlockNamespaceAssociationPropertyTests
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

    // ─── Property 7a: Types inside block namespace ──────────────────────

    /// <summary>
    /// For any block namespace declaration, types enclosed within the braces
    /// should be registered under that namespace.
    /// **Validates: Requirements 4.4, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BlockNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool BlockNamespace_EnclosedTypes_RegisteredUnderBlockNamespace(
        ValidNamespaceName nsName)
    {
        var source = $"namespace {nsName.Value} {{ class InsideClass {{ }} struct InsideStruct {{ }} }}";
        var nsOffset = "namespace ".Length;

        var classDecl = MakeClass("InsideClass");
        var structDecl = MakeStruct("InsideStruct");

        var blockNs = MakeBlockNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl },
                new BodyTypeTypeDeclarationNode { TypeBody = structDecl }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs }),
        };

        var model = BuildModel(ast, source);

        var classNs = GetContainingNamespace(model, "InsideClass");
        var structNs = GetContainingNamespace(model, "InsideStruct");

        return classNs == nsName.Value && structNs == nsName.Value;
    }

    // ─── Property 7b: Types outside block namespace use inferred ────────

    /// <summary>
    /// For any block namespace declaration, types declared outside the block
    /// (as top-level items) should be registered under the inferred namespace,
    /// not the block namespace.
    /// **Validates: Requirements 4.4, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BlockNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool BlockNamespace_OutsideTypes_NotUnderBlockNamespace(
        ValidNamespaceName nsName)
    {
        var source = $"class OutsideType {{ }} namespace {nsName.Value} {{ class InsideType {{ }} }}";
        var nsOffset = $"class OutsideType {{ }} namespace ".Length;

        var classInside = MakeClass("InsideType");
        var classOutside = MakeClass("OutsideType");

        var blockNs = MakeBlockNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classInside }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new BodyTypeTypeDeclarationNode { TypeBody = classOutside }
                },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs }),
        };

        var model = BuildModel(ast, source);

        var insideNs = GetContainingNamespace(model, "InsideType");
        var outsideNs = GetContainingNamespace(model, "OutsideType");

        // Inside should be under block namespace, outside should be under inferred (TestProject)
        return insideNs == nsName.Value && outsideNs != nsName.Value;
    }

    // ─── Property 7c: Block namespace does not change current namespace ─

    /// <summary>
    /// After a block namespace, the current namespace should remain unchanged
    /// (still the inferred namespace). Types declared after the block should
    /// be under the inferred namespace.
    /// **Validates: Requirements 4.4, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BlockNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool BlockNamespace_DoesNotChangeCurrentNamespace(
        ValidNamespaceName nsName)
    {
        var source = $"class BeforeBlock {{ }} namespace {nsName.Value} {{ class InsideBlock {{ }} }} class AfterBlock {{ }}";
        var nsOffset = $"class BeforeBlock {{ }} namespace ".Length;

        var classBefore = MakeClass("BeforeBlock");
        var classInside = MakeClass("InsideBlock");
        var classAfter = MakeClass("AfterBlock");

        var blockNs = MakeBlockNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classInside }),
            sourceOffset: nsOffset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new BodyTypeTypeDeclarationNode { TypeBody = classBefore }
                },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs },
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new BodyTypeTypeDeclarationNode { TypeBody = classAfter }
                }),
        };

        var model = BuildModel(ast, source);

        var beforeNs = GetContainingNamespace(model, "BeforeBlock");
        var insideNs = GetContainingNamespace(model, "InsideBlock");
        var afterNs = GetContainingNamespace(model, "AfterBlock");

        // Before and after should be under inferred namespace (TestProject),
        // inside should be under block namespace
        return insideNs == nsName.Value
            && beforeNs == "TestProject"
            && afterNs == "TestProject";
    }

    // ─── Property 7d: Multiple block namespaces are independent ─────────

    /// <summary>
    /// Multiple block namespaces in the same file should each independently
    /// register their enclosed types under their respective namespaces.
    /// **Validates: Requirements 4.4, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(BlockNamespaceAssociationPropertyTests) }, MaxTest = 100)]
    public bool MultipleBlockNamespaces_IndependentRegistration(
        ValidNamespaceName ns1, ValidNamespaceName ns2)
    {
        if(ns1.Value == ns2.Value)
        {
            return true; // skip trivial case
        }

        var part1 = $"namespace {ns1.Value} {{ class ClassInNs1 {{ }} }} ";
        var part2 = $"namespace {ns2.Value} {{ class ClassInNs2 {{ }} }}";
        var source = part1 + part2;

        var ns1Offset = "namespace ".Length;
        var ns2Offset = part1.Length + "namespace ".Length;

        var class1 = MakeClass("ClassInNs1");
        var class2 = MakeClass("ClassInNs2");

        var blockNs1 = MakeBlockNs(ns1.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = class1 }),
            sourceOffset: ns1Offset);

        var blockNs2 = MakeBlockNs(ns2.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = class2 }),
            sourceOffset: ns2Offset);

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs1 },
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = blockNs2 }),
        };

        var model = BuildModel(ast, source);

        var class1Ns = GetContainingNamespace(model, "ClassInNs1");
        var class2Ns = GetContainingNamespace(model, "ClassInNs2");

        return class1Ns == ns1.Value && class2Ns == ns2.Value;
    }
}