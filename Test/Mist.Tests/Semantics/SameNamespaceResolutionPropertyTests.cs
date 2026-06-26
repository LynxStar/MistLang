// Feature: inferred-namespaces, Property 10: Same-Namespace Type Resolution
// For any two types in the same namespace (whether inferred or explicit), a reference
// from one type to the other by simple name shall resolve successfully without a
// using declaration. Conversely, for any two types in different namespaces, a reference
// by simple name shall fail to resolve without a using declaration for the target namespace.

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying same-namespace type resolution:
/// types in the same namespace resolve without using, types in different
/// namespaces fail without using.
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public sealed class SameNamespaceResolutionPropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidClassName(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }

    public record ValidNamespaceName(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly string[] ClassNames =
    {
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Foo", "Bar", "Baz", "Qux", "Corge"
    };

    private static readonly string[] NamespaceNames =
    {
        "NsA", "NsB", "NsC", "NsD", "NsE",
        "App.Models", "Core.Utils", "Data.IO"
    };

    public static Arbitrary<ValidClassName> ArbitraryValidClassName()
    {
        return Gen.Elements(ClassNames)
            .Select(s => new ValidClassName(s))
            .ToArbitrary();
    }

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

    /// <summary>
    /// Creates a class with a field of the given type name, which forces type resolution.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithFieldOfType(string className, string fieldTypeName)
    {
        // Use a large offset so the span doesn't accidentally overlap with source text.
        var typeSpanStart = 10000;
        var fieldDecl = new FieldDeclarationNode
        {
            Name = MakeToken("myField"),
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = MakeToken(fieldTypeName, start: typeSpanStart),
                        Span = new Storm.Runtime.SourceSpan(typeSpanStart, fieldTypeName.Length),
                    },
                }
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
            Body = body.AsNamespaceBodyItems(),
            Span = new Storm.Runtime.SourceSpan(sourceOffset > 0 ? sourceOffset - 10 : 0, nsName.Length + 20),
        };
    }

    private static (ISemanticModel model, ImmutableArray<Diagnostic> diagnostics) BuildModel(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        ImmutableArray<CompilationUnit> units)
    {
        var compilation = new MistCompilation(units);
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(
            compilation, asts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── Property 10a: Same namespace resolves without using ────────────

    /// <summary>
    /// When two types are in the same explicit namespace, a field reference from
    /// one to the other by simple name should resolve without a using declaration.
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SameNamespaceResolutionPropertyTests) }, MaxTest = 100)]
    public bool SameExplicitNamespace_ResolvesWithoutUsing(ValidNamespaceName nsName)
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace NsA; class Target { }
        var targetClass = MakeClass("Target");
        var nsNode1 = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = targetClass }),
            sourceOffset: nsOffset);

        var source1 = $"namespace {nsName.Value}; class Target {{ }}";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: namespace NsA; class Consumer { Target myField; }
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Target");
        var nsNode2 = MakeFileScopedNs(nsName.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var source2 = $"namespace {nsName.Value}; class Consumer {{ Target myField; }}";
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);
        var units = ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2));

        var (model, diagnostics) = BuildModel(
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2), units);

        // Should NOT have MIST_UNDEFINED_TYPE for "Target"
        var hasUndefinedType = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Target"));

        return !hasUndefinedType;
    }

    // ─── Property 10b: Different namespace fails without using ──────────

    /// <summary>
    /// When two types are in different namespaces, a field reference from one to
    /// the other by simple name should fail to resolve without a using declaration.
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SameNamespaceResolutionPropertyTests) }, MaxTest = 100)]
    public bool DifferentNamespace_FailsWithoutUsing(ValidNamespaceName ns1, ValidNamespaceName ns2)
    {
        // Skip if namespaces are the same — that's the same-namespace case.
        if(ns1.Value == ns2.Value)
        {
            return true;
        }

        var nsOffset = "namespace ".Length;

        // File 1: namespace ns1; class Target { }
        var targetClass = MakeClass("Target");
        var nsNode1 = MakeFileScopedNs(ns1.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = targetClass }),
            sourceOffset: nsOffset);

        var source1 = $"namespace {ns1.Value}; class Target {{ }}";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: namespace ns2; class Consumer { Target myField; }
        // No using declaration for ns1!
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Target");
        var nsNode2 = MakeFileScopedNs(ns2.Value,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var source2 = $"namespace {ns2.Value}; class Consumer {{ Target myField; }}";
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);
        var units = ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2));

        var (model, diagnostics) = BuildModel(
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2), units);

        // SHOULD have MIST_UNDEFINED_TYPE for "Target" since no using
        var hasUndefinedType = diagnostics.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Target"));

        return hasUndefinedType;
    }

    // ─── Property 10c: Same inferred namespace resolves ─────────────────

    /// <summary>
    /// When two types are in the same inferred namespace (same directory, no explicit
    /// namespace), a field reference should resolve without a using declaration.
    /// **Validates: Requirements 6.1, 6.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SameNamespaceResolutionPropertyTests) }, MaxTest = 100)]
    public bool SameInferredNamespace_ResolvesWithoutUsing(ValidClassName targetName)
    {
        // Ensure unique names
        var consumerName = targetName.Value + "Consumer";

        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var filePath1 = Path.Combine(projectFolder, "File1.mist");
        var filePath2 = Path.Combine(projectFolder, "File2.mist");

        // File 1: class Target { } (inferred namespace = TestProject)
        var targetClass = MakeClass(targetName.Value);
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = targetClass }).AsTopLevelItems(),
        };

        // File 2: class Consumer { Target myField; } (inferred namespace = TestProject)
        var consumerClass = MakeClassWithFieldOfType(consumerName, targetName.Value);
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }).AsTopLevelItems(),
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
            namespaceRoot: "TestProject",
            projectFolder: projectFolder,
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should NOT have MIST_UNDEFINED_TYPE for the target type
        var hasUndefinedType = diags.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains(targetName.Value));

        return !hasUndefinedType;
    }
}