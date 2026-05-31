// Feature: traits, Task 4.1: Pass1_ResolveWithBlocks sub-pass
// Tests for with-block resolution and validation diagnostics.
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.9**

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for the Pass1_ResolveWithBlocks sub-pass.
/// Verifies that with-block resolution correctly validates type/trait references,
/// member mappings, and emits appropriate diagnostics.
/// </summary>
public class WithBlockResolutionTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Creates a class declaration with public field members.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members,
        };
    }

    /// <summary>
    /// Creates a class declaration with a private field member.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPrivateField(
        string className, string typeName, string fieldName)
    {
        return new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new FieldMemberMemberDeclarationNode
                {
                    Field = new FieldDeclarationNode
                    {
                        Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
                        Type = MakePrimitiveTypeRef(typeName),
                        Name = Tok(fieldName),
                    },
                }),
        };
    }

    /// <summary>
    /// Creates a trait declaration with field members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode
        {
            Name = Tok(traitName),
            Members = members,
        };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            "string" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            "bool" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            } },
            _ => new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(typeName) },
            } },
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items)
    {
        return new CompilationUnitNode
        {
            Items = items.ToImmutableArray(),
        };
    }

    private static TopLevelItemNode MakeTypeItem(ClassDeclarationNode classDecl)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = classDecl,
            },
        };
    }

    private static TopLevelItemNode MakeTypeItem(TraitDeclarationNode traitDecl)
    {
        return new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode
            {
                TraitDeclaration = traitDecl,
            },
        };
    }

    private static TopLevelItemNode MakeWithItem(WithDeclarationNode withDecl)
    {
        return new WithItemTopLevelItemNode
        {
            WithDeclaration = withDecl,
        };
    }

    private static WithDeclarationNode MakeWithDeclaration(
        string typeName, string traitName, params (string source, string target)[] mappings)
    {
        return new WithDeclarationNode
        {
            TypeName = Tok(typeName),
            TraitName = Tok(traitName),
            Mappings = mappings.Select(m => new MemberMappingNode
            {
                SourceName = Tok(m.source),
                TargetName = Tok(m.target),
            }).ToImmutableArray(),
        };
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void WithBlock_UndefinedType_EmitsDiagnostic()
    {
        // Arrange: trait exists but type does not
        var traitDecl = MakeTraitWithFields("Printable", ("int", "value"));
        var withDecl = MakeWithDeclaration("NonExistent", "Printable", ("x", "value"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TYPE"
            && d.Message.Contains("NonExistent"));
    }

    [Fact]
    public void WithBlock_UndefinedTrait_EmitsDiagnostic()
    {
        // Arrange: type exists but trait does not
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "value"));
        var withDecl = MakeWithDeclaration("Widget", "NonExistentTrait", ("value", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TRAIT"
            && d.Message.Contains("NonExistentTrait"));
    }

    [Fact]
    public void WithBlock_DuplicateWithBlock_EmitsDiagnostic()
    {
        // Arrange: two with blocks for the same type-trait pair
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "value"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl1 = MakeWithDeclaration("Widget", "Printable", ("value", "x"));
        var withDecl2 = MakeWithDeclaration("Widget", "Printable", ("value", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl1),
            MakeWithItem(withDecl2));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_DUPLICATE_WITH"
            && d.Message.Contains("Widget")
            && d.Message.Contains("Printable"));
    }

    [Fact]
    public void WithBlock_UndefinedSourceMember_EmitsDiagnostic()
    {
        // Arrange: source member does not exist on the type
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "value"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("nonExistentField", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_SOURCE"
            && d.Message.Contains("nonExistentField")
            && d.Message.Contains("Widget"));
    }

    [Fact]
    public void WithBlock_PrivateSourceMember_EmitsDiagnostic()
    {
        // Arrange: source member exists but is private
        var classDecl = MakeClassWithPrivateField("Widget", "int", "value");
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("value", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_SOURCE"
            && d.Message.Contains("value")
            && d.Message.Contains("Widget"));
    }

    [Fact]
    public void WithBlock_UndefinedTargetMember_EmitsDiagnostic()
    {
        // Arrange: target member does not exist in the trait
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "value"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("value", "nonExistentTarget"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TARGET"
            && d.Message.Contains("nonExistentTarget")
            && d.Message.Contains("Printable"));
    }

    [Fact]
    public void WithBlock_TypeMismatch_EmitsDiagnostic()
    {
        // Arrange: source member type (string) doesn't match target member type (int)
        var classDecl = MakeClassWithPublicFields("Widget", ("string", "label"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("label", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH"
            && d.Message.Contains("Widget.label")
            && d.Message.Contains("Printable.x"));
    }

    [Fact]
    public void WithBlock_ValidMapping_NoDiagnostics()
    {
        // Arrange: valid with block mapping (int -> int)
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "myValue"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "x"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("myValue", "x"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no with-block-related diagnostics
        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_"));
        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value == "MIST_TRAIT_DUPLICATE_WITH");
    }

    [Fact]
    public void WithBlock_ValidMapping_SameNameMapping_NoDiagnostics()
    {
        // Arrange: same-name mapping (value -> value) with compatible types
        var classDecl = MakeClassWithPublicFields("Widget", ("int", "value"));
        var traitDecl = MakeTraitWithFields("Printable", ("int", "value"));
        var withDecl = MakeWithDeclaration("Widget", "Printable", ("value", "value"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no with-block-related diagnostics
        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_"));
    }

    [Fact]
    public void WithBlock_MultipleValidMappings_NoDiagnostics()
    {
        // Arrange: multiple valid mappings
        var classDecl = MakeClassWithPublicFields("Widget",
            ("int", "myValue"), ("string", "myLabel"));
        var traitDecl = MakeTraitWithFields("Printable",
            ("int", "x"), ("string", "name"));
        var withDecl = MakeWithDeclaration("Widget", "Printable",
            ("myValue", "x"), ("myLabel", "name"));

        var ast = MakeCompilationUnit(
            MakeTypeItem(classDecl),
            MakeTypeItem(traitDecl),
            MakeWithItem(withDecl));

        // Act
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no with-block-related diagnostics
        Assert.DoesNotContain(diagnostics, d =>
            d.Id.Value.StartsWith("MIST_TRAIT_WITH_"));
    }
}
