// Unit tests for SemanticModelBuilder global usings resolution.
// Validates: Requirements 4.1–4.5

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests verifying that the <see cref="SemanticModelBuilder"/> correctly
/// resolves global usings during semantic model construction:
/// - Types from global using namespaces resolve by simple name.
/// - Redundant explicit using produces no diagnostic.
/// - Undefined global using namespace produces MIST_GLOBAL_USING_UNDEFINED_NAMESPACE warning.
/// - Ambiguous types from multiple global usings produce MIST_AMBIGUOUS_TYPE error.
/// - Ambiguity between global using and explicit using produces MIST_AMBIGUOUS_TYPE error.
///
/// Validates: Requirements 4.1–4.5.
/// </summary>
public sealed class SemanticModelBuilderGlobalUsingsTests
{
    private static readonly SemanticModelBuilder Builder = new();

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

    // ─── Test 1: Types from global using namespaces resolve by simple name ──

    /// <summary>
    /// When a namespace is listed as a global using on a compilation unit,
    /// types from that namespace should be resolvable by simple name without
    /// an explicit using declaration.
    /// Validates: Requirement 4.1
    /// </summary>
    [Fact]
    public void GlobalUsing_TypeResolvesbySimpleName()
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace Foo; public class Bar { }
        var barClass = MakeClass("Bar");
        var nsNode1 = MakeFileScopedNs("Foo",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = barClass }),
            sourceOffset: nsOffset);

        var source1 = "namespace Foo; public class Bar { }";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: namespace Consumer; class Consumer { Bar myField; }
        // No explicit using — relies on global using for "Foo"
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Bar");
        var nsNode2 = MakeFileScopedNs("Consumer",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var source2 = "namespace Consumer; class Consumer { Bar myField; }";
        var ast2 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);

        // File 2 has global using "Foo"
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2) { GlobalUsings = ImmutableArray.Create("Foo") }));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should NOT have MIST_UNDEFINED_TYPE for "Bar" — global using should resolve it
        var hasUndefinedType = diags.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Bar"));

        Assert.False(hasUndefinedType,
            "Type 'Bar' from global using namespace 'Foo' should resolve by simple name. " +
            $"Diagnostics: [{string.Join(", ", diags.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ─── Test 2: Redundant explicit using produces no diagnostic ─────────

    /// <summary>
    /// When a source file contains an explicit using declaration for a namespace
    /// that is also a global using, no diagnostic should be produced for the
    /// redundancy.
    /// Validates: Requirement 4.2
    /// </summary>
    [Fact]
    public void GlobalUsing_RedundantExplicitUsing_NoDiagnostic()
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace Foo; public class Bar { }
        var barClass = MakeClass("Bar");
        var nsNode1 = MakeFileScopedNs("Foo",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = barClass }),
            sourceOffset: nsOffset);

        var source1 = "namespace Foo; public class Bar { }";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        // File 2: using Foo; namespace Consumer; class Consumer { Bar myField; }
        // Has BOTH explicit using AND global using for "Foo"
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Bar");
        var nsNode2 = MakeFileScopedNs("Consumer",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var usingDecl = MakeUsingDecl("Foo");

        var source2 = "using Foo; namespace Consumer; class Consumer { Bar myField; }";
        var ast2 = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode2 }),
        };

        var sourceText1 = new SourceText("/project/File1.mist", source1);
        var sourceText2 = new SourceText("/project/File2.mist", source2);

        // File 2 has global using "Foo" AND explicit using "Foo"
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1),
            new CompilationUnit(sourceText2) { GlobalUsings = ImmutableArray.Create("Foo") }));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1, ast2),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should NOT have any redundancy-related diagnostic
        var hasRedundancyDiag = diags.Any(d =>
            d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase) ||
            d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

        Assert.False(hasRedundancyDiag,
            "Redundant explicit using for a global using namespace should not produce a diagnostic. " +
            $"Diagnostics: [{string.Join(", ", diags.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        // Type should still resolve correctly
        var hasUndefinedType = diags.Any(d =>
            d.Id.Value == "MIST_UNDEFINED_TYPE" && d.Message.Contains("Bar"));

        Assert.False(hasUndefinedType,
            "Type 'Bar' should still resolve when both global and explicit using are present.");
    }

    // ─── Test 3: Undefined global using namespace produces warning ────────

    /// <summary>
    /// When a global using namespace does not exist in the semantic model,
    /// the builder should produce a MIST_GLOBAL_USING_UNDEFINED_NAMESPACE
    /// warning diagnostic.
    /// Validates: Requirement 4.3
    /// </summary>
    [Fact]
    public void GlobalUsing_UndefinedNamespace_ProducesWarning()
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace MyApp; class Program { }
        var programClass = MakeClass("Program");
        var nsNode1 = MakeFileScopedNs("MyApp",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass }),
            sourceOffset: nsOffset);

        var source1 = "namespace MyApp; class Program { }";
        var ast1 = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNode1 }),
        };

        var sourceText1 = new SourceText("/project/Program.mist", source1);

        // Global using references a namespace that doesn't exist
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceText1) { GlobalUsings = ImmutableArray.Create("NonExistent.Namespace") }));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast1),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should have MIST_GLOBAL_USING_UNDEFINED_NAMESPACE warning
        var undefinedNsDiag = diags.FirstOrDefault(d =>
            d.Id.Value == "MIST_GLOBAL_USING_UNDEFINED_NAMESPACE");

        Assert.NotNull(undefinedNsDiag);
        Assert.Equal(DiagnosticSeverity.Warning, undefinedNsDiag.Severity);
        Assert.Contains("NonExistent.Namespace", undefinedNsDiag.Message);
    }

    // ─── Test 4: Ambiguous types from multiple global usings ─────────────

    /// <summary>
    /// When multiple global using namespaces contain types with the same simple
    /// name and that name is referenced without qualification, the builder should
    /// produce a MIST_AMBIGUOUS_TYPE error diagnostic.
    /// Validates: Requirement 4.4
    /// </summary>
    [Fact]
    public void GlobalUsing_AmbiguousType_FromMultipleGlobalUsings_ProducesError()
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace NsA; class Widget { }
        var widgetA = MakeClass("Widget");
        var nsNodeA = MakeFileScopedNs("NsA",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = widgetA }),
            sourceOffset: nsOffset);

        var sourceA = "namespace NsA; class Widget { }";
        var astA = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeA }),
        };

        // File 2: namespace NsB; class Widget { }
        var widgetB = MakeClass("Widget");
        var nsNodeB = MakeFileScopedNs("NsB",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = widgetB }),
            sourceOffset: nsOffset);

        var sourceB = "namespace NsB; class Widget { }";
        var astB = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeB }),
        };

        // File 3: namespace Consumer; class Consumer { Widget myField; }
        // No explicit using — relies on global usings for both NsA and NsB
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Widget");
        var nsNodeC = MakeFileScopedNs("Consumer",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var sourceC = "namespace Consumer; class Consumer { Widget myField; }";
        var astC = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeC }),
        };

        var sourceTextA = new SourceText("/project/FileA.mist", sourceA);
        var sourceTextB = new SourceText("/project/FileB.mist", sourceB);
        var sourceTextC = new SourceText("/project/FileC.mist", sourceC);

        // File C has global usings for both NsA and NsB (both contain "Widget")
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceTextA),
            new CompilationUnit(sourceTextB),
            new CompilationUnit(sourceTextC) { GlobalUsings = ImmutableArray.Create("NsA", "NsB") }));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(astA, astB, astC),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should have MIST_AMBIGUOUS_TYPE for "Widget"
        var ambiguousDiag = diags.FirstOrDefault(d =>
            d.Id.Value == "MIST_AMBIGUOUS_TYPE" && d.Message.Contains("Widget"));

        Assert.NotNull(ambiguousDiag);
        Assert.Equal(DiagnosticSeverity.Error, ambiguousDiag.Severity);
        Assert.Contains("NsA", ambiguousDiag.Message);
        Assert.Contains("NsB", ambiguousDiag.Message);
    }

    // ─── Test 5: Ambiguity between global using and explicit using ───────

    /// <summary>
    /// When both a global using and an explicit using declaration in the same
    /// file provide a type with the same simple name and that name is referenced
    /// without qualification, the builder should produce a MIST_AMBIGUOUS_TYPE
    /// error diagnostic (global usings have the same priority as explicit usings).
    /// Validates: Requirement 4.5
    /// </summary>
    [Fact]
    public void GlobalUsing_AmbiguousType_BetweenGlobalAndExplicitUsing_ProducesError()
    {
        var nsOffset = "namespace ".Length;

        // File 1: namespace NsA; class Widget { }
        var widgetA = MakeClass("Widget");
        var nsNodeA = MakeFileScopedNs("NsA",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = widgetA }),
            sourceOffset: nsOffset);

        var sourceA = "namespace NsA; class Widget { }";
        var astA = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeA }),
        };

        // File 2: namespace NsB; class Widget { }
        var widgetB = MakeClass("Widget");
        var nsNodeB = MakeFileScopedNs("NsB",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = widgetB }),
            sourceOffset: nsOffset);

        var sourceB = "namespace NsB; class Widget { }";
        var astB = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeB }),
        };

        // File 3: using NsB; namespace Consumer; class Consumer { Widget myField; }
        // Has explicit using for NsB, global using for NsA — both contain "Widget"
        var consumerClass = MakeClassWithFieldOfType("Consumer", "Widget");
        var nsNodeC = MakeFileScopedNs("Consumer",
            ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = consumerClass }),
            sourceOffset: nsOffset);

        var usingDecl = MakeUsingDecl("NsB");

        var sourceC = "using NsB; namespace Consumer; class Consumer { Widget myField; }";
        var astC = new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(usingDecl),
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new NamespaceItemTopLevelItemNode { NamespaceDeclaration = nsNodeC }),
        };

        var sourceTextA = new SourceText("/project/FileA.mist", sourceA);
        var sourceTextB = new SourceText("/project/FileB.mist", sourceB);
        var sourceTextC = new SourceText("/project/FileC.mist", sourceC);

        // File C has global using for NsA and explicit using for NsB
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(sourceTextA),
            new CompilationUnit(sourceTextB),
            new CompilationUnit(sourceTextC) { GlobalUsings = ImmutableArray.Create("NsA") }));
        var diagnostics = new DiagnosticBag();

        Builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(astA, astB, astC),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/project",
            diagnostics);

        var diags = diagnostics.ToImmutableArray();

        // Should have MIST_AMBIGUOUS_TYPE for "Widget"
        var ambiguousDiag = diags.FirstOrDefault(d =>
            d.Id.Value == "MIST_AMBIGUOUS_TYPE" && d.Message.Contains("Widget"));

        Assert.NotNull(ambiguousDiag);
        Assert.Equal(DiagnosticSeverity.Error, ambiguousDiag.Severity);
    }
}