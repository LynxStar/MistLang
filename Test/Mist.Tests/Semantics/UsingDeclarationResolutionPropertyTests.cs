// Feature: inferred-namespaces, Property 11: Using Declaration Enables Cross-Namespace Resolution
// For any using declaration that names an existing namespace, all types from that
// namespace shall be resolvable by simple name in the importing file.

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
/// Property-based tests verifying that using declarations enable cross-namespace
/// type resolution by simple name.
/// **Validates: Requirements 7.1**
/// </summary>
public sealed class UsingDeclarationResolutionPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidNamespaceName(string Value)
    {
        public override string ToString() => Value;
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly string[] NamespaceNames =
    {
        "NsA", "NsB", "NsC", "NsD", "NsE",
        "App.Models", "Core.Utils", "Data.IO"
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

    private static ClassDeclarationNode MakeClass(string name)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
    }

    private static ClassDeclarationNode MakeClassWithFieldOfType(string className, string fieldTypeName)
    {
        // Use a large offset so the span doesn't accidentally overlap with source text.
        var typeSpanStart = 10000;
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("myField"),
            Type = new NamedTypeRefTypeReferenceNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken(fieldTypeName, start: typeSpanStart),
                    Span = new Storm.Runtime.SourceSpan(typeSpanStart, fieldTypeName.Length),
                },
            },
        };

        return new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode { Field = fieldDecl }),
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

    private static UsingDeclarationNode MakeUsingDecl(string nsName, int start = 6)
    {
        var firstSegment = nsName.Contains('.') ? nsName[..nsName.IndexOf('.')] : nsName;
        return new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(firstSegment, start: start),
                Span = new Storm.Runtime.SourceSpan(start, nsName.Length),
            },
        };
    }

    // ─── Property 11: Using declaration enables cross-namespace resolution ──

    /// <summary>
    /// When a file has a using declaration for a namespace containing a type,
    /// that type should be resolvable by simple name in the importing file.
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(UsingDeclarationResolutionPropertyTests) }, MaxTest = 100)]
    public bool UsingDeclaration_EnablesCrossNamespaceResolution(
        ValidNamespaceName targetNs, ValidNamespaceName consumerNs)
    {
        // Skip if namespaces are the same — that's the same-namespace case.
        if (targetNs.Value == consumerNs.Value) return true;

        var nsOffset = "namespace ".Length;

        // File 1: namespace targetNs; class Target { }
        var targetClass = MakeClass("Target");
        var nsNode1 = MakeFileScopedNs(targetNs.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = targetClass }),
            sourceOffset: nsOffset);

        var source1 = $"namespace {targetNs.Value}; class Target {{ }}";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: using targetNs; namespace consumerNs; class Consumer { Target myField; }
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Target");
        var nsNode2 = MakeFileScopedNs(consumerNs.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var usingDecl = MakeUsingDecl(targetNs.Value);

        var source2 = $"using {targetNs.Value}; namespace {consumerNs.Value}; class Consumer {{ Target myField; }}";
        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
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

        var diags = diagnostics.ToImmutableArray();

        // Should NOT have MIST_UNDEFINED_TYPE for "Target" — using declaration should resolve it
        var hasUndefinedType = diags.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Target"));

        return !hasUndefinedType;
    }
}
