// Feature: inferred-namespaces, Property 13: Ambiguous Type Reference Diagnostic
// For any file that imports two or more namespaces via using declarations where those
// namespaces contain types with the same simple name, referencing that name without
// qualification shall produce an ambiguity error diagnostic.

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
/// Property-based tests verifying that importing two namespaces with same-named types
/// produces an ambiguity error when the name is referenced.
/// **Validates: Requirements 7.3**
/// </summary>
public sealed class AmbiguousTypeReferencePropertyTests
{
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidClassName(string Value)
    {
        public override string ToString() => Value;
    }

    public record NamespacePair(string Ns1, string Ns2)
    {
        public override string ToString() => $"({Ns1}, {Ns2})";
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    private static readonly string[] ClassNames =
    {
        "Widget", "Service", "Handler", "Manager", "Factory",
        "Helper", "Builder", "Runner", "Mapper", "Config"
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

    public static Arbitrary<NamespacePair> ArbitraryNamespacePair()
    {
        // Generate pairs of distinct namespace names.
        var gen = from ns1 in Gen.Elements(NamespaceNames)
                  from ns2 in Gen.Elements(NamespaceNames)
                  where ns1 != ns2
                  select new NamespacePair(ns1, ns2);
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
            Body = body.AsNamespaceBodyItems(),
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

    // ─── Property 13: Ambiguous type reference produces diagnostic ──────

    /// <summary>
    /// When two namespaces each contain a type with the same name, and both are
    /// imported via using declarations, referencing that type name should produce
    /// a MIST_AMBIGUOUS_TYPE diagnostic.
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AmbiguousTypeReferencePropertyTests) }, MaxTest = 100)]
    public bool AmbiguousType_FromTwoUsings_ProducesDiagnostic(
        NamespacePair nsPair, ValidClassName sharedTypeName)
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace ns1; class SharedType { }
        var class1 = MakeClass(sharedTypeName.Value);
        var nsNode1 = MakeFileScopedNs(nsPair.Ns1,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = class1 }),
            sourceOffset: nsOffset);

        var source1 = $"namespace {nsPair.Ns1}; class {sharedTypeName.Value} {{ }}";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: namespace ns2; class SharedType { }
        var class2 = MakeClass(sharedTypeName.Value);
        var nsNode2 = MakeFileScopedNs(nsPair.Ns2,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = class2 }),
            sourceOffset: nsOffset);

        var source2 = $"namespace {nsPair.Ns2}; class {sharedTypeName.Value} {{ }}";
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        // File 3: using ns1; using ns2; namespace ConsumerNs; class Consumer { SharedType myField; }
        // Use a consumer namespace that differs from both ns1 and ns2.
        var consumerNs = "ConsumerNs";
        var consumerClass = MakeClassWithFieldOfType("Consumer", sharedTypeName.Value);
        var nsNode3 = MakeFileScopedNs(consumerNs,
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var usingDecl1 = MakeUsingDecl(nsPair.Ns1, start: 6);
        var usingDecl2 = MakeUsingDecl(nsPair.Ns2, start: 6 + nsPair.Ns1.Length + 8);

        var source3 = $"using {nsPair.Ns1}; using {nsPair.Ns2}; namespace {consumerNs}; class Consumer {{ {sharedTypeName.Value} myField; }}";
        var ast3 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl1, usingDecl2),
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode3 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);
        var sourceText3 = new SourceText("/project/File3.mist", source3);
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2),
            new CompilationUnit(sourceText3)));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2, ast3),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should have MIST_AMBIGUOUS_TYPE diagnostic for the shared type name
        return diags.Any(d =>
            d.Id.Value == "MIST_AMBIGUOUS_TYPE" &&
            d.Message.Contains(sharedTypeName.Value));
    }
}
